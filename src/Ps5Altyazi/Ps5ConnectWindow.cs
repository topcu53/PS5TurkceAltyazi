// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CouchSubs.RemotePlay;
using Ps5Altyazi.Logging;
using Ps5Altyazi.RemotePlayLink;
using Ps5Altyazi.Settings;

namespace Ps5Altyazi;

/// <summary>
/// PS5 Bağlantısı: Remote Play uygulaması olmadan PS5'e doğrudan bağlanmak için bir kerelik kurulum
/// (ikinci PSN hesabıyla giriş → PS5'i bul → "Cihazı Bağla" koduyla eşleştir → dene).
/// Yöntem ve Remote Play katmanı CouchSubs'tan (github.com/yorulmazsinan/couchsubs) ve chiaki-ng'den.
/// </summary>
public sealed class Ps5ConnectWindow : Window
{
    private readonly AppSettings _s;
    private readonly Action _save;

    private readonly TextBlock _accountState = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
    private readonly TextBox _pasteBox = new() { Width = 430, Padding = new Thickness(4), Margin = new Thickness(0, 0, 8, 0) };
    private readonly StackPanel _pastePanel = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
    private readonly ComboBox _consoleCombo = new() { Width = 330, Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBox _ipBox = new() { Width = 150, Padding = new Thickness(4), Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBox _pinBox = new() { Width = 150, Padding = new Thickness(4), Margin = new Thickness(0, 0, 8, 0), FontSize = 16, MaxLength = 11 };
    private readonly PasswordBox _passcodeBox = new() { Width = 80, Padding = new Thickness(4), Margin = new Thickness(0, 0, 8, 0), MaxLength = 4 };
    private readonly TextBlock _consoleState = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
    private readonly CheckBox _directCheck = new() { Content = "Program açılınca PS5'e doğrudan bağlan (PS Remote Play gerekmez)", Margin = new Thickness(0, 10, 0, 0), FontWeight = FontWeights.Bold };
    private readonly TextBox _log = new()
    {
        IsReadOnly = true, Height = 140, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = new FontFamily("Consolas"), FontSize = 12, Margin = new Thickness(0, 10, 0, 0)
    };
    private readonly List<Button> _buttons = new();

    public Ps5ConnectWindow(AppSettings settings, Action saveSettings)
    {
        _s = settings;
        _save = saveSettings;
        Title = "PS5 Bağlantısı – Remote Play uygulaması olmadan";
        Width = 760;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(Note(
            "Bir kerelik kurulum. Bittiğinde PS Remote Play uygulamasına gerek kalmaz: program açılınca PS5'e arka planda kendisi " +
            "bağlanır (PS5 dinlenmedeyse uyandırır), ekranda pencere açılmaz. Siz oyunu TV'de, kendi kolunuzla oynamaya devam edersiniz."));
        root.Children.Add(Header("Gerekenler"));
        root.Children.Add(Note(
            "• Ücretsiz İKİNCİ bir PSN hesabı, PS5'e kullanıcı olarak eklenmiş. (Kendi hesabınızla bağlanılırsa kolunuz oyundan kopar.)\n" +
            "• PS5'te ikinci kullanıcıda: Ayarlar › Sistem › Uzaktan Oynatma › \"Uzaktan Oynatmayı Etkinleştir\" açık.\n" +
            "• Önerilen: Ayarlar › Sistem › Güç Tasarrufu › Dinlenme Modunda Kullanılabilen Özellikler › \"İnternete Bağlı Kal\" ve " +
            "\"PS5'in Ağ Üzerinden Açılmasını Etkinleştir\" açık.\n" +
            "• PS Remote Play uygulaması kapalı olmalı (aynı anda tek bağlantı olur)."));

        // 1) PSN
        root.Children.Add(Header("1) İkinci PSN hesabıyla giriş"));
        root.Children.Add(_accountState);
        var row1 = new StackPanel { Orientation = Orientation.Horizontal };
        row1.Children.Add(Btn("PSN ile giriş yap", LoginAsync));
        row1.Children.Add(Btn("Tarayıcıda aç", () => { OpenInBrowser(); return Task.CompletedTask; }));
        root.Children.Add(row1);
        _pastePanel.Children.Add(_pasteBox);
        _pastePanel.Children.Add(Btn("Adresle devam", ContinueWithPastedAsync));
        root.Children.Add(_pastePanel);

        // 2) PS5
        root.Children.Add(Header("2) PS5'i bul"));
        root.Children.Add(Note("PS5 açık ya da dinlenme modunda ve bu bilgisayarla aynı ağda olmalı. Bulunamazsa IP adresini elle yazın " +
                               "(PS5: Ayarlar › Ağ › Bağlantı Durumu)."));
        var row2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        row2.Children.Add(Btn("Ağda ara", DiscoverAsync));
        row2.Children.Add(_consoleCombo);
        row2.Children.Add(new TextBlock { Text = "veya IP:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        row2.Children.Add(_ipBox);
        root.Children.Add(row2);

        // 3) Eşleştir
        root.Children.Add(Header("3) Eşleştir"));
        root.Children.Add(Note("PS5'te İKİNCİ kullanıcıya geçin → Ayarlar › Sistem › Uzaktan Oynatma › Cihazı Bağla. Ekrandaki 8 haneli " +
                               "kodu yazıp \"Eşleştir\"e basın. Eşleşince PS5'te ana hesabınıza dönüp oynayabilirsiniz."));
        var row3 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        row3.Children.Add(new TextBlock { Text = "8 haneli kod:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        row3.Children.Add(_pinBox);
        row3.Children.Add(Btn("Eşleştir", RegisterAsync));
        row3.Children.Add(new TextBlock { Text = "Giriş şifresi (varsa, 4 hane):", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0) });
        row3.Children.Add(_passcodeBox);
        root.Children.Add(row3);
        root.Children.Add(_consoleState);

        // 4) Dene
        root.Children.Add(Header("4) Dene"));
        var row4 = new StackPanel { Orientation = Orientation.Horizontal };
        row4.Children.Add(Btn("Bağlantıyı dene", TestAsync));
        row4.Children.Add(Btn("Eşlemeyi sil", () => { Forget(); return Task.CompletedTask; }));
        root.Children.Add(row4);
        root.Children.Add(_directCheck);
        root.Children.Add(_log);

        var close = new Button { Content = "Kapat", Padding = new Thickness(18, 4, 18, 4), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Right, IsCancel = true };
        close.Click += (_, _) => Close();
        root.Children.Add(close);

        Content = root;
        Theme.Apply(this);

        _passcodeBox.Password = Ps5Link.LoadPasscode(_s) ?? "";
        _passcodeBox.PasswordChanged += (_, _) =>
        {
            string p = _passcodeBox.Password.Trim();
            _s.Ps5LoginPasscodeProtected = p.Length == 0 ? null : CouchSubs.RemotePlay.LoginPasscode.Normalized(p) is { } ok ? SecretStore.Protect(ok) : _s.Ps5LoginPasscodeProtected;
            _save();
        };
        _directCheck.IsChecked = _s.DirectConnect;
        _directCheck.Checked += (_, _) => { _s.DirectConnect = true; _save(); };
        _directCheck.Unchecked += (_, _) => { _s.DirectConnect = false; _save(); };

        RefreshState();
        if (!Ps5Link.LibraryAvailable)
            Write("✖ PS5 bağlantı kütüphanesi (altyazi_rp.dll) bu derlemede yok. GitHub'daki son sürümü indirin.");
        else if (Ps5Link.LoadConsole(_s) is null)
            Loaded += async (_, _) => await Run(DiscoverAsync);
    }

    // ───────── Adımlar ─────────

    private async Task LoginAsync()
    {
        if (!PsnLoginWindow.IsBrowserAvailable())
        {
            Write("Program içi tarayıcı (Microsoft Edge WebView2) bulunamadı; giriş tarayıcınızda açılıyor.");
            OpenInBrowser();
            return;
        }
        var (redirect, problem, useBrowser) = PsnLoginWindow.Show(this);
        if (redirect == null)
        {
            if (useBrowser)
            {
                Write("Sony'nin sayfası program içinde sunucuya bağlanamadı; giriş tarayıcınızda açılıyor.");
                OpenInBrowser();
            }
            else if (problem != null) Write($"✖ Giriş tamamlanmadı ({problem}). Tekrar deneyin ya da 'Tarayıcıda aç'.");
            else Write("Giriş yapılmadı.");
            return;
        }
        await FinishLoginAsync(redirect);
    }

    private void OpenInBrowser()
    {
        try { Process.Start(new ProcessStartInfo(PsnAccount.AuthorizeUrl.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { Write("✖ Tarayıcı açılamadı: " + ex.Message); return; }
        _pastePanel.Visibility = Visibility.Visible;
        Write("Tarayıcıda İKİNCİ hesabınızla giriş yapın. Giriş bitince sayfa boş ya da \"bulunamadı\" görünebilir; " +
              "adres çubuğundaki adresin TAMAMINI (remoteplay.dl.playstation.net/... ile başlar) kopyalayıp aşağıya yapıştırın.");
    }

    private async Task ContinueWithPastedAsync()
    {
        var url = PsnAccount.RedirectFromPastedText(_pasteBox.Text);
        if (url == null) { Write("✖ Yapıştırılan metinde Sony'nin yönlendirme adresi yok (remoteplay.dl.playstation.net/remoteplay/redirect?code=…)."); return; }
        await FinishLoginAsync(url);
        _pasteBox.Clear();
    }

    private async Task FinishLoginAsync(Uri redirect)
    {
        Write("Hesap kimliği alınıyor…");
        try
        {
            byte[] id = await PsnAccount.AccountIdFromRedirectAsync(redirect);
            Ps5Link.SaveAccountId(_s, id);
            _save();
            _pastePanel.Visibility = Visibility.Collapsed;
            Write("✔ PSN girişi tamam. (Şifre ve oturum saklanmadı; yalnız hesap kimliği şifreli kaydedildi.)");
            Log.Success("PSN girişi tamam (ikinci hesap).");
        }
        catch (PsnAccountException ex)
        {
            Write("✖ " + Ps5Link.Describe(ex));
        }
        RefreshState();
    }

    private sealed record ConsoleItem(ConsoleInfo Info)
    {
        public override string ToString() => $"{Info.Name} – {Info.Host} ({(Info.IsAwake ? "açık" : "dinlenmede")})";
    }

    private async Task DiscoverAsync()
    {
        Write("Ağda PS5 aranıyor…");
        var found = await ConsoleDiscovery.DiscoverAsync(timeout: TimeSpan.FromSeconds(3));
        _consoleCombo.ItemsSource = found.Select(c => new ConsoleItem(c)).ToList();
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
        byte[]? account = Ps5Link.AccountId(_s);
        if (account == null) { Write("✖ Önce 1. adımda ikinci PSN hesabıyla giriş yapın."); return; }
        string host = _ipBox.Text.Trim();
        if (host.Length == 0 && _consoleCombo.SelectedItem is ConsoleItem item) host = item.Info.Host;
        if (host.Length == 0) { Write("✖ Önce 2. adımda PS5'i bulun ya da IP adresini yazın."); return; }
        uint? pin = Registration.ParsePin(_pinBox.Text);
        if (pin == null) { Write("✖ Kod 8 rakam olmalı (PS5'in 'Cihazı Bağla' ekranındaki)."); return; }

        Write($"PS5 ile eşleştiriliyor ({host})… (30 sn kadar sürebilir)");
        try
        {
            var console = await Registration.RegisterAsync(host, account, pin.Value);
            Ps5Link.SaveConsole(_s, console);
            _s.DirectConnect = true;
            _directCheck.IsChecked = true;
            _save();
            _pinBox.Clear();
            Write($"✔ Eşleşti: {console.Name} ({console.Host}). Şimdi 'Bağlantıyı dene'. PS5'te ana hesabınıza dönebilirsiniz.");
            Log.Success($"PS5 eşleşti: {console.Name} ({console.Host}).");
        }
        catch (RegistrationException ex)
        {
            Write("✖ " + Ps5Link.Describe(ex));
            Log.Warn("PS5 eşleştirme: " + ex.Failure + " " + ex.Detail);
        }
        RefreshState();
    }

    private async Task TestAsync()
    {
        var console = Ps5Link.LoadConsole(_s);
        byte[]? account = Ps5Link.AccountId(_s);
        if (console == null || account == null) { Write("✖ Önce giriş yapıp PS5'i eşleştirin."); return; }
        Write("PS5'e bağlanılıyor (dinlenmedeyse uyandırılıyor; 1 dakikaya kadar sürebilir)…");
        var frame = new TaskCompletionSource<(int W, int H)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopReason = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var session = new StreamSession(console, account, Ps5Link.LoadPasscode(_s));
        session.FrameReceived += f => frame.TrySetResult((f.Width, f.Height));
        session.StateChanged += st =>
        {
            if (st.Phase == SessionPhase.Streaming) Dispatcher.InvokeAsync(() => Write("Bağlandı, görüntü bekleniyor…"));
            else if (st.Phase == SessionPhase.Disconnected)
            {
                string text = Ps5Link.Describe(st.Reason);
                if (session.HasGivenUp) stopReason.TrySetResult(text);
                else Dispatcher.InvokeAsync(() => Write("… " + text));
            }
        };
        session.ConsoleMoved += moved => Dispatcher.InvokeAsync(() => { Ps5Link.SaveConsole(_s, moved); _save(); });
        session.Start();
        var done = await Task.WhenAny(frame.Task, stopReason.Task, Task.Delay(TimeSpan.FromSeconds(75)));
        session.Stop();
        if (done == frame.Task)
        {
            var (w, h) = frame.Task.Result;
            Write($"✔ Görüntü geliyor ({w}×{h}). Kurulum tamam: F6 ile çeviriyi başlatın, Remote Play uygulamasına gerek yok.");
            Log.Success($"PS5 doğrudan bağlantı denemesi başarılı ({w}×{h}).");
        }
        else if (done == stopReason.Task) Write("✖ " + stopReason.Task.Result);
        else Write("✖ 75 sn içinde görüntü gelmedi. PS5 açık mı, aynı ağda mı? Ayrıntı: %LOCALAPPDATA%\\PS5TurkceAltyazi\\remoteplay.log");
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

    private void RefreshState()
    {
        bool hasAccount = Ps5Link.AccountId(_s) != null;
        _accountState.Text = hasAccount ? "✔ Giriş yapıldı (hesap kimliği kayıtlı)." : "Henüz giriş yapılmadı.";
        _accountState.Foreground = hasAccount ? Theme.Good : Theme.Dim;
        var c = Ps5Link.LoadConsole(_s);
        _consoleState.Text = c != null ? $"✔ Eşlenmiş PS5: {c.Name} ({c.Host})" : "Henüz eşlenmiş PS5 yok.";
        _consoleState.Foreground = c != null ? Theme.Good : Theme.Dim;
        _directCheck.IsEnabled = c != null && hasAccount && Ps5Link.LibraryAvailable;
    }

    // ───────── Yardımcılar ─────────

    private Button Btn(string text, Func<Task> action)
    {
        var b = new Button { Content = text, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        b.Click += async (_, _) => await Run(action);
        _buttons.Add(b);
        return b;
    }

    private async Task Run(Func<Task> action)
    {
        foreach (var x in _buttons) x.IsEnabled = false;
        try { await action(); }
        catch (DllNotFoundException) { Write("✖ PS5 bağlantı kütüphanesi (altyazi_rp.dll) yüklenemedi."); }
        catch (Exception ex)
        {
            Write("✖ " + ex.Message);
            Log.Warn("PS5 bağlantısı: " + ex.Message);
        }
        finally
        {
            foreach (var x in _buttons) x.IsEnabled = true;
        }
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
