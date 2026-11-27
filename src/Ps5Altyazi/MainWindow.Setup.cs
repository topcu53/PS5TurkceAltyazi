using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Ps5Altyazi.Logging;
using Ps5Altyazi.Ps5;

namespace Ps5Altyazi;

/// <summary>İlk açılış kurulum sihirbazı, PS5 / TV durum ışıkları ve test altyazısı.</summary>
public partial class MainWindow
{
    /// <summary>PS5 bağlantı durumu: 0 bağlı değil (kırmızı), 1 bağlanıyor (turuncu), 2 bağlı (yeşil).</summary>
    private int _ps5Level;
    private string _ps5Text = "bağlı değil";

    private const string TestText = "Bu bir test altyazısıdır. Altyazı+ çalışıyor! ✔";

    /// <summary>Kurulum adımlarını ana pencerenin içinde (sekmelerin yerine) gösterir; bitince ana sayfa açılır.</summary>
    private void RunSetupWizard()
    {
        if (SetupPage.Visibility == Visibility.Visible) return;
        bool wasRunning = DirectRunning;
        if (wasRunning) StopCapture("Kurulum açıldı.");
        var panel = new SetupWizardPanel(_settings, SaveSettings, EnsureServer,
            OpenTranslationSettingsAsync, TranslationSummary, SendTestSubtitle);
        panel.Finished += done =>
        {
            SetupPage.Content = null;
            SetupPage.Visibility = Visibility.Collapsed;
            NavBar.Visibility = Visibility.Visible;
            NavHome.IsChecked = true;
            Nav_Checked(this, new RoutedEventArgs());
            UpdateSourceUi();
            if (done)
            {
                Log.Success("Kurulum tamamlandı. Çeviriyi başlatmak için ▶ Başlat (F6).");
                Notify("Kurulum tamam – ▶ Başlat (F6)");
            }
            else if (!_settings.SetupCompleted)
                Log.Info("Kurulum şimdilik geçildi; Ayarlar → Bağlantı → \"Kurulum adımlarını aç\" ile devam edebilirsiniz.");
            if (wasRunning && UseDirect()) StartCapture();
        };
        SetupPage.Content = panel;
        HomePage.Visibility = HistoryPage.Visibility = SettingsPage.Visibility = Visibility.Collapsed;
        NavBar.Visibility = Visibility.Hidden;
        SetupPage.Visibility = Visibility.Visible;
    }

    private void SetupWizard_Click(object sender, RoutedEventArgs e) => RunSetupWizard();

    private string TranslationSummary()
    {
        var list = new System.Collections.Generic.List<string>();
        if (_deepLKey != null) list.Add("DeepL");
        if (_geminiKey != null) list.Add("Gemini");
        if (_azureKey != null) list.Add("Azure");
        if (list.Count == 0) return "Yalnız MyMemory (ücretsiz, sınırlı) – daha iyi çeviri için bir anahtar girin";
        return string.Join(" + ", list) + (_settings.MyMemoryEnabled ? " (yedek: MyMemory)" : "") + " hazır";
    }

    private string TranslationShort()
    {
        var list = new System.Collections.Generic.List<string>();
        if (_deepLKey != null) list.Add("DeepL");
        if (_geminiKey != null) list.Add("Gemini");
        if (_azureKey != null) list.Add("Azure");
        return list.Count == 0 ? "MyMemory" : string.Join(" + ", list);
    }

    private void TestSubtitle_Click(object sender, RoutedEventArgs e)
    {
        if (_server is not { IsRunning: true } && _settings.TvServerEnabled) EnsureServer();
        int n = _server is { IsRunning: true } s ? s.ClientCount : 0;
        SendTestSubtitle();
        Log.Info(n > 0 ? $"Test altyazısı gönderildi ({n} ekran + PC)." : "Test altyazısı PC'de gösterildi; TV bağlı değil (TV'de Altyazı+ uygulamasını açın).");
    }

    /// <summary>"Bu bir test altyazısıdır" yazısını PC'ye ve bağlı TV/telefon ekranlarına gönderir, 6 sn sonra siler.</summary>
    private async void SendTestSubtitle()
    {
        ShowTurkish(TestText);
        await Task.Delay(6000);
        if (_shownTurkish == TestText) ShowTurkish("");
    }

