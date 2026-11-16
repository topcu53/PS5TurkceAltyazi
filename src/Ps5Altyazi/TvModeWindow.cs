using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ps5Altyazi.Logging;
using Ps5Altyazi.Settings;
using Ps5Altyazi.Tv;

namespace Ps5Altyazi;

/// <summary>
/// TV Modu: Türkçe altyazıyı TV'de, PS5 görüntüsünün üstünde göstermek için TV uygulamasını kurar ve başlatır.
/// LG webOS: kurulum ve başlatma buradan otomatik. Samsung Tizen ve Android TV: uygulama elle kurulur (KURULUM.md),
/// TV uygulaması bu bilgisayara bağlanır.
/// </summary>
public sealed class TvModeWindow : Window
{
    private readonly AppSettings _s;
    private readonly Func<SubtitleServer> _ensureServer;
    private readonly Action _saveSettings;

    private readonly TextBox _ipBox = new() { Width = 180, Padding = new Thickness(4), Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBox _passBox = new() { Width = 120, Padding = new Thickness(4), Margin = new Thickness(0, 0, 8, 0), FontSize = 16, MaxLength = 6 };
    private readonly ComboBox _hdmiCombo = new() { Width = 90, Margin = new Thickness(0, 0, 8, 0) };
    private readonly CheckBox _autoLaunch = new() { Content = "F6 ile çeviri başlayınca TV uygulamasını da otomatik aç", Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBox _log = new()
    {
        IsReadOnly = true, Height = 150, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = new FontFamily("Consolas"), FontSize = 12, Margin = new Thickness(0, 10, 0, 0)
    };
    private readonly TextBlock _urlText = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _otherIp = new() { FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 6, 0, 0) };
    private readonly Button[] _actionButtons;

    public TvModeWindow(AppSettings settings, Func<SubtitleServer> ensureServer, Action saveSettings)
    {
        _s = settings;
        _ensureServer = ensureServer;
        _saveSettings = saveSettings;
        Title = "TV Modu – Türkçe altyazıyı TV'de göster";
        Width = 720;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new StackPanel { Margin = new Thickness(16) };

        // Platform
        root.Children.Add(Header("TV türü"));
        var platforms = new StackPanel { Orientation = Orientation.Horizontal };
        var rbLg = new RadioButton { Content = "LG (webOS 4 ve üstü)", Margin = new Thickness(0, 0, 16, 0) };
        var rbSam = new RadioButton { Content = "Samsung (Tizen)", Margin = new Thickness(0, 0, 16, 0) };
        var rbAnd = new RadioButton { Content = "Android TV / Google TV" };
        platforms.Children.Add(rbLg);
        platforms.Children.Add(rbSam);
        platforms.Children.Add(rbAnd);
        root.Children.Add(platforms);
        root.Children.Add(Note("Nasıl çalışır: TV'ye küçük bir uygulama kurulur. Bu uygulama PS5'in HDMI görüntüsünü tam ekran gösterir ve " +
                               "PC'den gelen Türkçe altyazıyı üstüne çizer. PS5–TV kablosu değişmez. Not: Görüntü TV'nin kendi HDMI ekranı " +
                               "yerine uygulama içinde gösterildiği için TV'nin oyun modu / HDR'ı devreye girmeyebilir; gecikmeyi kendiniz deneyin."));

        var lg = new StackPanel();
        // 1. adım
        lg.Children.Add(Header("1) TV'de geliştirici modunu açın (bir kez)"));
        lg.Children.Add(Note("• Bilgisayardan webostv.developer.lge.com → SIGN IN → Create Account (ücretsiz).\n" +
                               "• TV'de LG Content Store → \"Developer Mode\" uygulamasını kurup açın, hesabınızla giriş yapın.\n" +
                               "• \"Dev Mode Status\"u açın (TV yeniden başlar). Sonra uygulamayı tekrar açıp \"Key Server\"ı açın ve ekranda bırakın."));

        // 2. adım: IP
        lg.Children.Add(Header("2) TV'nin IP adresi"));
        var ipRow = new StackPanel { Orientation = Orientation.Horizontal };
        _ipBox.Text = _s.TvIp ?? "";
        ipRow.Children.Add(_ipBox);
        var findBtn = Btn("Ağda Bul", async () => await DiscoverAsync());
        ipRow.Children.Add(findBtn);
        lg.Children.Add(ipRow);
        lg.Children.Add(Note("Bulamazsa: TV'de Ayarlar → Genel → Ağ → (Kablolu/Wi-Fi) → Gelişmiş ayarlar altında IP adresi yazar."));

        // 3. adım: Passphrase
        lg.Children.Add(Header("3) Developer Mode ekranındaki 6 karakterli Passphrase"));
        var passRow = new StackPanel { Orientation = Orientation.Horizontal };
        passRow.Children.Add(_passBox);
        var pairBtn = Btn("Bağlan", async () => await PairAsync());
        passRow.Children.Add(pairBtn);
        lg.Children.Add(passRow);
        if (_s.TvKeyProtected != null) lg.Children.Add(Note("Bu TV ile daha önce eşleştirildi. Yeniden eşleştirmek gerekmedikçe bu adımı atlayabilirsiniz."));

        // 4–5. adım
        lg.Children.Add(Header("4) Kur ve başlat"));
        var actRow = new StackPanel { Orientation = Orientation.Horizontal };
        var installBtn = Btn("TV'ye Kur", async () => await InstallAsync());
        actRow.Children.Add(installBtn);
        actRow.Children.Add(new TextBlock { Text = "PS5'in HDMI girişi:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) });
        for (int i = 1; i <= 4; i++) _hdmiCombo.Items.Add($"HDMI {i}");
        _hdmiCombo.SelectedIndex = Math.Clamp(_s.TvHdmi - 1, 0, 3);
        _hdmiCombo.SelectionChanged += (_, _) => { _s.TvHdmi = _hdmiCombo.SelectedIndex + 1; _saveSettings(); };
        actRow.Children.Add(_hdmiCombo);
        var launchBtn = Btn("TV'de Başlat", async () => await LaunchAsync());
        actRow.Children.Add(launchBtn);
        var extendBtn = Btn("Geliştirici Süresini Uzat", async () => await ExtendAsync());
        actRow.Children.Add(extendBtn);
        lg.Children.Add(actRow);

        _autoLaunch.IsChecked = _s.TvAutoLaunch;
        _autoLaunch.Checked += (_, _) => { _s.TvAutoLaunch = true; _saveSettings(); };
        _autoLaunch.Unchecked += (_, _) => { _s.TvAutoLaunch = false; _saveSettings(); };
        lg.Children.Add(_autoLaunch);
        root.Children.Add(lg);

        // Samsung / Android TV
        var other = new StackPanel { Visibility = Visibility.Collapsed };
        other.Children.Add(Header("Samsung / Android TV kurulumu"));
        other.Children.Add(Note(
            "Samsung (Tizen 2016+): Altyazı+ TV uygulaması Tizen Studio ile TV'ye kurulur (Samsung geliştirici hesabı ve sertifika " +
            "gerekir, bir kez yapılır). Adım adım anlatım: KURULUM.md → \"Samsung TV\".\n\n" +
            "Android TV / Google TV (Sony, Philips, TCL, Hisense, Xiaomi, Nvidia Shield…): AltyaziPlus-AndroidTV.apk dosyasını TV'ye " +
            "kurun (\"Downloader\" uygulaması ya da USB bellek ile). Adım adım: KURULUM.md → \"Android TV\".\n\n" +
            "TV uygulamasını açın: aynı ağdaki bu bilgisayarı kendiliğinden bulur (Android). Bulamazsa ya da Samsung'da, " +
            "uygulamada PC adresi olarak aşağıdakini girin."));
        other.Children.Add(_otherIp);
        root.Children.Add(other);

        void ShowPlatform(string p)
        {
            lg.Visibility = p == "webos" ? Visibility.Visible : Visibility.Collapsed;
            other.Visibility = p == "webos" ? Visibility.Collapsed : Visibility.Visible;
            if (_s.TvPlatform != p) { _s.TvPlatform = p; _saveSettings(); }
        }
        rbLg.Checked += (_, _) => ShowPlatform("webos");
        rbSam.Checked += (_, _) => ShowPlatform("tizen");
        rbAnd.Checked += (_, _) => ShowPlatform("android");
        (_s.TvPlatform == "tizen" ? rbSam : _s.TvPlatform == "android" ? rbAnd : rbLg).IsChecked = true;

        var mask = new CheckBox
        {
            Content = "TV'de İngilizce altyazıyı siyah kutuyla ört (Türkçe aynı yere gelir)",
            IsChecked = _s.TvMaskEnglish,
            Margin = new Thickness(0, 6, 0, 0)
        };
        mask.Checked += (_, _) => { _s.TvMaskEnglish = true; _saveSettings(); };
        mask.Unchecked += (_, _) => { _s.TvMaskEnglish = false; _saveSettings(); };
        root.Children.Add(mask);
        root.Children.Add(Note("TV kumandası (TV uygulamasında): Sol/Sağ = HDMI girişi • Yukarı/Aşağı = yazı boyutu • OK = bilgi • Geri = çıkış."));

        // Telefon / tarayıcı
        root.Children.Add(Header("Telefon / tablet / başka ekran (her cihaz)"));
        root.Children.Add(_urlText);

        root.Children.Add(_log);

        var close = new Button { Content = "Kapat", Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0), IsCancel = true };
        close.Click += (_, _) => Close();
        root.Children.Add(close);

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 900 };
        Theme.Apply(this);
        _actionButtons = new[] { findBtn, pairBtn, installBtn, launchBtn, extendBtn };

        Loaded += (_, _) =>
        {
            var server = _ensureServer();
            string? pc = SubtitleServer.GetLocalIPv4(_s.TvIp);
            _otherIp.Text = pc == null ? "Bu bilgisayarın ağ adresi bulunamadı." : $"PC adresi: {pc}";
            _urlText.Text = pc == null
                ? "Bu bilgisayarın ağ adresi bulunamadı."
                : $"Aynı Wi-Fi'deki telefon/tablette tarayıcıyla açın:  http://{pc}:{SubtitleServer.Port}/\n" +
                  "(Windows güvenlik duvarı izin sorarsa \"Özel ağlar\" için izin verin.)";
            Write($"Altyazı yayını açık (port {SubtitleServer.Port}). Bağlı ekran: {server.ClientCount}.");
        };
    }

    // ───────── Adımlar ─────────

    private async Task DiscoverAsync()
    {
        Write("Ağda LG TV aranıyor…");
        var list = await LgWebOsTv.DiscoverAsync(TimeSpan.FromSeconds(3));
        if (list.Count == 0)
        {
            Write("TV bulunamadı. TV açık ve aynı ağda mı? IP adresini elle girebilirsiniz.");
            return;
        }
        _ipBox.Text = list[0].Ip;
        Write("Bulunan TV'ler: " + string.Join(", ", list.Select(x => $"{x.Name} ({x.Ip})")));
    }

    private async Task PairAsync()
    {
        string ip = _ipBox.Text.Trim(), pass = _passBox.Text.Trim();
        if (ip.Length == 0) { Write("Önce TV'nin IP adresini girin."); return; }
        if (pass.Length != 6) { Write("Passphrase 6 karakter olmalı (Developer Mode ekranının sol altında)."); return; }
        Write("TV'den anahtar alınıyor (Key Server açık olmalı)…");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        string pem = await LgWebOsTv.FetchKeyAsync(ip, cts.Token);
        // Yeni eşleştirme: TV'nin SSH anahtarını yeniden öğren (TV sıfırlanmış olabilir)
        _s.TvHostKeys.Remove(ip);
        var tv = new LgWebOsTv(ip, pem, pass);
        string info = await tv.TestAsync();
        _s.TvIp = ip;
        _s.TvKeyProtected = SecretStore.Protect(pem);
        _s.TvPassphraseProtected = SecretStore.Protect(pass);
        _saveSettings();
        Write($"✔ Eşleştirildi: {info}. Şimdi 'TV'ye Kur'a basın. (Key Server'ı artık kapatabilirsiniz.)");
        Log.Success($"LG TV eşleştirildi: {ip} – {info}");
    }

    private LgWebOsTv RequireTv()
    {
        string? pem = SecretStore.Unprotect(_s.TvKeyProtected), pass = SecretStore.Unprotect(_s.TvPassphraseProtected);
        string ip = _ipBox.Text.Trim().Length > 0 ? _ipBox.Text.Trim() : _s.TvIp ?? "";
        if (pem == null || pass == null || ip.Length == 0)
            throw new InvalidOperationException("Önce 3. adımda TV ile eşleştirin (IP + Passphrase → Bağlan).");
        return new LgWebOsTv(ip, pem, pass);
    }

    private async Task InstallAsync()
    {
        var tv = RequireTv();
        var progress = new Progress<string>(Write);
        await tv.InstallAsync(progress, CancellationToken.None);
        Write("✔ TV uygulaması kuruldu (TV'nin uygulama listesinde \"Altyazı+\"). Şimdi 'TV'de Başlat'.");
        Log.Success("LG TV uygulaması kuruldu.");
    }

    private async Task LaunchAsync()
    {
        var tv = RequireTv();
        _ensureServer();
        if (await tv.EnsureInstalledAsync(new Progress<string>(Write), CancellationToken.None)) Write("✔ TV uygulaması güncellendi.");
        string? pc = SubtitleServer.GetLocalIPv4(tv.Ip) ?? throw new InvalidOperationException("Bu bilgisayarın ağ adresi bulunamadı.");
        await tv.LaunchAsync(pc, _s.TvHdmi);
        Write($"✔ TV uygulaması başlatıldı (PC: {pc}, HDMI {_s.TvHdmi}). TV ekranında PS5 görüntüsü ve altyazılar görünmeli.");
        Log.Success($"TV uygulaması başlatıldı (PC {pc}, HDMI {_s.TvHdmi}).");
    }

    private async Task ExtendAsync()
    {
        var tv = RequireTv();
        await tv.ExtendDevModeAsync();
        _s.TvLastExtend = DateTime.Now;
        _saveSettings();
        Write("✔ Geliştirici modu süresi yenilendi (TV'de Developer Mode uygulaması kısa süre açılabilir).");
    }

    // ───────── Yardımcılar ─────────

    private Button Btn(string text, Func<Task> action)
    {
        var b = new Button { Content = text, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        b.Click += async (_, _) =>
        {
            foreach (var x in _actionButtons) x.IsEnabled = false;
            try { await action(); }
            catch (Exception ex)
            {
                Write("✖ " + ex.Message);
                Log.Warn("TV modu: " + ex.Message);
            }
            finally
            {
                foreach (var x in _actionButtons) x.IsEnabled = true;
            }
        };
        return b;
    }

    private void Write(string line)
    {
        _log.AppendText($"{DateTime.Now:HH:mm:ss}  {line}\n");
        _log.ScrollToEnd();
    }

    private static TextBlock Header(string text) =>
        new() { Text = text, FontWeight = FontWeights.Bold, FontSize = 14, Margin = new Thickness(0, 14, 0, 4) };

    private static TextBlock Note(string text) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Theme.Dim, Margin = new Thickness(0, 2, 0, 0) };
}
