using System;
using System.Collections.Generic;
using System.Linq;
using Ps5Altyazi.Capture;
using Ps5Altyazi.Vision;

namespace Ps5Altyazi.TextRecognition;

/// <summary>OCR motoruna verilecek görüntü (BGRA, koyu metin / açık zemin).</summary>
public sealed record OcrInput(byte[] Bgra, int Width, int Height, PixelRect SourceRect, double Scale);

/// <summary>
/// Altyazı satırlarını OCR için hazırlar: satırların çevresini kırp, griye çevir, kontrastı ger,
/// ters çevir (siyah yazı / beyaz zemin) ve satır yüksekliği ~40 px olacak şekilde büyüt.
/// Bu adımlar RDR2 örnek kareleriyle Tesseract üzerinde doğrulandı (tam ve küçük çözünürlükte doğru okuma).
/// </summary>
public static class OcrPreprocessor
{
    public const double TargetLineHeight = 40;

    public static OcrInput? Build(CapturedFrame game, IReadOnlyList<TextLine> lines, int maxDimension)
    {
        if (lines.Count == 0) return null;
        int l = lines.Min(x => x.Bounds.X), t = lines.Min(x => x.Bounds.Y);
        int r = lines.Max(x => x.Bounds.Right), b = lines.Max(x => x.Bounds.Bottom);
        int lineH = Math.Max(1, lines.Max(x => x.Bounds.Height));

        var rect = PixelRect.FromLTRB(
            // Yanlarda 1 satır yüksekliği pay: satır başındaki tek harfli kelimeler ("I will.") kaybolmasın.
            (int)(l - 1.0 * lineH), (int)(t - 0.4 * lineH),
            (int)(r + 1.0 * lineH), (int)(b + 0.4 * lineH)).ClampTo(game.Width, game.Height);
        if (rect.Width < 8 || rect.Height < 8) return null;

        // Gri tonlama
        int w = rect.Width, h = rect.Height;
        var gray = new byte[w * h];
        var hist = new int[256];
        for (int y = 0; y < h; y++)
        {
            int src = (rect.Y + y) * game.Stride + rect.X * 4;
            for (int x = 0; x < w; x++, src += 4)
            {
                byte v = (byte)((299 * game.Pixels[src + 2] + 587 * game.Pixels[src + 1] + 114 * game.Pixels[src]) / 1000);
                gray[y * w + x] = v;
                hist[v]++;
            }
        }

        // Kontrast germe: %5 → 0, %99,5 → 255
        int lo = Percentile(hist, w * h, 0.05), hi = Percentile(hist, w * h, 0.995);
        if (hi <= lo + 10) hi = Math.Min(255, lo + 10);
        var lut = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            double v = (i - lo) * 255.0 / (hi - lo);
            lut[i] = (byte)(255 - Math.Clamp((int)Math.Round(v), 0, 255)); // ters çevir
        }

        // Ölçek: satır yüksekliği ~40 px, en fazla 3x, motorun boyut sınırını aşmadan
        double scale = Math.Clamp(TargetLineHeight / lineH, 1.0, 3.0);
        if (maxDimension > 0) scale = Math.Min(scale, (double)maxDimension / Math.Max(w, h));
        scale = Math.Max(scale, 0.1);
        int ow = Math.Max(1, (int)Math.Round(w * scale)), oh = Math.Max(1, (int)Math.Round(h * scale));

        var outPx = new byte[ow * oh * 4];
        for (int oy = 0; oy < oh; oy++)
        {
            double sy = (oy + 0.5) / scale - 0.5;
            int y0 = Math.Clamp((int)Math.Floor(sy), 0, h - 1), y1 = Math.Min(h - 1, y0 + 1);
            double fy = Math.Clamp(sy - y0, 0, 1);
            for (int ox = 0; ox < ow; ox++)
            {
                double sx = (ox + 0.5) / scale - 0.5;
                int x0 = Math.Clamp((int)Math.Floor(sx), 0, w - 1), x1 = Math.Min(w - 1, x0 + 1);
                double fx = Math.Clamp(sx - x0, 0, 1);
                double v = lut[gray[y0 * w + x0]] * (1 - fx) * (1 - fy) + lut[gray[y0 * w + x1]] * fx * (1 - fy)
                         + lut[gray[y1 * w + x0]] * (1 - fx) * fy + lut[gray[y1 * w + x1]] * fx * fy;
                byte g = (byte)Math.Clamp((int)Math.Round(v), 0, 255);
                int j = (oy * ow + ox) * 4;
                outPx[j] = g; outPx[j + 1] = g; outPx[j + 2] = g; outPx[j + 3] = 255;
            }
        }

