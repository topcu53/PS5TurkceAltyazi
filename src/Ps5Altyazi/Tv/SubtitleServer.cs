using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Ps5Altyazi.Logging;

namespace Ps5Altyazi.Tv;

/// <summary>
/// Türkçe altyazıyı ev ağındaki ekranlara yayınlar (WebSocket, port 47800):
/// - TV uygulamaları (LG webOS; ileride Samsung, Android TV) ws://PC:47800/ws adresine bağlanır.
/// - Telefon / tablet / herhangi bir tarayıcı: http://PC:47800/ (aynı altyazıyı gösteren sayfa).
/// Mesajlar JSON: sub, clear, notice, mute, style, hdmi, ping.
/// Ek kütüphane gerektirmeyen küçük bir WebSocket sunucusu (RFC 6455, yalnızca metin çerçeveleri).
/// </summary>
public sealed class SubtitleServer : IDisposable
{
    public const int Port = 47800;
    private const string WsGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    private sealed class Client
    {
        public required TcpClient Tcp { get; init; }
        public required Stream Stream { get; init; }
        public required string Remote { get; init; }
        public string Kind { get; set; } = "?";
        /// <summary>
        /// Gönderim kuyruğu: yavaş / donmuş bir ekran, arayüzü (çeviri akışını) bekletmesin. Kuyruk dolarsa en eski
        /// mesaj atılır (altyazıda yalnızca en yenisi önemlidir).
        /// </summary>
        public readonly Channel<byte[]> Outbox = Channel.CreateBounded<byte[]>(
            new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    }

    private const int MaxClients = 16;

    private readonly List<Client> _clients = new();
    private readonly object _gate = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Timer? _pingTimer, _announceTimer;
    public const int DiscoveryPort = 47801;

    // Yeni bağlanan istemciye durum aktarımı için son mesajlar
    private string? _lastStyle, _lastSub, _lastMute, _lastScreen;

    public bool IsRunning => _listener != null;
    public int ClientCount { get { lock (_gate) return _clients.Count; } }

    /// <summary>İstemci bağlandı / ayrıldı (Türkçe açıklama).</summary>
    public event Action<string>? ClientEvent;

    public void Start()
    {
        if (_listener != null) return;
        _cts = new CancellationTokenSource();
        var listener = new TcpListener(IPAddress.Any, Port);
        listener.Start(); // hata olursa _listener null kalır
        _listener = listener;
        _ = AcceptLoopAsync(_cts.Token);
        _pingTimer = new Timer(_ => Broadcast("{\"t\":\"ping\"}"), null, 20000, 20000);
        _announceTimer = new Timer(_ => Announce(), null, 500, 2000);
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        _listener = null;
        _pingTimer?.Dispose();
        _pingTimer = null;
        _announceTimer?.Dispose();
        _announceTimer = null;
        lock (_gate)
        {
            foreach (var c in _clients) { c.Outbox.Writer.TryComplete(); try { c.Tcp.Close(); } catch { } }
            _clients.Clear();
        }
    }

    public void Dispose() => Stop();

    // ───────── Yayın API'si ─────────

    public void ShowSubtitle(string text, double[]? coverNorm)
    {
        if (string.IsNullOrWhiteSpace(text)) { Clear(); return; }
        object msg = coverNorm is { Length: 4 }
            ? new { t = "sub", text, cover = new { x = coverNorm[0], y = coverNorm[1], w = coverNorm[2], h = coverNorm[3] } }
            : coverNorm is { Length: 5 }
            ? new { t = "sub", text, cover = new { x = coverNorm[0], y = coverNorm[1], w = coverNorm[2], h = coverNorm[3] }, lh = coverNorm[4] }
            : new { t = "sub", text };
        _lastSub = JsonSerializer.Serialize(msg);
        Broadcast(_lastSub);
    }

    public void Clear()
    {
        _lastSub = null;
        Broadcast("{\"t\":\"clear\"}");
    }

    /// <summary>İngilizce altyazıyı örten kutu (oyun alanına göre 0..1: x, y, w, h).</summary>
    public void Mask(double[] rect) =>
        Broadcast(JsonSerializer.Serialize(new { t = "mask", x = rect[0], y = rect[1], w = rect[2], h = rect[3], lh = rect.Length > 4 ? rect[4] : 0 }));

    public void Unmask() => Broadcast("{\"t\":\"unmask\"}");

    public void Notice(string text, int ms = 2000) => Broadcast(JsonSerializer.Serialize(new { t = "notice", text, ms }));

    public void Mute(bool on)
    {
        _lastMute = JsonSerializer.Serialize(new { t = "mute", on });
        Broadcast(_lastMute);
    }

    /// <summary>font: 1080p ekranda piksel; bg: 0–1; bottom/width: ekran yüzdesi; cover: İngilizce altyazıyı ört.</summary>
    public void Style(double font, double bg, double bottom, double width, bool cover, bool match)
    {
        _lastStyle = JsonSerializer.Serialize(new { t = "style", font = Math.Round(font), bg = Math.Round(bg, 2), bottom, width = Math.Round(width), cover, match });
        Broadcast(_lastStyle);
    }

    /// <summary>
    /// Oyun içi yazı çevirisi: çizilecek parçalar (oyun alanına göre 0..1). Liste her seferinde tamamen gönderilir
    /// (boş liste = hepsini kaldır).
    /// </summary>
    public void Screen(IReadOnlyList<ScreenPiece> pieces)
    {
        var arr = new List<object>(pieces.Count);
        foreach (var p in pieces)
            arr.Add(new
            {
                x = Math.Round(p.X, 4), y = Math.Round(p.Y, 4), w = Math.Round(p.W, 4), h = Math.Round(p.H, 4),
                fs = Math.Round(p.Fs, 4), text = p.Text, al = p.Align.ToString(), wr = p.Wrap, mw = Math.Round(p.MaxW, 4), ic = p.Icons
            });
        _lastScreen = pieces.Count == 0 ? null : JsonSerializer.Serialize(new { t = "screen2", items = arr });
        Broadcast(_lastScreen ?? "{\"t\":\"screen2\",\"items\":[]}");
    }

    public void SetHdmi(int port) => Broadcast(JsonSerializer.Serialize(new { t = "hdmi", port }));

    public string[] ClientDescriptions()
    {
        lock (_gate) return _clients.Select(c => $"{c.Kind} ({c.Remote})").ToArray();
    }

    // ───────── Ağ ─────────

    /// <summary>
    /// Ev ağına "buradayım" duyurusu (UDP yayın, port 47801): Android TV uygulaması PC'yi kendiliğinden bulur.
    /// İçerik yalnızca uygulama adı; TV, adresi duyurunun geldiği yerden alır. Yalnızca özel ağ arayüzlerinden gönderilir.
    /// </summary>
    private void Announce()
    {
        try
        {
            byte[] msg = Encoding.ASCII.GetBytes("ALTYAZIPLUS 1");
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null || !IsLocalNetwork(ua.Address)) continue;
                    byte[] ip = ua.Address.GetAddressBytes(), m = ua.IPv4Mask.GetAddressBytes(), bc = new byte[4];
                    for (int i = 0; i < 4; i++) bc[i] = (byte)(ip[i] | ~m[i]);
                    using var udp = new UdpClient(new IPEndPoint(ua.Address, 0)) { EnableBroadcast = true };
                    udp.Send(msg, msg.Length, new IPEndPoint(new IPAddress(bc), DiscoveryPort));
                }
            }
        }
        catch { /* duyuru isteğe bağlı */ }
    }

    /// <summary>Bu bilgisayarın ev ağındaki IPv4 adresi (verilen TV adresiyle aynı alt ağda olanı tercih eder).</summary>
    public static string? GetLocalIPv4(string? preferSameSubnetAs = null)
    {
        IPAddress? target = null;
        if (preferSameSubnetAs != null) IPAddress.TryParse(preferSameSubnetAs, out target);
        string? fallback = null;

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            var props = ni.GetIPProperties();
            bool hasGateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
            foreach (var ua in props.UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (target != null && ua.IPv4Mask != null && SameSubnet(ua.Address, target, ua.IPv4Mask))
                    return ua.Address.ToString();
                if (hasGateway && fallback == null) fallback = ua.Address.ToString();
            }
        }
        return fallback;
    }

    /// <summary>Ev ağı adresi mi? (10.x, 172.16-31.x, 192.168.x, 169.254.x, 127.x, IPv6 yerel)</summary>
    public static bool IsLocalNetwork(IPAddress a)
    {
        if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
        if (IPAddress.IsLoopback(a)) return true;
        if (a.AddressFamily == AddressFamily.InterNetworkV6)
            return a.IsIPv6LinkLocal || a.IsIPv6SiteLocal || (a.GetAddressBytes()[0] & 0xFE) == 0xFC;
        if (a.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = a.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
    }

    private static bool SameSubnet(IPAddress a, IPAddress b, IPAddress mask)
    {
        byte[] x = a.GetAddressBytes(), y = b.GetAddressBytes(), m = mask.GetAddressBytes();
        for (int i = 0; i < 4; i++) if ((x[i] & m[i]) != (y[i] & m[i])) return false;
        return true;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null)
        {
            TcpClient tcp;
            try { tcp = await _listener.AcceptTcpClientAsync(ct); }
            catch { break; }
            // Güvenlik: yalnızca ev ağından (özel IP) bağlantı kabul et; internetten gelen bağlantıları hemen kapat
            var addr = (tcp.Client.RemoteEndPoint as IPEndPoint)?.Address;
            if (addr == null || !IsLocalNetwork(addr) || ClientCount >= MaxClients)
            {
                try { tcp.Close(); } catch { }
                continue;
            }
            _ = Task.Run(() => HandleAsync(tcp, ct), ct);
        }
    }

    private async Task HandleAsync(TcpClient tcp, CancellationToken ct)
    {
        string remote = (tcp.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "?";
        tcp.NoDelay = true;
        var stream = tcp.GetStream();
        try
        {
            // HTTP isteğini oku (5 sn içinde gelmezse kapat: yarım bağlantılarla sunucuyu meşgul etmeyi önler)
            var header = new StringBuilder();
            var buf = new byte[1];
            using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            headerCts.CancelAfter(TimeSpan.FromSeconds(5));
            while (header.Length < 8192)
            {
                int n = await stream.ReadAsync(buf.AsMemory(0, 1), headerCts.Token);
                if (n == 0) { tcp.Close(); return; }
                header.Append((char)buf[0]);
                if (header.Length >= 4 && header.ToString(header.Length - 4, 4) == "\r\n\r\n") break;
            }
            var lines = header.ToString().Split("\r\n");
            string[] reqLine = lines[0].Split(' ');
            string path = reqLine.Length > 1 ? reqLine[1] : "/";
            var headers = lines.Skip(1).Where(l => l.Contains(':'))
                .Select(l => (Key: l[..l.IndexOf(':')].Trim().ToLowerInvariant(), Val: l[(l.IndexOf(':') + 1)..].Trim()))
                .GroupBy(h => h.Key).ToDictionary(g => g.Key, g => g.First().Val);

            if (path.StartsWith("/ws") && headers.TryGetValue("sec-websocket-key", out var key))
            {
                string accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + WsGuid)));
                string resp = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
                              $"Sec-WebSocket-Accept: {accept}\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(resp), ct);

                var client = new Client { Tcp = tcp, Stream = stream, Remote = remote };
                lock (_gate) _clients.Add(client);
                _ = WriteLoopAsync(client, ct);
                Send(client, JsonSerializer.Serialize(new { t = "hello", app = "PS5TurkceAltyazi" }));
                if (_lastStyle != null) Send(client, _lastStyle);
                if (_lastMute != null) Send(client, _lastMute);
                if (_lastSub != null) Send(client, _lastSub);
                if (_lastScreen != null) Send(client, _lastScreen);
                ClientEvent?.Invoke($"Altyazı ekranı bağlandı: {remote}");
                await ReadLoopAsync(client, ct);
                return;
            }

            if (path == "/" || path.StartsWith("/?") || path.StartsWith("/index"))
            {
                byte[] body = Encoding.UTF8.GetBytes(PhonePage.Html);
                string resp = "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\n" +
                              $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(resp), ct);
                await stream.WriteAsync(body, ct);
            }
            else
            {
                await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), ct);
            }
            tcp.Close();
        }
        catch
        {
            try { tcp.Close(); } catch { }
        }
    }

    private async Task ReadLoopAsync(Client c, CancellationToken ct)
    {
        try
        {
            var hdr = new byte[2];
            while (!ct.IsCancellationRequested)
            {
                await ReadExactAsync(c.Stream, hdr, 2, ct);
                int opcode = hdr[0] & 0x0F;
                bool masked = (hdr[1] & 0x80) != 0;
                long len = hdr[1] & 0x7F;
                if (len == 126) { var b = new byte[2]; await ReadExactAsync(c.Stream, b, 2, ct); len = (b[0] << 8) | b[1]; }
                else if (len == 127) { var b = new byte[8]; await ReadExactAsync(c.Stream, b, 8, ct); len = 0; for (int i = 0; i < 8; i++) len = (len << 8) | b[i]; }
                if (len > 65536 || len < 0) break;
                var mask = new byte[4];
                if (masked) await ReadExactAsync(c.Stream, mask, 4, ct);
                var payload = new byte[len];
                if (len > 0) await ReadExactAsync(c.Stream, payload, (int)len, ct);
                if (masked) for (int i = 0; i < payload.Length; i++) payload[i] ^= mask[i % 4];

                if (opcode == 8) break; // kapat
                if (opcode == 9) { SendFrame(c, 0x8A, payload); continue; } // ping → pong
                if (opcode == 1)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(payload);
                        if (doc.RootElement.TryGetProperty("t", out var t) && t.GetString() == "hi")
                        {
                            c.Kind = doc.RootElement.TryGetProperty("client", out var k) ? k.GetString() ?? "?" : "?";
                            ClientEvent?.Invoke($"Altyazı ekranı tanıtıldı: {c.Kind} ({c.Remote})");
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }
        finally
        {
            Remove(c, "ayrıldı");
        }
    }

    private static async Task ReadExactAsync(Stream s, byte[] buf, int count, CancellationToken ct)
    {
        int off = 0;
        while (off < count)
        {
            int n = await s.ReadAsync(buf.AsMemory(off, count - off), ct);
            if (n == 0) throw new EndOfStreamException();
            off += n;
        }
    }

    private void Broadcast(string json)
    {
        Client[] list;
        lock (_gate) list = _clients.ToArray();
        foreach (var c in list) Send(c, json);
    }

    private void Send(Client c, string json) => SendFrame(c, 0x81, Encoding.UTF8.GetBytes(json));

    private void SendFrame(Client c, byte first, byte[] payload)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(first);
        if (payload.Length < 126) ms.WriteByte((byte)payload.Length);
        else if (payload.Length <= 0xFFFF) { ms.WriteByte(126); ms.WriteByte((byte)(payload.Length >> 8)); ms.WriteByte((byte)payload.Length); }
        else { ms.WriteByte(127); for (int i = 7; i >= 0; i--) ms.WriteByte((byte)((long)payload.Length >> (8 * i))); }
        ms.Write(payload);
        c.Outbox.Writer.TryWrite(ms.ToArray()); // beklemez; ağ yazımı WriteLoopAsync'te
    }

    /// <summary>İstemcinin kuyruğundaki çerçeveleri sırayla gönderir (10 sn'de yazılamazsa bağlantıyı kapatır).</summary>
    private async Task WriteLoopAsync(Client c, CancellationToken ct)
    {
        try
        {
            await foreach (var frame in c.Outbox.Reader.ReadAllAsync(ct))
            {
                using var wcts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                wcts.CancelAfter(TimeSpan.FromSeconds(10));
                await c.Stream.WriteAsync(frame, wcts.Token);
            }
        }
        catch
        {
            Remove(c, "bağlantı koptu");
        }
    }

    private void Remove(Client c, string why)
    {
        bool removed;
        lock (_gate) removed = _clients.Remove(c);
        c.Outbox.Writer.TryComplete();
        try { c.Tcp.Close(); } catch { }
        if (removed) ClientEvent?.Invoke($"Altyazı ekranı {why}: {c.Kind} ({c.Remote})");
    }
}

/// <summary>
/// Oyun içi yazı çevirisinin tek çizim parçası (oyun alanına göre 0..1; Fs = yazı boyu / oyun alanı yüksekliği).
/// Align 'l': kutunun sol kenarı X'te, sağa büyür; 'r': sağ kenarı X+W'de, sola büyür (tuş simgesinden uzağa).
/// Kutu en az W×H büyüklüğündedir ve dikeyde Y+H/2'ye ortalanır. Text içindeki '\u0001' + rakam = Icons[rakam] (PNG, base64).
/// </summary>
public sealed record ScreenPiece(double X, double Y, double W, double H, double Fs, string Text, char Align, bool Wrap, double MaxW, string[] Icons);
