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
    // Renkler seçili temadan gelir (ThemePalettes.Apply açılışta ayarlar).
    public static Brush Bg { get; internal set; } = Frozen("#0E0C13");
    public static Brush Bar { get; internal set; } = Frozen("#09080D");
    public static Brush Text { get; internal set; } = Frozen("#ECE8F3");
    public static Brush Dim { get; internal set; } = Frozen("#A097B3");
    public static Brush Good { get; internal set; } = Frozen("#6EE7A8");
    public static Brush Accent { get; internal set; } = Frozen("#B49CF2");
    public static Brush Warn { get; internal set; } = Frozen("#F6C177");
    public static Brush Bad { get; internal set; } = Frozen("#F38BA8");
    public static Brush Line { get; internal set; } = Frozen("#2A2535");

    internal static Brush Frozen(string hex)
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
            CaptionHeight = 56,
            CornerRadius = new CornerRadius(0),
            GlassFrameThickness = new Thickness(0),
            UseAeroCaptionButtons = false,
            ResizeBorderThickness = canResize ? new Thickness(6) : new Thickness(0)
        });

        var content = w.Content as UIElement;
        if (w is PageWindow pw) pw.PageContent = content; // ana pencerede açılırken başlık çubuğu olmadan kullanılır
        w.Content = null;
        // Küçük ekranda pencere ekrandan taşmasın: en fazla çalışma alanı kadar, sığmayan kısım kaydırılır
        var wa = SystemParameters.WorkArea;
        w.MaxHeight = wa.Height;
        w.MaxWidth = wa.Width;
        if (!canResize && content != null && content is not ScrollViewer)
            content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                                         HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var bar = new Grid { Background = Bg };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var logo = new Border
        {
            Width = 6, Height = 22, CornerRadius = new CornerRadius(3), Margin = new Thickness(20, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center, Background = Accent
        };
        bar.Children.Add(logo);

        var title = new TextBlock { Foreground = Text, FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        title.SetBinding(TextBlock.TextProperty, new Binding(nameof(Window.Title)) { Source = w });
        Grid.SetColumn(title, 1);
        bar.Children.Add(title);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
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
        // Başlığın altında ince, yumuşak ayraç
        var divider = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(20, 0, 20, 0), Background = Line };
        root.Children.Add(divider);
        RoundCorners(w);
        if (content != null)
        {
            Grid.SetRow(content, 1);
            root.Children.Add(content);
        }
        w.Content = root;
    }

    /// <summary>
    /// Ana pencere için: ayrı başlık çubuğu yok, uygulama tek parça. Pencere, üstteki <paramref name="captionHeight"/> piksellik
    /// alandan sürüklenir; küçült / büyüt / kapat düğmeleri <paramref name="buttonHost"/>'a (uygulamanın kendi üst bandına) eklenir.
    /// </summary>
    public static void ApplyMerged(Window w, Panel buttonHost, FrameworkElement root, double captionHeight)
    {
        w.Background = Bg;
        w.Foreground = Text;
        w.BorderBrush = Line;
        w.BorderThickness = new Thickness(1);
        WindowChrome.SetWindowChrome(w, new WindowChrome
        {
            CaptionHeight = captionHeight,
            CornerRadius = new CornerRadius(0),
            GlassFrameThickness = new Thickness(0),
            UseAeroCaptionButtons = false,
            ResizeBorderThickness = new Thickness(6)
        });
        RoundCorners(w);
        buttonHost.Children.Add(CaptionButton("\uE921", "Küçült", false, () => w.WindowState = WindowState.Minimized));
        var max = CaptionButton("\uE922", "Büyüt", false,
            () => w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);
        buttonHost.Children.Add(max);
        buttonHost.Children.Add(CaptionButton("\uE8BB", "Kapat", true, w.Close));
        void Sync()
        {
            ((TextBlock)max.Content).Text = w.WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        }
        w.StateChanged += (_, _) => Sync();
        Sync();
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Windows 11'de pencere köşelerini yuvarlatır (Windows 10'da etkisizdir).</summary>
    public static void RoundCorners(Window w)
    {
        void Do()
        {
            try
            {
                var h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                if (h == IntPtr.Zero) return;
                int round = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(h, 33, ref round, sizeof(int));
            }
            catch { /* eski Windows */ }
        }
        if (new System.Windows.Interop.WindowInteropHelper(w).Handle != IntPtr.Zero) Do();
        else w.SourceInitialized += (_, _) => Do();
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
        hover.Setters.Add(new Setter(Border.BackgroundProperty, danger ? Frozen("#E81123") : Frozen(ThemePalettes.Map("#1F2A42")), "Bd"));
        t.Triggers.Add(hover);
        return t;
    }
}
