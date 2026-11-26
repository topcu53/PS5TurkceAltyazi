using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ps5Altyazi.Fonts;
using Ps5Altyazi.Settings;

namespace Ps5Altyazi;

/// <summary>
/// Altyazı görünümü: yazı boyutu, zemin saydamlığı, genişlik, her zaman üstte, konum düzenleme.
/// Değişiklikler anında uygulanır (Changed olayı) ve ayarlara yazılır.
/// </summary>
public sealed class SubtitleSettingsWindow : Window
{
    private readonly AppSettings _s;
    private readonly SubtitleWindow _sub;
    private readonly CheckBox _editCheck = new() { Content = "Konumu düzenle (altyazı penceresini fareyle sürükleyin)", Margin = new Thickness(0, 10, 0, 0) };

    private const string SampleText = "Ah, sen de mi? Bırakın beni rahat, hepiniz.";

    public event Action? Changed;
    /// <summary>Örnek altyazı TV'ye ve PC'ye gönderilsin ("" = sil).</summary>
    public event Action<string>? SampleRequested;
    private bool _sampleOn;

    private static readonly (string Name, string Value)[] TextColors =
    {
        ("Otomatik (oyundaki gibi)", "auto"), ("Beyaz", "#FFFFFF"), ("Sarı", "#FFE066"), ("Açık mor", "#D8C8FF"),
        ("Camgöbeği", "#9EEBFF"), ("Açık yeşil", "#B8F5C8"),
    };
    private static readonly (string Name, string Value)[] BgColors =
    {
        ("Siyah", "#000000"), ("Koyu mor", "#1E1630"), ("Gece mavisi", "#0D1628"), ("Koyu gri", "#1C1C1E"),
    };

