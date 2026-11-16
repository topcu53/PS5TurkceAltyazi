using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ps5Altyazi.Capture;
using Ps5Altyazi.Fonts;
using Ps5Altyazi.Logging;
using Ps5Altyazi.Tv;

namespace Ps5Altyazi;

/// <summary>
/// Çevirili ekran görüntüsü: oyunun karesi + üstüne Türkçe altyazı (ve oyun içi yazı çevirileri), TV'de / PC'de
/// görünen şekliyle (aynı yazı tipi, boy, renk, örtme kutusu). Windows'un ekran görüntüsü TV'deki yazıyı göremez.
/// </summary>
public partial class MainWindow
{
    private string _shownTurkish = "";
    private List<ScreenPiece> _shownPieces = new();

    /// <summary>Çevirili kareyi PNG olarak kaydeder; yolunu döndürür (kare yoksa null).</summary>
    private string? SaveTranslatedSnapshot()
    {
        var f = _last?.Game;
        if (f == null) return null;
        double W = f.Width, H = f.Height;

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var frame = BitmapSource.Create(f.Width, f.Height, 96, 96, PixelFormats.Bgr32, null, f.Pixels, f.Stride);
            dc.DrawImage(frame, new Rect(0, 0, W, H));
            foreach (var p in _shownPieces) DrawPiece(dc, p, W, H);
            if (_shownTurkish.Length > 0) DrawSubtitle(dc, _shownTurkish, W, H);
        }
        var rtb = new RenderTargetBitmap(f.Width, f.Height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);

        Directory.CreateDirectory(Log.CaptureDir);
        string path = Path.Combine(Log.CaptureDir, $"ceviri-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using (var fs = File.Create(path)) enc.Save(fs);
        return path;
    }

    private (Typeface face, Brush brush, double emPx) SnapshotFont(double H)
    {
        var c = _fontMatcher.Current;
        FontFamily family = new("Segoe UI");
        bool bold = true;
        if (c != null)
        {
            try
            {
                var baseUri = new Uri(FontCatalog.Folder.TrimEnd('\\', '/') + Path.DirectorySeparatorChar);
                family = new FontFamily(baseUri, "./" + c.Font.File + "#" + c.Font.Family);
                bold = c.Font.IsBold;
            }
            catch { }
        }
        Brush brush = Brushes.White;
        if (c != null)
        {
            try { brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(c.ColorHex)); } catch { }
        }
        double em = c != null && c.EmNorm > 0 ? c.EmNorm * H : H * 0.04;
        return (new Typeface(family, FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal), brush, em);
    }

    private void DrawSubtitle(DrawingContext dc, string text, double W, double H)
    {
        var (face, brush, em) = SnapshotFont(H);
        Rect cover = _lastCoverNorm is { Length: 4 } c
            ? new Rect(c[0] * W, c[1] * H, c[2] * W, c[3] * H)
            : new Rect(W * 0.2, H * 0.86, W * 0.6, em * 1.3);
        double maxW = Math.Max(cover.Width, W * 0.6);
        var ft = new FormattedText(text, CultureInfo.GetCultureInfo("tr-TR"), FlowDirection.LeftToRight, face, em, brush, 1.0)
        {
            MaxTextWidth = maxW,
            TextAlignment = TextAlignment.Center
        };
        double pad = 10;
        double boxW = Math.Max(cover.Width, ft.WidthIncludingTrailingWhitespace) + 2 * pad;
        double boxH = Math.Max(cover.Height, ft.Height) + 2 * pad;
        double cx = cover.X + cover.Width / 2;
        double top = Math.Min(cover.Y - pad, H - boxH - 4); // uzun metin aşağı iner, ekrandan taşarsa yukarı kayar
        dc.DrawRoundedRectangle(Brushes.Black, null, new Rect(cx - boxW / 2, top, boxW, boxH), 12, 12);
        dc.DrawText(ft, new Point(cx - maxW / 2, top + pad + Math.Max(0, (boxH - 2 * pad - ft.Height) / 2)));
    }

    private void DrawPiece(DrawingContext dc, ScreenPiece p, double W, double H)
    {
        // Simge işaretleri (\u0001 + rakam) görüntüde boşluk olarak bırakılır: oyunun tuş simgesi altta görünür kalır
        string text = System.Text.RegularExpressions.Regex.Replace(p.Text, "\u0001\\d", "   ").Trim();
        if (text.Length == 0) return;
        var face = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var ft = new FormattedText(text, CultureInfo.GetCultureInfo("tr-TR"), FlowDirection.LeftToRight, face,
            Math.Max(8, p.Fs * H), Brushes.White, 1.0);
        if (p.Wrap && p.MaxW > 0) ft.MaxTextWidth = p.MaxW * W;
        double x = p.X * W, y = p.Y * H;
        double w = Math.Max(p.W * W, ft.WidthIncludingTrailingWhitespace + 8), h = Math.Max(p.H * H, ft.Height + 2);
        if (p.Align == 'r') x = p.X * W + p.W * W - w;
        dc.DrawRectangle(Brushes.Black, null, new Rect(x, y, w, h));
        dc.DrawText(ft, new Point(x + 4, y + (h - ft.Height) / 2));
    }

    /// <summary>F9: çevirili ekran görüntüsü.</summary>
    private void TakeTranslatedSnapshot()
    {
        try
        {
            string? path = SaveTranslatedSnapshot();
            if (path == null) { Log.Warn("Çevirili görüntü için kare yok. Önce yakalamayı başlatın."); return; }
            Log.Success($"Çevirili ekran görüntüsü kaydedildi: {path}");
            Notify("Çevirili ekran görüntüsü kaydedildi");
        }
        catch (Exception ex)
        {
            Log.Error($"Çevirili ekran görüntüsü kaydedilemedi: {ex.Message}");
        }
    }
}
