/*
 * ps5-baglanti – Altyazı+ için PS5 bağlantı yardımcısı.
 *
 * Ayrı bir programdır: Altyazı+ (AltyaziPlus.exe) onu alt süreç olarak çalıştırır ve yalnız standart giriş/çıkış
 * üzerinden konuşur. PS5'in Remote Play protokolünü chiaki-ng'nin kütüphanesi (libchiaki, AGPL-3.0) konuşur; bu
 * program da onunla bağlandığı için AGPL-3.0 lisanslıdır (bkz. tools/ps5-baglanti/LICENSE).
 *
 * Komutlar (çıktı: satır başına bir JSON nesnesi; günlük satırları stderr'e):
 *   ps5-baglanti discover [ms]                 stdin'den adres listesi okur, yanıt veren PS5'leri yazar
 *   ps5-baglanti wake <ip> <kimlik-hex>        dinlenmedeki PS5'i uyandırır
 *   ps5-baglanti register <ip> <hesap-hex16> <pin8>
 *   ps5-baglanti stream <ip> <anahtar-hex32> <morning-hex32> <hesap-hex16> [fps] [yükseklik]
 *
 * stream çıktısı ikilidir (stdout): her paket 12 baytlık başlık (tür, a, uzunluk; küçük-uçlu uint32) + yük.
 *   tür 1: olay (yük: JSON metni)   tür 2: kare (a: genişlik, yük: BGRA pikseller, yükseklik = uzunluk / (genişlik*4))
 * stream girdisi (stdin, satır satır): "pin 1234" giriş şifresi, "stop" ya da stdin'in kapanması: bağlantıyı kapat.
 */
#include <chiaki/common.h>
#include <chiaki/discovery.h>
#include <chiaki/log.h>
#include <chiaki/regist.h>
#include <chiaki/session.h>
#include <chiaki/thread.h>

#include <libavcodec/avcodec.h>
#include <libavutil/frame.h>
#include <libavutil/imgutils.h>
#include <libswscale/swscale.h>

#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <fcntl.h>
#include <io.h>
#else
#include <arpa/inet.h>
#include <netinet/in.h>
#include <sys/socket.h>
#include <unistd.h>
#endif

/* ───────────────────────── yardımcılar ───────────────────────── */

static void sleep_ms(unsigned ms)
{
#ifdef _WIN32
	Sleep(ms);
#else
	usleep(ms * 1000);
#endif
}

static void log_cb(ChiakiLogLevel level, const char *msg, void *user)
{
	(void)user;
	const char *tag = level == CHIAKI_LOG_ERROR ? "E" : level == CHIAKI_LOG_WARNING ? "W" : "I";
	fprintf(stderr, "[%s] %s\n", tag, msg);
	fflush(stderr);
}

/* JSON dizgesi olarak yazar (tırnaklarıyla). */
static void json_str(char *out, size_t cap, const char *s)
{
	size_t n = 0;
	if(cap < 3) { if(cap) out[0] = 0; return; }
	out[n++] = '"';
	for(; s && *s && n + 7 < cap; s++)
	{
		unsigned char c = (unsigned char)*s;
		if(c == '"' || c == '\\') { out[n++] = '\\'; out[n++] = (char)c; }
		else if(c < 0x20) n += (size_t)snprintf(out + n, cap - n, "\\u%04x", c);
		else out[n++] = (char)c;
	}
	out[n++] = '"';
	out[n] = 0;
}

static int hex_val(char c)
{
	if(c >= '0' && c <= '9') return c - '0';
	if(c >= 'a' && c <= 'f') return c - 'a' + 10;
	if(c >= 'A' && c <= 'F') return c - 'A' + 10;
	return -1;
}

static bool parse_hex(const char *s, uint8_t *out, size_t len)
{
	if(!s || strlen(s) != len * 2) return false;
	for(size_t i = 0; i < len; i++)
	{
		int h = hex_val(s[2 * i]), l = hex_val(s[2 * i + 1]);
		if(h < 0 || l < 0) return false;
		out[i] = (uint8_t)(h << 4 | l);
	}
	return true;
}

