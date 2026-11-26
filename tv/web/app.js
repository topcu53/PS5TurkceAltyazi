/*
 * Altyazı+ – TV uygulaması (ortak kod: LG webOS, Samsung Tizen, Android TV / Google TV)
 * - PS5'in bağlı olduğu HDMI girişini tam ekran gösterir:
 *     LG webOS  → <video> + ext://hdmi:N
 *     Samsung   → tizen.tvwindow (HDMI penceresi uygulamanın ARKASINDA)
 *     Android   → yerel TvView (AndroidTv köprüsü), sayfa saydam olarak üstünde
 * - PC'deki Altyazı+ programına WebSocket ile bağlanır ve Türkçe altyazıyı üstüne çizer.
 * Eski TV tarayıcıları (Chromium 53) için ES5 ile yazıldı.
 *
 * Kumanda: Sol/Sağ = HDMI girişi, Yukarı/Aşağı = yazı boyutu, OK = bilgi,
 *          Kırmızı / Menü = PC adresi ayarı, Geri = çık.
 */
(function () {
  'use strict';

  var PORT = 47800;
  var video = document.getElementById('hdmi');
  var sub = document.getElementById('sub');
  var maskEl = document.getElementById('mask');
  var statusEl = document.getElementById('status');
  var panel = document.getElementById('panel');
  var screenEl = document.getElementById('screen');

  // ───── Platform ─────
  var PLATFORM = location.hash === '#overlay' ? 'overlay'
    : window.AndroidTv ? 'android'
    : (window.tizen && window.tizen.tvwindow) ? 'tizen'
    : (window.PalmSystem || /Web0S|webOS/i.test(navigator.userAgent)) ? 'webos'
    : 'browser';
  var KEY_BACK = { webos: 461, tizen: 10009 }[PLATFORM] || -1;
  if (PLATFORM !== 'webos') {
    // HDMI görüntüsü uygulamanın arkasında (Samsung / Android): sayfa saydam olsun
    video.style.display = 'none';
    document.documentElement.style.background = 'transparent';
    document.body.style.background = 'transparent';
  }
  if (PLATFORM === 'overlay') { panel.style.display = 'none'; }
  if (PLATFORM === 'tizen') {
    try {
      var keys = ['ColorF0Red', 'ChannelUp', 'ChannelDown', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9'];
      for (var ki = 0; ki < keys.length; ki++) { try { tizen.tvinputdevice.registerKey(keys[ki]); } catch (e1) { } }
    } catch (e) { }
  }

  function exitApp() {
    try {
      if (PLATFORM === 'tizen') tizen.application.getCurrentApplication().exit();
      else if (PLATFORM === 'android') window.AndroidTv.exit();
      else window.close();
    } catch (e) { }
  }

  var state = {
    server: load('server', ''),
    hdmi: parseInt(load('hdmi', '1'), 10) || 1,
    fontScale: parseFloat(load('fontScale', '1')) || 1,
    style: { font: 58, bg: 0.6, bottom: 6, width: 70, cover: false, match: true },
    lineH: 0,        // İngilizce altyazının tek satır yüksekliği (0..1) – Türkçe aynı boyda olsun
    cover: null,
    mask: null,      // İngilizce altyazıyı örten kutu (0..1)
    lastMask: null,  // Türkçe, İngilizce bittikten sonra da aynı yerde kalsın
    text: '',
    screen: [],      // ekran çevirisi öğeleri (menü / HUD): {x,y,w,h,lh,n,text}
    muted: false,
    connected: false
  };
  var ws = null, reconnectTimer = null, noticeTimer = null, infoTimer = null, setupMode = false;
  var ipParts = [192, 168, 1, 100], ipSel = 0;

  function load(k, d) { try { var v = localStorage.getItem('ps5tr.' + k); return v === null ? d : v; } catch (e) { return d; } }
  function save(k, v) { try { localStorage.setItem('ps5tr.' + k, String(v)); } catch (e) { } }

  // ───── HDMI girişi ─────
  function setHdmi(n) {
    state.hdmi = n;
    save('hdmi', n);
    if (PLATFORM === 'android') { try { window.AndroidTv.setHdmi(n); } catch (e) { } return; }
    if (PLATFORM === 'tizen') { tizenHdmi(n); return; }
    if (PLATFORM !== 'webos') return;
    while (video.firstChild) video.removeChild(video.firstChild);
    var src = document.createElement('source');
    src.setAttribute('type', 'service/webos-external');
    src.setAttribute('src', 'ext://hdmi:' + n);
    video.appendChild(src);
    try { video.load(); video.play(); } catch (e) { }
  }

  // Samsung: bağlı HDMI kaynağını bul, TV penceresine ver, tam ekran ve uygulamanın ARKASINDA göster
  function tizenHdmi(n) {
    function show() {
      try {
        tizen.tvwindow.show(function () { }, function (e) { notice('TV penceresi açılamadı: ' + e.message, 4000); },
          ['0px', '0px', '1920px', '1080px'], 'MAIN', 'BEHIND');
      } catch (e) {
        try { tizen.tvwindow.show(function () { }, function () { }, ['0px', '0px', '1920px', '1080px'], 'MAIN'); } catch (e2) { }
      }
    }
    try {
      tizen.systeminfo.getPropertyValue('VIDEOSOURCE', function (vs) {
        var list = (vs.connected || []).concat(vs.disconnected || []), pick = null;
        for (var i = 0; i < list.length; i++) if (list[i].type === 'HDMI' && String(list[i].number) === String(n)) pick = list[i];
        if (!pick) { notice('HDMI ' + n + ' bulunamadı', 3000); return; }
        tizen.tvwindow.setSource(pick, show, function (e) { notice('HDMI ' + n + ' açılamadı: ' + e.message, 4000); }, 'MAIN');
      }, function (e) { notice('Giriş listesi okunamadı: ' + e.message, 4000); });
    } catch (e) { notice('Bu TV HDMI gömmeyi desteklemiyor', 4000); }
  }

  // ───── Altyazı çizimi ─────
  // ───── Oyunun yazı tipi (PC ölçer, en benzer fontu seçer; font dosyası PC'den indirilir) ─────
  var loadedFonts = {};
  function ensureFont(gf) {
    if (!gf || !gf.css || !gf.file || loadedFonts[gf.css] || !state.server) return;
    if (!/^[A-Za-z0-9.-]+$/.test(gf.file) || !/^[A-Za-z0-9-]+$/.test(gf.css)) return;
    loadedFonts[gf.css] = true;
    var st = document.createElement('style');
    st.textContent = "@font-face{font-family:'" + gf.css + "';src:url('http://" + state.server + ":47800/fonts/" + gf.file +
      "');font-display:swap;}";
    document.head.appendChild(st);
  }

  function render() {
    var s = state.style;
    var gf = s.gf || null;
    ensureFont(gf);
    var showText = state.text && !state.muted;
    var noticeText = sub.getAttribute('data-notice') || '';
    var text = showText ? state.text : noticeText;
    if (!text) { sub.style.display = 'none'; return; }

    // Yerleşim: cover = orijinalin yerine (orijinal siyahla örtülür), above = orijinalin üstünde, below = altında,
    // bottom = ekranın altında sabit
    var place = s.place || 'cover';
    var anchor = showText && place !== 'bottom' ? (state.mask || state.lastMask) : null;
    var base = Math.round(s.font * state.fontScale);
    if (showText && s.match !== false && gf && gf.em > 0) {
      // Oyunun yazı tipiyle ölçülmüş boy: harfler orijinalle aynı yükseklikte (örtme kapalıyken de)
      base = Math.round(gf.em * 1080 * state.fontScale);
    } else if (anchor && s.match !== false && state.lineH > 0) {
      // Orijinal İngilizce altyazıyla aynı boy
      base = Math.round(state.lineH * 1080 * 0.95 * state.fontScale);
    }

    sub.className = showText ? '' : 'notice';
    sub.textContent = text;
    // Oyunun yazı tipi, kalınlığı ve rengi (yalnızca altyazıda; bildirimler varsayılan yazıyla)
    sub.style.fontFamily = showText && gf ? "'" + gf.css + "', 'LG Smart UI', 'Samsung One', Roboto, Arial, sans-serif" : '';
    sub.style.fontWeight = showText && gf ? (gf.bold ? 'bold' : 'normal') : '';
    var userColor = s.color && /^#[0-9A-Fa-f]{6}$/.test(s.color) ? s.color : null;
    sub.style.color = showText ? (userColor || (gf && /^#[0-9A-Fa-f]{6}$/.test(gf.color) ? gf.color : '')) : '';
    // Zemin rengi (kullanıcı seçimi); örtme modunda tam opak: İngilizce alttan görünmesin
    var bgc = s.bgc && /^#[0-9A-Fa-f]{6}$/.test(s.bgc) ? s.bgc : '#000000';
    var br = parseInt(bgc.substr(1, 2), 16), bgg = parseInt(bgc.substr(3, 2), 16), bb = parseInt(bgc.substr(5, 2), 16);
    sub.style.background = anchor && place === 'cover' ? '#000' : 'rgba(' + br + ',' + bgg + ',' + bb + ',' + s.bg + ')';
    sub.style.textShadow = showText && s.shadow !== false ? '0 2px 6px rgba(0,0,0,0.9), 0 0 2px rgba(0,0,0,0.9)' : '';
    sub.style.fontSize = showText ? base + 'px' : '';
    sub.style.lineHeight = anchor ? '1.15' : '';
    sub.style.padding = anchor ? '6px 18px 8px' : '';
    sub.style.borderRadius = (typeof s.radius === 'number' ? Math.max(0, Math.min(30, s.radius)) : 12) + 'px';
    sub.style.minWidth = '';
    sub.style.minHeight = '';
    sub.setAttribute('data-base', base);

    var c = anchor;
    if (c) {
      sub.style.left = ((c.x + c.w / 2) * 1920) + 'px';
      sub.style.bottom = 'auto';
      sub.style.top = '0px';
      sub.style.maxWidth = Math.max(c.w * 1920 + 40, s.width / 100 * 1920) + 'px';
    } else {
      sub.style.left = '50%';
      sub.style.top = 'auto';
      sub.style.bottom = (s.bottom / 100 * 1080) + 'px';
      sub.style.maxWidth = (s.width / 100 * 1920) + 'px';
    }
    sub.style.display = 'block';
    if (c) {
      // Kutu yazı kadar (büyütülmez); yeri seçilen yerleşime göre
      var h = sub.offsetHeight, top;
      if (place === 'above') top = c.y * 1080 - 10 - h;
      else if (place === 'below') {
        top = (c.y + c.h) * 1080 + 10;
        if (top + h > 1072) top = c.y * 1080 - 10 - h; // altta yer yoksa üste
      } else top = (c.y + c.h / 2) * 1080 - h / 2;       // orijinalin tam ortasına
      sub.style.top = Math.max(0, Math.min(1072 - h, top)) + 'px';
    }
  }

  // ───── Ekran çevirisi (menü, HUD, görev yazıları) ─────
  // Her öğe İngilizce yazının tam üstüne, aynı yazı boyunda, opak siyah kutu içinde çizilir.
  function renderScreen() {
    while (screenEl.firstChild) screenEl.removeChild(screenEl.firstChild);
    if (state.muted || !state.screen || !state.screen.length) { screenEl.style.display = 'none'; return; }
    screenEl.style.display = 'block';
    for (var i = 0; i < state.screen.length; i++) {
      var it = state.screen[i];
      var el = document.createElement('div');
      el.className = 'sbox';
      el.textContent = it.text;
      var x = it.x * 1920, y = it.y * 1080, w = it.w * 1920, h = it.h * 1080;
      // fs: PC'nin hesapladığı Türkçe yazı boyu (BÜYÜK/küçük harf farkı dahil); eski sürümle uyum için lh
      var base = Math.max(14, Math.round((it.fs ? it.fs * 1080 : (it.lh || it.h) * 1080 * 0.95) * state.fontScale));
      var maxW = Math.max(60, Math.min(w * ((it.n || 1) <= 1 ? 1.6 : 1.15), 1920 - x - 8));
      el.style.left = Math.round(x - 4) + 'px';
      el.style.top = Math.round(y - 2) + 'px';
      el.style.minWidth = Math.round(w + 8) + 'px';
      el.style.minHeight = Math.round(h + 4) + 'px';
      el.style.maxWidth = Math.round(maxW + 8) + 'px';
      el.style.fontSize = base + 'px';
      screenEl.appendChild(el);
      // Türkçe uzunsa orijinal kutuyu çok aşmasın: gerekirse küçült (en az %60)
      var size = base;
      for (var k = 0; k < 8; k++) {
        if (el.offsetHeight <= h * 1.25 + 8 || size <= base * 0.6) break;
        size = Math.round(size * 0.92);
        el.style.fontSize = size + 'px';
      }
      // Kutuyu orijinal yazının dikey ortasına oturt
      var bh = Math.max(el.offsetHeight, h + 4);
      el.style.top = Math.round(y + h / 2 - bh / 2) + 'px';
    }
  }

  // ───── Oyun içi yazılar v2: parçalar (tuş simgesi örtülmez) ─────
  // Her parça: x,y,w,h (0..1), fs (yazı boyu / yükseklik), text ('\u0001'+rakam = simge kopyası), al 'l'|'r',
  // wr (çok satır), mw (en fazla genişlik), ic (simge PNG base64 listesi)
  function renderPieces() {
    while (screenEl.firstChild) screenEl.removeChild(screenEl.firstChild);
    var list = state.pieces || [];
    if (state.muted || !list.length) { screenEl.style.display = 'none'; return; }
    screenEl.style.display = 'block';
    for (var i = 0; i < list.length; i++) {
      var p = list[i];
      var el = document.createElement('div');
      el.className = 'sbox';
      var x = p.x * 1920, y = p.y * 1080, w = p.w * 1920, h = p.h * 1080;
      var base = Math.max(14, Math.round(p.fs * 1080 * state.fontScale));
      el.style.whiteSpace = p.wr ? 'normal' : 'nowrap';
      el.style.wordWrap = 'normal';
      if (p.wr && p.mw) el.style.maxWidth = Math.round(p.mw * 1920) + 'px';
      el.style.minWidth = Math.round(w + 8) + 'px';
      el.style.minHeight = Math.round(h + 4) + 'px';
      el.style.left = '0px';
      el.style.top = '0px';
      fillPiece(el, p, base);
      screenEl.appendChild(el);
      var size = base;
      for (var k = 0; k < 10; k++) {
        var fits = p.wr ? el.offsetHeight <= h * 1.3 + 8 : el.offsetWidth <= Math.max(w * 2.2, w + 6 * size);
        if (fits || size <= base * (p.wr ? 0.6 : 0.7)) break;
        size = Math.round(size * 0.93);
        fillPiece(el, p, size);
      }
      var bw = Math.max(el.offsetWidth, w + 8), bh = Math.max(el.offsetHeight, h + 4);
      var left = p.al === 'r' ? x + w + 4 - bw : x - 4;
      el.style.left = Math.round(Math.max(0, left)) + 'px';
      el.style.top = Math.round(y + h / 2 - bh / 2) + 'px';
    }
  }

  function fillPiece(el, p, size) {
    while (el.firstChild) el.removeChild(el.firstChild);
    el.style.fontSize = size + 'px';
    var t = p.text || '', buf = '';
    for (var i = 0; i < t.length; i++) {
      var c = t.charAt(i);
      if (c === '\u0001' && i + 1 < t.length) {
        var idx = t.charCodeAt(++i) - 48;
        if (buf) { el.appendChild(document.createTextNode(buf)); buf = ''; }
        if (p.ic && p.ic[idx]) {
          var img = document.createElement('img');
          img.src = 'data:image/png;base64,' + p.ic[idx];
          img.style.height = Math.round(size * 1.15) + 'px';
          img.style.verticalAlign = 'middle';
          img.style.margin = '0 3px';
          el.appendChild(img);
        }
        continue;
      }
      buf += c;
    }
    if (buf) el.appendChild(document.createTextNode(buf));
  }

  function drawMask() {
    var c = state.mask;
    // Orijinal yalnız "yerine" yerleşiminde örtülür; üstünde/altında seçiliyse orijinal görünür kalır
    if (!c || (state.style.place || 'cover') !== 'cover' || state.muted) { maskEl.style.display = 'none'; return; }
    var pad = 4;
    maskEl.style.left = (c.x * 1920 - pad) + 'px';
    maskEl.style.top = (c.y * 1080 - pad) + 'px';
    maskEl.style.width = (c.w * 1920 + 2 * pad) + 'px';
    maskEl.style.height = (c.h * 1080 + 2 * pad) + 'px';
    maskEl.style.display = 'block';
  }

  function notice(msg, ms) {
    sub.setAttribute('data-notice', msg);
    clearTimeout(noticeTimer);
    noticeTimer = setTimeout(function () { sub.setAttribute('data-notice', ''); render(); }, ms || 2000);
    render();
  }

  function setStatus(msg) {
    if (!msg) { statusEl.style.display = 'none'; return; }
    statusEl.textContent = msg;
    statusEl.style.display = 'block';
  }

  // ───── PC bağlantısı ─────
  function connect() {
    clearTimeout(reconnectTimer);
    if (!state.server) {
      if (PLATFORM === 'overlay') { setStatus('PC adresi yok – önce Altyazı+ uygulamasını açıp ayarlayın'); return; }
      showSetup(); return;
    }
    if (ws) { try { ws.onclose = null; ws.close(); } catch (e) { } ws = null; }
    setStatus('PC\'ye bağlanılıyor… (' + state.server + ')');
    try {
      ws = new WebSocket('ws://' + state.server + ':' + PORT + '/ws');
    } catch (e) {
      scheduleReconnect();
      return;
    }
    ws.onopen = function () {
      state.connected = true;
      setStatus('');
      ws.send(JSON.stringify({ t: 'hi', client: PLATFORM, hdmi: state.hdmi }));
      notice('Altyazı+ bağlandı', 2500);
    };
    ws.onmessage = function (ev) {
      var m;
      try { m = JSON.parse(ev.data); } catch (e) { return; }
      handle(m);
    };
    ws.onclose = function () {
      if (state.connected) notice('PC bağlantısı koptu – yeniden deneniyor', 2500);
      state.connected = false;
      state.text = '';
      state.mask = null;
      drawMask();
      render();
      scheduleReconnect();
    };
    ws.onerror = function () { };
  }

  function scheduleReconnect() {
    setStatus('PC\'ye bağlanılamıyor (' + state.server + '). PC programı açık mı?');
    clearTimeout(reconnectTimer);
    reconnectTimer = setTimeout(connect, 2000);
  }

  function handle(m) {
    switch (m.t) {
      case 'sub': state.text = m.text || ''; if (m.cover) state.cover = m.cover; if (m.lh) state.lineH = m.lh; render(); break;
      case 'clear': state.text = ''; state.lastMask = state.mask; render(); break;
      case 'mask': state.mask = { x: m.x, y: m.y, w: m.w, h: m.h }; state.lastMask = state.mask; if (m.lh) state.lineH = m.lh; drawMask(); render(); break;
      case 'unmask': state.mask = null; drawMask(); if (!state.text) state.lastMask = null; render(); break;
      case 'notice': notice(m.text || '', m.ms || 2000); break;
      case 'mute': state.muted = !!m.on; render(); if (state.pieces) renderPieces(); else renderScreen(); break;
      case 'screen': state.screen = m.items || []; renderScreen(); break;
      case 'screen2': state.pieces = m.items || []; renderPieces(); break;
      case 'style':
        for (var k in m) if (k !== 't' && m.hasOwnProperty(k)) state.style[k] = m[k];
        drawMask();
        render();
        break;
      case 'hdmi': if (m.port >= 1 && m.port <= 4) setHdmi(m.port); break;
      case 'ping': try { ws.send('{"t":"pong"}'); } catch (e) { } break;
    }
  }

  // ───── LG geliştirici modu süresini TV'nin kendisi de yenilesin ─────
  // (PC kapalıyken bile: uygulama her açılışta ve açık kaldıkça 6 saatte bir, LG Developer Mode
  //  uygulamasının "Extend" düğmesinin çağırdığı adresi arka planda çağırır; ekranda bir şey açılmaz.)
  function keepDevMode(force) {
    if (PLATFORM !== 'webos') return;
    var tok = load('devToken', '');
    if (!tok) return;
    var last = parseInt(load('devExtendAt', '0'), 10) || 0, now = Date.now();
    if (!force && now - last < 6 * 3600 * 1000) return;
    try {
      var img = new Image();
      img.src = 'https://developer.lge.com/secure/ResetDevModeSession.dev?sessionToken=' + encodeURIComponent(tok) + '&_=' + now;
      save('devExtendAt', now);
    } catch (e) { }
  }
  setInterval(function () { keepDevMode(false); }, 30 * 60 * 1000);

  // ───── Başlatma parametreleri (PC "TV'de Başlat" ile gönderir) ─────
  function applyParams(p) {
    if (!p) return;
    if (typeof p === 'string') { try { p = JSON.parse(p); } catch (e) { return; } }
    if (p.server) { state.server = String(p.server); save('server', state.server); }
    if (p.hdmi) { setHdmi(parseInt(p.hdmi, 10) || 1); }
    if (p.devToken && /^[A-Za-z0-9_-]{16,256}$/.test(String(p.devToken))) { save('devToken', String(p.devToken)); keepDevMode(true); }
    hideSetup();
    connect();
  }

  // ───── Elle IP girişi (PC'den başlatılmadıysa) ─────
  function showSetup() {
    setupMode = true;
    if (state.server) {
      var parts = state.server.split('.');
      if (parts.length === 4) ipParts = parts.map(function (x) { return parseInt(x, 10) || 0; });
    }
    drawSetup();
    panel.style.display = 'block';
  }
  function hideSetup() { setupMode = false; panel.style.display = 'none'; }
  function drawSetup() {
    var html = '<h1>Altyazı+</h1>' +
      'PC programında <b>TV Modu → TV\'de Başlat</b>\'a basın.<br>Ya da PC\'nin IP adresini buraya girin:' +
      '<div class="ip" style="margin-top:18px">';
    for (var i = 0; i < 4; i++) html += '<span class="' + (i === ipSel ? 'sel' : '') + '">' + ipParts[i] + '</span>' + (i < 3 ? '.' : '');
    html += '</div><div class="hint">Sol/Sağ: bölüm seç • Yukarı/Aşağı: değiştir • Rakam tuşları: yaz • OK: bağlan<br>' +
      'HDMI girişi: ' + state.hdmi + ' (CH+/CH- ile değiştirin)</div>';
    panel.innerHTML = html;
  }

  function showInfo() {
    clearTimeout(infoTimer);
    panel.innerHTML = '<h1>Altyazı+</h1>PC: ' + (state.server || '—') + ' • ' +
      (state.connected ? '<b style="color:#4cd964">bağlı</b>' : '<b style="color:#ff5c5c">bağlı değil</b>') +
      '<br>HDMI girişi: ' + state.hdmi + ' • Yazı ölçeği: %' + Math.round(state.fontScale * 100) +
      '<div class="hint">Sol/Sağ: HDMI • Yukarı/Aşağı: yazı boyutu • ' + (PLATFORM === 'android' ? 'Menü tuşu' : 'Kırmızı tuş') +
      ': PC IP ayarı • Geri: çık' +
      (PLATFORM === 'android' ? '<br>Mavi tuş: katman modu (TV uygulama içinde HDMI göstermiyorsa)' : '') + '</div>';
    panel.style.display = 'block';
    infoTimer = setTimeout(function () { if (!setupMode) panel.style.display = 'none'; }, 4000);
  }

  // ───── Kumanda ─────
  document.addEventListener('keydown', function (e) {
    var k = e.keyCode;
    if (setupMode) {
      if (k === 37) { ipSel = (ipSel + 3) % 4; }
      else if (k === 39) { ipSel = (ipSel + 1) % 4; }
      else if (k === 38) { ipParts[ipSel] = (ipParts[ipSel] + 1) % 256; }
      else if (k === 40) { ipParts[ipSel] = (ipParts[ipSel] + 255) % 256; }
      else if (k >= 48 && k <= 57) {
        var v = ipParts[ipSel] * 10 + (k - 48);
        ipParts[ipSel] = v > 255 ? (k - 48) : v;
      }
      else if (k === 33 || k === 427) { setHdmi(state.hdmi % 4 + 1); }
      else if (k === 34 || k === 428) { setHdmi((state.hdmi + 2) % 4 + 1); }
      else if (k === 13) { state.server = ipParts.join('.'); save('server', state.server); hideSetup(); connect(); return; }
      else if (k === KEY_BACK) { if (state.server) { hideSetup(); e.preventDefault(); return; } exitApp(); return; }
      drawSetup();
      e.preventDefault();
      return;
    }
    switch (k) {
      case 37: setHdmi((state.hdmi + 2) % 4 + 1); notice('HDMI ' + state.hdmi); break;   // sol
      case 39: setHdmi(state.hdmi % 4 + 1); notice('HDMI ' + state.hdmi); break;         // sağ
      case 38: state.fontScale = Math.min(2, state.fontScale + 0.1); save('fontScale', state.fontScale);
        notice('Yazı %' + Math.round(state.fontScale * 100)); break;
      case 40: state.fontScale = Math.max(0.5, state.fontScale - 0.1); save('fontScale', state.fontScale);
        notice('Yazı %' + Math.round(state.fontScale * 100)); break;
      case 13: showInfo(); break;
      case 403: showSetup(); break;    // kırmızı
      default:
        if (k === KEY_BACK) { exitApp(); break; }
        return;
    }
    e.preventDefault();
  });

  document.addEventListener('webOSRelaunch', function (ev) { applyParams(ev.detail); }, true);

  // Android köprüsü: yerel uygulama tuşları ve PC adresini (ağda otomatik bulunca) buradan iletir
  window.ps5trApply = function (p) { applyParams(p); };
  // Android: PC ağda bulundu (PC programı duyuru yapar) → adres kayıtlı değilse ya da bağlı değilsek kullan
  window.ps5trDiscovered = function (ip) {
    if (!/^\d+\.\d+\.\d+\.\d+$/.test(ip)) return;
    if (!state.server || (!state.connected && state.server !== ip)) {
      state.server = ip; save('server', ip);
      if (setupMode) hideSetup();
      notice('PC bulundu: ' + ip, 2500);
      connect();
    }
  };
  // Android: TV uygulama içinde HDMI göstermiyor → katman modunu öner
  window.ps5trNoHdmi = function () {
    notice('Bu TV uygulama içinde HDMI göstermiyor. MAVİ tuş: katman modu (altyazı TV\'nin HDMI ekranının üstünde)', 8000);
  };
  window.ps5trKey = function (name) {
    if (name === 'menu') { if (setupMode) hideSetup(); else showSetup(); }
    else if (name === 'back') { if (setupMode && state.server) hideSetup(); else exitApp(); }
    else if (name === 'chup') setHdmi(state.hdmi % 4 + 1);
    else if (name === 'chdown') setHdmi((state.hdmi + 2) % 4 + 1);
  };

  // ───── Başlangıç ─────
  setHdmi(state.hdmi);
  keepDevMode(false);
  var params = null;
  try { params = window.PalmSystem && window.PalmSystem.launchParams ? JSON.parse(window.PalmSystem.launchParams) : null; } catch (e) { }
  if (params && params.server) applyParams(params);
  else connect();
})();
