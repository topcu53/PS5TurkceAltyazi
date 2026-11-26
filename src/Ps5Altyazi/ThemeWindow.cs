using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ps5Altyazi;

/// <summary>Tema seçimi: her temanın renk örneği; seçilen tema program yeniden açılınca uygulanır.</summary>
public sealed class ThemeWindow : Window
{
    public string? Chosen { get; private set; }

    public ThemeWindow(string current)
    {
        Title = "Tema";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new StackPanel { Margin = new Thickness(20, 14, 20, 16) };
        root.Children.Add(new TextBlock { Text = "Arayüz teması", FontWeight = FontWeights.Bold, FontSize = 16, Margin = new Thickness(0, 0, 0, 10) });
        string selected = current;
        foreach (var p in ThemePalettes.All)
        {
            var rb = new RadioButton { GroupName = "tema", IsChecked = p.Key == current, Margin = new Thickness(0, 6, 0, 6) };
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            var swatch = new Border
            {
                Width = 120, Height = 44, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 12, 0),
                Background = Theme.Frozen(p.Bg), BorderBrush = Theme.Frozen(p.Line), BorderThickness = new Thickness(1),
            };
            var inner = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            foreach (var c in new[] { ThemePalettes.MapPublic(p, "#182033"), p.Accent, ThemePalettes.MapPublic(p, "#1E5BFF"), p.Text })
                inner.Children.Add(new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Margin = new Thickness(0, 0, 6, 0), Background = Theme.Frozen(c) });
            swatch.Child = inner;
            line.Children.Add(swatch);
            var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            txt.Children.Add(new TextBlock { Text = p.Name, FontWeight = FontWeights.SemiBold, FontSize = 14 });
            txt.Children.Add(new TextBlock { Text = p.Description, Foreground = Theme.Dim, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 300 });
            line.Children.Add(txt);
            rb.Content = line;
            string key = p.Key;
            rb.Checked += (_, _) => selected = key;
            root.Children.Add(rb);
        }
        root.Children.Add(new TextBlock
        {
            Text = "Yeni tema, program yeniden açılınca uygulanır (birkaç saniye sürer; PS5 bağlantısı kendiliğinden yeniden kurulur).",
            Foreground = Theme.Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), FontSize = 12
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = new Button { Content = "Vazgeç", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var apply = new Button { Content = "Uygula ve yeniden aç", Padding = new Thickness(16, 6, 16, 6), FontWeight = FontWeights.Bold, IsDefault = true };
        apply.Click += (_, _) => { Chosen = selected; DialogResult = true; };
        buttons.Children.Add(cancel);
        buttons.Children.Add(apply);
        root.Children.Add(buttons);
        Content = root;
        Theme.Apply(this);
    }
}