static void to_hex(const uint8_t *in, size_t len, char *out)
{
	static const char d[] = "0123456789abcdef";
	for(size_t i = 0; i < len; i++) { out[2 * i] = d[in[i] >> 4]; out[2 * i + 1] = d[in[i] & 15]; }
	out[2 * len] = 0;
}

/* ───────────────────────── discover ───────────────────────── */

typedef struct
{
	ChiakiMutex lock;
	char seen[256][48];
	int count;
} DiscoverState;

static void discover_cb(ChiakiDiscoveryHost *host, void *user)
{
	DiscoverState *st = user;
	if(!host->host_addr || !chiaki_discovery_host_is_ps5(host)) return;
	chiaki_mutex_lock(&st->lock);
	for(int i = 0; i < st->count; i++)
		if(strcmp(st->seen[i], host->host_addr) == 0) { chiaki_mutex_unlock(&st->lock); return; }
	if(st->count < 256) snprintf(st->seen[st->count++], 48, "%s", host->host_addr);
	char addr[96], name[160], id[96], app[200];
	json_str(addr, sizeof(addr), host->host_addr);
	json_str(name, sizeof(name), host->host_name ? host->host_name : "PS5");
	json_str(id, sizeof(id), host->host_id ? host->host_id : "");
	json_str(app, sizeof(app), host->running_app_name ? host->running_app_name : "");
	printf("{\"host\":%s,\"name\":%s,\"id\":%s,\"awake\":%s,\"app\":%s}\n", addr, name, id,
		host->state == CHIAKI_DISCOVERY_HOST_STATE_READY ? "true" : "false", app);
	fflush(stdout);
	chiaki_mutex_unlock(&st->lock);
}

static int cmd_discover(ChiakiLog *log, int argc, char **argv)
{
	int timeout_ms = argc > 0 ? atoi(argv[0]) : 2000;
	if(timeout_ms < 300) timeout_ms = 300;

	/* Sorulacak adresler stdin'den (satır başına bir IPv4); boşsa yalnız genel yayın. */
	static struct sockaddr_in targets[4096];
	int n = 0;
	char line[128];
	while(n < 4096 && fgets(line, sizeof(line), stdin))
	{
		line[strcspn(line, "\r\n ")] = 0;
		if(!line[0]) continue;
		struct sockaddr_in a;
		memset(&a, 0, sizeof(a));
		a.sin_family = AF_INET;
		a.sin_port = htons(CHIAKI_DISCOVERY_PORT_PS5);
		if(inet_pton(AF_INET, line, &a.sin_addr) == 1) targets[n++] = a;
	}
	if(n == 0)
	{
		memset(&targets[0], 0, sizeof(targets[0]));
		targets[0].sin_family = AF_INET;
		targets[0].sin_port = htons(CHIAKI_DISCOVERY_PORT_PS5);
		targets[0].sin_addr.s_addr = htonl(INADDR_BROADCAST);
		n = 1;
	}

	ChiakiDiscovery discovery;
	if(chiaki_discovery_init(&discovery, log, AF_INET) != CHIAKI_ERR_SUCCESS)
	{
		printf("{\"error\":\"discovery init failed\"}\n");
		return 1;
	}
	DiscoverState st;
	memset(&st, 0, sizeof(st));
	chiaki_mutex_init(&st.lock, false);
	ChiakiDiscoveryThread thread;
	if(chiaki_discovery_thread_start(&thread, &discovery, discover_cb, &st) != CHIAKI_ERR_SUCCESS)
	{
		chiaki_discovery_fini(&discovery);
		printf("{\"error\":\"discovery thread failed\"}\n");
		return 1;
	}

	ChiakiDiscoveryPacket packet;
	memset(&packet, 0, sizeof(packet));
	packet.cmd = CHIAKI_DISCOVERY_CMD_SRCH;
	packet.protocol_version = CHIAKI_DISCOVERY_PROTOCOL_VERSION_PS5;
	for(int waited = 0; waited < timeout_ms; waited += 500)
	{
		for(int i = 0; i < n; i++)
			chiaki_discovery_send(&discovery, &packet, (struct sockaddr *)&targets[i], sizeof(targets[i]));
		sleep_ms(500);
	}
	chiaki_discovery_thread_stop(&thread);
	chiaki_discovery_fini(&discovery);
	chiaki_mutex_fini(&st.lock);
	return 0;
}

