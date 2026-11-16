using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

namespace Ps5Altyazi;

/// <summary>
/// Altyazı+ görünümü: Windows'un başlık çubuğu yerine uygulamaya ait koyu başlık çubuğu (logo, başlık,
/// küçült / büyüt / kapat) ve bütün pencerelerde aynı koyu tema.
/// </summary>
public static class Theme
{
    public static readonly Brush Bg = Frozen("#14161B");
    public static readonly Brush Bar = Frozen("#101216");
    public static readonly Brush Text = Frozen("#ECEEF3");
    public static readonly Brush Dim = Frozen("#9AA1AE");
    public static readonly Brush Good = Frozen("#4ADE80");
    public static readonly Brush Warn = Frozen("#FFB03B");
    public static readonly Brush Bad = Frozen("#FF6B6B");
    public static readonly Brush Line = Frozen("#2F3440");

    private static Brush Frozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    /// <summary>
    /// Pencereye temayı ve uygulamanın başlık çubuğunu uygular. Pencerenin içeriği hazır olduktan sonra çağrılır
    /// (içerik, başlık çubuğunun altına taşınır).
    /// </summary>
    public static void Apply(Window w, bool canResize = false)
    {
        w.Background = Bg;
        w.Foreground = Text;
        w.FontFamily = new FontFamily("Segoe UI");
        w.BorderBrush = Line;
        w.BorderThickness = new Thickness(1);

        WindowChrome.SetWindowChrome(w, new WindowChrome
        {
            CaptionHeight = 40,
            CornerRadius = new CornerRadius(0),
            GlassFrameThickness = new Thickness(0),
            UseAeroCaptionButtons = false,
            ResizeBorderThickness = canResize ? new Thickness(6) : new Thickness(0)
        });

        var content = w.Content as UIElement;
        w.Content = null;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var bar = new Grid { Background = Bar };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var logo = new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/Resources/logo-256.png")),
            Width = 22, Height = 22, Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center
        };
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
        bar.Children.Add(logo);

        var title = new TextBlock { Foreground = Dim, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        title.SetBinding(TextBlock.TextProperty, new Binding(nameof(Window.Title)) { Source = w });
        Grid.SetColumn(title, 1);
        bar.Children.Add(title);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        Grid.SetColumn(buttons, 2);
        if (canResize || w.ResizeMode != ResizeMode.NoResize)
        {
            buttons.Children.Add(CaptionButton("", "Küçült", false, () => w.WindowState = WindowState.Minimized));
            var max = CaptionButton("", "Büyüt", false,
                () => w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);
            buttons.Children.Add(max);
            w.StateChanged += (_, _) =>
            {
                ((TextBlock)max.Content).Text = w.WindowState == WindowState.Maximized ? "" : "";
                // Büyütülmüş çerçevesiz pencere ekran kenarından taşar: içeriği içeri al
                root.Margin = w.WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            };
        }
        buttons.Children.Add(CaptionButton("", "Kapat", true, w.Close));
        bar.Children.Add(buttons);

        root.Children.Add(bar);
        if (content != null)
        {
            Grid.SetRow(content, 1);
            root.Children.Add(content);
        }
        w.Content = root;
    }

    private static Button CaptionButton(string glyph, string tip, bool danger, Action click)
    {
        var b = new Button
        {
            Content = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 10 },
            Width = 46, Height = 40, Margin = new Thickness(0), Padding = new Thickness(0),
            ToolTip = tip, Foreground = Text, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Cursor = Cursors.Arrow, Focusable = false
        };
        b.Template = CaptionTemplate(danger);
        WindowChrome.SetIsHitTestVisibleInChrome(b, true);
        b.Click += (_, _) => click();
        return b;
    }

    private static ControlTemplate CaptionTemplate(bool danger)
    {
        var t = new ControlTemplate(typeof(Button));
        var bd = new FrameworkElementFactory(typeof(Border)) { Name = "Bd" };
        bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        bd.AppendChild(cp);
        t.VisualTree = bd;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, danger ? Frozen("#E81123") : Frozen("#2A2F3A"), "Bd"));
        t.Triggers.Add(hover);
        return t;
    }
}
