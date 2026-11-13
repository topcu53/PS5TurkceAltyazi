package tr.altyaziplus.tv;

import android.annotation.SuppressLint;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.PixelFormat;
import android.os.Build;
import android.os.IBinder;
import android.view.Gravity;
import android.view.WindowManager;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;

/**
 * Katman modu (Altyazı+ – Burak Topcu): TV, uygulama içinde HDMI göstermiyorsa altyazı arayüzü
 * TV'nin kendi HDMI ekranının ÜSTÜNDE, dokunulamaz ve odak almayan saydam bir pencerede çalışır.
 * Kapatmak için uygulamayı yeniden açın (açılınca katman kendiliğinden kapanır).
 */
public class OverlayService extends Service {

    private static final String CHANNEL = "altyaziplus_overlay";
    private WebView web;

    @Override
    public IBinder onBind(Intent intent) { return null; }

    @SuppressLint("SetJavaScriptEnabled")
    @Override
    public void onCreate() {
        super.onCreate();
        startInForeground();

        web = new WebView(this);
        web.setBackgroundColor(Color.TRANSPARENT);
        WebSettings ws = web.getSettings();
        ws.setJavaScriptEnabled(true);
        ws.setDomStorageEnabled(true);
        ws.setAllowFileAccess(true);
        web.setWebViewClient(new WebViewClient());
        web.loadUrl("file:///android_asset/index.html#overlay");

        int type = Build.VERSION.SDK_INT >= 26
                ? WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY
                : WindowManager.LayoutParams.TYPE_PHONE;
        WindowManager.LayoutParams lp = new WindowManager.LayoutParams(
                WindowManager.LayoutParams.MATCH_PARENT,
                WindowManager.LayoutParams.MATCH_PARENT,
                type,
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE
                        | WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE
                        | WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,
                PixelFormat.TRANSLUCENT);
        lp.gravity = Gravity.TOP | Gravity.START;
        try {
            ((WindowManager) getSystemService(Context.WINDOW_SERVICE)).addView(web, lp);
        } catch (Exception e) {
            stopSelf();
        }
    }

    private void startInForeground() {
        Notification.Builder b;
        if (Build.VERSION.SDK_INT >= 26) {
            NotificationManager nm = (NotificationManager) getSystemService(Context.NOTIFICATION_SERVICE);
            if (nm != null) nm.createNotificationChannel(new NotificationChannel(CHANNEL, "Altyazı+ katmanı", NotificationManager.IMPORTANCE_LOW));
            b = new Notification.Builder(this, CHANNEL);
        } else {
            b = new Notification.Builder(this);
        }
        Notification n = b.setContentTitle("Altyazı+")
                .setContentText("Türkçe altyazı katmanı açık")
                .setSmallIcon(android.R.drawable.ic_menu_info_details)
                .build();
        startForeground(1, n);
    }

    @Override
    public void onDestroy() {
        try { ((WindowManager) getSystemService(Context.WINDOW_SERVICE)).removeView(web); } catch (Exception ignored) { }
        try { web.destroy(); } catch (Exception ignored) { }
        super.onDestroy();
    }
}
