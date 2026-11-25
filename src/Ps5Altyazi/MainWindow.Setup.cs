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

    private void RunSetupWizard()
    {
        bool wasRunning = DirectRunning;
        if (wasRunning) StopCapture("Kurulum açıldı.");
        var w = new SetupWizardWindow(_settings, SaveSettings, EnsureServer,
            () => TranslationSettings_Click(this, new RoutedEventArgs()), TranslationSummary, SendTestSubtitle) { Owner = this };
        bool? done = w.ShowDialog();
        UpdateSourceUi();
        if (done == true)
        {
            Log.Success("Kurulum tamamlandı. Çeviriyi başlatmak için ▶ Başlat (F6).");
            Notify("Kurulum tamam – ▶ Başlat (F6)");
        }
        else if (!_settings.SetupCompleted)
            Log.Info("Kurulum yarıda kaldı; üstteki '🧭 Kurulum' ile devam edebilirsiniz.");
        if (wasRunning && UseDirect()) StartCapture();
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
        Ps5PillText.Text = "PS5: " + ps5Text;
        Ps5PillText.ToolTip = Ps5PillText.Text;

        int tv;
        string tvText;
        int clients = _server is { IsRunning: true } srv ? srv.ClientCount : 0;
        bool tvSetUp = _settings.TvPlatform == "webos" ? _settings.TvKeyProtected != null : _settings.TvServerEnabled;
        if (clients > 0) { tv = 2; tvText = $"bağlı ({clients} ekran)"; }
        else if (_server is { IsRunning: true } && tvSetUp) { tv = 1; tvText = "TV uygulaması bekleniyor…"; }
        else { tv = 0; tvText = tvSetUp ? "bağlı değil" : "kurulmadı"; }
        TvDot.Foreground = Level(tv);
        TvPillText.Text = "TV: " + tvText;
        TvPillText.ToolTip = TvPillText.Text;
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
