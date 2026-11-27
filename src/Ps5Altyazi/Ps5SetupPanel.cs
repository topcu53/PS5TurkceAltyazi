using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ps5Altyazi.Logging;
using Ps5Altyazi.Ps5;
using Ps5Altyazi.Settings;

namespace Ps5Altyazi;

/// <summary>
/// PS5 bağlantısı kurulum paneli (hem kurulum sihirbazında hem "PS5 Bağlantısı" penceresinde): PS Remote Play uygulaması olmadan PS5'e doğrudan bağlanmak için bir kerelik kurulum
/// (ikinci PSN hesabıyla giriş → PS5'i bul → PS5'teki "Cihazı Bağla" koduyla eşleştir → dene).
/// </summary>
public sealed class Ps5SetupPanel : UserControl
{
    private readonly AppSettings _s;
    private readonly Action _save;

    private readonly TextBlock _accountState = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
    private readonly TextBox _pasteBox = new() { Width = 440, Padding = new Thickness(4), Margin = new Thickness(0, 0, 8, 0) };
    private readonly StackPanel _pastePanel = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
    private readonly ComboBox _consoleCombo = new() { Width = 320, Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBox _ipBox = new() { Width = 140, Padding = new Thickness(4), Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBox _pinBox = new() { Width = 150, Padding = new Thickness(4), Margin = new Thickness(0, 0, 8, 0), FontSize = 16, MaxLength = 12 };
    private readonly PasswordBox _passcodeBox = new() { Width = 80, Padding = new Thickness(4), Margin = new Thickness(0, 0, 8, 0), MaxLength = 4 };
    private readonly TextBlock _consoleState = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
    private readonly CheckBox _directCheck = new() { Content = "Program açılınca PS5'e doğrudan bağlan (PS Remote Play gerekmez)", Margin = new Thickness(0, 10, 0, 0), FontWeight = FontWeights.Bold };
    private readonly TextBox _log = new()
    {
        IsReadOnly = true, Height = 140, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = new FontFamily("Consolas"), FontSize = 12, Margin = new Thickness(0, 10, 0, 0)
    };
    private readonly List<Button> _buttons = new();

    public Ps5SetupPanel(AppSettings settings, Action saveSettings)
    {
        _s = settings;
        _save = saveSettings;
        var root = new StackPanel();
        root.Children.Add(Note(
            "Bir kerelik kurulum. Bittiğinde PS Remote Play uygulamasına gerek kalmaz: Altyazı+ açılınca PS5'e arka planda kendisi " +
            "bağlanır (PS5 dinlenmedeyse uyandırır), ekranda pencere açılmaz. Siz oyunu TV'de, kendi kolunuzla oynarsınız."));
        root.Children.Add(Header("Gerekenler"));
        root.Children.Add(Note(
            "• Ücretsiz İKİNCİ bir PSN hesabı, PS5'e kullanıcı olarak eklenmiş. (Bağlantı sizin hesabınızla kurulursa PS5 kolunuzu " +
            "oyundan koparır; ikinci hesap yalnız Altyazı+ içindir.)\n" +
            "• PS5'te ikinci kullanıcıda: Ayarlar › Sistem › Uzaktan Oynatma › \"Uzaktan Oynatmayı Etkinleştir\" açık.\n" +
            "• Önerilen: Ayarlar › Sistem › Güç Tasarrufu › Dinlenme Modunda Kullanılabilen Özellikler › \"İnternete Bağlı Kal\" ve " +
            "\"PS5'in Ağ Üzerinden Açılmasını Etkinleştir\" açık.\n" +
            "• PS Remote Play uygulaması kapalı olmalı (PS5 aynı anda tek bağlantı kabul eder)."));

        root.Children.Add(Header("1) İkinci PSN hesabıyla giriş"));
        root.Children.Add(_accountState);
        var r1 = Row();
        r1.Children.Add(Btn("PSN ile giriş yap", LoginAsync));
        r1.Children.Add(Btn("Tarayıcıda aç", () => { OpenInBrowser(); return Task.CompletedTask; }));
        root.Children.Add(r1);
        _pastePanel.Children.Add(_pasteBox);
        _pastePanel.Children.Add(Btn("Adresle devam", async () =>
        {
            string? url = PsnLogin.FindRedirect(_pasteBox.Text);
            if (url == null) { Write("✖ Yapıştırılan metinde \"remoteplay.dl.playstation.net/remoteplay/redirect?code=…\" adresi yok."); return; }
            await FinishLoginAsync(url);
        }));
        root.Children.Add(_pastePanel);

        root.Children.Add(Header("2) PS5'i bul"));
        root.Children.Add(Note("PS5 açık ya da dinlenme modunda ve bu bilgisayarla aynı ağda olmalı. Bulunamazsa IP adresini yazın " +
                               "(PS5: Ayarlar › Ağ › Bağlantı Durumu)."));
        var r2 = Row();
        r2.Children.Add(Btn("Ağda ara", DiscoverAsync));
        r2.Children.Add(_consoleCombo);
        r2.Children.Add(new TextBlock { Text = "veya IP:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        r2.Children.Add(_ipBox);
        root.Children.Add(r2);

        root.Children.Add(Header("3) Eşleştir"));
        root.Children.Add(Note("PS5'te İKİNCİ kullanıcıya geçin → Ayarlar › Sistem › Uzaktan Oynatma › Cihazı Bağla. Ekrandaki 8 haneli " +
                               "kodu yazıp \"Eşleştir\"e basın. Eşleşince PS5'te kendi hesabınıza dönebilirsiniz."));
        var r3 = Row();
        r3.Children.Add(new TextBlock { Text = "8 haneli kod:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        r3.Children.Add(_pinBox);
        r3.Children.Add(Btn("Eşleştir", RegisterAsync));
        r3.Children.Add(new TextBlock { Text = "Giriş şifresi (varsa, 4 hane):", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0) });
        r3.Children.Add(_passcodeBox);
        root.Children.Add(r3);
        root.Children.Add(_consoleState);

        root.Children.Add(Header("4) Dene"));
        var r4 = Row();
        r4.Children.Add(Btn("Bağlantıyı dene", TestAsync));
        r4.Children.Add(Btn("Eşlemeyi sil", () => { Forget(); return Task.CompletedTask; }));
        root.Children.Add(r4);
        root.Children.Add(_directCheck);
        root.Children.Add(_log);

        Content = root;

        _passcodeBox.Password = Ps5Store.Passcode(_s) ?? "";
        _passcodeBox.PasswordChanged += (_, _) =>
        {
            string p = _passcodeBox.Password.Trim();
            if (p.Length == 0) _s.Ps5LoginPasscodeProtected = null;
            else if (Regex.IsMatch(p, "^[0-9]{4}$")) _s.Ps5LoginPasscodeProtected = SecretStore.Protect(p);
            _save();
        };
        _directCheck.IsChecked = _s.DirectConnect;
        _directCheck.Checked += (_, _) => { _s.DirectConnect = true; _save(); };
        _directCheck.Unchecked += (_, _) => { _s.DirectConnect = false; _save(); };

        RefreshState();
        if (!Ps5Helper.Available)
            Write("✖ PS5 bağlantı yardımcısı (ps5-baglanti.exe) bu derlemede yok. GitHub'daki son sürümü indirin.");
        else if (Ps5Store.Pairing(_s) == null)
            Loaded += async (_, _) => await RunGuarded(DiscoverAsync);
    }

    // ───────── Adımlar ─────────

    private async Task LoginAsync()
    {
        if (!PsnLoginWindow.WebViewInstalled)
        {
            Write("Program içi tarayıcı (Microsoft Edge WebView2) yok; giriş tarayıcınızda açılıyor.");
            OpenInBrowser();
            return;
        }
        var (url, error) = await PsnLoginWindow.RunAsync();
        if (url == null)
        {
            Write(error != null ? $"✖ Giriş tamamlanmadı ({error}). Tekrar deneyin ya da 'Tarayıcıda aç'." : "Giriş yapılmadı.");
            return;
        }
        await FinishLoginAsync(url);
    }

    private void OpenInBrowser()
    {
        try { Process.Start(new ProcessStartInfo(PsnLogin.LoginUrl) { UseShellExecute = true }); }
        catch (Exception ex) { Write("✖ Tarayıcı açılamadı: " + ex.Message); return; }
        _pastePanel.Visibility = Visibility.Visible;
        Write("Tarayıcıda İKİNCİ hesabınızla giriş yapın. Giriş bitince sayfa boş ya da hata gibi görünebilir; adres çubuğundaki " +
              "adresin TAMAMINI (remoteplay.dl.playstation.net/... ile başlar) kopyalayıp aşağıya yapıştırın.");
    }

    private async Task FinishLoginAsync(string redirectUrl)
    {
        Write("Hesap kimliği alınıyor…");
        try
        {
            byte[] id = await PsnLogin.AccountIdAsync(redirectUrl);
            Ps5Store.SaveAccountId(_s, id);
            _save();
            _pastePanel.Visibility = Visibility.Collapsed;
            _pasteBox.Clear();
            Write("✔ PSN girişi tamam. (Şifre ve oturum saklanmadı; yalnız hesap kimliği şifreli kaydedildi.)");
            Log.Success("PSN girişi tamam (ikinci hesap).");
        }
        catch (InvalidOperationException ex) { Write("✖ " + ex.Message); }
        RefreshState();
    }

    private async Task DiscoverAsync()
    {
        Write("Ağda PS5 aranıyor…");
        var found = await Ps5Helper.DiscoverAsync();
        _consoleCombo.ItemsSource = found;
        if (found.Count == 0)
        {
            Write("PS5 bulunamadı. PS5 açık ya da dinlenmede mi, aynı ağda mı? IP adresini elle yazabilirsiniz.");
            return;
        }
        _consoleCombo.SelectedIndex = 0;
        Write("Bulunan: " + string.Join(", ", found.Select(c => $"{c.Name} ({c.Host})")));
    }

    private async Task RegisterAsync()
    {
        byte[]? account = Ps5Store.AccountId(_s);
        if (account == null) { Write("✖ Önce 1. adımda ikinci PSN hesabıyla giriş yapın."); return; }
        string host = _ipBox.Text.Trim();
        string mac = "";
        if (host.Length == 0 && _consoleCombo.SelectedItem is Ps5Console c) { host = c.Host; mac = c.Id; }
        if (host.Length == 0) { Write("✖ Önce 2. adımda PS5'i bulun ya da IP adresini yazın."); return; }
        string pin = new string(_pinBox.Text.Where(char.IsDigit).ToArray());
        if (pin.Length != 8) { Write("✖ Kod 8 rakam olmalı (PS5'in 'Cihazı Bağla' ekranındaki)."); return; }

        Write($"PS5 ile eşleştiriliyor ({host})… (30 sn kadar sürebilir)");
        try
        {
            var p = await Ps5Helper.RegisterAsync(host, account, pin);
            if (p.Mac.Length == 0) p.Mac = mac;
            if (p.Name.Length == 0) p.Name = "PS5";
            Ps5Store.SavePairing(_s, p);
            _s.DirectConnect = true;
            _directCheck.IsChecked = true;
            _save();
            _pinBox.Clear();
            Write($"✔ Eşleşti: {p.Name} ({p.Host}). Şimdi 'Bağlantıyı dene'. PS5'te kendi hesabınıza dönebilirsiniz.");
            Log.Success($"PS5 eşleşti: {p.Name} ({p.Host}).");
        }
        catch (InvalidOperationException ex)
        {
            Write("✖ " + ex.Message);
            Log.Warn("PS5 eşleştirme: " + ex.Message);
        }
        RefreshState();
    }

    private async Task TestAsync()
    {
        var pairing = Ps5Store.Pairing(_s);
        byte[]? account = Ps5Store.AccountId(_s);
        if (pairing == null || account == null) { Write("✖ Önce giriş yapıp PS5'i eşleştirin."); return; }
        Write("PS5'e bağlanılıyor (dinlenmedeyse uyandırılıyor; 1 dakikaya kadar sürebilir)…");
        var frame = new TaskCompletionSource<(int W, int H)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gaveUp = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var conn = new Ps5Connection(pairing, account, Ps5Store.Passcode(_s), 5);
        string last = "";
        conn.FrameReceived += (_, w, h) => frame.TrySetResult((w, h));
        conn.StateChanged += (_, text) => { if (text != last) { last = text; Dispatcher.InvokeAsync(() => Write("… " + text)); } };
        conn.GaveUp += reason => gaveUp.TrySetResult(reason);
        conn.Moved += p => Dispatcher.InvokeAsync(() => { Ps5Store.SavePairing(_s, p); _save(); });
        conn.Start();
        var done = await Task.WhenAny(frame.Task, gaveUp.Task, Task.Delay(TimeSpan.FromSeconds(90)));
        conn.Stop();
        if (done == frame.Task)
        {
            var (w, h) = frame.Task.Result;
            Write($"✔ Görüntü geliyor ({w}×{h}). Kurulum tamam: pencereyi kapatıp F6 ile çeviriyi başlatın.");
            Log.Success($"PS5 doğrudan bağlantı denemesi başarılı ({w}×{h}).");
        }
        else if (done == gaveUp.Task) Write("✖ " + gaveUp.Task.Result);
        else Write("✖ 90 sn içinde görüntü gelmedi. Ayrıntı: " + Ps5Helper.LogPath);
    }

    private void Forget()
    {
        _s.Ps5ConsoleProtected = null;
        _s.PsnAccountIdProtected = null;
        _s.DirectConnect = false;
        _directCheck.IsChecked = false;
        _save();
        Write("PS5 eşlemesi ve PSN hesap kimliği silindi.");
        RefreshState();
    }

    /// <summary>Giriş / eşleme durumu değişti (sihirbaz ilerlemeyi günceller).</summary>
    public event Action? Changed;

    /// <summary>İkinci hesapla giriş yapıldı ve PS5 eşlendi mi.</summary>
    public bool IsComplete => Ps5Store.IsReady(_s);

    private void RefreshState()
    {
        Changed?.Invoke();
        bool hasAccount = Ps5Store.AccountId(_s) != null;
        _accountState.Text = hasAccount ? "✔ Giriş yapıldı (hesap kimliği kayıtlı)." : "Henüz giriş yapılmadı.";
        _accountState.Foreground = hasAccount ? Theme.Good : Theme.Dim;
        var p = Ps5Store.Pairing(_s);
        _consoleState.Text = p != null ? $"✔ Eşlenmiş PS5: {p.Name} ({p.Host})" : "Henüz eşlenmiş PS5 yok.";
        _consoleState.Foreground = p != null ? Theme.Good : Theme.Dim;
        _directCheck.IsEnabled = p != null && hasAccount && Ps5Helper.Available;
    }

    // ───────── Yardımcılar ─────────

    private static StackPanel Row() => new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };

    private Button Btn(string text, Func<Task> action)
    {
        var b = new Button { Content = text, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        b.Click += async (_, _) => await RunGuarded(action);
        _buttons.Add(b);
        return b;
    }

    private async Task RunGuarded(Func<Task> action)
    {
        foreach (var x in _buttons) x.IsEnabled = false;
        try { await action(); }
        catch (Exception ex)
        {
            Write("✖ " + ex.Message);
            Log.Warn("PS5 bağlantısı: " + ex.Message);
        }
        finally { foreach (var x in _buttons) x.IsEnabled = true; }
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

/// <summary>"🎮 PS5 Bağlantısı" penceresi: kurulum panelini gösterir.</summary>
public sealed class Ps5ConnectWindow : PageWindow
{
    public Ps5ConnectWindow(AppSettings settings, Action saveSettings)
    {
        Title = "PS5 Bağlantısı – PS Remote Play uygulaması olmadan";
        Width = 760;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new Ps5SetupPanel(settings, saveSettings));
        var close = new Button { Content = "Kapat", Padding = new Thickness(18, 4, 18, 4), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Right, IsCancel = true };
        close.Click += (_, _) => Finish();
        root.Children.Add(close);
        Content = root;
        Theme.Apply(this);
    }
}
