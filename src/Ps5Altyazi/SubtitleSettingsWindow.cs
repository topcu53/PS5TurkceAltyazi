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
    /// <summary>Yazı tipi ayarı değişti (true = başka oyuna geçildi).</summary>
    public event Action<bool>? FontChanged;

    private readonly Func<string> _describeFont;
    private readonly TextBlock _fontInfo = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 4, 0, 0) };
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
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new StackPanel { Margin = new Thickness(16) };

        root.Children.Add(SliderRow("Yazı boyutu", 20, 96, _s.SubtitleFontSize, v => $"{v:F0} pt", v => _s.SubtitleFontSize = v));
        root.Children.Add(SliderRow("Arka plan saydamlığı (koyuluk)", 0, 100, _s.SubtitleBackgroundOpacity * 100, v => $"%{v:F0}",
            v => _s.SubtitleBackgroundOpacity = v / 100));
        root.Children.Add(SliderRow("Pencere genişliği (ekranın)", 30, 100, _s.SubtitleWidthFraction * 100, v => $"%{v:F0}",
            v => _s.SubtitleWidthFraction = v / 100));

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
        sample.Click += (_, _) => _sub.ShowSubtitle(_sub.CurrentText == SampleText ? "" : SampleText);
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
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 12, 0, 0)
        });

        Content = root;
        Closed += (_, _) =>
        {
            _sub.SetEditMode(false);
            if (_sub.CurrentText == SampleText) _sub.ShowSubtitle("");
        };
    }

    // ───────── Yazı tipi (oyun başına) ─────────

    private FrameworkElement FontSection()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        panel.Children.Add(new TextBlock { Text = "Yazı tipi (oyunun altyazısına benzet)", FontWeight = FontWeights.Bold });
        panel.Children.Add(new TextBlock
        {
            Text = "Program oyunun altyazısını ölçer ve Türkçeyi en benzer yazı tipiyle, aynı harf boyunda ve renkte yazar. " +
                   "Her oyun için ayrı hatırlanır. Oyunun kendi font dosyası sizde varsa 'Dosyadan ekle' ile kullanabilirsiniz.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 2, 0, 6)
        });

        var gameRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        gameRow.Children.Add(new TextBlock { Text = "Oyun:", Width = 70, VerticalAlignment = VerticalAlignment.Center });
        foreach (var name in _s.GameFonts.Keys.Append("Genel").Append(_s.ActiveGame)
                     .Concat(Ps5Altyazi.Fonts.GamePresets.All.Select(g => g.Name)).Append("Red Dead Redemption 2")
                     .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n)) _gameBox.Items.Add(name);
        _gameBox.Text = _s.ActiveGame;
        _gameBox.ToolTip = "Oyunun adını yazın ya da listeden seçin (ör. RDR2, GTA 6). Her oyunun yazı tipi ayrı öğrenilir.";
        _gameBox.SelectionChanged += (_, _) => { if (_gameBox.SelectedItem is string g) Dispatcher.BeginInvoke(() => SwitchGame(g)); };
        _gameBox.LostKeyboardFocus += (_, _) => SwitchGame(_gameBox.Text);
        _gameBox.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) SwitchGame(_gameBox.Text); };
        gameRow.Children.Add(_gameBox);
        panel.Children.Add(gameRow);

        var fontRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        fontRow.Children.Add(new TextBlock { Text = "Yazı tipi:", Width = 70, VerticalAlignment = VerticalAlignment.Center });
        _fontBox.SelectionChanged += (_, _) => OnFontPicked();
        fontRow.Children.Add(_fontBox);
        var add = new Button { Content = "Dosyadan ekle…", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(8, 0, 0, 0) };
        add.Click += (_, _) => AddFontFile();
        fontRow.Children.Add(add);
        panel.Children.Add(fontRow);
        panel.Children.Add(_fontInfo);

        FillFonts();
        return panel;
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
            var item = new ComboBoxItem { Content = f.DisplayName, Tag = f.Key };
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
