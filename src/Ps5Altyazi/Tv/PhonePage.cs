namespace Ps5Altyazi.Tv;

/// <summary>Telefon / tablet / tarayıcı için altyazı sayfası (http://PC-IP:47800/).</summary>
internal static class PhonePage
{
    public const string Html = """
<!DOCTYPE html>
<html lang="tr">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<meta name="theme-color" content="#000000">
<title>Altyazı+</title>
<style>
  html, body { margin: 0; height: 100%; background: #000; color: #fff; font-family: system-ui, "Segoe UI", Roboto, Arial, sans-serif; }
  body { display: flex; align-items: center; justify-content: center; text-align: center; }
  #sub { font-weight: 700; font-size: 7vw; line-height: 1.25; padding: 4vw; text-shadow: 0 2px 6px #000; }
  #sub.notice { color: #ffd400; font-size: 4.5vw; }
  #st { position: fixed; top: 8px; right: 12px; font-size: 13px; color: #888; }
  @media (orientation: portrait) { #sub { font-size: 9vw; } }
</style>
</head>
<body>
<div id="sub">Altyazı+<br><small style="font-size:4vw;color:#9aa0a6">Altyazı bekleniyor…</small></div>
<div id="st">bağlanıyor…</div>
<script>
(function () {
  var sub = document.getElementById('sub'), st = document.getElementById('st');
  var text = '', muted = false, noticeTimer = null, ws = null;
  function show(t, isNotice) { sub.className = isNotice ? 'notice' : ''; sub.textContent = t; }
  function render() { show(text && !muted ? text : ''); }
  function connect() {
    ws = new WebSocket('ws://' + location.host + '/ws');
    ws.onopen = function () { st.textContent = 'bağlı'; ws.send(JSON.stringify({ t: 'hi', client: 'tarayıcı' })); };
    ws.onclose = function () { st.textContent = 'bağlantı yok – yeniden deneniyor'; setTimeout(connect, 2000); };
    ws.onmessage = function (e) {
      var m; try { m = JSON.parse(e.data); } catch (x) { return; }
      if (m.t === 'sub') { text = m.text || ''; render(); }
      else if (m.t === 'clear') { text = ''; render(); }
      else if (m.t === 'mute') { muted = !!m.on; render(); }
      else if (m.t === 'notice') { show(m.text, true); clearTimeout(noticeTimer); noticeTimer = setTimeout(render, m.ms || 2000); }
      else if (m.t === 'ping') { try { ws.send('{"t":"pong"}'); } catch (x) {} }
    };
  }
  // Ekran kararmasın (destekleyen tarayıcılarda)
  try { if (navigator.wakeLock) navigator.wakeLock.request('screen'); } catch (e) {}
  connect();
})();
</script>
</body>
</html>
""";
}