/* ───────────────────────── wake ───────────────────────── */

static int cmd_wake(ChiakiLog *log, int argc, char **argv)
{
	if(argc < 2) { printf("{\"ok\":false,\"error\":\"usage\"}\n"); return 2; }
	uint64_t credential = (uint64_t)strtoull(argv[1], NULL, 16);
	ChiakiErrorCode err = chiaki_discovery_wakeup(log, NULL, argv[0], credential, true);
	printf("{\"ok\":%s}\n", err == CHIAKI_ERR_SUCCESS ? "true" : "false");
	return err == CHIAKI_ERR_SUCCESS ? 0 : 1;
}

/* ───────────────────────── register ───────────────────────── */

typedef struct
{
	ChiakiMutex lock;
	ChiakiCond cond;
	bool done;
	ChiakiRegistEventType type;
	ChiakiRegisteredHost host;
} RegistState;

static void regist_cb(ChiakiRegistEvent *event, void *user)
{
	RegistState *st = user;
	chiaki_mutex_lock(&st->lock);
	st->type = event->type;
	if(event->type == CHIAKI_REGIST_EVENT_TYPE_FINISHED_SUCCESS && event->registered_host)
		st->host = *event->registered_host;
	st->done = true;
	chiaki_cond_signal(&st->cond);
	chiaki_mutex_unlock(&st->lock);
}

static int cmd_register(ChiakiLog *log, int argc, char **argv)
{
	if(argc < 3) { printf("{\"ok\":false,\"error\":\"usage\"}\n"); return 2; }
	ChiakiRegistInfo info;
	memset(&info, 0, sizeof(info));
	info.target = CHIAKI_TARGET_PS5_1;
	info.host = argv[0];
	info.broadcast = strcmp(argv[0], "255.255.255.255") == 0;
	if(!parse_hex(argv[1], info.psn_account_id, CHIAKI_PSN_ACCOUNT_ID_SIZE))
	{
		printf("{\"ok\":false,\"error\":\"account\"}\n");
		return 2;
	}
	info.pin = (uint32_t)strtoul(argv[2], NULL, 10);

	RegistState st;
	memset(&st, 0, sizeof(st));
	chiaki_mutex_init(&st.lock, false);
	chiaki_cond_init(&st.cond);
	ChiakiRegist regist;
	if(chiaki_regist_start(&regist, log, &info, regist_cb, &st) != CHIAKI_ERR_SUCCESS)
	{
		printf("{\"ok\":false,\"error\":\"start\"}\n");
		return 1;
	}
	chiaki_mutex_lock(&st.lock);
	for(int i = 0; i < 45 && !st.done; i++)
		chiaki_cond_timedwait(&st.cond, &st.lock, 1000);
	bool done = st.done;
	chiaki_mutex_unlock(&st.lock);
	if(!done) chiaki_regist_stop(&regist);
	chiaki_regist_fini(&regist);

	int rc = 1;
	if(done && st.type == CHIAKI_REGIST_EVENT_TYPE_FINISHED_SUCCESS)
	{
		char key[2 * CHIAKI_SESSION_AUTH_SIZE + 1], morning[33], mac[13], name[96], nick[0x21];
		to_hex((const uint8_t *)st.host.rp_regist_key, CHIAKI_SESSION_AUTH_SIZE, key);
		to_hex(st.host.rp_key, 0x10, morning);
		to_hex(st.host.server_mac, 6, mac);
		memcpy(nick, st.host.server_nickname, 0x20);
		nick[0x20] = 0;
		json_str(name, sizeof(name), nick);
		printf("{\"ok\":true,\"name\":%s,\"key\":\"%s\",\"morning\":\"%s\",\"mac\":\"%s\"}\n", name, key, morning, mac);
		rc = 0;
	}
	else
		printf("{\"ok\":false,\"error\":\"%s\"}\n", !done ? "timeout" : st.type == CHIAKI_REGIST_EVENT_TYPE_FINISHED_CANCELED ? "canceled" : "failed");
	fflush(stdout);
	chiaki_cond_fini(&st.cond);
	chiaki_mutex_fini(&st.lock);
	return rc;
}

