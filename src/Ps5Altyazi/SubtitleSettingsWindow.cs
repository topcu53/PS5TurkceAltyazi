using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

    public SubtitleSettingsWindow(AppSettings settings, SubtitleWindow sub)
    {
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
