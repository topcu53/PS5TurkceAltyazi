package tr.altyaziplus.tv;

import android.annotation.SuppressLint;
import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.graphics.Color;
import android.media.tv.TvContract;
import android.media.tv.TvInputInfo;
import android.media.tv.TvInputManager;
import android.media.tv.TvView;
import android.net.Uri;
import android.net.wifi.WifiManager;
import android.os.Build;
import android.os.Bundle;
import android.provider.Settings;
import android.view.KeyEvent;
import android.view.View;
import android.view.ViewGroup;
import android.webkit.JavascriptInterface;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.FrameLayout;
import android.widget.Toast;

import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.List;
import java.util.Locale;

/**
 * Altyazı+ – Android TV / Google TV.
 * Yapımcı: Burak Topcu.
 *
 * Ekran düzeni: arkada TvView (PS5'in bağlı olduğu HDMI girişi, "passthrough"), önde saydam WebView
 * (LG / Samsung ile ortak arayüz: web/index.html + web/app.js). WebView, PC'deki Altyazı+ programına
 * WebSocket ile bağlanır ve Türkçe altyazıyı / tuş ipuçlarını çizer.
 *
 * TV, uygulama içinde HDMI göstermeye izin vermiyorsa "Katman modu" kullanılır (OverlayService):
 * altyazı, TV'nin kendi HDMI ekranının üstünde gösterilir.
 */
public class MainActivity extends Activity {

    static final int DISCOVERY_PORT = 47801;

    private TvView tvView;
    private WebView web;
    private volatile boolean running;
    private DatagramSocket discoverySocket;
    private WifiManager.MulticastLock multicastLock;

    @SuppressLint({"SetJavaScriptEnabled", "AddJavascriptInterface"})
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        FrameLayout root = new FrameLayout(this);
        root.setBackgroundColor(Color.BLACK);