/* ───────────────────────── stream ───────────────────────── */

typedef struct
{
	ChiakiSession session;
	ChiakiMutex out_lock;     /* stdout'a yazma */
	/* Kare teslimi: ağ iş parçacığı beklemesin; yazıcı meşgulse yeni kare çevrilmez. */
	ChiakiMutex frame_lock;
	ChiakiCond frame_cond;
	uint8_t *frame;           /* BGRA */
	int frame_w, frame_h;
	bool frame_ready, writer_busy, closing;
	int min_interval_ms;
	uint64_t last_frame_ms;

	AVCodecContext *codec;
	AVFrame *decoded;
	struct SwsContext *sws;
	int sws_w, sws_h, sws_fmt;

	volatile bool quit;
	volatile int quit_reason;
	volatile bool connected;
} Stream;

static uint64_t now_ms(void)
{
#ifdef _WIN32
	return GetTickCount64();
#else
	struct timespec t;
	clock_gettime(CLOCK_MONOTONIC, &t);
	return (uint64_t)t.tv_sec * 1000 + (uint64_t)t.tv_nsec / 1000000;
#endif
}

static void write_packet(Stream *s, uint32_t type, uint32_t a, const void *data, uint32_t len)
{
	uint32_t hdr[3] = { type, a, len };
	chiaki_mutex_lock(&s->out_lock);
	fwrite(hdr, sizeof(hdr), 1, stdout);
	if(len) fwrite(data, 1, len, stdout);
	fflush(stdout);
	chiaki_mutex_unlock(&s->out_lock);
}

static void emit_event(Stream *s, const char *json)
{
	write_packet(s, 1, 0, json, (uint32_t)strlen(json));
}

static void *frame_writer(void *user)
{
	Stream *s = user;
	chiaki_mutex_lock(&s->frame_lock);
	while(!s->closing)
	{
		if(!s->frame_ready) { chiaki_cond_timedwait(&s->frame_cond, &s->frame_lock, 200); continue; }
		s->frame_ready = false;
		s->writer_busy = true;
		int w = s->frame_w, h = s->frame_h;
		chiaki_mutex_unlock(&s->frame_lock);
		write_packet(s, 2, (uint32_t)w, s->frame, (uint32_t)(w * h * 4));
		chiaki_mutex_lock(&s->frame_lock);
		s->writer_busy = false;
	}
	chiaki_mutex_unlock(&s->frame_lock);
	return NULL;
}

/* Çözülen kareyi (gerekirse) BGRA'ya çevirip yazıcıya verir. */
static void offer_frame(Stream *s, AVFrame *f)
{
	uint64_t t = now_ms();
	if(t - s->last_frame_ms < (uint64_t)s->min_interval_ms) return;
	chiaki_mutex_lock(&s->frame_lock);
	bool busy = s->writer_busy || s->frame_ready;
	chiaki_mutex_unlock(&s->frame_lock);
	if(busy) return;

	int w = f->width, h = f->height;
	if(w <= 0 || h <= 0) return;
	if(!s->sws || s->sws_w != w || s->sws_h != h || s->sws_fmt != f->format)
	{
		sws_freeContext(s->sws);
		s->sws = sws_getContext(w, h, (enum AVPixelFormat)f->format, w, h, AV_PIX_FMT_BGRA, SWS_POINT, NULL, NULL, NULL);
		s->sws_w = w; s->sws_h = h; s->sws_fmt = f->format;
		free(s->frame);
		s->frame = malloc((size_t)w * h * 4);
		if(!s->sws || !s->frame) return;
	}
	uint8_t *dst[4] = { s->frame, NULL, NULL, NULL };
	int dst_stride[4] = { w * 4, 0, 0, 0 };
	sws_scale(s->sws, (const uint8_t *const *)f->data, f->linesize, 0, h, dst, dst_stride);
	s->last_frame_ms = t;
	chiaki_mutex_lock(&s->frame_lock);
	s->frame_w = w; s->frame_h = h;
	s->frame_ready = true;
	chiaki_cond_signal(&s->frame_cond);
	chiaki_mutex_unlock(&s->frame_lock);
}

