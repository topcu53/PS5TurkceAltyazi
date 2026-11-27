using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ps5Altyazi.Tv;

namespace Ps5Altyazi;

/// <summary>
/// Oyun içi yazı çevirisi katmanı (PC): Remote Play'in oyun alanının tam üstünde, saydam ve tıklamaları geçiren pencere.
/// Parçaları (<see cref="ScreenPiece"/>) opak siyah kutularda, orijinal yazı boyunda çizer. Tuş simgeleri örtülmez:
/// parça simgeye dayanır ve simgeden uzağa doğru büyür.
/// </summary>
public sealed class ScreenOverlayWindow : Window
{
    private readonly Canvas _canvas = new();
    private string _lastKey = "";

    public ScreenOverlayWindow()
    {
        Title = "Altyazı+ – Oyun içi yazılar";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -10000; Top = -10000; Width = 10; Height = 10;
        Content = _canvas;
        SourceInitialized += (_, _) => ApplyWindowStyles();
    }

    /// <summary>
    /// Parçaları çizer. <paramref name="gameScreenPx"/>: oyun alanının ekrandaki yeri (fiziksel piksel).
    /// Boş liste → katman gizlenir.
    /// </summary>
    public void SetPieces(Rect gameScreenPx, IReadOnlyList<ScreenPiece> pieces)
    {
        var key = $"{gameScreenPx}|" + string.Join("|", pieces);
        if (key == _lastKey) return;
        _lastKey = key;

        if (pieces.Count == 0)
        {
            _canvas.Children.Clear();
            if (IsVisible) Hide();
            return;
        }
        if (!IsVisible) Show();

        double sx = 1, sy = 1;
        try { var dpi = VisualTreeHelper.GetDpi(this); sx = dpi.DpiScaleX; sy = dpi.DpiScaleY; } catch { }
        Left = gameScreenPx.X / sx;
        Top = gameScreenPx.Y / sy;
        Width = Math.Max(10, gameScreenPx.Width / sx);
        Height = Math.Max(10, gameScreenPx.Height / sy);
        double gw = Width, gh = Height; // DIP cinsinden oyun alanı

        _canvas.Children.Clear();
        foreach (var p in pieces)
        {
            double x = p.X * gw, y = p.Y * gh, w = p.W * gw, h = p.H * gh;
            double baseFont = Math.Max(9, p.Fs * gh);

            var tb = new TextBlock
            {
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Segoe UI"),
                TextWrapping = p.Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight
            };
            if (p.Wrap && p.MaxW > 0) tb.MaxWidth = p.MaxW * gw;

            var box = new Border
            {
                Background = Brushes.Black, // opak: İngilizce alttan görünmesin
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4, 1, 4, 1),
                MinWidth = w + 8,
                MinHeight = h + 4,
                Child = tb
            };

            // Tek satır: çok taşarsa küçült (en az %70); çok satır: orijinal yüksekliği fazla aşmasın (en az %60)
            double size = baseFont;
            for (int i = 0; i < 10; i++)
            {
                Fill(tb, p, size);
                box.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                bool fits = p.Wrap ? box.DesiredSize.Height <= h * 1.3 + 8 : box.DesiredSize.Width <= Math.Max(w * 2.2, w + 6 * size);
                if (fits || size <= baseFont * (p.Wrap ? 0.6 : 0.7)) break;
                size *= 0.93;
            }

            double bw = Math.Max(box.DesiredSize.Width, w + 8), bh = Math.Max(box.DesiredSize.Height, h + 4);
            double left = p.Align == 'r' ? x + w + 4 - bw : x - 4;
            Canvas.SetLeft(box, Math.Max(0, left));
            Canvas.SetTop(box, y + h / 2 - bh / 2);
            _canvas.Children.Add(box);
        }
    }

    /// <summary>Metni ve simge kopyalarını (\u0001 + sıra) TextBlock'a yazar.</summary>
    private static void Fill(TextBlock tb, ScreenPiece p, double size)
    {
        tb.FontSize = size;
        tb.LineHeight = size * 1.15;
        tb.Inlines.Clear();
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < p.Text.Length; i++)
        {
            char c = p.Text[i];
            if (c == '\u0001' && i + 1 < p.Text.Length)
            {
                int idx = p.Text[++i] - '0';
                if (sb.Length > 0) { tb.Inlines.Add(new Run(sb.ToString())); sb.Clear(); }
                var img = IconImage(p.Icons, idx, size * 1.15);
                if (img != null) tb.Inlines.Add(new InlineUIContainer(img) { BaselineAlignment = BaselineAlignment.Center });
                continue;
            }
            sb.Append(c);
        }
        if (sb.Length > 0) tb.Inlines.Add(new Run(sb.ToString()));
    }

    private static Image? IconImage(string[] icons, int idx, double height)
    {
        if (idx < 0 || idx >= icons.Length || string.IsNullOrEmpty(icons[idx])) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(Convert.FromBase64String(icons[idx]));
            bmp.EndInit();
            bmp.Freeze();
            return new Image { Source = bmp, Height = height, Margin = new Thickness(2, 0, 2, 0), Stretch = Stretch.Uniform };
        }
        catch
        {
            return null;
        }
    }

    public void ClearItems()
    {
        _lastKey = "";
        _canvas.Children.Clear();
        if (IsVisible) Hide();
    }

    // ───────── Tıklama geçirme ─────────

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x00000020;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);

    private void ApplyWindowStyles()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        ex |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
    }
}