    private FrameworkElement ColorRow(string label, (string Name, string Value)[] items, string current, Action<string> set)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(new TextBlock { Text = label + ":", Width = 110, VerticalAlignment = VerticalAlignment.Center });
        var box = new ComboBox { Width = 230 };
        foreach (var (name, value) in items)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal };
            item.Children.Add(new Border
            {
                Width = 16, Height = 16, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 8, 0), BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Gray,
                Background = value == "auto" ? Brushes.Transparent : new SolidColorBrush((Color)ColorConverter.ConvertFromString(value))
            });
            item.Children.Add(new TextBlock { Text = name });
            box.Items.Add(new ComboBoxItem { Content = item, Tag = value });
        }
        int idx = Array.FindIndex(items, i => string.Equals(i.Value, current, StringComparison.OrdinalIgnoreCase));
        box.SelectedIndex = idx < 0 ? 0 : idx;
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is ComboBoxItem { Tag: string v }) { set(v); Changed?.Invoke(); }
        };
        row.Children.Add(box);
        return row;
    }
    /// <summary>Yazı tipi ayarı değişti (true = başka oyuna geçildi).</summary>
    public event Action<bool>? FontChanged;

    private readonly Func<string> _describeFont;
    private readonly TextBlock _fontInfo = new() { TextWrapping = TextWrapping.Wrap, Foreground = Theme.Dim, Margin = new Thickness(0, 4, 0, 0) };
    private readonly ComboBox _gameBox = new() { IsEditable = true, MinWidth = 200 };
    private readonly ComboBox _fontBox = new() { MinWidth = 260 };
    private bool _loadingFont;

    private const string AutoLabel = "Otomatik – oyunun altyazısına en benzer";

    public SubtitleSettingsWindow(AppSettings settings, SubtitleWindow sub, Func<string> describeFont)
    {
        _describeFont = describeFont;
        _s = settings;
        _sub = sub;
        Title = "Altyazı Görünümü";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new StackPanel { Margin = new Thickness(16) };

        root.Children.Add(SliderRow("Yazı boyutu", 20, 96, _s.SubtitleFontSize, v => $"{v:F0} pt", v => _s.SubtitleFontSize = v));
        root.Children.Add(SliderRow("Arka plan saydamlığı (koyuluk)", 0, 100, _s.SubtitleBackgroundOpacity * 100, v => $"%{v:F0}",
            v => _s.SubtitleBackgroundOpacity = v / 100));
        root.Children.Add(SliderRow("Pencere genişliği (ekranın)", 30, 100, _s.SubtitleWidthFraction * 100, v => $"%{v:F0}",
            v => _s.SubtitleWidthFraction = v / 100));

        root.Children.Add(new TextBlock { Text = "Renkler ve zemin", FontWeight = FontWeights.Bold, FontSize = 14, Margin = new Thickness(0, 14, 0, 4) });
        root.Children.Add(ColorRow("Yazı rengi", TextColors, _s.SubtitleTextColor, v => _s.SubtitleTextColor = v));
        root.Children.Add(ColorRow("Zemin rengi", BgColors, _s.SubtitleBgColor, v => _s.SubtitleBgColor = v));
        root.Children.Add(SliderRow("Köşe yuvarlaklığı", 0, 24, _s.SubtitleCornerRadius, v => $"{v:F0} px", v => _s.SubtitleCornerRadius = v));
        var shadow = new CheckBox { Content = "Yazı gölgesi (her zeminde okunur)", IsChecked = _s.SubtitleShadow, Margin = new Thickness(0, 6, 0, 0) };
        shadow.Checked += (_, _) => { _s.SubtitleShadow = true; Changed?.Invoke(); };
        shadow.Unchecked += (_, _) => { _s.SubtitleShadow = false; Changed?.Invoke(); };
        root.Children.Add(shadow);
        root.Children.Add(new TextBlock { Text = "Değişiklikler TV'de ve (açıksa) PC'de anında uygulanır. 'Örnek Altyazı Göster' ile deneyin.",
                                          Foreground = Theme.Dim, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 4, 0, 0) });

        var topmost = new CheckBox { Content = "Her zaman üstte", IsChecked = _s.SubtitleTopmost, Margin = new Thickness(0, 10, 0, 0) };
        topmost.Checked += (_, _) => { _s.SubtitleTopmost = true; Changed?.Invoke(); };
        topmost.Unchecked += (_, _) => { _s.SubtitleTopmost = false; Changed?.Invoke(); };
        root.Children.Add(topmost);

        var cover = new CheckBox
        {
            Content = "Oyunun İngilizce altyazısının üstüne yerleştir (onu örter)",
            IsChecked = _s.SubtitleCoverOriginal,
            Margin = new Thickness(0, 10, 0, 0),
            ToolTip = "Türkçe kutu, ekrandaki İngilizce altyazının tam üstüne geçer. Zemin koyuluğunu %85+ yapmanız önerilir."
        };
        cover.Checked += (_, _) => { _s.SubtitleCoverOriginal = true; _sub.SetCoverMode(true); Changed?.Invoke(); };
        cover.Unchecked += (_, _) => { _s.SubtitleCoverOriginal = false; _sub.SetCoverMode(false); Changed?.Invoke(); };
        root.Children.Add(cover);

        var match = new CheckBox
        {
            Content = "Örtme modunda yazı boyutu orijinal altyazıyla aynı olsun (TV'de de)",
            IsChecked = _s.SubtitleMatchOriginalSize,
            Margin = new Thickness(0, 6, 0, 0)
        };
        match.Checked += (_, _) => { _s.SubtitleMatchOriginalSize = true; Changed?.Invoke(); };
        match.Unchecked += (_, _) => { _s.SubtitleMatchOriginalSize = false; Changed?.Invoke(); };
        root.Children.Add(match);

        root.Children.Add(FontSection());

        _editCheck.Checked += (_, _) => _sub.SetEditMode(true);
        _editCheck.Unchecked += (_, _) => _sub.SetEditMode(false);
        root.Children.Add(_editCheck);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        var sample = new Button { Content = "Örnek Altyazı Göster", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        sample.Click += (_, _) =>
        {
            _sampleOn = !_sampleOn;
            if (SampleRequested != null) SampleRequested(_sampleOn ? SampleText : "");
            else _sub.ShowSubtitle(_sampleOn ? SampleText : "");
        };
        buttons.Children.Add(sample);

        var reset = new Button { Content = "Varsayılan Konum", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        reset.Click += (_, _) => _sub.ResetPosition();
        buttons.Children.Add(reset);

        var close = new Button { Content = "Kapat", Padding = new Thickness(12, 4, 12, 4), IsCancel = true };
        close.Click += (_, _) => Close();
        buttons.Children.Add(close);
        root.Children.Add(buttons);

        root.Children.Add(new TextBlock
        {
            Text = "İpucu: Altyazı penceresi normalde fare tıklamalarını geçirir, Remote Play'i engellemez. " +
                   "Taşımak için 'Konumu düzenle'yi işaretleyin. Altyazı bitince pencere kendiliğinden gizlenir.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Theme.Dim,
            Margin = new Thickness(0, 12, 0, 0)
        });

        Content = root;
        Theme.Apply(this);
        Closed += (_, _) =>
        {
            _sub.SetEditMode(false);
            if (_sampleOn) { if (SampleRequested != null) SampleRequested(""); else _sub.ShowSubtitle(""); }
        };
    }

    // ───────── Yazı tipi (oyun başına) ─────────

    private readonly TextBlock _preview = new()
    {
        Text = "Hadi ama millet, oy vermeme izin verin!\nÇĞİÖŞÜ çğıöşü 0123",
        FontSize = 24, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center
    };
    private Button? _removeBtn;

    private FrameworkElement FontSection()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        panel.Children.Add(new TextBlock { Text = "YAZI TİPİ", FontWeight = FontWeights.Bold, FontSize = 11, Foreground = Theme.Accent });
        panel.Children.Add(new TextBlock
        {
            Text = "Listeden seçin ya da kendi .ttf / .otf dosyanızı ekleyin. Boyut her zaman oyundaki altyazıdan ölçülür.",
            TextWrapping = TextWrapping.Wrap, Foreground = Theme.Dim, Margin = new Thickness(0, 4, 0, 6)
        });

        _fontBox.MaxDropDownHeight = 380;
        _fontBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        _fontBox.Margin = new Thickness(0, 2, 0, 0);
        _fontBox.SelectionChanged += (_, _) => { OnFontPicked(); UpdatePreview(); };
        panel.Children.Add(_fontBox);

        var row = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var add = new Button { Content = "＋ Yazı tipi ekle (.ttf / .otf)…" };
        add.Click += (_, _) => AddFontFile();
        _removeBtn = new Button { Content = "🗑 Eklenen fontu kaldır", ToolTip = "Yalnızca sizin eklediğiniz fontlar kaldırılabilir" };
        _removeBtn.Click += (_, _) => RemoveSelectedFont();
        row.Children.Add(add);
        row.Children.Add(_removeBtn);
        panel.Children.Add(row);

        panel.Children.Add(new Border
        {
            Background = Brushes.Black, CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 8, 0, 0), Child = _preview
        });
        panel.Children.Add(_fontInfo);

        var relearn = new Button { Content = "Boyu baştan öğren", Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
                                   ToolTip = "Yazı boyu tutmadıysa: öğrenileni siler, sonraki altyazılardan yeniden ölçer." };
        relearn.Click += (_, _) =>
        {
            string key = Profile.FontKey;
            _s.GameFonts.Clear();
            Profile.FontKey = key;
            FontChanged?.Invoke(true);
            _fontInfo.Text = "Sıfırlandı: sonraki birkaç altyazıdan yeniden ölçülecek.";
        };
        panel.Children.Add(relearn);

        FillFonts();
        UpdatePreview();
        return panel;
    }

    /// <summary>Örnek yazıyı seçili fontla gösterir; Türkçe harf kontrolü ve "kaldır" düğmesinin durumu.</summary>
    private void UpdatePreview()
    {
        var f = FontCatalog.Find(Profile.FontKey);
        if (f == null)
        {
            _preview.FontFamily = new FontFamily("Segoe UI");
            _preview.FontWeight = FontWeights.Bold;
            _fontInfo.Text = "Otomatik: oyunun altyazısına en benzer yazı tipi kullanılır.\n" + _describeFont();
        }
        else
        {
            try
            {
                var uri = new Uri(FontCatalog.Folder.TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar);
                _preview.FontFamily = new FontFamily(uri, "./" + f.File + "#" + f.Family);
                _preview.FontWeight = f.IsBold ? FontWeights.Bold : FontWeights.Normal;
            }
            catch { }
            _fontInfo.Text = $"Seçili: {f.DisplayName}" + TurkishNote(f);
        }
        if (_removeBtn != null) _removeBtn.IsEnabled = f?.Custom == true;
    }

    private void RemoveSelectedFont()
    {
        var f = FontCatalog.Find(Profile.FontKey);
        if (f is not { Custom: true }) return;
        FontCatalog.RemoveCustom(f);
        Profile.FontKey = "arimo-700";
        FontChanged?.Invoke(false);
        FillFonts();
        UpdatePreview();
    }

    /// <summary>Fontta Türkçe harflerden eksik olan var mı? (eksikler otomatik olarak yedek yazı tipiyle çizilir)</summary>
    private static string TurkishNote(FontEntry f)
    {
        try
        {
            var gt = new GlyphTypeface(new Uri(FontCatalog.PathOf(f)));
            var missing = "çğıİöşüÇĞÖŞÜ".Where(c => !gt.CharacterToGlyphMap.ContainsKey(c)).ToArray();
            return missing.Length == 0
                ? "\n✔ Türkçe harflerin hepsi var."
                : $"\n⚠ Bu fontta şu Türkçe harfler yok: {string.Join(" ", missing)} – bunlar benzer bir yedek yazı tipiyle gösterilir.";
        }
        catch { return ""; }
    }

    private GameFontProfile Profile
    {
        get
        {
            if (!_s.GameFonts.TryGetValue(_s.ActiveGame, out var p)) { p = new GameFontProfile(); _s.GameFonts[_s.ActiveGame] = p; }
            return p;
        }
    }

    private void FillFonts()
    {
        _loadingFont = true;
        _fontBox.Items.Clear();
        _fontBox.Items.Add(new ComboBoxItem { Content = AutoLabel, Tag = "auto" });
        foreach (var f in FontCatalog.All.OrderBy(f => f.Custom ? 0 : 1).ThenBy(f => f.DisplayName))
        {
            var item = new ComboBoxItem { Content = (f.Custom ? "★ " : "") + f.DisplayName, Tag = f.Key };
            try
            {
                var uri = new Uri(FontCatalog.Folder.TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar);
                item.FontFamily = new FontFamily(uri, "./" + f.File + "#" + f.Family);
                item.FontWeight = f.IsBold ? FontWeights.Bold : FontWeights.Normal;
            }
            catch { }
            _fontBox.Items.Add(item);
        }
        string key = Profile.FontKey;
        _fontBox.SelectedItem = _fontBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == key) ?? _fontBox.Items[0];
        _loadingFont = false;
        _fontInfo.Text = _describeFont();
    }

    private void SwitchGame(string? name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0 || name.Length > 40 || name == _s.ActiveGame) return;
        _s.ActiveGame = name;
        _ = Profile; // yoksa oluştur
        if (!_gameBox.Items.Contains(name)) _gameBox.Items.Add(name);
        FontChanged?.Invoke(true);
        FillFonts();
    }

    private void OnFontPicked()
    {
        if (_loadingFont || _fontBox.SelectedItem is not ComboBoxItem item) return;
        string key = (string)item.Tag;
        if (key == Profile.FontKey) return;
        Profile.FontKey = key;
        FontChanged?.Invoke(false);
        _fontInfo.Text = _describeFont();
    }

    private void AddFontFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Yazı tipi dosyası seçin",
            Filter = "Yazı tipi (*.ttf;*.otf)|*.ttf;*.otf"
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var f = FontCatalog.AddCustom(dlg.FileName);
            Profile.FontKey = f.Key;
            FontChanged?.Invoke(false);
            FillFonts();
            UpdatePreview();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Yazı tipi eklenemedi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private FrameworkElement SliderRow(string label, double min, double max, double value, Func<double, string> fmt, Action<double> apply)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        var header = new DockPanel();
        var valueText = new TextBlock { Text = fmt(value), FontWeight = FontWeights.Bold };
        DockPanel.SetDock(valueText, Dock.Right);
        header.Children.Add(valueText);
        header.Children.Add(new TextBlock { Text = label });
        panel.Children.Add(header);

        var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Margin = new Thickness(0, 2, 0, 0) };
        slider.ValueChanged += (_, e) =>
        {
            valueText.Text = fmt(e.NewValue);
            apply(e.NewValue);
            Changed?.Invoke();
        };
        panel.Children.Add(slider);
        return panel;
    }
}
