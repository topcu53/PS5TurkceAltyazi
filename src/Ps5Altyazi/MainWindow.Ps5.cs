// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Windows;
using Ps5Altyazi.Capture;
using Ps5Altyazi.Logging;
using Ps5Altyazi.RemotePlayLink;
using RP = CouchSubs.RemotePlay;

namespace Ps5Altyazi;

/// <summary>
/// PS5'e doğrudan bağlantı (Remote Play uygulaması olmadan): görüntü kaynağı olarak pencere yerine PS5'in kendisi.
/// Yöntem CouchSubs'tan (github.com/yorulmazsinan/couchsubs), Remote Play protokolü chiaki-ng'den.
/// </summary>
public partial class MainWindow
{
    /// <summary>Doğrudan bağlantı akıyor mu (bağlanırken "kare gelmiyor" uyarısı verilmesin).</summary>
    private volatile bool _directStreaming;

    /// <summary>Görüntü PS5'ten doğrudan mı alınacak (ayar açık, kütüphane var, hesap ve PS5 eşlenmiş).</summary>
    private bool UseDirect() => _settings.DirectConnect && _directReady;

    /// <summary>Kütüphane var, hesap ve PS5 eşlenmiş (UpdateSourceUi'de güncellenir; zamanlayıcı her yarım saniyede sorar).</summary>
    private bool _directReady;

    private bool DirectRunning => _capture is Ps5DirectCaptureSource;

    /// <summary>KAYNAK satırı: doğrudan bağlantı hazırsa onay kutusu görünür, açıkken pencere seçimi gizlenir.</summary>
    private void UpdateSourceUi()
    {
        bool ready = _directReady = Ps5Link.IsReady(_settings);
        DirectCheck.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        _updatingDirectCheck = true;
        DirectCheck.IsChecked = _settings.DirectConnect && ready;
        _updatingDirectCheck = false;
        WindowSourcePanel.Visibility = UseDirect() ? Visibility.Collapsed : Visibility.Visible;
        if (UseDirect() && !DirectRunning)
        {
            var c = Ps5Link.LoadConsole(_settings);
            SetText(RemotePlayStatus, $"PS5'e doğrudan bağlantı hazır – {c?.Name} ({c?.Host})", Neutral);
        }
    }

    private bool _updatingDirectCheck;

    private void DirectCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingDirectCheck || !_settingsReady) return;
        bool on = DirectCheck.IsChecked == true;
        if (_settings.DirectConnect == on) return;
        _settings.DirectConnect = on;
        SaveSettings();
        bool wasRunning = _capture != null;
        if (wasRunning) StopCapture(on ? "Kaynak: PS5'e doğrudan bağlantı seçildi." : "Kaynak: PS Remote Play penceresi seçildi.");
        UpdateSourceUi();
        Log.Info(on ? "Görüntü kaynağı: PS5'e doğrudan bağlantı (Remote Play uygulaması gerekmez)." : "Görüntü kaynağı: PS Remote Play penceresi.");
        if (!on) RefreshWindowList();
        if (wasRunning) StartCapture();
    }

    private void Ps5Connect_Click(object sender, RoutedEventArgs e)
    {
        // Aynı anda tek Remote Play bağlantısı olur: ayarlar (ve "Bağlantıyı dene") için çalışan bağlantı kapatılır.
        bool wasRunning = DirectRunning;
        if (wasRunning) StopCapture("PS5 Bağlantısı penceresi açıldı.");
        var dlg = new Ps5ConnectWindow(_settings, SaveSettings) { Owner = this };
        dlg.ShowDialog();
        UpdateSourceUi();
        if (UseDirect() && (wasRunning || (_capture == null && !_userStopped && _settings.AutoStart)))
        {
            if (_capture != null) StopCapture("Kaynak değişti: PS5'e doğrudan bağlantı.");
            StartCapture();
        }
    }

    /// <summary>PS5'e doğrudan bağlanarak yakalamayı başlatır (ekranda pencere açılmaz).</summary>
    private void StartDirectCapture()
    {
        StopCapture(null);
        var console = Ps5Link.LoadConsole(_settings);
        byte[]? account = Ps5Link.AccountId(_settings);
        if (console == null || account == null)
        {
            Log.Warn("PS5'e doğrudan bağlantı kurulmamış. '🎮 PS5 Bağlantısı' ile kurun.");
            return;
        }

        var src = new Ps5DirectCaptureSource(console, account, Ps5Link.LoadPasscode(_settings));
        src.FrameArrived += OnFrame;
        src.StateChanged += st => Dispatcher.InvokeAsync(() =>
        {
            if (!ReferenceEquals(_capture, src)) return;
            switch (st.Phase)
            {
                case RP.SessionPhase.Connecting:
                    _directStreaming = false;
                    SetText(RemotePlayStatus, $"PS5'e bağlanılıyor – {src.ConsoleName}", Orange);
                    SetText(CaptureStatus, "PS5'e bağlanılıyor…", Orange);
                    break;
                case RP.SessionPhase.Streaming:
                    _directStreaming = true;
                    _lastFrameTime = DateTime.MinValue;
                    _captureStart = DateTime.Now;
                    SetText(RemotePlayStatus, $"BAĞLI (doğrudan) – {src.ConsoleName}", Green);
                    Log.Success($"PS5'e doğrudan bağlanıldı: {src.ConsoleName}. Remote Play uygulaması gerekmiyor.");
                    break;
                default:
                    _directStreaming = false;
                    string why = Ps5Link.Describe(st.Reason);
                    SetText(RemotePlayStatus, "Bağlantı koptu – yeniden deneniyor", Orange);
                    if (st.Reason?.Kind != RP.DisconnectKind.Stopped) Log.Warn("PS5 bağlantısı: " + why);
                    break;
            }
        });
        src.ConsoleMoved += moved => Dispatcher.InvokeAsync(() =>
        {
            Ps5Link.SaveConsole(_settings, moved);
            SaveSettings();
            Log.Info($"PS5 yeni adreste bulundu: {moved.Host}");
        });
        src.Stopped += reason => Dispatcher.InvokeAsync(() =>
        {
            if (!ReferenceEquals(_capture, src)) return;
            Log.Warn("PS5 bağlantısı durdu: " + reason);
            StopCapture(null);
            SetText(RemotePlayStatus, "Bağlantı durdu – ayrıntı logda", Red);
            Notify("PS5 bağlantısı durdu: " + reason);
        });

        _cropHwnd = IntPtr.Zero;
        try
        {
            src.Start(IntPtr.Zero, SelectedFps());
        }
        catch (Exception ex)
        {
            src.FrameArrived -= OnFrame;
            src.Dispose();
            Log.Error($"PS5'e doğrudan bağlantı başlatılamadı: {ex.GetType().Name}: {ex.Message}");
            SetText(CaptureStatus, "BAŞLATILAMADI – ayrıntı logda", Red);
            return;
        }

        _capture = src;
        _target = null;
        _directStreaming = false;
        ResetSessionStats();
        StartStopButton.Content = "■ Durdur (F6)";
        SetText(CaptureStatus, "PS5'e bağlanılıyor…", Orange);
        SetText(RemotePlayStatus, $"PS5'e bağlanılıyor – {console.Name} ({console.Host})", Orange);
        Log.Info($"Yakalama başladı → PS5'e doğrudan bağlantı: {console.Name} ({console.Host}). Ekranda pencere açılmaz; " +
                 "PS5 dinlenmedeyse uyandırılır.");
    }
}