        return new OcrInput(outPx, ow, oh, rect, scale);
    }

    /// <summary>
    /// İkinci yöntem (parlak / hareketli zeminler için): yalnızca kenarında koyu çizgi ya da gölge bulunan parlak
    /// pikseller yazı sayılır. Oyun altyazıları (GTA, RDR2…) beyaz yazı + siyah kenar çizgisidir; gökyüzü, beyaz araba,
    /// ışık gibi geniş parlak alanların yanında koyu piksel olmadığı için elenir. Çıktı: siyah yazı / beyaz zemin.
    /// </summary>
    public static OcrInput? BuildOutlined(CapturedFrame game, IReadOnlyList<TextLine> lines, int maxDimension)
    {
        if (lines.Count == 0) return null;
        int l = lines.Min(x => x.Bounds.X), t = lines.Min(x => x.Bounds.Y);
        int r = lines.Max(x => x.Bounds.Right), b = lines.Max(x => x.Bounds.Bottom);
        int lineH = Math.Max(1, lines.Max(x => x.Bounds.Height));
        var rect = PixelRect.FromLTRB((int)(l - 1.0 * lineH), (int)(t - 0.4 * lineH),
            (int)(r + 1.0 * lineH), (int)(b + 0.4 * lineH)).ClampTo(game.Width, game.Height);
        if (rect.Width < 8 || rect.Height < 8) return null;

        int w = rect.Width, h = rect.Height;
        var v = new byte[w * h];
        var hist = new int[256];
        for (int y = 0; y < h; y++)
        {
            int src = (rect.Y + y) * game.Stride + rect.X * 4;
            for (int x = 0; x < w; x++, src += 4)
            {
                byte bb = game.Pixels[src], gg = game.Pixels[src + 1], rr = game.Pixels[src + 2];
                byte mx = Math.Max(rr, Math.Max(gg, bb));
                v[y * w + x] = mx;
                hist[mx]++;
            }
        }
        // Parlaklık eşiği: en parlak %8'lik kısmın altı, ama 150–235 arasında
        int bright = Math.Clamp(Percentile(hist, w * h, 0.92) - 25, 150, 235);
        const int dark = 95;

        // Koyu piksellerin yakınlığı: koyu maskeyi rad yarıçapında genişlet (ayrılabilir kutu filtresi)
        int rad = Math.Max(2, lineH / 9);
        var darkRow = new int[w * h];
        for (int y = 0; y < h; y++)
        {
            int acc = 0, row = y * w;
            for (int x = -rad; x < w; x++)
            {
                int xa = x + rad, xr = x - rad - 1;
                if (xa < w && v[row + xa] < dark) acc++;
                if (xr >= 0 && v[row + xr] < dark) acc--;
                if (x >= 0) darkRow[row + x] = acc;
            }
        }
        var near = new bool[w * h];
        for (int x = 0; x < w; x++)
        {
            int acc = 0;
            for (int y = -rad; y < h; y++)
            {
                int ya = y + rad, yr = y - rad - 1;
                if (ya < h) acc += darkRow[ya * w + x] > 0 ? 1 : 0;
                if (yr >= 0) acc -= darkRow[yr * w + x] > 0 ? 1 : 0;
                if (y >= 0) near[y * w + x] = acc > 0;
            }
        }

        var gray = new byte[w * h];
        int inkCount = 0;
        for (int i = 0; i < gray.Length; i++)
        {
            bool ink = v[i] >= bright && near[i];
            gray[i] = ink ? (byte)0 : (byte)255;
            if (ink) inkCount++;
        }
        if (inkCount < 20) return null;

        double scale = Math.Clamp(TargetLineHeight / lineH, 1.0, 3.0);
        if (maxDimension > 0) scale = Math.Min(scale, (double)maxDimension / Math.Max(w, h));
        scale = Math.Max(scale, 0.1);
        int ow = Math.Max(1, (int)Math.Round(w * scale)), oh = Math.Max(1, (int)Math.Round(h * scale));
        var outPx = new byte[ow * oh * 4];
        for (int oy = 0; oy < oh; oy++)
        {
            double sy = (oy + 0.5) / scale - 0.5;
            int y0 = Math.Clamp((int)Math.Floor(sy), 0, h - 1), y1 = Math.Min(h - 1, y0 + 1);
            double fy = Math.Clamp(sy - y0, 0, 1);
            for (int ox = 0; ox < ow; ox++)
            {
                double sx = (ox + 0.5) / scale - 0.5;
                int x0 = Math.Clamp((int)Math.Floor(sx), 0, w - 1), x1 = Math.Min(w - 1, x0 + 1);
                double fx = Math.Clamp(sx - x0, 0, 1);
                double g = gray[y0 * w + x0] * (1 - fx) * (1 - fy) + gray[y0 * w + x1] * fx * (1 - fy)
                         + gray[y1 * w + x0] * (1 - fx) * fy + gray[y1 * w + x1] * fx * fy;
                byte gv = (byte)Math.Clamp((int)Math.Round(g), 0, 255);
                int j = (oy * ow + ox) * 4;
                outPx[j] = gv; outPx[j + 1] = gv; outPx[j + 2] = gv; outPx[j + 3] = 255;
            }
        }
        return new OcrInput(outPx, ow, oh, rect, scale);
    }

    private static int Percentile(int[] hist, int total, double p)
    {
        long target = (long)(total * p), acc = 0;
        for (int i = 0; i < 256; i++)
        {
            acc += hist[i];
            if (acc > target) return i;
        }
        return 255;
    }
}