    private static readonly Brush DotGreen = Frozen(0x4A, 0xDE, 0x80), DotOrange = Frozen(0xFF, 0xB0, 0x3B), DotRed = Frozen(0xFF, 0x5C, 0x5C);

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var x = new SolidColorBrush(Color.FromRgb(r, g, b));
        x.Freeze();
        return x;
    }

    private static Brush Level(int l) => l >= 2 ? DotGreen : l == 1 ? DotOrange : DotRed;

    /// <summary>Üst banttaki PS5 ve TV durum ışıkları (zamanlayıcıdan çağrılır).</summary>
    private void UpdateConnectionPills()
    {
        int ps5;
        string ps5Text;
        if (DirectRunning) { ps5 = _ps5Level; ps5Text = _ps5Text; }
        else if (_capture != null) { ps5 = 2; ps5Text = "Remote Play penceresinden"; }
        else if (!_directReady) { ps5 = 0; ps5Text = "kurulmadı – 🧭 Kurulum"; }
        else { ps5 = 0; ps5Text = "bağlı değil – ▶ Başlat"; }
        Ps5Dot.Foreground = Level(ps5);
        Ps5PillText.Text = ps5Text;
        Ps5PillText.ToolTip = Ps5PillText.Text;

        int tv;
        string tvText;
        int clients = _server is { IsRunning: true } srv ? srv.ClientCount : 0;
        bool tvSetUp = _settings.TvPlatform == "webos" ? _settings.TvKeyProtected != null : _settings.TvServerEnabled;
        if (clients > 0) { tv = 2; tvText = $"bağlı ({clients} ekran)"; }
        else if (_server is { IsRunning: true } && tvSetUp) { tv = 1; tvText = "TV uygulaması bekleniyor…"; }
        else { tv = 0; tvText = tvSetUp ? "bağlı değil" : "kurulmadı"; }
        TvDot.Foreground = Level(tv);
        TvPillText.Text = tvText;
        TranslatorTileText.Text = TranslationShort();
        TvPillText.ToolTip = TvPillText.Text;

        // Bağlı değilken kartın altında "↻ Yeniden bağlan" (yalnız kurulmuşsa; kurulmadıysa Kurulum gerekir)
        bool ps5Busy = DateTime.Now < _ps5ReconnectUntil, tvBusy = DateTime.Now < _tvReconnectUntil;
        Ps5ReconnectButton.Visibility = ps5 < 2 && (_directReady || _capture != null || ps5Busy) ? Visibility.Visible : Visibility.Collapsed;
        Ps5ReconnectButton.IsEnabled = !ps5Busy;
        Ps5ReconnectButton.Content = ps5Busy ? "Bağlanıyor…" : "↻ Yeniden bağlan";
        TvReconnectButton.Visibility = tv < 2 && (tvSetUp || tvBusy) ? Visibility.Visible : Visibility.Collapsed;
        TvReconnectButton.IsEnabled = !tvBusy;
        TvReconnectButton.Content = tvBusy ? "Bağlanıyor…" : "↻ Yeniden bağlan";
    }

    private DateTime _ps5ReconnectUntil, _tvReconnectUntil;

    /// <summary>PS5 kartındaki "Yeniden bağlan": bağlantıyı kapatıp baştan kurar.</summary>
    private void Ps5Reconnect_Click(object sender, RoutedEventArgs e)
    {
        _ps5ReconnectUntil = DateTime.Now.AddSeconds(8);
        Log.Info("PS5'e yeniden bağlanılıyor (kullanıcı istedi)…");
        if (_capture != null) StopCapture("Yeniden bağlanılıyor.");
        _userStopped = false;
        StartCapture();
        UpdateConnectionPills();
    }

    /// <summary>TV kartındaki "Yeniden bağlan": yayını yeniden açar, LG'de TV uygulamasını yeniden başlatır.</summary>
    private async void TvReconnect_Click(object sender, RoutedEventArgs e)
    {
        _tvReconnectUntil = DateTime.Now.AddSeconds(30);
        UpdateConnectionPills();
        try
        {
            Log.Info("TV'ye yeniden bağlanılıyor (kullanıcı istedi)…");
            // Ekran bağlı değilse yayın sunucusunu baştan aç (takılı kalmış bağlantılar temizlenir)
            if (_server is not { IsRunning: true } || _server.ClientCount == 0)
            {
                _server?.Dispose();
                _server = null;
            }
            EnsureServer();
            if (_settings.TvPlatform == "webos" && _settings.TvKeyProtected != null)
                await AutoLaunchTvAsync(); // TV uygulamasını yeniden açar (gerekirse günceller)
            else
                Log.Info("TV'de Altyazı+ uygulamasını açın: uygulama bu bilgisayara kendiliğinden bağlanır.");
            // Ekran bağlanana kadar (en çok ~15 sn) düğme "Bağlanıyor…" kalır
            for (int i = 0; i < 30 && (_server?.ClientCount ?? 0) == 0; i++) await Task.Delay(500);
            if ((_server?.ClientCount ?? 0) > 0) Log.Success("TV bağlandı.");
            else Log.Warn("TV henüz bağlanmadı. TV açık mı, bilgisayarla aynı ağda mı? TV'de Altyazı+ uygulamasının açık olduğunu kontrol edin.");
        }
        catch (Exception ex) { Log.Warn("TV'ye yeniden bağlanılamadı: " + ex.Message); }
        finally
        {
            _tvReconnectUntil = DateTime.MinValue;
            UpdateConnectionPills();
        }
    }

    /// <summary>Doğrudan bağlantının durumunu ışığa yansıtır.</summary>
    private void SetPs5Pill(Ps5State state, string text)
    {
        _ps5Level = state == Ps5State.Streaming ? 2 : 1;
        _ps5Text = state switch
        {
            Ps5State.Streaming => "bağlı",
            Ps5State.Waking => "uyandırılıyor…",
            Ps5State.Retrying => "bağlantı bekleniyor…",
            _ => "bağlanıyor…",
        };
    }
}
