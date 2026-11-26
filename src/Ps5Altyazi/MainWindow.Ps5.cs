using System;
using System.Windows;
using Ps5Altyazi.Capture;
using Ps5Altyazi.Logging;
using Ps5Altyazi.Ps5;

namespace Ps5Altyazi;

/// <summary>
/// PS5'e doğrudan bağlantı (PS Remote Play uygulaması olmadan): görüntü kaynağı olarak pencere yerine PS5'in kendisi.
/// </summary>
public partial class MainWindow
{
    private volatile bool _directStreaming;
    private bool _directReady;
    private bool _updatingDirectCheck;

    /// <summary>Görüntü PS5'ten doğrudan mı alınacak (ayar açık; yardımcı, hesap ve PS5 hazır).</summary>
    private bool UseDirect() => _settings.DirectConnect && _directReady;

    private bool DirectRunning => _capture is Ps5DirectCaptureSource;

    /// <summary>KAYNAK satırı: doğrudan bağlantı hazırsa onay kutusu görünür; açıkken Remote Play pencere seçimi gizlenir.</summary>
    private void UpdateSourceUi()
    {
        _directReady = Ps5Store.IsReady(_settings);
        DirectCheck.Visibility = _directReady ? Visibility.Visible : Visibility.Collapsed;
        _updatingDirectCheck = true;
        DirectCheck.IsChecked = _settings.DirectConnect && _directReady;
        _updatingDirectCheck = false;
        // Pencereden okuma (eski yöntem) yalnız doğrudan bağlantı kurulu olup kullanıcı onu kapattıysa gösterilir.
        WindowSourcePanel.Visibility = _directReady && !_settings.DirectConnect ? Visibility.Visible : Visibility.Collapsed;
        if (UseDirect() && !DirectRunning)
        {
            var p = Ps5Store.Pairing(_settings);
            SetText(RemotePlayStatus, $"PS5'e doğrudan bağlantı hazır – {p?.Name} ({p?.Host})", Neutral);
        }
        else if (!_directReady)
            SetText(RemotePlayStatus, "PS5 bağlantısı kurulmadı – '🎮 PS5 Bağlantısı'", Orange);
    }

    /// <summary>PS5 bağlantısı kurulmamışken başlatılmak istendi: kurulum sihirbazı açılır.</summary>
    private void OfferPs5Setup()
    {
        var answer = MessageBox.Show(this,
            "PS5 bağlantısı henüz kurulmadı.\n\nKurulum adımları (PSN girişi, PS5 eşleme, TV) şimdi açılsın mı?",
            "Altyazı+ – Kurulum", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (answer == MessageBoxResult.Yes) RunSetupWizard();
    }

    private void DirectCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingDirectCheck || !_settingsReady) return;
        bool on = DirectCheck.IsChecked == true;
        if (_settings.DirectConnect == on) return;
        _settings.DirectConnect = on;
        SaveSettings();
        bool wasRunning = _capture != null;
        if (wasRunning) StopCapture(on ? "Kaynak: PS5'e doğrudan bağlantı." : "Kaynak: PS Remote Play penceresi.");
        UpdateSourceUi();
        Log.Info(on ? "Görüntü kaynağı: PS5'e doğrudan bağlantı (PS Remote Play gerekmez)." : "Görüntü kaynağı: PS Remote Play penceresi (eski yöntem).");
        if (!on) RefreshWindowList();
        if (wasRunning) StartCapture();
    }

    private void Ps5Connect_Click(object sender, RoutedEventArgs e)
    {
        // PS5 aynı anda tek bağlantı kabul eder: ayarlar ve "Bağlantıyı dene" için çalışan bağlantı kapatılır.
        bool wasRunning = DirectRunning;
        if (wasRunning) StopCapture("PS5 Bağlantısı penceresi açıldı.");
        new Ps5ConnectWindow(_settings, SaveSettings) { Owner = this }.ShowDialog();
        UpdateSourceUi();
        if (UseDirect() && (wasRunning || (_capture == null && !_userStopped && _settings.AutoStart)))
        {
            if (_capture != null) StopCapture("Kaynak: PS5'e doğrudan bağlantı.");
            StartCapture();
        }
    }

    private void StartDirectCapture()
    {
        StopCapture(null);
        var pairing = Ps5Store.Pairing(_settings);
        byte[]? account = Ps5Store.AccountId(_settings);
        if (pairing == null || account == null)
        {
            Log.Warn("PS5 bağlantısı kurulmamış. '🎮 PS5 Bağlantısı' ile kurun.");
            return;
        }

        var src = new Ps5DirectCaptureSource(pairing, account, Ps5Store.Passcode(_settings), Math.Min(15, SelectedFps()));
        src.FrameArrived += OnFrame;
        src.StateChanged += (state, text) => Dispatcher.InvokeAsync(() =>
        {
            if (!ReferenceEquals(_capture, src)) return;
            SetPs5Pill(state, text);
            bool streaming = state == Ps5State.Streaming;
            if (streaming && !_directStreaming)
            {
                _lastFrameTime = DateTime.MinValue;
                _captureStart = DateTime.Now;
                Log.Success($"PS5'e doğrudan bağlanıldı: {src.ConsoleName}.");
                _ = DetectRunningGameAsync();
            }
            _directStreaming = streaming;
            SetText(RemotePlayStatus, text, streaming ? Green : Orange);
            if (!streaming) SetText(CaptureStatus, state == Ps5State.Retrying ? "Bağlantı bekleniyor…" : "PS5'e bağlanılıyor…", Orange);
            if (state == Ps5State.Retrying) Log.Warn("PS5: " + text);
        });
        src.Moved += p => Dispatcher.InvokeAsync(() =>
        {
            Ps5Store.SavePairing(_settings, p);
            SaveSettings();
            Log.Info($"PS5 yeni adreste bulundu: {p.Host}");
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
        try { src.Start(IntPtr.Zero, SelectedFps()); }
        catch (Exception ex)
        {
            src.FrameArrived -= OnFrame;
            src.Dispose();
            Log.Error($"PS5'e doğrudan bağlantı başlatılamadı: {ex.Message}");
            SetText(CaptureStatus, "BAŞLATILAMADI – ayrıntı logda", Red);
            return;
        }

        _capture = src;
        _target = null;
        _directStreaming = false;
        _ps5Level = 1;
        _ps5Text = "bağlanıyor…";
        ResetSessionStats();
        StartStopButton.Content = "■   Çeviriyi Durdur";
        SetText(CaptureStatus, "PS5'e bağlanılıyor…", Orange);
        SetText(RemotePlayStatus, $"PS5'e bağlanılıyor – {pairing.Name} ({pairing.Host})", Orange);
        Log.Info($"Yakalama başladı → PS5'e doğrudan bağlantı: {pairing.Name} ({pairing.Host}). Ekranda pencere açılmaz; " +
                 "PS5 dinlenmedeyse uyandırılır. Bağlantı günlüğü: " + Ps5Helper.LogPath);
    }
}