static bool video_cb(uint8_t *buf, size_t buf_size, int32_t frames_lost, bool frame_recovered, void *user)
{
	(void)frames_lost; (void)frame_recovered;
	Stream *s = user;
	AVPacket *pkt = av_packet_alloc();
	if(!pkt) return false;
	pkt->data = buf;
	pkt->size = (int)buf_size;
	int r = avcodec_send_packet(s->codec, pkt);
	av_packet_free(&pkt);
	if(r < 0 && r != AVERROR(EAGAIN)) return false;
	while(avcodec_receive_frame(s->codec, s->decoded) == 0)
		offer_frame(s, s->decoded);
	return true;
}

static void event_cb(ChiakiEvent *event, void *user)
{
	Stream *s = user;
	char buf[512], text[400];
	switch(event->type)
	{
		case CHIAKI_EVENT_CONNECTED:
			s->connected = true;
			emit_event(s, "{\"event\":\"connected\"}");
			break;
		case CHIAKI_EVENT_LOGIN_PIN_REQUEST:
			snprintf(buf, sizeof(buf), "{\"event\":\"pin\",\"incorrect\":%s}", event->login_pin_request.pin_incorrect ? "true" : "false");
			emit_event(s, buf);
			break;
		case CHIAKI_EVENT_QUIT:
			json_str(text, sizeof(text), event->quit.reason_str ? event->quit.reason_str : "");
			snprintf(buf, sizeof(buf), "{\"event\":\"quit\",\"reason\":%d,\"name\":\"%s\",\"text\":%s}", (int)event->quit.reason,
				chiaki_quit_reason_string(event->quit.reason), text);
			emit_event(s, buf);
			s->quit_reason = (int)event->quit.reason;
			s->quit = true;
			break;
		default:
			break;
	}
}

typedef struct { Stream *s; } StdinArgs;

static void *stdin_reader(void *user)
{
	Stream *s = user;
	char line[128];
	while(fgets(line, sizeof(line), stdin))
	{
		line[strcspn(line, "\r\n")] = 0;
		if(strncmp(line, "pin ", 4) == 0)
		{
			const char *pin = line + 4;
			chiaki_session_set_login_pin(&s->session, (const uint8_t *)pin, strlen(pin));
		}
		else if(strcmp(line, "stop") == 0)
			break;
	}
	/* stdin kapandı (Altyazı+ kapandı) ya da "stop": bağlantı kapatılır. */
	s->quit = true;
	return NULL;
}

