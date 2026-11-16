using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Ps5Altyazi.Capture;
using Ps5Altyazi.Logging;
using Ps5Altyazi.Settings;
using Ps5Altyazi.TextRecognition;
using Ps5Altyazi.Translation;
using Ps5Altyazi.Tv;
using System.Net.Http;
using System.Windows.Interop;
using Ps5Altyazi.Vision;

namespace Ps5Altyazi;

public partial class MainWindow : Window
{
    private static readonly Brush Green = new SolidColorBrush(Color.FromRgb(0x4C, 0xD9, 0x64));
    private static readonly Brush Red = new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x5C));
    private static readonly Brush Orange = new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x3B));
    private static readonly Brush Neutral = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));
    private static readonly Brush Gray = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA6));
    private static readonly Brush LineBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xD9, 0x64));

    /// <summary>Arka plan iş parçacığında hazırlanan, arayüze aktarılan kare sonucu.</summary>
    private sealed record FrameResult(
        CapturedFrame Game,
        PixelRect GameRect,
        int RawWidth,
        int RawHeight,
        FrameAnalysis Analysis,
        PixelRect? RegionPx,
        CapturedFrame? RegionCrop,
        byte[]? MaskPixels,
        TextScan? Presence,
        AutoDetectResult? Auto,
        bool AutoTried,
        bool[]? Fingerprint,
        double ProcMs);

    private sealed record RegionHolder(NormRect Rect);

    private readonly FrameAnalyzer _analyzer = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private AppSettings _settings = new();
    private bool _settingsReady;

    private ICaptureSource? _capture;
    private WindowInfo? _target;
    private IntPtr _cropHwnd;
    private volatile bool _cropToGame = true;
    private volatile RegionHolder? _region;
    private volatile bool _autoDetectActive;
    private DateTime _autoDetectEnd;

    private WriteableBitmap? _preview, _regionWb, _maskWb;
    private int _uiPending;
    private long _dropped;

    private FrameResult? _last;
    private CapturedFrame? _lastGoodFrame;
    private DateTime _lastFrameTime;
    private DateTime _captureStart;
    private FrameVerdict? _lastVerdict;
    private bool _noFrameWarned, _userStopped, _minimizedWarned;
    private DateTime _lastSummary, _lastRpScan;
    private PixelRect? _lastGameRect;

    private int _fpsCount;
    private DateTime _lastPreviewDraw;
    private string? _lastSkippedText;
    private DateTime _fpsStart = DateTime.Now;
    private double _fps;
    private long _total, _valid, _black, _uniform, _frozen;

    // Altyazı var/yok durumu (titremeyi önlemek için art arda 2 kare şartı)
    private bool _subPresent;
    private int _presentStreak, _absentStreak;
    private long _subAppearances;
    private DateTime _subSince;

    // OCR (Aşama 3)
    private ISubtitleOcr? _ocr;
    private bool _ocrBusy;
    private (CapturedFrame Game, List<TextLine> Lines, long Seq, DateTime ChangeStart)? _pendingOcr;
    private long _ocrSeq, _appliedSeq;
    private bool[]? _prevFp, _lastOcrFp;
    private DateTime? _changeStart;
    private string? _currentText;
    private DateTime _currentSince;
    private OcrInput? _lastOcrInput;
    private long _ocrCount, _ocrSkipped, _committedCount;
    private double _ocrMsSum, _latencySum;

    // Çeviri (Aşama 4)
    private TranslationService? _translation;
    private string? _deepLKey, _azureKey, _geminiKey;
    private string _lastLearnedText = "";
    private CancellationTokenSource? _trCts;
    private long _trSeq;
    private readonly List<string> _recentEnglish = new();
    private long _trCount, _trErrors;
    private double _trTotalLatencySum;

    // Altyazı penceresi (Aşama 5)
    private SubtitleWindow? _subWin;

    // TV / ağ yayını (Aşama 8)
    private SubtitleServer? _server;
    private double[]? _lastCoverNorm;

    // Alan seçimi
    private bool _selecting, _dragging;
    private Point _dragStart;

    // Doğrulama testi
    private bool _verifying;
    private DateTime _verifyStart, _verifyEnd;
    private long _vTotal, _vValid, _vBlack, _vUniform, _vFrozen;
    private double _vMaxMotion, _vProcSum;

    public MainWindow()
    {
        InitializeComponent();
        _hideTimer.Tick += HideTimer_Tick;
        Log.Written += OnLogWritten;
        _timer.Tick += Timer_Tick;
    }

    // ───────────────────────────── Başlangıç / kapanış ─────────────────────────────

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Log.Info("══════ Altyazı+ v1.12 – PS5 oyunları için canlı Türkçe çeviri • Yapımcı: Burak Topcu ══════");
        Log.Info($"İşletim sistemi: {Environment.OSVersion} | .NET {Environment.Version} | 64 bit: {Environment.Is64BitProcess}");
        Log.Info($"Log dosyası: {Log.CurrentFile}");

        _settings = AppSettings.Load();
        MigrateSettings();
        MethodCombo.SelectedIndex = Math.Clamp(_settings.CaptureMethod, 0, 1);
        FpsCombo.SelectedIndex = Math.Max(0, new[] { 5, 10, 15, 30, 60 }.ToList().IndexOf(_settings.Fps));
        CropCheck.IsChecked = _settings.CropToGame;
        _cropToGame = _settings.CropToGame;
        var saved = _settings.GetRegion();
        _region = saved is { } r ? new RegionHolder(r) : null;
        if (saved != null)
            Log.Success($"Kayıtlı altyazı alanı yüklendi ({_settings.SubtitleRegionSource ?? "?"}, {_settings.SubtitleRegionUpdated:dd.MM.yyyy HH:mm}): {saved}");
        else
            Log.Info("Kayıtlı altyazı alanı yok. 'Elle Seç' veya 'Otomatik Bul' ile belirleyin.");
        _settingsReady = true;
        UpdateRegionText();

        InitOcr();
        InitTranslation();
        InitSubtitleWindow();
        InitScreenTranslation();
        InitGameFont();
        LgWebOsTv.GetPinnedHostKey = ip => _settings.TvHostKeys.TryGetValue(ip, out var fp) ? fp : null;
        LgWebOsTv.SavePinnedHostKey = (ip, fp) => Dispatcher.Invoke(() => { _settings.TvHostKeys[ip] = fp; SaveSettings(); });
        TvServerCheck.IsChecked = _settings.TvServerEnabled;
        if (_settings.TvServerEnabled) EnsureServer();

        if (!WgcCaptureSource.IsSupported())
        {
            Log.Warn("Windows Graphics Capture bu sistemde desteklenmiyor; PrintWindow yöntemine geçildi.");
            MethodCombo.SelectedIndex = 1;
        }

        RefreshWindowList();
        WindowInfo? rp = WindowFinder.FindRemotePlay();
        _userStopped = !_settings.AutoStart;
        LaunchRemotePlayButton.IsEnabled = FindRemotePlayExe() != null;
        if (rp != null)
        {
            SelectWindow(rp.Handle);
            Log.Success($"PS Remote Play penceresi bulundu: {rp}");
            if (_settings.AutoStart) StartCapture();
            else Log.Info("Otomatik başlatma kapalı. Başlatmak için F6.");
        }
        else
        {
            SetText(RemotePlayStatus, "BULUNAMADI – PS Remote Play açık değil", Red);
            Log.Warn("PS Remote Play penceresi bulunamadı.");
            string? exe = FindRemotePlayExe();
            var answer = MessageBox.Show(this,
                "PS Remote Play penceresi bulunamadı.\n\n" +
                "1) PS Remote Play uygulamasını açın ve PS5'inize bağlanın.\n" +
                "2) Oyun görüntüsü pencerede görünsün; pencereyi simge durumuna küçültmeyin.\n\n" +
                "Program Remote Play'i arka planda aramaya devam eder; bulunca çeviriyi kendiliğinden başlatır " +
                "(veya F6'ya basın).\n\n" +
                (exe != null ? "PS Remote Play şimdi açılsın mı?" : "(PS Remote Play bu bilgisayarda bulunamadı; Sony'nin sitesinden kurun.)"),
                "Remote Play hazır değil", exe != null ? MessageBoxButton.YesNo : MessageBoxButton.OK, MessageBoxImage.Warning);
            if (answer == MessageBoxResult.Yes) LaunchRemotePlay();
        }

        _timer.Start();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _timer.Stop();
        UnregisterHotkeys();
        StopCapture("Uygulama kapatılıyor.");
        SaveSettings();
        try { _trCts?.Cancel(); _translation?.Dispose(); } catch { }
        try { _subWin?.Close(); } catch { }
        try { _screenWin?.Close(); } catch { }
        try { _server?.Dispose(); } catch { }
        Log.Written -= OnLogWritten;
        Log.Info("Uygulama kapatıldı.");
    }

    private void Setting_Changed(object sender, RoutedEventArgs e)
    {
        if (!_settingsReady) return;
        _cropToGame = CropCheck.IsChecked == true;
        SaveSettings();
        if (ReferenceEquals(sender, CropCheck))
            Log.Info(_cropToGame
                ? "Oyun alanı kırpma AÇIK: başlık çubuğu ve siyah bantlar atılıyor."
                : "Oyun alanı kırpma KAPALI: pencerenin tamamı kullanılıyor.");
        else if (_capture != null)
            Log.Info("Yöntem / FPS değişikliği yakalama yeniden başlatıldığında uygulanır.");
    }

    private void SaveSettings()
    {
        if (!_settingsReady) return;
        _settings.CaptureMethod = MethodCombo.SelectedIndex;
        _settings.Fps = SelectedFps();
        _settings.CropToGame = CropCheck.IsChecked == true;
        _settings.Save();
    }

    private int SelectedFps() =>
        int.TryParse((FpsCombo.SelectedItem as ComboBoxItem)?.Content?.ToString(), out int f) ? f : 15;

    // ───────────────────────────── Pencere listesi ─────────────────────────────

    private void RefreshWindowList()
    {
        IntPtr selected = (WindowCombo.SelectedItem as WindowInfo)?.Handle ?? IntPtr.Zero;
        var list = WindowFinder.EnumerateWindows();
        WindowCombo.ItemsSource = list;
        if (selected != IntPtr.Zero) SelectWindow(selected);
        if (WindowCombo.SelectedIndex < 0 && list.Count > 0 && list[0].IsRemotePlay) WindowCombo.SelectedIndex = 0;
    }

    private void SelectWindow(IntPtr handle)
    {
        if (WindowCombo.ItemsSource is not List<WindowInfo> list) return;
        var item = list.FirstOrDefault(w => w.Handle == handle);
        if (item != null) WindowCombo.SelectedItem = item;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshWindowList();
        Log.Info($"Pencere listesi yenilendi ({WindowCombo.Items.Count} pencere).");
    }

    private void FindRemotePlay_Click(object sender, RoutedEventArgs e)
    {
        RefreshWindowList();
        var rp = WindowFinder.FindRemotePlay();
        if (rp == null)
        {
            Log.Warn("PS Remote Play penceresi bulunamadı. Uygulamanın açık ve PS5'e bağlı olduğundan emin olun.");
            SetText(RemotePlayStatus, "BULUNAMADI", Red);
            return;
        }
        SelectWindow(rp.Handle);
        Log.Success($"PS Remote Play penceresi bulundu: {rp}");
    }

    // ───────────────────────────── Yakalama ─────────────────────────────

    private void StartStop_Click(object sender, RoutedEventArgs e) => ToggleSystem("düğme");

    private void StartCapture()
    {
        if (WindowCombo.SelectedItem is not WindowInfo w)
        {
            Log.Warn("Yakalanacak pencere seçilmedi.");
            return;
        }

        StopCapture(null);

        if (!NativeMethods.IsWindow(w.Handle))
        {
            Log.Error("Seçilen pencere artık mevcut değil. Listeyi yenileyin.");
            RefreshWindowList();
            return;
        }

        if (!w.IsRemotePlay)
            Log.Warn($"Seçilen pencere PS Remote Play değil ({w.ProcessName}). Test amaçlı yakalanacak.");
        if (NativeMethods.IsIconic(w.Handle))
            Log.Warn("Pencere simge durumunda (küçültülmüş). Windows küçültülmüş pencereleri yakalayamaz; pencereyi geri açın.");

        uint affinity = WindowFinder.GetDisplayAffinity(w.Handle);
        if (affinity != 0)
            Log.Warn($"Bu pencerede ekran yakalama koruması etkin (WDA=0x{affinity:X}). Koruma aşılmayacak; görüntü siyah gelebilir.");

        int fps = SelectedFps();
        ICaptureSource src = MethodCombo.SelectedIndex == 1 ? new PrintWindowCaptureSource() : new WgcCaptureSource();
        src.FrameArrived += OnFrame;
        src.Stopped += reason => Dispatcher.InvokeAsync(() =>
        {
            Log.Warn($"Yakalama durdu: {reason}");
            StopCapture(null);
            SetText(RemotePlayStatus, "Pencere kapandı", Red);
        });

        _cropHwnd = w.Handle;
        try
        {
            src.Start(w.Handle, fps);
        }
        catch (Exception ex)
        {
            src.FrameArrived -= OnFrame;
            src.Dispose();
            Log.Error($"Yakalama başlatılamadı ({src.Name}): {ex.GetType().Name}: {ex.Message}");
            SetText(CaptureStatus, "BAŞLATILAMADI – ayrıntı logda", Red);
            return;
        }

        _capture = src;
        _target = w;
        ResetSessionStats();
        StartStopButton.Content = "■ Durdur (F6)";
        SetText(CaptureStatus, "Başladı, ilk kare bekleniyor…", Orange);
        Log.Info($"Yakalama başladı → {w.Title} [{w.ProcessName}, PID {w.ProcessId}] | yöntem: {src.Name} | hedef FPS: {fps}");
    }

    private void StopCapture(string? reason)
    {
        if (_capture == null) return;
        var src = _capture;
        _capture = null;
        _target = null;
        try
        {
            src.FrameArrived -= OnFrame;
            src.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error($"Yakalama durdurulurken hata: {ex.Message}");
        }

        if (_verifying) FinishVerification(aborted: true);
        if (_autoDetectActive) { _autoDetectActive = false; Log.Info("Otomatik altyazı bulma iptal edildi."); }

        StartStopButton.Content = "▶ Başlat (F6)";
        SetText(CaptureStatus, "Durduruldu", Neutral);
        if (reason != null) Log.Info($"Yakalama durduruldu: {reason}");
        ClearScreenItems();
        LogSessionSummary();
        LogScreenSummary();
    }

    private void ResetSessionStats()
    {
        _analyzer.Reset();
        _total = _valid = _black = _uniform = _frozen = 0;
        Interlocked.Exchange(ref _dropped, 0);
        _lastVerdict = null;
        _lastGoodFrame = null;
        _lastGameRect = null;
        _noFrameWarned = false;
        _minimizedWarned = false;
        _captureStart = DateTime.Now;
        WarmTranslation(force: true);
        _lastFrameTime = DateTime.MinValue;
        _lastSummary = DateTime.Now;
        _fpsCount = 0;
        _fpsStart = DateTime.Now;
        _fps = 0;
        _subPresent = false;
        _presentStreak = _absentStreak = 0;
        _subAppearances = 0;
        ClearSubtitle(silent: true);
        _ocrCount = _ocrSkipped = _committedCount = 0;
        _ocrMsSum = _latencySum = 0;
        _trCount = _trErrors = 0;
        _trTotalLatencySum = 0;
        _translation?.ResetStats();
    }

    /// <summary>Yakalama iş parçacığından çağrılır.</summary>
    private void OnFrame(CapturedFrame raw)
    {
        if (Interlocked.CompareExchange(ref _uiPending, 1, 0) != 0)
        {
            Interlocked.Increment(ref _dropped);
            return;
        }

        FrameResult result;
        try
        {
            result = ProcessFrame(raw, _cropHwnd, _cropToGame, _autoDetectActive);
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _uiPending, 0);
            Log.Error($"Kare işleme hatası: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        // En yüksek öncelik: altyazı karesi arayüzdeki diğer işlerin (çizim vb.) önüne geçsin
        Dispatcher.InvokeAsync(() =>
        {
            try { HandleResult(result, live: true); }
            catch (Exception ex) { Log.Error($"Önizleme hatası: {ex.Message}"); }
            finally { Interlocked.Exchange(ref _uiPending, 0); }
        }, System.Windows.Threading.DispatcherPriority.Send);
    }

    /// <summary>Kırpma, kare analizi, altyazı alanı ve otomatik bulma (arka plan iş parçacığı).</summary>
    private FrameResult ProcessFrame(CapturedFrame raw, IntPtr hwnd, bool crop, bool runAuto)
    {
        var sw = Stopwatch.StartNew();

        PixelRect gameRect = GameArea.Compute(hwnd, raw, crop);
        CapturedFrame game = FrameOps.Crop(raw, gameRect);
        FrameAnalysis analysis = _analyzer.Analyze(game);

        PixelRect? regionPx = null;
        CapturedFrame? regionCrop = null;
        byte[]? mask = null;
        TextScan? presence = null;
        if (_region is { } holder)
        {
            var px = holder.Rect.ToPixels(game.Width, game.Height);
            if (px.Width >= 8 && px.Height >= 8)
            {
                regionPx = px;
                regionCrop = FrameOps.Crop(game, px);
                // Altyazılar ortalıdır: satır merkezi alan merkezine yakın olmalı (arka plan dokularını eler)
                // Kısa altyazılar ("Hello.") için en-boy şartı gevşek (2:1)
                presence = SubtitleDetector.Scan(game, px, centerX: px.X + px.Width / 2.0, centerTol: 0.12,
                    minWidthFrac: 0.02, keepMask: true, minAspect: 2.0);
                if (presence.Mask != null && presence.Mask.Length == presence.Search.Width * presence.Search.Height)
                    mask = FrameOps.MaskToBgr32(presence.Mask, presence.Search.Width, presence.Search.Height);
            }
        }

        AutoDetectResult? auto = runAuto ? SubtitleDetector.AutoDetect(game) : null;

        bool[]? fp = null;
        if (presence is { HasText: true, Mask: { } m })
        {
            // Parmak izi yalnızca bulunan satırların kutusundan (arka plan hareketi etkilemesin)
            var sr = presence.Search;
            int l = presence.Lines.Min(x => x.Bounds.X) - sr.X, t = presence.Lines.Min(x => x.Bounds.Y) - sr.Y;
            int rr = presence.Lines.Max(x => x.Bounds.Right) - sr.X, bb = presence.Lines.Max(x => x.Bounds.Bottom) - sr.Y;
            l = Math.Clamp(l, 0, sr.Width); rr = Math.Clamp(rr, 0, sr.Width);
            t = Math.Clamp(t, 0, sr.Height); bb = Math.Clamp(bb, 0, sr.Height);
            fp = SubtitleText.Fingerprint(m, sr.Width, l, t, rr - l, bb - t);
        }

        sw.Stop();
        return new FrameResult(game, gameRect, raw.Width, raw.Height, analysis, regionPx, regionCrop, mask,
            presence, auto, runAuto, fp, sw.Elapsed.TotalMilliseconds);
    }

    private void HandleResult(FrameResult r, bool live)
    {
        if (live && _capture == null) return;
        CapturedFrame f = r.Game;
        FrameAnalysis a = r.Analysis;

        _last = r;

        if (live)
        {
            _lastFrameTime = DateTime.Now;
            _total++;
            switch (a.Verdict)
            {
                case FrameVerdict.Gecerli: _valid++; _lastGoodFrame = f; break;
                case FrameVerdict.Siyah: _black++; break;
                case FrameVerdict.TekRenk: _uniform++; break;
                case FrameVerdict.Donmus: _frozen++; _lastGoodFrame = f; break;
            }

            if (_total == 1)
            {
                Log.Success($"İlk kare alındı: pencere {r.RawWidth}x{r.RawHeight}, oyun alanı {f.Width}x{f.Height} ({f.Method}), " +
                            $"{(DateTime.Now - _captureStart).TotalMilliseconds:F0} ms içinde.");
                _noFrameWarned = false;
            }

            if (_lastGameRect != r.GameRect)
            {
                if (_lastGameRect != null)
                    Log.Info($"Oyun alanı değişti: {r.GameRect} (pencere {r.RawWidth}x{r.RawHeight})");
                if (_cropToGame && f.Height < 700 && f.Width >= 400) // simge durumundaki geçici kareleri yok say
                    Log.Warn($"Oyun görüntüsü küçük ({f.Width}x{f.Height}). Altyazı okuma için Remote Play'i tam ekran / büyütülmüş " +
                             "kullanın ve Remote Play ayarlarında çözünürlüğü 1080p yapın.");
                _lastGameRect = r.GameRect;
            }

            _fpsCount++;
            double elapsed = (DateTime.Now - _fpsStart).TotalSeconds;
            if (elapsed >= 1)
            {
                _fps = _fpsCount / elapsed;
                _fpsCount = 0;
                _fpsStart = DateTime.Now;
            }

            SetText(CaptureStatus, "ÇALIŞIYOR – kareler alınıyor", Green);
            if (_lastVerdict != a.Verdict)
            {
                LogVerdictChange(a);
                _lastVerdict = a.Verdict;
            }

            if (_verifying)
            {
                _vTotal++;
                _vProcSum += r.ProcMs;
                switch (a.Verdict)
                {
                    case FrameVerdict.Gecerli: _vValid++; break;
                    case FrameVerdict.Siyah: _vBlack++; break;
                    case FrameVerdict.TekRenk: _vUniform++; break;
                    case FrameVerdict.Donmus: _vFrozen++; break;
                }
                _vMaxMotion = Math.Max(_vMaxMotion, a.Motion);
            }
        }

        // ÖNCE altyazı (gecikme kritik), sonra önizleme / durum yazıları
        UpdatePresence(r, live);
        if (live) TrackSubtitle(r);
        else if (r.Presence is { HasText: true } && _ocr != null) _ = RecognizeOnceAsync(r);
        UpdateOverlay();
        if (live) ScreenTick(r);

        // Önizleme görüntüleri ve durum yazıları saniyede en çok ~8 kez (her karede 1080p görüntü çizmek
        // arayüz iş parçacığını meşgul eder ve altyazının işlenmesini geciktirir)
        if (live && (DateTime.Now - _lastPreviewDraw).TotalMilliseconds < 125 && !r.AutoTried) return;
        _lastPreviewDraw = DateTime.Now;

        // Ana önizleme (yalnızca oyun alanı)
        WriteFrame(ref _preview, PreviewImage, f.Pixels, f.Width, f.Height, f.Stride);
        PreviewPlaceholder.Visibility = Visibility.Collapsed;

        // Altyazı alanı görünümleri
        if (r.RegionCrop is { } rc)
            WriteFrame(ref _regionWb, RegionImage, rc.Pixels, rc.Width, rc.Height, rc.Stride);
        else
            RegionImage.Source = null;

        if (r.MaskPixels != null && r.Presence != null)
            WriteFrame(ref _maskWb, MaskImage, r.MaskPixels, r.Presence.Search.Width, r.Presence.Search.Height,
                r.Presence.Search.Width * 4);
        else
            MaskImage.Source = null;


        // Otomatik bulma sonucu
        if (r.AutoTried && _autoDetectActive && r.Auto != null)
        {
            _autoDetectActive = false;
            AutoDetectButton.IsEnabled = true;
            ApplyRegion(r.Auto.SuggestedRegion, "Otomatik");
            Log.Success($"Altyazı otomatik bulundu: {r.Auto.LineCount} satır, metin {r.Auto.TextBounds}. " +
                        $"Önerilen alan: {r.Auto.SuggestedRegion}");
        }

        // Durum metinleri
        FrameSizeText.Text = $"{f.Width} x {f.Height}" +
                             (r.RawWidth != f.Width || r.RawHeight != f.Height ? $"  (pencere {r.RawWidth} x {r.RawHeight})" : "") +
                             $"\n{f.Method}";
        FpsText.Text = live ? $"{_fps:F1} FPS  •  işleme {r.ProcMs:F1} ms" : $"tek görüntü  •  işleme {r.ProcMs:F1} ms";
        LumaText.Text = $"{a.MeanLuma:F0} / {a.StdDev:F1} / hareket {a.Motion:F2}";
        SetText(VerdictText, FrameAnalyzer.Describe(a.Verdict), a.Verdict switch
        {
            FrameVerdict.Gecerli => Green,
            FrameVerdict.Donmus => Neutral,
            _ => Red
        });
        if (live)
            StatsText.Text = $"Toplam {_total}  •  geçerli {_valid}  •  siyah {_black}\n" +
                             $"boş {_uniform}  •  sabit {_frozen}  •  atlanan {Interlocked.Read(ref _dropped)}\n" +
                             $"altyazı görünme sayısı: {_subAppearances}\n" +
                             $"OCR çalışması {_ocrCount}  •  tanınan altyazı {_committedCount}\n" +
                             $"çeviri {_trCount}  •  API {_translation?.ApiCalls ?? 0}  •  önbellek {_translation?.CacheHits ?? 0}";
    }

    private void UpdatePresence(FrameResult r, bool live)
    {
        if (_region == null || r.Presence == null)
        {
            SetText(PresenceText, "Alan seçilmedi", Gray);
            return;
        }

        bool now = r.Presence.HasText;
        if (!live)
        {
            SetText(PresenceText, now ? $"VAR ({r.Presence.Lines.Count} satır)" : "YOK", now ? Green : Gray);
            Log.Info(now ? $"Görüntüde, seçili alanda altyazı VAR ({r.Presence.Lines.Count} satır)." : "Görüntüde, seçili alanda altyazı YOK.");
            return;
        }

        if (now) { _presentStreak++; _absentStreak = 0; }
        else { _absentStreak++; _presentStreak = 0; }

        if (!_subPresent && _presentStreak >= 2)
        {
            _subPresent = true;
            _subSince = DateTime.Now;
            _subAppearances++;
        }
        else if (_subPresent && _absentStreak >= 2)
        {
            _subPresent = false;
            EndSubtitle();
        }

        SetText(PresenceText, _subPresent ? $"VAR ({r.Presence.Lines.Count} satır)" : "YOK", _subPresent ? Green : Gray);
    }

    private static void WriteFrame(ref WriteableBitmap? wb, Image target, byte[] pixels, int w, int h, int stride)
    {
        if (w <= 0 || h <= 0) return;
        if (wb == null || wb.PixelWidth != w || wb.PixelHeight != h)
        {
            wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr32, null);
            target.Source = wb;
        }
        else if (!ReferenceEquals(target.Source, wb))
        {
            target.Source = wb;
        }
        wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
    }

    private static void LogVerdictChange(FrameAnalysis a)
    {
        string stats = $"(parlaklık {a.MeanLuma:F0}, kontrast {a.StdDev:F1}, kenar %{a.EdgeDensity * 100:F1})";
        switch (a.Verdict)
        {
            case FrameVerdict.Gecerli:
                Log.Info($"Görüntü durumu: GEÇERLİ {stats}");
                break;
            case FrameVerdict.Siyah:
                Log.Warn($"Görüntü durumu: SİYAH EKRAN {stats}. Olası nedenler: Remote Play bağlantısı yok/kopuk, " +
                         "pencere küçültülmüş, PS5 korumalı içerik gösteriyor veya pencere koruması.");
                break;
            case FrameVerdict.TekRenk:
                Log.Warn($"Görüntü durumu: BOŞ / TEK RENK KARE {stats}.");
                break;
            case FrameVerdict.Donmus:
                // Oyunda durmak normaldir; altyazı okumak için sabit görüntü de geçerlidir.
                Log.Info($"Görüntü durumu: sabit sahne ({a.SinceLastChange.TotalSeconds:F0} sn değişmedi).");
                break;
            case FrameVerdict.CokKucuk:
                Log.Info("Görüntü durumu: pencere çok küçük (simge durumundan çıkılıyor olabilir).");
                break;
        }
    }

    // ───────────────────────────── Altyazı alanı ─────────────────────────────

    private void SelectRegion_Click(object sender, RoutedEventArgs e)
    {
        if (_last == null)
        {
            Log.Warn("Önce yakalamayı başlatın ya da bir görüntü dosyası açın; alan, önizleme üzerinde çizilir.");
            return;
        }
        _selecting = true;
        Overlay.Cursor = Cursors.Cross;
        SelectHint.Visibility = Visibility.Visible;
        Log.Info("Alan seçimi: önizlemede altyazının çıktığı bölgeyi fareyle çizin (2 satırlık altyazıya yer bırakın).");
    }

    private void CancelSelection()
    {
        _selecting = false;
        _dragging = false;
        DragRect.Visibility = Visibility.Collapsed;
        Overlay.Cursor = null;
        SelectHint.Visibility = Visibility.Collapsed;
        if (Overlay.IsMouseCaptured) Overlay.ReleaseMouseCapture();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _selecting)
        {
            CancelSelection();
            Log.Info("Alan seçimi iptal edildi.");
        }
    }

    private void Overlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_selecting) return;
        _dragging = true;
        _dragStart = e.GetPosition(Overlay);
        Overlay.CaptureMouse();
        PlaceRect(DragRect, new Rect(_dragStart, _dragStart));
        DragRect.Visibility = Visibility.Visible;
        e.Handled = true;
    }

    private void Overlay_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        PlaceRect(DragRect, new Rect(_dragStart, e.GetPosition(Overlay)));
    }

    private void Overlay_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        var end = e.GetPosition(Overlay);
        var screenRect = new Rect(_dragStart, end);
        CancelSelection();

        if (_last == null || !TryGetTransform(out double scale, out double offX, out double offY)) return;
        int fw = _last.Game.Width, fh = _last.Game.Height;
        var px = PixelRect.FromLTRB(
            (int)Math.Round((screenRect.Left - offX) / scale), (int)Math.Round((screenRect.Top - offY) / scale),
            (int)Math.Round((screenRect.Right - offX) / scale), (int)Math.Round((screenRect.Bottom - offY) / scale)).ClampTo(fw, fh);

        if (px.Width < fw * 0.03 || px.Height < fh * 0.015)
        {
            Log.Warn("Seçilen alan çok küçük. Altyazının tamamını (ve 2. satır için payı) içine alacak şekilde çizin.");
            return;
        }
        ApplyRegion(NormRect.FromPixels(px, fw, fh), "Elle");
        Log.Success($"Altyazı alanı elle seçildi: {px} (oyun alanı {fw}x{fh}).");
    }

    private void AutoDetect_Click(object sender, RoutedEventArgs e)
    {
        if (_capture == null && _last != null)
        {
            // Dosya / son kare üzerinde tek seferlik
            var game = _last.Game;
            var auto = SubtitleDetector.AutoDetect(game);
            if (auto == null)
            {
                Log.Warn("Bu görüntüde altyazı bulunamadı. Altyazının ekranda olduğu bir kare kullanın veya alanı elle seçin.");
                return;
            }
            ApplyRegion(auto.SuggestedRegion, "Otomatik");
            Log.Success($"Altyazı otomatik bulundu: {auto.LineCount} satır, metin {auto.TextBounds}. Önerilen alan: {auto.SuggestedRegion}");
            _ = ReprocessLastAsync();
            return;
        }
        if (_capture == null)
        {
            Log.Warn("Önce yakalamayı başlatın ya da bir görüntü dosyası açın.");
            return;
        }
        _autoDetectEnd = DateTime.Now.AddSeconds(30);
        _autoDetectActive = true;
        AutoDetectButton.IsEnabled = false;
        Log.Info("Otomatik bulma: 30 sn boyunca altyazı bekleniyor. Oyunda biri konuşurken altyazı ekranda olmalı.");
    }

    private void ClearRegion_Click(object sender, RoutedEventArgs e)
    {
        ApplyRegion(null, null);
        Log.Info("Altyazı alanı temizlendi.");
    }

    private void ApplyRegion(NormRect? rect, string? source)
    {
        _region = rect is { } r ? new RegionHolder(r) : null;
        _settings.SetRegion(rect, source);
        SaveSettings();
        _subPresent = false;
        _presentStreak = _absentStreak = 0;
        UpdateRegionText();
        UpdateOverlay();
    }

    private void UpdateRegionText()
    {
        if (_region is not { } h)
        {
            SetText(RegionText, "Seçilmedi – 'Elle Seç' veya 'Otomatik Bul'", Orange);
            return;
        }
        string px = _last != null ? $"\n{h.Rect.ToPixels(_last.Game.Width, _last.Game.Height)} px" : "";
        SetText(RegionText, $"{_settings.SubtitleRegionSource ?? "?"} • {h.Rect}{px}", Neutral);
    }

    private void PreviewHost_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateOverlay();

    private bool TryGetTransform(out double scale, out double offX, out double offY)
    {
        scale = offX = offY = 0;
        if (_last == null) return false;
        double hw = PreviewHost.ActualWidth, hh = PreviewHost.ActualHeight;
        int fw = _last.Game.Width, fh = _last.Game.Height;
        if (hw <= 0 || hh <= 0 || fw <= 0 || fh <= 0) return false;
        scale = Math.Min(hw / fw, hh / fh);
        offX = (hw - fw * scale) / 2;
        offY = (hh - fh * scale) / 2;
        return true;
    }

    private void UpdateOverlay()
    {
        LinesLayer.Children.Clear();
        if (!TryGetTransform(out double s, out double ox, out double oy) || _last == null)
        {
            RegionRect.Visibility = Visibility.Collapsed;
            return;
        }

        if (_region is { } h)
        {
            var px = h.Rect.ToPixels(_last.Game.Width, _last.Game.Height);
            PlaceRect(RegionRect, new Rect(ox + px.X * s, oy + px.Y * s, px.Width * s, px.Height * s));
            RegionRect.Visibility = Visibility.Visible;
        }
        else
        {
            RegionRect.Visibility = Visibility.Collapsed;
        }

        // Bulunan metin satırları (yeşil)
        if (_last.Presence != null)
        {
            foreach (var line in _last.Presence.Lines)
            {
                var b = line.Bounds;
                var rect = new Rectangle { Stroke = LineBrush, StrokeThickness = 1.5 };
                PlaceRect(rect, new Rect(ox + b.X * s, oy + b.Y * s, b.Width * s, b.Height * s));
                LinesLayer.Children.Add(rect);
            }
        }
    }

    private static void PlaceRect(Rectangle r, Rect area)
    {
        Canvas.SetLeft(r, area.X);
        Canvas.SetTop(r, area.Y);
        r.Width = Math.Max(0, area.Width);
        r.Height = Math.Max(0, area.Height);
    }

    // ───────────────────────────── OCR (Aşama 3) ─────────────────────────────

    private void InitOcr()
    {
        Log.Info($"Windows OCR yüklü diller: {WindowsOcrEngine.AvailableLanguages()}");
        var engine = WindowsOcrEngine.Create(out string status);
        _ocr = engine;
        if (engine == null)
        {
            Log.Error(status);
            SetText(OcrStatusText, status, Red);
            InstallOcrButton.Visibility = Visibility.Visible;
        }
        else if (!engine.IsEnglish)
        {
            Log.Warn(status);
            SetText(OcrStatusText, status, Orange);
            InstallOcrButton.Visibility = Visibility.Visible;
        }
        else
        {
            Log.Success($"OCR motoru: {engine.Name}. {status}");
            SetText(OcrStatusText, $"{engine.Name} hazır", Green);
        }
    }

    private void InstallOcr_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            WindowsOcrEngine.StartEnglishPackInstall();
            Log.Info("İngilizce OCR dil paketi kurulumu başlatıldı (yönetici onayı istenir). Bitince uygulamayı yeniden başlatın.");
        }
        catch (Exception ex)
        {
            Log.Error($"Kurulum başlatılamadı (onay verilmemiş olabilir): {ex.Message}. Elle kurulum: Ayarlar > Saat ve dil > " +
                      "Dil ve bölge > English (United States) ekleyip 'Optik karakter tanıma' seçeneğini işaretleyin.");
        }
    }

    private void OcrCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        bool on = OcrCheck.IsChecked == true;
        if (!on) ClearSubtitle(silent: true);
        Log.Info(on ? "OCR açıldı." : "OCR kapatıldı.");
    }

    /// <summary>
    /// Her canlı karede çağrılır. OCR yalnızca: altyazı VAR, metin görüntüsü bir önceki kareyle aynı (geçiş/solma bitti)
    /// ve son okunan metin görüntüsünden farklıysa çalışır. Böylece aynı altyazı tekrar tekrar okunmaz.
    /// </summary>
    private void TrackSubtitle(FrameResult r)
    {
        if (_ocr == null || OcrCheck.IsChecked != true) return;
        // Altyazı ilk görüldüğü karede okunur (gecikmeyi azaltmak için 2. kare beklenmez).
        if (r.Fingerprint == null || r.Presence is not { HasText: true })
        {
            _prevFp = null;
            return;
        }

        var fp = r.Fingerprint;
        bool changedSinceOcr = _lastOcrFp == null || SubtitleText.Distance(fp, _lastOcrFp) >= 0.2;
        if (changedSinceOcr && _changeStart == null) _changeStart = r.Game.Timestamp;
        _prevFp = fp;
        if (!changedSinceOcr) return;

        _lastOcrFp = fp;
        long seq = ++_ocrSeq;
        var job = (r.Game, r.Presence.Lines.ToList(), seq, _changeStart ?? r.Game.Timestamp);
        _changeStart = null;

        if (_ocrBusy) _pendingOcr = job; // yalnızca en yenisi bekler
        else _ = RunOcrAsync(job);
    }

    /// <summary>Altyazı satırlarının ekrandaki yeri (fiziksel piksel) – örtme modu için.</summary>
    private static Rect CoverRectOf(CapturedFrame game, List<TextLine> lines)
    {
        int l = lines.Min(x => x.Bounds.X), t = lines.Min(x => x.Bounds.Y);
        int r = lines.Max(x => x.Bounds.Right), b = lines.Max(x => x.Bounds.Bottom);
        int lh = lines.Max(x => x.Bounds.Height);
        double pad = lh * 0.35;
        return new Rect(game.OriginX + l - pad, game.OriginY + t - pad, r - l + 2 * pad, b - t + 2 * pad);
    }

    private async Task RunOcrAsync((CapturedFrame Game, List<TextLine> Lines, long Seq, DateTime ChangeStart) job)
    {
        var engine = _ocr;
        if (engine == null) return;
        _ocrBusy = true;
        try
        {
            int maxDim = engine.MaxImageDimension;
            var input = await Task.Run(() => OcrPreprocessor.Build(job.Game, job.Lines, maxDim));
            if (input == null) return;
            var output = await Task.Run(() => engine.RecognizeAsync(input));
            _lastOcrInput = input;
            _ocrCount++;
            _ocrMsSum += output.Milliseconds;
            var norm = CoverNormOf(job.Game, job.Lines);
            ApplyOcr(job.Seq, output, job.ChangeStart, engine.Name, CoverRectOf(job.Game, job.Lines), norm, norm[4] * job.Game.Height);
            // Oyunun altyazı yazısını ölç (yazı tipi + boy + renk), yalnızca gerçek altyazı okunduysa
            // Her altyazıdan yalnızca bir kez ölç (aynı altyazının kararan / değişen karelerinden tekrar tekrar değil)
            string learnText = SubtitleText.Normalize(output.Lines);
            if (SubtitleText.IsPlausible(learnText) && learnText != _lastLearnedText)
            {
                _lastLearnedText = learnText;
                LearnGameFont(job.Game, job.Lines, output.Lines);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"OCR hatası: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _ocrBusy = false;
            if (_pendingOcr is { } next)
            {
                _pendingOcr = null;
                _ = RunOcrAsync(next);
            }
        }
    }

    /// <summary>Altyazı satırlarının oyun alanına göre oransal yeri (TV'de örtme modu için).</summary>
    private static double[] CoverNormOf(CapturedFrame game, List<TextLine> lines)
    {
        int l = lines.Min(x => x.Bounds.X), t = lines.Min(x => x.Bounds.Y);
        int r = lines.Max(x => x.Bounds.Right), b = lines.Max(x => x.Bounds.Bottom);
        double pad = lines.Max(x => x.Bounds.Height) * 0.35;
        double W = game.Width, H = game.Height;
        // 5. değer: tek satır yüksekliği (orta değer) – Türkçe yazı orijinalle aynı boyda olsun
        var hs = lines.Select(x => x.Bounds.Height).OrderBy(x => x).ToList();
        double lineH = hs[hs.Count / 2] / H;
        return new[] { Math.Max(0, (l - pad) / W), Math.Max(0, (t - pad) / H), Math.Min(1, (r - l + 2 * pad) / W), Math.Min(1, (b - t + 2 * pad) / H), lineH };
    }

    private void ApplyOcr(long seq, OcrOutput output, DateTime changeStart, string engineName, Rect cover, double[] coverNorm, double lineHeightPx)
    {
        SetText(OcrStatusText, $"{engineName}  •  son {output.Milliseconds:F0} ms  •  ort. {_ocrMsSum / Math.Max(1, _ocrCount):F0} ms",
            Green);

        // Eski (geç gelen) sonuç yeni altyazının üzerine yazılmasın
        if (seq < _appliedSeq) return;
        _appliedSeq = seq;
        if (!_subPresent && _presentStreak == 0) return; // altyazı bu arada kayboldu

        string text = SubtitleText.Normalize(output.Lines);
        _hideTimer.Stop(); // yeni metin geldi: bekleyen gizleme iptal
        if (!SubtitleText.IsPlausible(text))
        {
            // Altyazı henüz belirme (fade-in) aşamasında olabilir: bir sonraki karede yeniden okunsun
            // (önceden aynı görüntü tekrar okunmuyordu → Türkçe ancak altyazı değişince geliyordu)
            if (_currentText == null) { _lastOcrFp = null; _changeStart ??= changeStart; }
            _ocrSkipped++;
            if (text.Length > 0 && text != _lastSkippedText) Log.Info($"OCR anlamsız sonucu atlandı: \"{text}\"");
            _lastSkippedText = text;
            return;
        }

        if (_currentText != null && SubtitleText.Similarity(text, _currentText) >= 0.85)
        {
            // Aynı altyazı (OCR farkı). Belirgin şekilde uzadıysa (ör. 2. satır sonradan geldi) yeniden çevir.
            if (text.Length >= _currentText.Length + 3)
            {
                Log.Info($"Altyazı tamamlandı: \"{text}\" → yeniden çevriliyor.");
                _currentText = text;
                SetText(EnglishText, text, Neutral);
                _subWin?.SetCoverRect(cover, lineHeightPx);
                _lastCoverNorm = coverNorm;
                SendTvMask(coverNorm, extendPrevious: true);
                _ = TranslateCurrentAsync(text, changeStart, replacesPrevious: true);
            }
            return;
        }

        _currentText = text;
        _currentSince = DateTime.Now;
        _committedCount++;
        double latency = (DateTime.Now - changeStart).TotalMilliseconds;
        _latencySum += latency;
        SetText(EnglishText, text, Neutral);
        _subWin?.SetCoverRect(cover, lineHeightPx);
        _lastCoverNorm = coverNorm;
        SendTvMask(coverNorm, extendPrevious: false);
        Log.Success($"ALTYAZI (EN): \"{text}\"  [OCR {output.Milliseconds:F0} ms, metin değişiminden {latency:F0} ms sonra]");
        _ = TranslateCurrentAsync(text, changeStart);
    }

    private void ClearSubtitle(bool silent = false)
    {
        if (_currentText != null && !silent)
            Log.Info($"Altyazı temizlendi ({(DateTime.Now - _currentSince).TotalSeconds:F1} sn ekranda kaldı).");
        _currentText = null;
        _prevFp = null;
        _lastOcrFp = null;
        _changeStart = null;
        _pendingOcr = null;
        _appliedSeq = _ocrSeq + 1; // yoldaki OCR sonuçları artık geçersiz
        SetText(EnglishText, "—", Gray);
        CancelTranslation();
        _hideTimer.Stop();
        _turkishVisible = false;
        SetText(TurkishText, "—", Gray);
        ShowTurkish(""); // altyazı bitti → pencere gizlenir
        _server?.Unmask();
        _maskSent = null;
    }

    /// <summary>Kısa altyazılar da okunabilsin diye Türkçe en az bu kadar ekranda kalır.</summary>
    private static readonly TimeSpan MinTurkishDisplay = TimeSpan.FromSeconds(1.5);
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private DateTime _hideAt, _turkishShownAt;
    private bool _turkishVisible;

    /// <summary>
    /// İngilizce altyazı ekrandan kalktı. Bekleyen çeviri iptal EDİLMEZ (kısa altyazılar kaybolmasın);
    /// Türkçe en az MinTurkishDisplay kadar gösterilip sonra gizlenir.
    /// </summary>
    private void EndSubtitle()
    {
        if (_currentText != null)
            Log.Info($"Altyazı bitti ({(DateTime.Now - _currentSince).TotalSeconds:F1} sn ekranda kaldı).");
        _currentText = null;
        _prevFp = null;
        _lastOcrFp = null;
        _changeStart = null;
        _pendingOcr = null;
        _appliedSeq = _ocrSeq + 1;
        SetText(EnglishText, "—", Gray);
        _server?.Unmask();
        _maskSent = null;
        if (_turkishVisible) ScheduleHide(Max(DateTime.Now, _turkishShownAt + MinTurkishDisplay));
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    private void ScheduleHide(DateTime at)
    {
        _hideAt = at;
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void HideTimer_Tick(object? sender, EventArgs e)
    {
        if (DateTime.Now < _hideAt) return;
        _hideTimer.Stop();
        if (_currentText != null) return; // yeni altyazı başladı
        _turkishVisible = false;
        SetText(TurkishText, "—", Gray);
        ShowTurkish("");
    }

    /// <summary>Görüntü dosyası testinde tek seferlik OCR.</summary>
    private async Task RecognizeOnceAsync(FrameResult r)
    {
        var engine = _ocr;
        if (engine == null || r.Presence == null) return;
        try
        {
            var lines = r.Presence.Lines.ToList();
            int maxDim = engine.MaxImageDimension;
            var input = await Task.Run(() => OcrPreprocessor.Build(r.Game, lines, maxDim));
            if (input == null) return;
            var output = await Task.Run(() => engine.RecognizeAsync(input));
            _lastOcrInput = input;
            string text = SubtitleText.Normalize(output.Lines);
            SetText(EnglishText, text.Length > 0 ? text : "(okunamadı)", text.Length > 0 ? Neutral : Red);
            SetText(OcrStatusText, $"{engine.Name}  •  {output.Milliseconds:F0} ms", Green);
            Log.Success($"Dosya OCR sonucu: \"{text}\" ({output.Milliseconds:F0} ms, ham: \"{output.Text.Replace("\n", " | ")}\")");
            if (SubtitleText.IsPlausible(text) && _translation != null && TranslateCheck.IsChecked == true)
            {
                var o = await _translation.TranslateAsync(text, null, CancellationToken.None);
                SetText(TurkishText, o.Text, Neutral);
                ShowTurkish(o.Text);
                Log.Success($"Dosya ÇEVİRİ (TR): \"{o.Text}\" [{o.Engine}, {o.Milliseconds:F0} ms]");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"OCR hatası: {ex.Message}");
        }
    }

    // ───────────────────────────── Çeviri (Aşama 4) ─────────────────────────────

    private void InitTranslation()
    {
        try
        {
            _translation = new TranslationService(new TranslationCache());
            Log.Info($"Çeviri önbelleği açıldı: {TranslationCache.FilePath} ({_translation.CacheCount} kayıt).");
        }
        catch (Exception ex)
        {
            Log.Error($"Çeviri önbelleği açılamadı: {ex.Message}");
            SetText(TranslationStatusText, "Önbellek açılamadı – ayrıntı logda", Red);
            return;
        }

        _translation.EngineEvent += (msg, warn) => Dispatcher.InvokeAsync(() =>
        {
            if (warn) Log.Warn(msg); else Log.Success(msg);
            UpdateTranslationStatus();
        });
        _translation.CooldownChanged += (id, until) => Dispatcher.InvokeAsync(() =>
        {
            if (until is { } u) _settings.EngineCooldowns[id] = u;
            else _settings.EngineCooldowns.Remove(id);
            SaveSettings();
        });

        TranslateCheck.IsChecked = _settings.TranslationEnabled;
        _deepLKey = SecretStore.Unprotect(_settings.DeepLKeyProtected);
        _azureKey = SecretStore.Unprotect(_settings.AzureKeyProtected);
        _geminiKey = SecretStore.Unprotect(_settings.GeminiKeyProtected);
        ApplyTranslators(checkUsage: true);
    }

    /// <summary>Çeviri zincirini kurar: DeepL → Gemini → Azure (anahtar varsa) → MyMemory (açıksa).</summary>
    private void ApplyTranslators(bool checkUsage)
    {
        if (_translation == null) return;
        var engines = new List<ITranslator>();
        DeepLTranslator? deepL = null;
        if (!string.IsNullOrWhiteSpace(_deepLKey)) engines.Add(deepL = new DeepLTranslator(_deepLKey));
        if (!string.IsNullOrWhiteSpace(_geminiKey)) engines.Add(new GeminiTranslator(_geminiKey, _settings.GeminiModel));
        if (!string.IsNullOrWhiteSpace(_azureKey)) engines.Add(new AzureTranslator(_azureKey, _settings.AzureRegion));
        if (_settings.MyMemoryEnabled) engines.Add(new MyMemoryTranslator(_settings.MyMemoryEmail));

        _translation.SetEngines(engines, _settings.EngineCooldowns);
        if (engines.Count == 0)
        {
            Log.Warn("Hiç çeviri servisi yok. 'Çeviri Ayarları…' düğmesinden DeepL anahtarınızı girin.");
        }
        else
        {
            Log.Info($"Çeviri zinciri: {_translation.DescribeChain()}");
            if (deepL == null) Log.Warn("DeepL anahtarı girilmedi; çeviri yedek servislerle yapılacak (kalite düşük olabilir).");
        }
        UpdateTranslationStatus();
        if (checkUsage && deepL != null) _ = CheckDeepLUsageAsync(deepL);
    }

    private void UpdateTranslationStatus()
    {
        if (_translation == null) return;
        if (_translation.Slots.Count == 0)
        {
            SetText(TranslationStatusText, "Servis yok – 'Çeviri Ayarları…'", Orange);
            return;
        }
        var active = _translation.ActiveSlot;
        bool primary = active != null && ReferenceEquals(active, _translation.Slots[0]);
        SetText(TranslationStatusText,
            active == null ? $"Kullanılabilir servis yok: {_translation.DescribeChain()}"
                           : $"Aktif: {active.Translator.Name}{(primary ? "" : " (yedek)")} • önbellek {_translation.CacheCount}\n{_translation.DescribeChain()}",
            active == null ? Red : primary ? Green : Orange);
    }

    /// <summary>Açılışta DeepL kotası: dolmuşsa doğrudan yedeğe geçilir, %90'da uyarılır.</summary>
    private async Task CheckDeepLUsageAsync(DeepLTranslator tr)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var (used, limit) = await tr.GetUsageAsync(cts.Token);
            double pct = limit > 0 ? used * 100.0 / limit : 0;
            Log.Success($"DeepL bağlantısı tamam ({tr.Name}). Bu ay: {used:N0} / {limit:N0} karakter (%{pct:F1}).");
            if (limit > 0 && used >= limit)
                _translation?.MarkQuota("deepl", "DeepL aylık kotası dolu.");
            else if (pct >= 90)
                Log.Warn("DeepL aylık kotasının %90'ı kullanıldı; dolunca otomatik olarak yedek servise geçilecek.");
            else if (_settings.EngineCooldowns.Remove("deepl"))
            {
                // Yeni dönem başlamış: DeepL'i hemen geri al
                SaveSettings();
                ApplyTranslators(checkUsage: false);
                Log.Success("DeepL kotası yenilenmiş; çeviri yeniden DeepL ile yapılacak.");
            }
        }
        catch (TranslationException ex)
        {
            Log.Error($"DeepL: {ex.Message}");
        }
        catch (Exception ex)
        {
            Log.Warn($"DeepL kullanım bilgisi alınamadı (internet?): {ex.Message}");
        }
        UpdateTranslationStatus();
    }

    private void TranslationSettings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new TranslationSettingsWindow(_deepLKey, _azureKey, _settings.AzureRegion, _settings.MyMemoryEnabled, _settings.MyMemoryEmail,
            _geminiKey, _settings.GeminiModel)
        {
            Owner = this
        };
        dlg.ShowDialog();
        if (dlg.Result is not { } r) return;

        bool deepLChanged = r.DeepLKey != _deepLKey, azureChanged = r.AzureKey != _azureKey, geminiChanged = r.GeminiKey != _geminiKey;
        _geminiKey = r.GeminiKey;
        _settings.GeminiKeyProtected = _geminiKey == null ? null : SecretStore.Protect(_geminiKey);
        _settings.GeminiModel = r.GeminiModel;
        if (geminiChanged) _settings.EngineCooldowns.Remove("gemini");
        _deepLKey = r.DeepLKey;
        _azureKey = r.AzureKey;
        _settings.DeepLKeyProtected = _deepLKey == null ? null : SecretStore.Protect(_deepLKey);
        _settings.AzureKeyProtected = _azureKey == null ? null : SecretStore.Protect(_azureKey);
        _settings.AzureRegion = r.AzureRegion;
        _settings.MyMemoryEnabled = r.MyMemoryEnabled;
        _settings.MyMemoryEmail = r.MyMemoryEmail;
        if (deepLChanged) _settings.EngineCooldowns.Remove("deepl");
        if (azureChanged) _settings.EngineCooldowns.Remove("azure");
        SaveSettings();
        Log.Info($"Çeviri ayarları kaydedildi (DeepL: {(_deepLKey == null ? "yok" : DeepLTranslator.Mask(_deepLKey))}, " +
                 $"Gemini: {(_geminiKey == null ? "yok" : DeepLTranslator.Mask(_geminiKey))}, " +
                 $"Azure: {(_azureKey == null ? "yok" : DeepLTranslator.Mask(_azureKey))}, MyMemory: {(r.MyMemoryEnabled ? "açık" : "kapalı")}). Anahtarlar şifreli.");
        ApplyTranslators(checkUsage: deepLChanged);
    }

    private void TranslateCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settings.TranslationEnabled = TranslateCheck.IsChecked == true;
        SaveSettings();
        if (TranslateCheck.IsChecked != true)
        {
            CancelTranslation();
            SetText(TurkishText, "—", Gray);
            ShowTurkish("");
        }
        Log.Info(TranslateCheck.IsChecked == true ? "Çeviri açıldı." : "Çeviri kapatıldı.");
    }

    private void CancelTranslation()
    {
        _trSeq++; // yoldaki çeviri sonuçları artık geçersiz
        try { _trCts?.Cancel(); } catch { }
        _trCts = null;
    }

    /// <summary>
    /// Yeni altyazıyı çevirir. Yalnızca hâlâ ekrandaki altyazıya ait sonuç uygulanır:
    /// altyazı değişir/kaybolursa önceki istek iptal edilir ve geç gelen sonuç atılır.
    /// </summary>
    private async Task TranslateCurrentAsync(string english, DateTime changeStart, bool replacesPrevious = false)
    {
        if (replacesPrevious && _recentEnglish.Count > 0) _recentEnglish.RemoveAt(_recentEnglish.Count - 1);
        string? context = _recentEnglish.Count > 0 ? string.Join(" ", _recentEnglish) : null;
        _recentEnglish.Add(english);
        while (_recentEnglish.Count > 3) _recentEnglish.RemoveAt(0);

        if (_translation == null || TranslateCheck.IsChecked != true) return;

        CancelTranslation();
        var cts = new CancellationTokenSource();
        _trCts = cts;
        long seq = _trSeq;
        SetText(TurkishText, "…", Gray);
        if (!replacesPrevious) ShowTurkish(""); // eski çeviri yeni altyazının üzerinde kalmasın

        try
        {
            var o = await _translation.TranslateAsync(english, context, cts.Token);
            if (seq != _trSeq || cts.IsCancellationRequested)
            {
                Log.Info($"Geç gelen çeviri atıldı (altyazı değişmişti): \"{o.Text}\"");
                return;
            }

            double total = (DateTime.Now - changeStart).TotalMilliseconds;
            _trCount++;
            _trTotalLatencySum += total;
            SetText(TurkishText, o.Text, Neutral);
            ShowTurkish(o.Text);
            _turkishVisible = true;
            _turkishShownAt = DateTime.Now;
            if (_currentText == null) ScheduleHide(DateTime.Now + MinTurkishDisplay); // İngilizcesi çoktan bitti
            string src = o.FromCache ? (o.Fuzzy ? "önbellek (benzer)" : "önbellek") : o.Engine;
            UpdateTranslationStatus();
            Log.Success($"ÇEVİRİ (TR): \"{o.Text}\"  [{src} {o.Milliseconds:F0} ms, metin değişiminden Türkçeye {total:F0} ms]");
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Altyazı değişti; normal.
        }
        catch (OperationCanceledException)
        {
            _trErrors++;
            Log.Warn("Çeviri zaman aşımına uğradı (6 sn). İnternet bağlantısını kontrol edin.");
            if (seq == _trSeq) SetText(TurkishText, "(çeviri gecikti)", Orange);
        }
        catch (TranslationException ex)
        {
            _trErrors++;
            Log.Error($"Çeviri hatası: {ex.Message}");
            SetText(TranslationStatusText, ex.Message, Red);
            if (seq == _trSeq) SetText(TurkishText, "(çeviri hatası)", Red);
        }
        catch (HttpRequestException ex)
        {
            _trErrors++;
            Log.Error($"Çeviri bağlantı hatası: {ex.Message}");
            SetText(TranslationStatusText, "Bağlantı hatası – internet?", Red);
            if (seq == _trSeq) SetText(TurkishText, "(bağlantı yok)", Red);
        }
        catch (Exception ex)
        {
            _trErrors++;
            Log.Error($"Beklenmeyen çeviri hatası: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ───────────────────────────── Kısayollar ve tek tuşla kullanım (Aşama 6) ─────────────────────────────

    private const int HotkeyF6 = 0x5046, HotkeyF7 = 0x5047, HotkeyF8 = 0x5048, HotkeyF9 = 0x5049;
    private const uint ModNoRepeat = 0x4000, VkF6 = 0x75, VkF7 = 0x76, VkF8 = 0x77, VkF9 = 0x78;
    private const int WmHotkey = 0x0312;
    private HwndSource? _hwndSource;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProc);
        // Genel (global) kısayol: Remote Play veya başka pencere öndeyken de çalışır.
        bool f6 = NativeMethods.RegisterHotKey(hwnd, HotkeyF6, ModNoRepeat, VkF6);
        bool f7 = NativeMethods.RegisterHotKey(hwnd, HotkeyF7, ModNoRepeat, VkF7);
        bool f8 = NativeMethods.RegisterHotKey(hwnd, HotkeyF8, ModNoRepeat, VkF8);
        bool f9 = NativeMethods.RegisterHotKey(hwnd, HotkeyF9, ModNoRepeat, VkF9);
        if (!f9) Log.Warn("F9 (çevirili ekran görüntüsü) kısayolu kaydedilemedi: başka bir program kullanıyor olabilir. 'Kare kaydet' düğmesi de aynı görüntüyü kaydeder.");
        if (f6 && f7 && f8) Log.Success("Kısayollar hazır: F6 = çeviriyi başlat/durdur, F7 = ekranı çevir (tuş ipuçları, görevler; tekrar basınca kalkar), " +
                                        "F8 = Türkçe altyazıyı göster/gizle, F9 = çevirili ekran görüntüsü (her pencerede çalışır).");
        else Log.Warn($"Kısayol kaydedilemedi ({(f6 ? "" : "F6 ")}{(f7 ? "" : "F7 ")}{(f8 ? "" : "F8")}): başka bir program bu tuşu kullanıyor olabilir. " +
                      "Düğmeleri kullanabilir veya o programı kapatıp yeniden başlatabilirsiniz.");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            int id = wParam.ToInt32();
            if (id == HotkeyF6) { ToggleSystem("F6"); handled = true; }
            else if (id == HotkeyF7) { ToggleScreenTranslation(); handled = true; }
            else if (id == HotkeyF8) { ToggleSubtitleVisibility(); handled = true; }
            else if (id == HotkeyF9) { TakeTranslatedSnapshot(); handled = true; }
        }
        return IntPtr.Zero;
    }

    private void UnregisterHotkeys()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.UnregisterHotKey(hwnd, HotkeyF6);
        NativeMethods.UnregisterHotKey(hwnd, HotkeyF7);
        NativeMethods.UnregisterHotKey(hwnd, HotkeyF8);
        NativeMethods.UnregisterHotKey(hwnd, HotkeyF9);
        _hwndSource?.RemoveHook(WndProc);
    }

    /// <summary>F6 / Başlat-Durdur: bütün süreci (yakalama → OCR → çeviri → altyazı) başlatır veya durdurur.</summary>
    private void ToggleSystem(string source)
    {
        if (_capture != null)
        {
            _userStopped = true;
            StopCapture($"Kullanıcı durdurdu ({source}).");
            Notify("Çeviri durduruldu (F6)");
            Log.Info($"[{source}] Çeviri sistemi DURDURULDU.");
            return;
        }

        _userStopped = false;
        if (WindowCombo.SelectedItem is not WindowInfo w || !NativeMethods.IsWindow(w.Handle) || !w.IsRemotePlay)
        {
            RefreshWindowList();
            var rp = WindowFinder.FindRemotePlay();
            if (rp != null) SelectWindow(rp.Handle);
        }
        StartCapture();
        if (_capture != null)
        {
            if (OcrCheck.IsChecked != true) OcrCheck.IsChecked = true;
            if (TranslateCheck.IsChecked != true) TranslateCheck.IsChecked = true;
            string warn = _region == null ? " – altyazı alanı seçilmedi!" : _translation?.ActiveSlot == null ? " – çeviri servisi yok!" : "";
            Notify("Çeviri başladı (F6)" + warn);
            if (_settings.TvServerEnabled && _settings.TvAutoLaunch && _settings.TvPlatform == "webos" && _settings.TvKeyProtected != null) _ = AutoLaunchTvAsync();
            Log.Success($"[{source}] Çeviri sistemi BAŞLATILDI{warn}.");
        }
        else
        {
            Notify("PS Remote Play bulunamadı – önce PS5'e bağlanın (F6)");
            Log.Warn($"[{source}] Başlatılamadı: PS Remote Play penceresi yok veya yakalanamıyor.");
        }
    }

    /// <summary>F8: Türkçe altyazıyı göster / gizle.</summary>
    private void ToggleSubtitleVisibility()
    {
        bool on = SubtitleWindowCheck.IsChecked != true;
        SubtitleWindowCheck.IsChecked = on; // olay işleyicisi ayarı kaydeder ve uygular
        Notify(on ? "Türkçe altyazı açık (F8)" : "Türkçe altyazı gizlendi (F8)");
        Log.Info($"[F8] Türkçe altyazı {(on ? "AÇILDI" : "GİZLENDİ")}.");
    }

    private static string? FindRemotePlayExe()
    {
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
                 })
        {
            if (string.IsNullOrEmpty(root)) continue;
            string path = System.IO.Path.Combine(root, "Sony", "PS Remote Play", "RemotePlay.exe");
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private void LaunchRemotePlay_Click(object sender, RoutedEventArgs e) => LaunchRemotePlay();

    /// <summary>Resmi PS Remote Play uygulamasını açar. Bağlantı ve PSN girişi kullanıcıya aittir (otomatikleştirilmez).</summary>
    private void LaunchRemotePlay()
    {
        string? exe = FindRemotePlayExe();
        if (exe == null)
        {
            Log.Warn("PS Remote Play bu bilgisayarda bulunamadı (Program Files\\Sony\\PS Remote Play). Sony'nin resmi sitesinden kurun.");
            return;
        }
        if (WindowFinder.FindRemotePlay() != null)
        {
            Log.Info("PS Remote Play zaten açık.");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            _userStopped = false;
            Log.Info("PS Remote Play açılıyor. PS5'e bağlanınca çeviri kendiliğinden başlayacak.");
        }
        catch (Exception ex)
        {
            Log.Error($"PS Remote Play açılamadı: {ex.Message}");
        }
    }

    /// <summary>Eski sürümlerden gelen ayarları günceller.</summary>
    private void MigrateSettings()
    {
        if (_settings.SettingsVersion < 2)
        {
            // v0.5 ve öncesi: otomatik alan 2. satır için altta pay bırakmıyordu → alanı aşağı doğru genişlet.
            if (_settings.GetRegion() is { } r)
            {
                double extra = 0.045;
                var grown = new NormRect(r.X, r.Y, r.Width, Math.Min(1.0 - r.Y, r.Height + extra));
                _settings.SetRegion(grown, _settings.SubtitleRegionSource ?? "Elle");
                Log.Info($"Altyazı alanı iki satırlık altyazılar için aşağı doğru genişletildi: {grown}");
            }
            _settings.SettingsVersion = 2;
            _settings.Save();
        }
        if (_settings.SettingsVersion < 3)
        {
            // v0.9: daha düşük gecikme için 30 FPS
            if (_settings.Fps == 15) { _settings.Fps = 30; Log.Info("Hedef FPS 30'a çıkarıldı (daha düşük altyazı gecikmesi)."); }
            _settings.SettingsVersion = 3;
            _settings.Save();
        }
        if (_settings.SettingsVersion < 4)
        {
            // v1.2: en düşük gecikme için 60 FPS (Remote Play "Yüksek" kare hızında 60 kare gönderir)
            if (_settings.Fps == 30) { _settings.Fps = 60; Log.Info("Hedef FPS 60'a çıkarıldı (en düşük altyazı gecikmesi)."); }
            _settings.SettingsVersion = 4;
            _settings.Save();
        }
        if (_settings.SettingsVersion < 5)
        {
            // v1.11: tuş ipuçları varsayılan olarak F7 ile anlık çevrilir; sürekli mod (titreyebilen) deneysel ve kapalı
            _settings.ScreenTranslateEnabled = false;
            _settings.SettingsVersion = 5;
            _settings.Save();
        }
    }

    // ───────────────────────────── TV / telefon yayını (Aşama 8) ─────────────────────────────

    private double[]? _maskSent;

    /// <summary>
    /// TV'de İngilizce altyazının üstünü, metin ilk görüldüğü karede siyah bir kutuyla örter (Türkçe gelene kadar
    /// İngilizce görünmesin). Kutu altyazı bitince kalkar.
    /// </summary>
    private void SendTvMask(double[] rect, bool extendPrevious)
    {
        if (_server == null || !_settings.TvMaskEnglish) return;
        if (!extendPrevious) _maskSent = null; // yeni altyazı: önceki kutuyu büyütme, yenisini koy
        if (_maskSent != null && Math.Abs(_maskSent[0] - rect[0]) < 0.01 && Math.Abs(_maskSent[1] - rect[1]) < 0.01 &&
            Math.Abs(_maskSent[2] - rect[2]) < 0.01 && Math.Abs(_maskSent[3] - rect[3]) < 0.01)
            return;
        // Satır büyüdüyse genişlet, küçüldüyse (aynı altyazı içinde) eskisini koru
        if (_maskSent != null)
        {
            double l = Math.Min(_maskSent[0], rect[0]), t = Math.Min(_maskSent[1], rect[1]);
            double r2 = Math.Max(_maskSent[0] + _maskSent[2], rect[0] + rect[2]), b2 = Math.Max(_maskSent[1] + _maskSent[3], rect[1] + rect[3]);
            rect = new[] { l, t, r2 - l, b2 - t, rect.Length > 4 ? rect[4] : 0 };
        }
        _maskSent = rect;
        _server.Mask(rect);
    }

    /// <summary>Türkçe altyazıyı PC penceresine ve bağlı TV/telefon ekranlarına gönderir ("" = temizle).</summary>
    private void ShowTurkish(string text)
    {
        _subWin?.ShowSubtitle(text);
        _shownTurkish = text ?? "";
        if (_server == null) return;
        if (string.IsNullOrEmpty(text)) _server.Clear();
        else _server.ShowSubtitle(text, _lastCoverNorm);
    }

    /// <summary>Kısa bildirim (F6/F8 vb.) – PC'de ve TV'de.</summary>
    private void Notify(string message)
    {
        _subWin?.ShowNotice(message);
        _server?.Notice(message);
    }

    private SubtitleServer EnsureServer()
    {
        if (_server is { IsRunning: true }) return _server;
        _server = new SubtitleServer();
        _server.ClientEvent += msg => Dispatcher.InvokeAsync(() => Log.Info(msg));
        try
        {
            _server.Start();
            string? ip = SubtitleServer.GetLocalIPv4(_settings.TvIp);
            Log.Success($"TV / telefon yayını açık: ws://{ip ?? "?"}:{SubtitleServer.Port}/ws  •  tarayıcı: http://{ip ?? "?"}:{SubtitleServer.Port}/ " +
                        "(Windows güvenlik duvarı sorarsa 'Özel ağlar' için izin verin).");
        }
        catch (Exception ex)
        {
            Log.Error($"Yayın başlatılamadı (port {SubtitleServer.Port} kullanımda olabilir): {ex.Message}");
        }
        SendStyleToScreens();
        _server.Mute(_settings.SubtitleWindowEnabled == false);
        if (!_settings.TvServerEnabled)
        {
            _settings.TvServerEnabled = true;
            if (TvServerCheck.IsChecked != true) TvServerCheck.IsChecked = true;
            SaveSettings();
        }
        return _server;
    }

    /// <summary>PC'deki altyazı görünüm ayarlarını TV/telefon ekranlarına aktarır (1080p ölçeğinde).</summary>
    private void SendStyleToScreens()
    {
        _server?.Style(
            font: _settings.SubtitleFontSize * 1.38,
            bg: _settings.SubtitleBackgroundOpacity,
            bottom: 6,
            width: _settings.SubtitleWidthFraction * 100,
            cover: _settings.SubtitleCoverOriginal,
            match: _settings.SubtitleMatchOriginalSize,
            gameFont: GameFontForScreens());
    }

    private void TvServerCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        if (TvServerCheck.IsChecked == true) EnsureServer();
        else
        {
            _server?.Dispose();
            _server = null;
            _settings.TvServerEnabled = false;
            SaveSettings();
            Log.Info("TV / telefon yayını kapatıldı.");
        }
    }

    private void TvMode_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new TvModeWindow(_settings, EnsureServer, SaveSettings) { Owner = this };
        dlg.ShowDialog();
    }

    /// <summary>F6 ile başlarken TV uygulamasını aç (gerekirse önce geliştirici süresini yenile).</summary>
    private async Task AutoLaunchTvAsync()
    {
        try
        {
            string? pem = SecretStore.Unprotect(_settings.TvKeyProtected), pass = SecretStore.Unprotect(_settings.TvPassphraseProtected);
            if (pem == null || pass == null || string.IsNullOrEmpty(_settings.TvIp)) return;
            var tv = new LgWebOsTv(_settings.TvIp, pem, pass);
            EnsureServer();
            if (_settings.TvLastExtend == null || DateTime.Now - _settings.TvLastExtend > TimeSpan.FromHours(12))
            {
                await tv.ExtendDevModeAsync();
                _settings.TvLastExtend = DateTime.Now;
                SaveSettings();
                Log.Info("LG geliştirici modu süresi yenilendi.");
                await Task.Delay(1500);
            }
            if (await tv.EnsureInstalledAsync(new Progress<string>(m => Log.Info("TV: " + m)), CancellationToken.None))
                Log.Success("TV uygulaması yeni sürüme güncellendi.");
            string? pc = SubtitleServer.GetLocalIPv4(tv.Ip);
            if (pc == null) { Log.Warn("TV uygulaması açılamadı: bu bilgisayarın ağ adresi bulunamadı."); return; }
            await tv.LaunchAsync(pc, _settings.TvHdmi);
            Log.Success($"TV uygulaması açıldı (HDMI {_settings.TvHdmi}).");
        }
        catch (Exception ex)
        {
            Log.Warn($"TV uygulaması otomatik açılamadı: {ex.Message} (TV kapalı veya geliştirici modu süresi dolmuş olabilir.)");
        }
    }

    // ───────────────────────────── Altyazı penceresi (Aşama 5) ─────────────────────────────

    private void InitSubtitleWindow()
    {
        _subWin = new SubtitleWindow();
        _subWin.ApplyAppearance(_settings.SubtitleFontSize, _settings.SubtitleBackgroundOpacity,
            _settings.SubtitleWidthFraction, _settings.SubtitleTopmost);
        _subWin.PositionChanged += (left, bottom) =>
        {
            _settings.SubtitleLeft = left;
            _settings.SubtitleBottom = bottom;
            SaveSettings();
        };
        _subWin.Show();
        _subWin.Place(_settings.SubtitleLeft, _settings.SubtitleBottom);
        _subWin.SetCoverMode(_settings.SubtitleCoverOriginal);
        _subWin.SetMatchOriginalSize(_settings.SubtitleMatchOriginalSize);
        SubtitleWindowCheck.IsChecked = _settings.SubtitleWindowEnabled;
        _subWin.SetMuted(!_settings.SubtitleWindowEnabled);
        Log.Info($"Türkçe altyazı penceresi {(_settings.SubtitleWindowEnabled ? "açık" : "kapalı")} " +
                 $"(yazı {_settings.SubtitleFontSize:F0} pt, zemin %{_settings.SubtitleBackgroundOpacity * 100:F0}, " +
                 $"genişlik %{_settings.SubtitleWidthFraction * 100:F0}, her zaman üstte: {(_settings.SubtitleTopmost ? "evet" : "hayır")}).");
    }

    private void SubtitleWindowCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _subWin == null) return;
        bool on = SubtitleWindowCheck.IsChecked == true;
        _settings.SubtitleWindowEnabled = on;
        SaveSettings();
        _subWin.SetMuted(!on);
        _server?.Mute(!on);
        PushScreen(force: true);
        Log.Info(on ? "Türkçe altyazı gösteriliyor." : "Türkçe altyazı gizlendi.");
    }

    private void SubtitleAppearance_Click(object sender, RoutedEventArgs e)
    {
        if (_subWin == null) return;
        if (SubtitleWindowCheck.IsChecked != true)
        {
            SubtitleWindowCheck.IsChecked = true; // ayarlar görülebilsin
        }
        var dlg = new SubtitleSettingsWindow(_settings, _subWin, DescribeGameFont) { Owner = this };
        dlg.FontChanged += gameChanged => { OnFontSettingChanged(gameChanged); SaveSettings(); };
        dlg.Changed += () =>
        {
            _subWin.ApplyAppearance(_settings.SubtitleFontSize, _settings.SubtitleBackgroundOpacity,
                _settings.SubtitleWidthFraction, _settings.SubtitleTopmost);
            _subWin.SetMatchOriginalSize(_settings.SubtitleMatchOriginalSize);
            SendStyleToScreens();
            SaveSettings();
        };
        dlg.ShowDialog();
        Log.Info($"Altyazı görünümü: yazı {_settings.SubtitleFontSize:F0} pt, zemin %{_settings.SubtitleBackgroundOpacity * 100:F0}, " +
                 $"genişlik %{_settings.SubtitleWidthFraction * 100:F0}, her zaman üstte: {(_settings.SubtitleTopmost ? "evet" : "hayır")}.");
    }

    // ───────────────────────────── Görüntü dosyasıyla test ─────────────────────────────

    private async void TestImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Test için oyun görüntüsü seçin",
            Filter = "Görüntüler (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
            InitialDirectory = Directory.Exists(Log.CaptureDir) ? Log.CaptureDir : null
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            if (_capture != null)
            {
                _userStopped = true;
                StopCapture("Görüntü dosyasıyla test için durduruldu.");
            }

            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.UriSource = new Uri(dlg.FileName);
            bi.EndInit();
            var conv = new FormatConvertedBitmap(bi, PixelFormats.Bgr32, null, 0);
            int w = conv.PixelWidth, h = conv.PixelHeight;
            var px = new byte[w * h * 4];
            conv.CopyPixels(px, w * 4, 0);
            var frame = new CapturedFrame(px, w, h, w * 4, DateTime.Now, $"Dosya: {System.IO.Path.GetFileName(dlg.FileName)}");

            Log.Info($"Test görüntüsü açıldı: {dlg.FileName} ({w}x{h})");
            _analyzer.Reset();
            var result = await Task.Run(() => ProcessFrame(frame, IntPtr.Zero, false, runAuto: true));
            HandleResult(result, live: false);

            if (result.Auto != null)
            {
                Log.Success($"Bu görüntüde altyazı bulundu: {result.Auto.LineCount} satır, metin {result.Auto.TextBounds}.");
                if (_region == null)
                {
                    ApplyRegion(result.Auto.SuggestedRegion, "Otomatik");
                    Log.Success($"Alan seçili olmadığı için önerilen alan uygulandı: {result.Auto.SuggestedRegion}");
                    await ReprocessLastAsync();
                }
                else
                {
                    Log.Info("Kayıtlı bir alan zaten var; değiştirmek için 'Otomatik Bul'a basın.");
                }
            }
            else
            {
                Log.Info("Bu görüntüde ortalanmış altyazı bulunamadı.");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Görüntü açılamadı: {ex.Message}");
        }
    }

    /// <summary>Canlı yakalama yokken son kareyi yeni alanla yeniden işler (dosya testi için).</summary>
    private async Task ReprocessLastAsync()
    {
        if (_capture != null || _last == null) return;
        var game = _last.Game;
        var result = await Task.Run(() => ProcessFrame(game, IntPtr.Zero, false, runAuto: false));
        HandleResult(result, live: false);
    }

    // ───────────────────────────── Zamanlayıcı ─────────────────────────────

    private DateTime _lastWarm;

    /// <summary>Çeviri bağlantısını sıcak tut: boşta kalan bağlantı kapanırsa ilk altyazı ~200 ms daha geç çevrilir.</summary>
    private void WarmTranslation(bool force = false)
    {
        if (_translation == null || TranslateCheck.IsChecked != true) return;
        if (!force && (DateTime.Now - _lastWarm).TotalSeconds < 40) return;
        _lastWarm = DateTime.Now;
        _ = _translation.WarmUpAsync();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_capture != null) WarmTranslation();
        DateTime now = DateTime.Now;

        if (_target != null && _capture != null)
        {
            if (!NativeMethods.IsWindow(_target.Handle))
            {
                SetText(RemotePlayStatus, "Pencere kapandı", Red);
                Log.Warn("Remote Play penceresi kapandı.");
                StopCapture(null);
            }
            else if (NativeMethods.IsIconic(_target.Handle))
            {
                SetText(RemotePlayStatus, "SİMGE DURUMUNDA – pencereyi geri açın", Orange);
                if (!_minimizedWarned)
                {
                    Log.Warn("Remote Play penceresi simge durumuna küçültüldü; küçültülmüş pencereden görüntü alınamaz.");
                    _minimizedWarned = true;
                }
            }
            else
            {
                _minimizedWarned = false;
                SetText(RemotePlayStatus,
                    _target.IsRemotePlay ? $"BAĞLI – {_target.Title}" : $"Test penceresi: {_target.Title}",
                    _target.IsRemotePlay ? Green : Orange);
            }
        }
        else if (now - _lastRpScan > TimeSpan.FromSeconds(2))
        {
            _lastRpScan = now;
            var rp = WindowFinder.FindRemotePlay();
            if (rp == null)
            {
                SetText(RemotePlayStatus, "BULUNAMADI – PS Remote Play açık değil", Red);
            }
            else
            {
                SetText(RemotePlayStatus, $"Bulundu – {rp.Title}", Orange);
                if (!_userStopped && _target == null && _settings.AutoStart)
                {
                    Log.Success($"PS Remote Play penceresi algılandı: {rp}. Yakalama otomatik başlatılıyor.");
                    RefreshWindowList();
                    SelectWindow(rp.Handle);
                    StartCapture();
                }
            }
        }

        if (_capture != null)
        {
            DateTime reference = _lastFrameTime == DateTime.MinValue ? _captureStart : _lastFrameTime;
            if (now - reference > TimeSpan.FromSeconds(3))
            {
                SetText(CaptureStatus, "KARE GELMİYOR", Red);
                if (!_noFrameWarned)
                {
                    Log.Warn("3 saniyedir yeni kare gelmiyor. Pencere küçültülmüş olabilir, içerik değişmiyor olabilir " +
                             "(WGC yalnızca değişen kareleri gönderir) veya Remote Play akışı durmuş olabilir.");
                    _noFrameWarned = true;
                }
            }
            else
            {
                _noFrameWarned = false;
            }

            if (now - _lastSummary > TimeSpan.FromSeconds(30))
            {
                _lastSummary = now;
                LogSessionSummary();
            }
        }

        if (_autoDetectActive && now > _autoDetectEnd)
        {
            _autoDetectActive = false;
            AutoDetectButton.IsEnabled = true;
            Log.Warn("Otomatik bulma: 30 sn içinde ortalanmış altyazı görülmedi. Altyazı ekrandayken tekrar deneyin veya alanı elle seçin.");
        }
        if (!_autoDetectActive) AutoDetectButton.IsEnabled = true;

        if (_verifying)
        {
            double left = (_verifyEnd - now).TotalSeconds;
            if (left <= 0) FinishVerification(aborted: false);
            else VerifyText.Text = $"Test sürüyor… {left:F0} sn kaldı ({_vTotal} kare).";
        }
    }

    private void LogSessionSummary()
    {
        if (_total == 0) return;
        Log.Info($"Özet: {_total} kare | geçerli {_valid} | siyah {_black} | boş {_uniform} | sabit {_frozen} | " +
                 $"atlanan {Interlocked.Read(ref _dropped)} | altyazı görünme {_subAppearances} | son FPS {_fps:F1}");
        if (_ocrCount > 0)
            Log.Info($"OCR özeti: {_ocrCount} çalışma, ort. {_ocrMsSum / _ocrCount:F0} ms | tanınan altyazı {_committedCount}" +
                     (_committedCount > 0 ? $", ort. gecikme (metin değişiminden ekrana) {_latencySum / _committedCount:F0} ms" : "") +
                     $" | atlanan anlamsız sonuç {_ocrSkipped}");
        if (_translation != null && (_trCount > 0 || _trErrors > 0))
            Log.Info($"Çeviri özeti: {_trCount} çeviri | API {_translation.ApiCalls} çağrı, ort. {_translation.ApiMsSum / Math.Max(1, _translation.ApiCalls):F0} ms, " +
                     $"{_translation.CharsSent} karakter | önbellekten {_translation.CacheHits} | hata {_trErrors} | " +
                     $"ort. toplam gecikme (metin değişiminden Türkçeye) {_trTotalLatencySum / Math.Max(1, _trCount):F0} ms | önbellekte {_translation.CacheCount} kayıt" +
                     (_translation.HedgeWins > 0 ? $" | yedek servis {_translation.HedgeWins} kez daha hızlı yanıt verdi" : "") +
                     $" | servisler: {string.Join(", ", _translation.Slots.Select(x => $"{x.Translator.Name} {x.Calls} çağrı/{x.Chars} kr."))}");
    }

    // ───────────────────────────── Doğrulama testi (Aşama 1) ─────────────────────────────

    private void Verify_Click(object sender, RoutedEventArgs e)
    {
        if (_capture == null)
        {
            _userStopped = false;
            StartCapture();
            if (_capture == null) return;
        }
        _vTotal = _vValid = _vBlack = _vUniform = _vFrozen = 0;
        _vMaxMotion = 0;
        _vProcSum = 0;
        _verifyStart = DateTime.Now;
        _verifyEnd = _verifyStart.AddSeconds(10);
        _verifying = true;
        VerifyButton.IsEnabled = false;
        SetText(VerifyText, "Test sürüyor… Oyunda karakteri hareket ettirin.", Orange);
        Log.Info("10 saniyelik doğrulama testi başladı. Oyunda hareketli bir sahnede olun.");
    }

    private void FinishVerification(bool aborted)
    {
        _verifying = false;
        VerifyButton.IsEnabled = true;
        if (aborted)
        {
            SetText(VerifyText, "Test yarıda kesildi (yakalama durdu).", Red);
            Log.Warn("Doğrulama testi yarıda kesildi.");
            return;
        }

        double seconds = Math.Max(0.1, (DateTime.Now - _verifyStart).TotalSeconds);
        double fps = _vTotal / seconds;
        double validRatio = _vTotal > 0 ? (double)_vValid / _vTotal : 0;
        bool motionSeen = _vMaxMotion > 2.0;
        double avgProc = _vTotal > 0 ? _vProcSum / _vTotal : 0;

        string breakdown = $"{_vTotal} kare, {fps:F1} FPS, geçerli %{validRatio * 100:F0}, siyah {_vBlack}, boş {_vUniform}, " +
                           $"sabit {_vFrozen}, en yüksek hareket {_vMaxMotion:F1}, ort. işleme {avgProc:F1} ms";

        string? png = null;
        var sample = _lastGoodFrame ?? _last?.Game;
        if (sample != null)
        {
            try { png = SavePng(sample, "dogrulama"); }
            catch (Exception ex) { Log.Error($"Doğrulama karesi kaydedilemedi: {ex.Message}"); }
        }

        string title;
        Brush color;
        if (_vTotal == 0)
        {
            title = "BAŞARISIZ – hiç kare alınamadı";
            color = Red;
            Log.Error("AŞAMA 1 DOĞRULAMA: BAŞARISIZ – 10 sn içinde hiç kare alınamadı.");
        }
        else if (validRatio >= 0.8 && motionSeen && _vTotal >= 10)
        {
            title = "BAŞARILI – gerçek, hareketli görüntü yakalandı";
            color = Green;
            Log.Success($"AŞAMA 1 DOĞRULAMA: BAŞARILI – {breakdown}");
        }
        else if (validRatio >= 0.8)
        {
            title = "KISMEN – görüntü geçerli ama hareket görülmedi";
            color = Orange;
            Log.Warn($"AŞAMA 1 DOĞRULAMA: KISMEN – hareket algılanmadı. {breakdown}");
        }
        else
        {
            title = "BAŞARISIZ – kareler siyah / boş / sabit";
            color = Red;
            Log.Error($"AŞAMA 1 DOĞRULAMA: BAŞARISIZ – {breakdown}");
        }

        SetText(VerifyText, $"{title}\n{breakdown}" + (png != null ? $"\nKare: {png}" : ""), color);
    }

    // ───────────────────────────── Yardımcılar ─────────────────────────────

    private void SavePng_Click(object sender, RoutedEventArgs e)
    {
        var f = _last?.Game;
        if (f == null)
        {
            Log.Warn("Kaydedilecek kare yok. Önce yakalamayı başlatın.");
            return;
        }
        try
        {
            string path = SavePng(f, "kare");
            Log.Success($"Kare kaydedildi (oyun alanı {f.Width}x{f.Height}): {path}");
            if (SaveTranslatedSnapshot() is { } tr) Log.Success($"Çevirili hali (TV'de görünen) kaydedildi: {tr}");
            if (_last?.RegionCrop is { } rc)
                Log.Success($"Altyazı alanı da kaydedildi: {SavePng(rc, "altyazi-alani")}");
            if (_lastOcrInput is { } oi)
                Log.Success($"Son OCR girdisi kaydedildi: {SavePng(new CapturedFrame(oi.Bgra, oi.Width, oi.Height, oi.Width * 4, DateTime.Now, "OCR"), "ocr-girdisi")}");
        }
        catch (Exception ex)
        {
            Log.Error($"Kare kaydedilemedi: {ex.Message}");
        }
    }

    private static string SavePng(CapturedFrame f, string prefix)
    {
        Directory.CreateDirectory(Log.CaptureDir);
        string path = System.IO.Path.Combine(Log.CaptureDir, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
        BitmapSource bs = BitmapSource.Create(f.Width, f.Height, 96, 96, PixelFormats.Bgr32, null, f.Pixels, f.Stride);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bs));
        using var fs = File.Create(path);
        enc.Save(fs);
        return path;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Log.AppDataDir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Log.AppDataDir}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Klasör açılamadı: {ex.Message}");
        }
    }

    private void OnLogWritten(LogLevel level, string line)
    {
        Dispatcher.InvokeAsync(() =>
        {
            var item = new ListBoxItem
            {
                Content = line,
                Foreground = level switch
                {
                    LogLevel.Hata => Red,
                    LogLevel.Uyari => Orange,
                    LogLevel.Basari => Green,
                    _ => new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0))
                }
            };
            LogList.Items.Add(item);
            while (LogList.Items.Count > 500) LogList.Items.RemoveAt(0);
            LogList.ScrollIntoView(item);
        });
    }

    private static void SetText(TextBlock tb, string text, Brush color)
    {
        tb.Text = text;
        tb.Foreground = color;
    }
}
