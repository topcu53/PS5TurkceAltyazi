using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Ps5Altyazi.Translation;

namespace Ps5Altyazi;

/// <summary>Çeviri servisleri ayarları (sonuç).</summary>
public sealed record TranslationSettingsResult(
    string? DeepLKey, string? AzureKey, string? AzureRegion, bool MyMemoryEnabled, string? MyMemoryEmail,
    string? GeminiKey = null, string? GeminiModel = null);

/// <summary>
/// Çeviri servisleri penceresi (kod ile oluşturulur):
/// 1) DeepL (asıl)  2) Google Gemini (isteğe bağlı yedek)  3) Azure Translator (isteğe bağlı)  4) MyMemory (anahtarsız son yedek).
/// </summary>
public sealed class TranslationSettingsWindow : PageWindow
{
    private readonly PasswordBox _deepLBox = NewPasswordBox();
    private readonly PasswordBox _azureBox = NewPasswordBox();
    private readonly PasswordBox _geminiBox = NewPasswordBox();
    private readonly TextBox _geminiModel = new() { Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(4), Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _azureRegion = new() { Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(4), Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly CheckBox _myMemoryCheck = new() { Content = "MyMemory yedeğini kullan (anahtarsız, ücretsiz)", Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBox _myMemoryEmail = new() { Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(4) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), FontSize = 13 };
    private readonly Button _saveBtn = new() { Content = "Kaydet ve dene", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };

    private readonly string? _curDeepL, _curAzure, _curGemini;

    public TranslationSettingsResult? Result { get; private set; }

    private static PasswordBox NewPasswordBox() => new() { Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(4), FontSize = 14 };

    public TranslationSettingsWindow(string? deepLKey, string? azureKey, string? azureRegion, bool myMemoryEnabled, string? myMemoryEmail,
        string? geminiKey = null, string? geminiModel = null, Ps5Altyazi.Settings.AppSettings? offlineSettings = null)
    {
        _curDeepL = deepLKey;
        _curGemini = geminiKey;
        _curAzure = azureKey;
        Title = "Çeviri ayarları";
        Width = 600;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(Note("Sıra: DeepL → Gemini → Azure (anahtar girildiyse) → MyMemory. Bir servisin ücretsiz kotası dolunca " +
                               "program otomatik olarak sıradakine geçer, kota yenilenince DeepL'e kendiliğinden döner. " +
                               "Anahtarlar bu bilgisayarda Windows şifrelemesiyle saklanır."));

        // 1) DeepL
        root.Children.Add(Header("1) DeepL API – asıl çevirmen"));
        root.Children.Add(LinkNote("Ücretsiz anahtar: ", "https://www.deepl.com/pro-api",
            " → \"DeepL API Free\" → Hesap > API Keys (\":fx\" ile biter). Ayda 500.000 karakter ücretsiz."));
        root.Children.Add(_deepLBox);
        if (!string.IsNullOrEmpty(deepLKey)) root.Children.Add(Note($"Kayıtlı: {DeepLTranslator.Mask(deepLKey)} (değiştirmek için yenisini yazın)"));

        // 2) Gemini
        root.Children.Add(Header("2) Google Gemini – yedek (isteğe bağlı, kart gerekmez)"));
        root.Children.Add(LinkNote("Ücretsiz anahtar: ", "https://aistudio.google.com/apikey",
            " → \"Create API key\". Ücretsiz katmanda dakikalık/günlük sınır var; dolunca sıradaki servise geçilir. " +
            "Not: Gemini uygulaması (Pro) aboneliği API kotası vermez. Ücretsiz katmanda gönderilen metinler Google tarafından " +
            "ürün geliştirmede kullanılabilir (yalnızca oyun altyazısı gönderilir). Anahtarı kimseyle paylaşmayın."));
        root.Children.Add(_geminiBox);
        if (!string.IsNullOrEmpty(geminiKey)) root.Children.Add(Note($"Kayıtlı: {DeepLTranslator.Mask(geminiKey)}"));
        var modelRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        modelRow.Children.Add(new TextBlock { Text = "Model (boş = en hızlı Flash-Lite): ", VerticalAlignment = VerticalAlignment.Center });
        _geminiModel.Text = geminiModel ?? "";
        modelRow.Children.Add(_geminiModel);
        root.Children.Add(modelRow);

        // 3) Azure
        root.Children.Add(Header("3) Azure Translator – yedek (isteğe bağlı)"));
        root.Children.Add(LinkNote("Ayda 2 milyon karakter ücretsiz (F0). Kayıt kredi/banka kartı ister. ", "https://portal.azure.com",
            " → Translator kaynağı oluştur (Fiyat katmanı: F0) → Keys and Endpoint: anahtar + konum/bölge."));
        root.Children.Add(_azureBox);
        if (!string.IsNullOrEmpty(azureKey)) root.Children.Add(Note($"Kayıtlı: {DeepLTranslator.Mask(azureKey)}"));
        var regionRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        regionRow.Children.Add(new TextBlock { Text = "Bölge (ör. westeurope, boş = global): ", VerticalAlignment = VerticalAlignment.Center });
        _azureRegion.Text = azureRegion ?? "";
        regionRow.Children.Add(_azureRegion);
        root.Children.Add(regionRow);

        // 3) MyMemory
        root.Children.Add(Header("4) MyMemory – son yedek (anahtar gerekmez)"));
        _myMemoryCheck.IsChecked = myMemoryEnabled;
        root.Children.Add(_myMemoryCheck);
        root.Children.Add(Note("Günlük sınır e-postasız ~5.000, e-postayla ~50.000 karakter. E-posta isteğe bağlıdır; " +
                               "yalnızca MyMemory'ye gönderilir (kayıt/doğrulama yok). Kalitesi DeepL'den düşüktür."));
        _myMemoryEmail.Text = myMemoryEmail ?? "";
        root.Children.Add(_myMemoryEmail);

        // 5) Bilgisayarda çeviri
        root.Children.Add(Header("5) Bilgisayarda çeviri – internetsiz, kotasız (önerilir)"));
        root.Children.Add(Note("İnternet servislerinin kotası / hız sınırı dolunca ya da geç kalınca çeviri bilgisayarınızda devam eder; " +
                               "çeviri hiç durmaz. Ekran kartı gerekmez (işlemcide çalışır); varsa ekran kartı (NVIDIA / AMD / Intel, DirectX 12) " +
                               "kullanılır ve daha hızlıdır. Model bir kez indirilir (işlemci ~530 MB, ekran kartı için +~890 MB; " +
                               "Helsinki-NLP opus-mt-tc-big-en-tr)."));
        root.Children.Add(BuildOfflineRow());
        if (offlineSettings != null) root.Children.Add(BuildDeviceRow(offlineSettings));

        root.Children.Add(_status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        _saveBtn.Click += async (_, _) => await SaveAndTestAsync();
        buttons.Children.Add(_saveBtn);

        var clearDeepL = new Button { Content = "DeepL anahtarını sil", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        clearDeepL.Click += (_, _) => { _deepLBox.Clear(); _clearDeepL = true; SetStatus("DeepL anahtarı kaydedince silinecek.", Theme.Warn); };
        buttons.Children.Add(clearDeepL);

        var clearAzure = new Button { Content = "Azure anahtarını sil", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        clearAzure.Click += (_, _) => { _azureBox.Clear(); _clearAzure = true; SetStatus("Azure anahtarı kaydedince silinecek.", Theme.Warn); };
        buttons.Children.Add(clearAzure);

        var clearGemini = new Button { Content = "Gemini anahtarını sil", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        clearGemini.Click += (_, _) => { _geminiBox.Clear(); _clearGemini = true; SetStatus("Gemini anahtarı kaydedince silinecek.", Theme.Warn); };
        buttons.Children.Add(clearGemini);

        var closeBtn = new Button { Content = "Kapat", Padding = new Thickness(12, 4, 12, 4), IsCancel = true };
        closeBtn.Click += (_, _) => Finish();
        buttons.Children.Add(closeBtn);

        root.Children.Add(buttons);
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        MaxHeight = SystemParameters.WorkArea.Height * 0.94;
        Theme.Apply(this);
        WhenOpened(() => _deepLBox.Focus());
    }

    private bool _clearDeepL, _clearAzure, _clearGemini;

    private async Task SaveAndTestAsync()
    {
        string? deepL = _deepLBox.Password.Trim();
        if (deepL.Length == 0) deepL = _clearDeepL ? null : _curDeepL;
        string? azure = _azureBox.Password.Trim();
        if (azure.Length == 0) azure = _clearAzure ? null : _curAzure;
        string? region = string.IsNullOrWhiteSpace(_azureRegion.Text) ? null : _azureRegion.Text.Trim();
        string? gemini = _geminiBox.Password.Trim();
        if (gemini.Length == 0) gemini = _clearGemini ? null : _curGemini;
        string? gModel = string.IsNullOrWhiteSpace(_geminiModel.Text) ? null : _geminiModel.Text.Trim();
        bool mm = _myMemoryCheck.IsChecked == true;
        string? email = string.IsNullOrWhiteSpace(_myMemoryEmail.Text) ? null : _myMemoryEmail.Text.Trim();

        if (deepL == null && azure == null && gemini == null && !mm)
        {
            SetStatus("En az bir çeviri servisi gerekli (DeepL anahtarı önerilir).", Theme.Bad);
            return;
        }

        Result = new TranslationSettingsResult(deepL, azure, region, mm, email, gemini, gModel);
        _saveBtn.IsEnabled = false;
        SetStatus("Kaydedildi. Servisler test ediliyor…", Theme.Dim);

        const string sample = "Hey, partner. We need to talk.";
        var report = new StringBuilder();
        bool anyOk = false;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        if (deepL != null)
        {
            var tr = new DeepLTranslator(deepL);
            try
            {
                var (used, limit) = await tr.GetUsageAsync(cts.Token);
                string t = await tr.TranslateAsync(sample, null, cts.Token);
                report.AppendLine($"✔ {tr.Name}: \"{t}\"  (bu ay {used:N0} / {limit:N0} karakter)");
                anyOk = true;
            }
            catch (Exception ex) { report.AppendLine($"✖ DeepL: {ex.Message}"); }
        }
        else report.AppendLine("– DeepL: anahtar yok");

        if (gemini != null)
        {
            try
            {
                var g = new GeminiTranslator(gemini, gModel);
                var gsw = Stopwatch.StartNew();
                string t = await g.TranslateAsync(sample, null, cts.Token);
                report.AppendLine($"✔ Google Gemini ({g.Model}): \"{t}\"  ({gsw.ElapsedMilliseconds} ms)");
                anyOk = true;
            }
            catch (Exception ex) { report.AppendLine($"✖ Gemini: {ex.Message}"); }
        }
        else report.AppendLine("– Gemini: anahtar yok (isteğe bağlı)");

        if (azure != null)
        {
            try
            {
                string t = await new AzureTranslator(azure, region).TranslateAsync(sample, null, cts.Token);
                report.AppendLine($"✔ Azure Translator: \"{t}\"");
                anyOk = true;
            }
            catch (Exception ex) { report.AppendLine($"✖ Azure: {ex.Message}"); }
        }
        else report.AppendLine("– Azure: anahtar yok (isteğe bağlı)");

        if (mm)
        {
            try
            {
                string t = await new MyMemoryTranslator(email).TranslateAsync(sample, null, cts.Token);
                report.AppendLine($"✔ MyMemory: \"{t}\"");
                anyOk = true;
            }
            catch (Exception ex) { report.AppendLine($"✖ MyMemory: {ex.Message}"); }
        }
        else report.AppendLine("– MyMemory: kapalı");

        report.Append(anyOk ? "Ayarlar kaydedildi; bu sayfayı kapatabilirsiniz." : "Hiçbir servis çalışmadı; anahtarları ve interneti kontrol edin.");
        SetStatus(report.ToString(), anyOk ? Theme.Good : Theme.Bad);
        _saveBtn.IsEnabled = true;
    }

    /// <summary>Çevrimdışı model satırı: durum + indir düğmesi + ilerleme + dene.</summary>
    private FrameworkElement BuildOfflineRow()
    {
        var box = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        var state = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        var bar = new ProgressBar { Height = 8, Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 6) };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var dl = new Button { Content = "⬇ Modeli indir", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0) };
        var test = new Button { Content = "Dene", Padding = new Thickness(14, 5, 14, 5) };
        if (TryFindResource("Primary") is Style ps) dl.Style = ps;
        row.Children.Add(dl);
        row.Children.Add(test);
        box.Children.Add(state);
        box.Children.Add(bar);
        box.Children.Add(row);

        void Refresh()
        {
            bool ok = OfflineTranslator.IsInstalled;
            state.Text = ok ? "✔ Model kurulu – çeviri zincirinin sonunda her zaman hazır bekliyor." : "Model indirilmedi.";
            state.Foreground = ok ? Theme.Good : Theme.Warn;
            dl.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
            test.IsEnabled = ok;
        }
        Refresh();

        dl.Click += async (_, _) =>
        {
            dl.IsEnabled = false;
            bar.Visibility = Visibility.Visible;
            var prog = new Progress<(long Done, long Total)>(p =>
            {
                double pct = p.Total > 0 ? 100.0 * p.Done / p.Total : 0;
                bar.Value = pct;
                state.Text = $"İndiriliyor… %{pct:F0}  ({p.Done / 1048576.0:F0} / {p.Total / 1048576.0:F0} MB)";
                state.Foreground = Theme.Dim;
            });
            try
            {
                await OfflineTranslator.DownloadAsync(prog, System.Threading.CancellationToken.None);
                state.Text = "Model yükleniyor…";
                OfflineModelReady?.Invoke(); // ana pencere cihazı (işlemci / ekran kartı) ayarlayıp modeli açar
                var load = OfflineTranslator.Preload();
                if (load != null) await load;
            }
            catch (Exception ex)
            {
                state.Text = "İndirilemedi: " + ex.Message + " (yeniden deneyebilirsiniz; kalan yerden devam eder)";
                state.Foreground = Theme.Bad;
                dl.IsEnabled = true;
                return;
            }
            finally { bar.Visibility = Visibility.Collapsed; }
            Refresh();
        };

        test.Click += async (_, _) =>
        {
            test.IsEnabled = false;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                string t = await new OfflineTranslator().TranslateAsync("Hey, let me call him real quick.", null, System.Threading.CancellationToken.None);
                state.Text = $"✔ \"{t}\"  ({sw.ElapsedMilliseconds} ms)";
                state.Foreground = Theme.Good;
            }
            catch (Exception ex) { state.Text = "✖ " + ex.Message; state.Foreground = Theme.Bad; }
            test.IsEnabled = true;
        };
        return box;
    }

    /// <summary>İşlemci / ekran kartı ayarı değişti (ana pencere kaydeder ve modeli yeni cihazda açar).</summary>
    public event Action? OfflineDeviceChanged;

    /// <summary>
    /// Donanım profili, cihaz seçimi (Otomatik / Yalnız işlemci / Yalnız ekran kartı), ekran kartı seçimi, iş parçacığı,
    /// CPU'ya dönüş, hız testi ve canlı izleme (işlemci %, RAM; NVIDIA için sıcaklık / kullanım / VRAM).
    /// </summary>
    private FrameworkElement BuildDeviceRow(Ps5Altyazi.Settings.AppSettings s)
    {
        var box = new StackPanel { Margin = new Thickness(0, 8, 0, 4) };
        var hwText = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Theme.Dim, FontSize = 12, Text = "Donanım algılanıyor…" };
        box.Children.Add(new TextBlock { Text = "Donanım", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 2) });
        box.Children.Add(hwText);

        // Cihaz
        box.Children.Add(new TextBlock { Text = "Çeviri cihazı", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 2) });
        var rAuto = new RadioButton { Content = "Otomatik (önerilen) – hız testiyle en hızlısı seçilir", GroupName = "offdev", Margin = new Thickness(0, 2, 0, 0) };
        var rCpu = new RadioButton { Content = "Yalnız işlemci (CPU)", GroupName = "offdev", Margin = new Thickness(0, 2, 0, 0) };
        var rGpu = new RadioButton { Content = "Yalnız ekran kartı (GPU)", GroupName = "offdev", Margin = new Thickness(0, 2, 0, 0) };
        (s.OfflineDevice switch { "cpu" => rCpu, "gpu" => rGpu, _ => rAuto }).IsChecked = true;
        box.Children.Add(rAuto); box.Children.Add(rCpu); box.Children.Add(rGpu);

        var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 2; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var gpuCombo = new ComboBox { Margin = new Thickness(0, 2, 0, 2), MinWidth = 300, HorizontalAlignment = HorizontalAlignment.Left };
        var thrCombo = new ComboBox { Margin = new Thickness(0, 2, 0, 2), Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
        void Cell(UIElement e, int r, int c) { Grid.SetRow(e, r); Grid.SetColumn(e, c); grid.Children.Add(e); }
        Cell(new TextBlock { Text = "Ekran kartı:", VerticalAlignment = VerticalAlignment.Center }, 0, 0); Cell(gpuCombo, 0, 1);
        Cell(new TextBlock { Text = "İşlemci iş parçacığı:", VerticalAlignment = VerticalAlignment.Center }, 1, 0); Cell(thrCombo, 1, 1);
        box.Children.Add(grid);

        thrCombo.Items.Add(new ComboBoxItem { Content = "Otomatik", Tag = 0 });
        for (int t = 1; t <= Environment.ProcessorCount; t++) thrCombo.Items.Add(new ComboBoxItem { Content = t.ToString(), Tag = t });
        thrCombo.SelectedIndex = Math.Clamp(s.OfflineThreads, 0, Environment.ProcessorCount);

        var fallback = new CheckBox { Content = "Ekran kartı açılamaz ya da hata verirse işlemciye dön (önerilen)", IsChecked = s.OfflineCpuFallback, Margin = new Thickness(0, 8, 0, 0) };
        box.Children.Add(fallback);
        box.Children.Add(Note("Ekran kartında model her zaman FP16 (yarım duyarlık) çalışır: daha az bellek, daha hızlı, kalite farkı yok denecek kadar azdır. " +
                              "İşlemcide INT8 (sıkıştırılmış) model kullanılır."));

        var active = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        var live = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Theme.Dim, FontSize = 12, Margin = new Thickness(0, 2, 0, 0) };
        var bench = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
        var benchBtn = new Button { Content = "⚡ Hız testi (işlemci ve ekran kartı)", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        box.Children.Add(active);
        box.Children.Add(live);
        box.Children.Add(benchBtn);
        box.Children.Add(bench);

        bool loading = true;
        HardwareProfile? hw = MainWindow.Hardware;

        void ShowBench()
        {
            var c = OfflineTranslator.LastCpuBenchmark; var g = OfflineTranslator.LastGpuBenchmark;
            if (c == null && g == null) { bench.Text = ""; return; }
            var sb = new StringBuilder("Son hız testi:\n");
            if (c != null) sb.Append("• ").Append(c).Append('\n');
            if (g != null) sb.Append("• ").Append(g).Append('\n');
            if (c != null && g != null)
                sb.Append(g.P50Ms < c.P50Ms * 0.95 ? $"→ Ekran kartı {c.P50Ms / Math.Max(1, g.P50Ms):F1} kat hızlı." : "→ İşlemci bu bilgisayarda yeterince hızlı (ekran kartı belirgin fark yaratmıyor).");
            bench.Text = sb.ToString().TrimEnd();
        }

        void ShowActive()
        {
            string dev = OfflineTranslator.ActiveDeviceName ?? "henüz açılmadı";
            active.Text = $"Şu an: {dev}\n{OfflineTranslator.Status}";
            active.Foreground = OfflineTranslator.Status.StartsWith("✖") ? Theme.Bad : OfflineTranslator.Status.StartsWith("⚠") ? Theme.Warn : Theme.Good;
        }

        void FillHw()
        {
            if (hw == null) return;
            hwText.Text = hw.Describe();
            gpuCombo.Items.Clear();
            foreach (var g in hw.Gpus) gpuCombo.Items.Add(new ComboBoxItem { Content = g.ToString(), Tag = g.Index });
            if (hw.Gpus.Count == 0)
            {
                gpuCombo.Items.Add(new ComboBoxItem { Content = "Ekran kartı yok – işlemci kullanılır", Tag = -1 });
                gpuCombo.IsEnabled = false; rGpu.IsEnabled = false;
            }
            int want = s.OfflineGpuIndex >= 0 ? s.OfflineGpuIndex : hw.BestGpu?.Index ?? -1;
            gpuCombo.SelectedIndex = 0;
            for (int i = 0; i < gpuCombo.Items.Count; i++)
                if (gpuCombo.Items[i] is ComboBoxItem ci && (int)ci.Tag == want) gpuCombo.SelectedIndex = i;
            if (hw.Gpus.Count > 0 && !OfflineTranslator.GpuFilesInstalled)
                hwText.Text += "\nEkran kartı modeli henüz inmedi; uygun kart varsa otomatik indirilir (bu sırada çeviri işlemcide sürer).";
        }

        void Changed()
        {
            if (loading) return;
            s.OfflineDevice = rCpu.IsChecked == true ? "cpu" : rGpu.IsChecked == true ? "gpu" : "auto";
            if (gpuCombo.SelectedItem is ComboBoxItem gi && (int)gi.Tag >= 0) s.OfflineGpuIndex = (int)gi.Tag;
            if (thrCombo.SelectedItem is ComboBoxItem ti) s.OfflineThreads = (int)ti.Tag;
            s.OfflineCpuFallback = fallback.IsChecked == true;
            OfflineDeviceChanged?.Invoke();
            active.Text = "Model seçilen cihazda açılıyor…";
            active.Foreground = Theme.Dim;
        }
        rAuto.Checked += (_, _) => Changed(); rCpu.Checked += (_, _) => Changed(); rGpu.Checked += (_, _) => Changed();
        gpuCombo.SelectionChanged += (_, _) => Changed();
        thrCombo.SelectionChanged += (_, _) => Changed();
        fallback.Click += (_, _) => Changed();

        benchBtn.Click += async (_, _) =>
        {
            if (!OfflineTranslator.IsInstalled) { bench.Text = "Önce modeli indirin."; return; }
            benchBtn.IsEnabled = false;
            bench.Foreground = Theme.Dim;
            bench.Text = "Ölçülüyor… (işlemci ve varsa ekran kartı ayrı ayrı; bu sırada çeviriler birkaç saniye bekler)";
            try
            {
                var r = await OfflineTranslator.BenchmarkAsync();
                bench.Foreground = r.IsError ? Theme.Bad : (Brush)Theme.Good;
                ShowBench();
                if (r.Gpu == null && hw?.Gpus.Count > 0 && !OfflineTranslator.GpuFilesInstalled) bench.Text += "\n(Ekran kartı modeli inmediği için yalnız işlemci ölçüldü.)";
                else if (r.Gpu == null && hw?.Gpus.Count > 0) bench.Text += "\n(Ekran kartı açılamadı: " + r.Message + ")";
            }
            catch (Exception ex) { bench.Text = "✖ " + ex.Message; bench.Foreground = Theme.Bad; }
            benchBtn.IsEnabled = true;
            ShowActive();
        };

        // Canlı izleme (2 sn): bu programın işlemci kullanımı ve belleği; NVIDIA kartta sıcaklık / kullanım / VRAM
        var proc = Process.GetCurrentProcess();
        TimeSpan lastCpu = proc.TotalProcessorTime; var lastAt = DateTime.UtcNow;
        bool polling = false;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += async (_, _) =>
        {
            if (polling) return;
            polling = true;
            try
            {
                proc.Refresh();
                var now = DateTime.UtcNow; var cpu = proc.TotalProcessorTime;
                double pct = (cpu - lastCpu).TotalMilliseconds / Math.Max(1, (now - lastAt).TotalMilliseconds) / Environment.ProcessorCount * 100;
                lastCpu = cpu; lastAt = now;
                string text = $"Canlı: program işlemci %{pct:F0} · RAM {proc.WorkingSet64 / 1048576} MB";
                if (hw?.Gpus.Any(g => g.IsNvidia) == true)
                {
                    var nv = await Task.Run(HardwareProfile.QueryNvidia);
                    foreach (var n in nv)
                        text += $"\n{n.Name}: {n.TempC} °C · kullanım %{n.UtilPercent} · VRAM {n.UsedMb} / {n.TotalMb} MB";
                }
                else if (hw?.Gpus.Count > 0) text += "\n(Sıcaklık / VRAM canlı bilgisi yalnız NVIDIA kartlarda gösterilir.)";
                live.Text = text;
                ShowActive();
            }
            catch { }
            finally { polling = false; }
        };
        WhenOpened(timer.Start);
        WhenClosed(timer.Stop);

        ShowActive();
        ShowBench();
        if (hw != null) { FillHw(); loading = false; }
        else
        {
            _ = Task.Run(HardwareProfile.Detect).ContinueWith(t =>
            {
                Dispatcher.Invoke(() => { hw = t.IsCompletedSuccessfully ? t.Result : null; FillHw(); loading = false; });
            });
        }
        return box;
    }

    /// <summary>Çevrimdışı model indirilip yüklendi (ana pencere çeviri zincirini yeniler).</summary>
    public event Action? OfflineModelReady;

    private static TextBlock Header(string text) =>
        new() { Text = text, FontWeight = FontWeights.Bold, FontSize = 14, Margin = new Thickness(0, 14, 0, 0) };

    private static TextBlock Note(string text) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Theme.Dim, Margin = new Thickness(0, 4, 0, 0) };

    private static TextBlock LinkNote(string before, string url, string after)
    {
        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Theme.Dim, Margin = new Thickness(0, 4, 0, 0) };
        tb.Inlines.Add(before);
        var link = new Hyperlink(new Run(url.Replace("https://", ""))) { NavigateUri = new Uri(url) };
        link.RequestNavigate += (_, e) =>
        {
            try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { }
        };
        tb.Inlines.Add(link);
        tb.Inlines.Add(after);
        return tb;
    }

    private void SetStatus(string text, Brush color)
    {
        _status.Text = text;
        _status.Foreground = color;
    }
}