static int cmd_stream(ChiakiLog *log, int argc, char **argv)
{
	if(argc < 4) { fprintf(stderr, "usage\n"); return 2; }
#ifdef _WIN32
	_setmode(_fileno(stdout), _O_BINARY);
#endif
	int fps = argc > 4 ? atoi(argv[4]) : 15;
	int height = argc > 5 ? atoi(argv[5]) : 1080;
	if(fps < 1) fps = 1;
	if(fps > 30) fps = 30;

	static Stream s;
	memset(&s, 0, sizeof(s));
	s.min_interval_ms = 1000 / fps - 5;
	chiaki_mutex_init(&s.out_lock, false);
	chiaki_mutex_init(&s.frame_lock, false);
	chiaki_cond_init(&s.frame_cond);

	const AVCodec *dec = avcodec_find_decoder(AV_CODEC_ID_H264);
	s.codec = dec ? avcodec_alloc_context3(dec) : NULL;
	s.decoded = av_frame_alloc();
	if(!s.codec || !s.decoded || avcodec_open2(s.codec, dec, NULL) < 0)
	{
		emit_event(&s, "{\"event\":\"quit\",\"reason\":-1,\"name\":\"decoder\",\"text\":\"H.264 decoder unavailable\"}");
		return 1;
	}

	ChiakiConnectInfo ci;
	memset(&ci, 0, sizeof(ci));
	ci.ps5 = true;
	ci.host = argv[0];
	uint8_t key[CHIAKI_SESSION_AUTH_SIZE];
	if(!parse_hex(argv[1], key, sizeof(key)) || !parse_hex(argv[2], ci.morning, sizeof(ci.morning))
		|| !parse_hex(argv[3], ci.psn_account_id, CHIAKI_PSN_ACCOUNT_ID_SIZE))
	{
		emit_event(&s, "{\"event\":\"quit\",\"reason\":-1,\"name\":\"args\",\"text\":\"bad keys\"}");
		return 2;
	}
	memcpy(ci.regist_key, key, sizeof(key));
	chiaki_connect_video_profile_preset(&ci.video_profile,
		height <= 720 ? CHIAKI_VIDEO_RESOLUTION_PRESET_720p : CHIAKI_VIDEO_RESOLUTION_PRESET_1080p, CHIAKI_VIDEO_FPS_PRESET_30);
	ci.video_profile.codec = CHIAKI_CODEC_H264;
	ci.video_profile_auto_downgrade = true;
	ci.enable_keyboard = false;
	ci.enable_dualsense = false;
	ci.audio_video_disabled = CHIAKI_AUDIO_DISABLED;
	ci.auto_regist = false;
	ci.packet_loss_max = 0.05;
	ci.enable_idr_on_fec_failure = true;

	if(chiaki_session_init(&s.session, &ci, log) != CHIAKI_ERR_SUCCESS)
	{
		emit_event(&s, "{\"event\":\"quit\",\"reason\":-1,\"name\":\"init\",\"text\":\"session init failed\"}");
		return 1;
	}
	chiaki_session_set_event_cb(&s.session, event_cb, &s);
	chiaki_session_set_video_sample_cb(&s.session, video_cb, &s);

	ChiakiThread writer, reader;
	chiaki_thread_create(&writer, frame_writer, &s);
	if(chiaki_session_start(&s.session) != CHIAKI_ERR_SUCCESS)
	{
		emit_event(&s, "{\"event\":\"quit\",\"reason\":-1,\"name\":\"start\",\"text\":\"session start failed\"}");
		s.quit = true;
	}
	else
	{
		chiaki_thread_create(&reader, stdin_reader, &s);
		while(!s.quit) sleep_ms(100);
		chiaki_session_stop(&s.session);
		chiaki_session_join(&s.session);
	}
	chiaki_session_fini(&s.session);

	chiaki_mutex_lock(&s.frame_lock);
	s.closing = true;
	chiaki_cond_signal(&s.frame_cond);
	chiaki_mutex_unlock(&s.frame_lock);
	chiaki_thread_join(&writer, NULL);

	avcodec_free_context(&s.codec);
	av_frame_free(&s.decoded);
	sws_freeContext(s.sws);
	free(s.frame);
	fflush(stdout);
	/* stdin okuyucusu fgets'te bekliyor olabilir; süreç çıkışı onu da bitirir. */
	return s.quit_reason == CHIAKI_QUIT_REASON_STOPPED || s.quit_reason == 0 ? 0 : 3;
}

/* ───────────────────────── giriş ───────────────────────── */

int main(int argc, char **argv)
{
	if(argc < 2)
	{
		fprintf(stderr, "ps5-baglanti: discover | wake | register | stream (Altyazı+ tarafından çalıştırılır)\n");
		return 2;
	}
	if(chiaki_lib_init() != CHIAKI_ERR_SUCCESS)
	{
		fprintf(stderr, "chiaki_lib_init failed\n");
		return 1;
	}
	ChiakiLog log;
	chiaki_log_init(&log, CHIAKI_LOG_ERROR | CHIAKI_LOG_WARNING | CHIAKI_LOG_INFO, log_cb, NULL);

	const char *cmd = argv[1];
	if(strcmp(cmd, "discover") == 0) return cmd_discover(&log, argc - 2, argv + 2);
	if(strcmp(cmd, "wake") == 0) return cmd_wake(&log, argc - 2, argv + 2);
	if(strcmp(cmd, "register") == 0) return cmd_register(&log, argc - 2, argv + 2);
	if(strcmp(cmd, "stream") == 0) return cmd_stream(&log, argc - 2, argv + 2);
	fprintf(stderr, "unknown command: %s\n", cmd);
	return 2;
}