        tvView = new TvView(this);
        root.addView(tvView, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        web = new WebView(this);
        web.setBackgroundColor(Color.TRANSPARENT);
        web.setLayerType(View.LAYER_TYPE_HARDWARE, null);
        WebSettings ws = web.getSettings();
        ws.setJavaScriptEnabled(true);
        ws.setDomStorageEnabled(true);
        ws.setMediaPlaybackRequiresUserGesture(false);
        ws.setAllowFileAccess(true);
        web.setWebViewClient(new WebViewClient());
        web.addJavascriptInterface(new Bridge(), "AndroidTv");
        web.setFocusable(true);
        root.addView(web, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        setContentView(root);
        // Uygulama açıkken TV ekran koruyucuya / uykuya geçmesin (oyun oynarken kumandaya dokunulmuyor)
        getWindow().addFlags(android.view.WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        web.loadUrl("file:///android_asset/index.html");
        web.requestFocus();
    }

    @Override
    protected void onResume() {
        super.onResume();
        stopService(new Intent(this, OverlayService.class)); // uygulama açıkken katmana gerek yok
        startDiscovery();
    }

    @Override
    protected void onPause() {
        super.onPause();
        stopDiscovery();
        try { tvView.reset(); } catch (Exception ignored) { }
    }

    @Override
    protected void onDestroy() {
        try { web.destroy(); } catch (Exception ignored) { }
        super.onDestroy();
    }

    // ───────── HDMI ─────────

    /** n. HDMI girişini (1..4) TvView'da açar. Bulunamazsa false. */
    private boolean tuneHdmi(int n) {
        try {
            TvInputManager tim = (TvInputManager) getSystemService(Context.TV_INPUT_SERVICE);
            if (tim == null) return false;
            List<TvInputInfo> hdmi = new ArrayList<>();
            for (TvInputInfo info : tim.getTvInputList()) {
                if (info.getType() == TvInputInfo.TYPE_HDMI && info.getParentId() == null) hdmi.add(info);
            }
            if (hdmi.isEmpty()) return false;

            // Önce adında "HDMI n" geçen girişi ara; yoksa sıraya göre
            TvInputInfo pick = null;
            for (TvInputInfo info : hdmi) {
                CharSequence label = info.loadLabel(this);
                String l = label == null ? "" : label.toString().toLowerCase(Locale.ROOT).replace(" ", "");
                if (l.contains("hdmi" + n)) { pick = info; break; }
            }
            if (pick == null) {
                Collections.sort(hdmi, new Comparator<TvInputInfo>() {
                    @Override public int compare(TvInputInfo a, TvInputInfo b) { return a.getId().compareTo(b.getId()); }
                });
                pick = hdmi.get(Math.max(0, Math.min(hdmi.size() - 1, n - 1)));
            }
            Uri channel = TvContract.buildChannelUriForPassthroughInput(pick.getId());
            tvView.tune(pick.getId(), channel);
            return true;
        } catch (Exception e) {
            return false;
        }
    }

    /** Katman modu: altyazı TV'nin kendi HDMI ekranının üstünde (izin gerekir). */
    private void startOverlayMode() {
        if (Build.VERSION.SDK_INT >= 23 && !Settings.canDrawOverlays(this)) {
            Toast.makeText(this, "Katman izni gerekli: Ayarlar → Uygulamalar → Altyazı+ → Diğer uygulamaların üzerinde göster", Toast.LENGTH_LONG).show();
            try {
                startActivity(new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION, Uri.parse("package:" + getPackageName())));
            } catch (Exception e) {
                Toast.makeText(this, "Bu TV izin ekranını açmıyor. Kurulum kılavuzundaki adb komutunu kullanın.", Toast.LENGTH_LONG).show();
            }
            return;
        }
        Intent i = new Intent(this, OverlayService.class);
        if (Build.VERSION.SDK_INT >= 26) startForegroundService(i); else startService(i);
        Toast.makeText(this, "Katman modu açık: şimdi TV'nin kumandasıyla PS5'in HDMI girişine geçin.", Toast.LENGTH_LONG).show();
        moveTaskToBack(true);
    }

    // ───────── PC'yi ağda otomatik bulma (PC programı 2 sn'de bir duyurur) ─────────

    private void startDiscovery() {
        if (running) return;
        running = true;
        try {
            WifiManager wm = (WifiManager) getApplicationContext().getSystemService(Context.WIFI_SERVICE);
            if (wm != null) { multicastLock = wm.createMulticastLock("altyaziplus"); multicastLock.acquire(); }
        } catch (Exception ignored) { }
        Thread t = new Thread(new Runnable() {
            @Override public void run() {
                try {
                    discoverySocket = new DatagramSocket(null);
                    discoverySocket.setReuseAddress(true);
                    discoverySocket.bind(new java.net.InetSocketAddress(DISCOVERY_PORT));
                    byte[] buf = new byte[256];
                    while (running) {
                        DatagramPacket p = new DatagramPacket(buf, buf.length);
                        discoverySocket.receive(p);
                        String msg = new String(p.getData(), 0, p.getLength(), StandardCharsets.UTF_8).trim();
                        // Biçim: "ALTYAZIPLUS 1 <ip>" – yalnızca mesajı gönderen adres kabul edilir
                        if (!msg.startsWith("ALTYAZIPLUS ")) continue;
                        InetAddress from = p.getAddress();
                        if (from == null || !from.isSiteLocalAddress()) continue;
                        final String ip = from.getHostAddress();
                        runOnUiThread(new Runnable() {
                            @Override public void run() {
                                web.evaluateJavascript("window.ps5trDiscovered && window.ps5trDiscovered('" + ip.replaceAll("[^0-9.]", "") + "')", null);
                            }
                        });
                    }
                } catch (Exception ignored) {
                } finally {
                    try { if (discoverySocket != null) discoverySocket.close(); } catch (Exception ignored) { }
                }
            }
        }, "discovery");
        t.setDaemon(true);
        t.start();
    }

    private void stopDiscovery() {
        running = false;
        try { if (discoverySocket != null) discoverySocket.close(); } catch (Exception ignored) { }
        try { if (multicastLock != null && multicastLock.isHeld()) multicastLock.release(); } catch (Exception ignored) { }
    }

    // ───────── Kumanda ─────────

    private void js(String key) {
        web.evaluateJavascript("window.ps5trKey && window.ps5trKey('" + key + "')", null);
    }

    @Override
    public boolean dispatchKeyEvent(KeyEvent e) {
        if (e.getAction() == KeyEvent.ACTION_DOWN) {
            switch (e.getKeyCode()) {
                case KeyEvent.KEYCODE_BACK: js("back"); return true;
                case KeyEvent.KEYCODE_MENU:
                case KeyEvent.KEYCODE_PROG_RED:
                case KeyEvent.KEYCODE_SETTINGS: js("menu"); return true;
                case KeyEvent.KEYCODE_CHANNEL_UP: js("chup"); return true;
                case KeyEvent.KEYCODE_CHANNEL_DOWN: js("chdown"); return true;
                case KeyEvent.KEYCODE_PROG_BLUE: startOverlayMode(); return true;
                default: break;
            }
        }
        return super.dispatchKeyEvent(e);
    }

    // ───────── JavaScript köprüsü (window.AndroidTv) ─────────

    private class Bridge {
        @JavascriptInterface
        public void setHdmi(final int n) {
            runOnUiThread(new Runnable() {
                @Override public void run() {
                    if (!tuneHdmi(n)) {
                        web.evaluateJavascript("window.ps5trNoHdmi && window.ps5trNoHdmi()", null);
                    }
                }
            });
        }

        @JavascriptInterface
        public void overlayMode() {
            runOnUiThread(new Runnable() { @Override public void run() { startOverlayMode(); } });
        }

        @JavascriptInterface
        public void exit() {
            runOnUiThread(new Runnable() { @Override public void run() { finish(); } });
        }
    }
}
