using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Ps5Altyazi.Capture;
using Ps5Altyazi.Vision;

namespace Ps5Altyazi.TextRecognition;

/// <summary>Kesirli dikdörtgen (oyun alanı pikseli).</summary>
public readonly record struct RectD(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;
    public PixelRect ToPixel() => PixelRect.FromLTRB((int)Math.Floor(X), (int)Math.Floor(Y), (int)Math.Ceiling(Right), (int)Math.Ceiling(Bottom));
}

/// <summary>
/// Ekranda bulunan bir yazı bloğu (tuş ipucu, görev / ipucu metni, HUD yazısı). Koordinatlar oyun alanı pikselidir.
/// Tuş simgeleri (Ⓧ, L2, R1 …) yazının parçası sayılmaz: yerleri <see cref="Icons"/>'ta tutulur ve çeviriye
/// &lt;x id="n"/&gt; yer tutucusu olarak gider; Türkçe, simgenin üstüne değil yanına yazılır.
/// </summary>
public sealed class ScreenTextBlock
{
    /// <summary>Simgesiz düz İngilizce (log, filtre, sözlük kontrolü için).</summary>
    public required string Text { get; init; }
    /// <summary>Çeviriye giden metin: XML'e uygun, satır içi simgeler &lt;x id="n"/&gt;.</summary>
    public required string Tagged { get; init; }
    public required PixelRect Bounds { get; init; }
    /// <summary>Tek satırın (ortanca) yüksekliği, piksel.</summary>
    public required double LineHeight { get; init; }
    public required int LineCount { get; init; }
    /// <summary>Satır İÇİNDEKİ simgeler (id sırasıyla).</summary>
    public required List<RectD> Icons { get; init; }
    /// <summary>Tek satırlı blokta simgelerin arasındaki İngilizce kelime grupları (Icons.Count + 1 adet).</summary>
    public required List<RectD> Runs { get; init; }
    /// <summary>Yazının hemen solunda (-1) / sağında (+1) tuş simgesi var mı (0 = yok / bilinmiyor).</summary>
    public int SideIcon { get; init; }
    /// <summary>Kelimelerin sözlükte bulunma oranı (0..1).</summary>
    public double Known { get; init; }
}

public sealed record ScreenReadResult(List<ScreenTextBlock> Blocks, int WordCount, int RejectedCount, double Milliseconds);

/// <summary>
/// Ekran çevirisi (yalnızca oynanış ekranı: tuş ipuçları, görev / ipucu kutuları, kenardaki HUD yazıları):
/// 1) Karenin ORTASI okunmaz (oyun dünyası: tabelalar, dokular → çöp okuma), altyazı alanı ayrıca hariç tutulur.
/// 2) Kalan kısım tek geçişte Windows OCR ile kelime kelime okunur.
/// 3) Tuş simgeleri kelimelerden ayrılır (OCR'ın "(m", "@" gibi okumaları ya da kelimeler arasındaki geniş boşluk).
/// 4) Kelimeler satırlara, kaydırılmış uzun cümleler bloklara birleştirilir; çöp okumalar elenir.
/// </summary>
public static class ScreenTextReader
{
    private const double SingleScale1080 = 1.5; // 1080p karede büyütme (küçük HUD yazıları için)

    // Okunmayan orta bölge (oranla): oyun dünyası
    public const double CenterL = 0.30, CenterR = 0.70, CenterT = 0.12, CenterB = 0.62;

    public static async Task<ScreenReadResult> ReadAsync(WindowsOcrEngine engine, CapturedFrame game, PixelRect? exclude, bool fullFrame = false)
    {
        var sw = Stopwatch.StartNew();
        int W = game.Width, H = game.Height;
        byte[] gray = ToGray(game);

        // Orta bölgeyi ve altyazı alanını sil (OCR orada yazı aramasın)
        if (!fullFrame) Blank(gray, W, PixelRect.FromLTRB((int)(CenterL * W), (int)(CenterT * H), (int)(CenterR * W), (int)(CenterB * H)));
        if (exclude is { } ex) Blank(gray, W, ex.ClampTo(W, H));
        // Üst orta şerit: oyun dünyasındaki tabelalar / dükkan yazıları burada görünür (L2 ile ata bakarken "BOX", "OFFICE"),
        // oyunların tuş ipuçları ise kenarlarda ve altta olur
        if (!fullFrame) Blank(gray, W, PixelRect.FromLTRB((int)(0.20 * W), 0, (int)(0.80 * W), (int)(CenterT * H)));

        int maxDim = Math.Max(1000, engine.MaxImageDimension);
        // Anlık (F7) okumada zaman kısıtı yok: daha büyütüp okur (süslü küçük harflerde daha az hata)
        double scale = Math.Clamp((fullFrame ? 2.0 : SingleScale1080) * 1080.0 / Math.Max(1, H), 1.0, 2.5);
        scale = Math.Max(0.5, Math.Min(scale, maxDim / (double)Math.Max(W, H)));
        var (bgra, ow, oh) = ScaleImage(gray, W, H, scale);
        var all = new List<OcrWordBox>();
        foreach (var wd in await engine.RecognizeWordsAsync(bgra, ow, oh))
            all.Add(new OcrWordBox(wd.Text, wd.X / scale, wd.Y / scale, wd.W / scale, wd.H / scale));

        var unique = Dedupe(all);
        var lines = GroupLines(unique).Select(CleanLine).Where(l => l != null).Select(l => l!).ToList();
        var blocks = GroupBlocks(lines);

        var result = new List<ScreenTextBlock>();
        int rejected = 0;
        foreach (var bl in blocks)
        {
            var b = bl.Build(W, H);
            if (b == null) continue;
            double known = EnglishWords.IsLoaded ? EnglishWords.KnownRatio(b.Text) : 1;
            bool names = b.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(t => t.Length > 1 && char.IsUpper(t[0]));
            if (b.LineHeight < 0.009 * H || b.LineHeight > 0.12 * H || !ScreenTextFilter.IsPlausible(b.Text) || (known < 0.34 && !names))
            {
                rejected++;
                continue;
            }
            result.Add(new ScreenTextBlock
            {
                Text = b.Text, Tagged = b.Tagged, Bounds = b.Bounds, LineHeight = b.LineHeight, LineCount = b.LineCount,
                Icons = b.Icons, Runs = b.Runs, SideIcon = b.SideIcon, Known = known
            });
        }

        sw.Stop();
        return new ScreenReadResult(result, unique.Count, rejected, sw.Elapsed.TotalMilliseconds);
    }

    private static void Blank(byte[] g, int w, PixelRect r)
    {
        for (int y = r.Y; y < r.Bottom; y++) Array.Clear(g, y * w + r.X, r.Width);
    }

    // ───────── Görüntü hazırlama ─────────

    /// <summary>
    /// Renk kanallarının en büyüğü (HSV "V"): renkli zemin / renkli yazı da belirgin kalır. Ardından %1–%99 germe.
    /// </summary>
    private static byte[] ToGray(CapturedFrame f)
    {
        var g = new byte[f.Width * f.Height];
        var hist = new int[256];
        for (int y = 0; y < f.Height; y++)
        {
            int row = y * f.Stride, o = y * f.Width;
            for (int x = 0; x < f.Width; x++)
            {
                int i = row + x * 4;
                byte v = Math.Max(f.Pixels[i], Math.Max(f.Pixels[i + 1], f.Pixels[i + 2]));
                g[o + x] = v;
                hist[v]++;
            }
        }
        int n = g.Length, lo = 0, hi = 255, acc = 0;
        for (int v = 0; v < 256; v++) { acc += hist[v]; if (acc >= n * 0.01) { lo = v; break; } }
        acc = 0;
        for (int v = 255; v >= 0; v--) { acc += hist[v]; if (acc >= n * 0.01) { hi = v; break; } }
        if (hi - lo >= 40 && (lo > 0 || hi < 255))
        {
            var lut = new byte[256];
            for (int v = 0; v < 256; v++) lut[v] = (byte)Math.Clamp((v - lo) * 255 / (hi - lo), 0, 255);
            for (int i = 0; i < n; i++) g[i] = lut[g[i]];
        }
        return g;
    }

    /// <summary>Gri görüntüyü çift doğrusal ölçekler, OCR için BGRA'ya çevirir.</summary>
    private static (byte[] Bgra, int W, int H) ScaleImage(byte[] gray, int sw, int sh, double scale)
    {
        int ow = Math.Max(1, (int)Math.Round(sw * scale)), oh = Math.Max(1, (int)Math.Round(sh * scale));
        var dst = new byte[ow * oh * 4];
        double inv = 1.0 / scale;
        for (int y = 0; y < oh; y++)
        {
            double fy = Math.Min(sh - 1.001, Math.Max(0, (y + 0.5) * inv - 0.5));
            int y0 = (int)fy;
            double wy = fy - y0;
            int r0 = y0 * sw, r1 = Math.Min(sh - 1, y0 + 1) * sw;
            int o = y * ow * 4;
            for (int x = 0; x < ow; x++)
            {
                double fx = Math.Min(sw - 1.001, Math.Max(0, (x + 0.5) * inv - 0.5));
                int x0 = (int)fx, x1 = Math.Min(sw - 1, x0 + 1);
                double wx = fx - x0;
                double top = gray[r0 + x0] * (1 - wx) + gray[r0 + x1] * wx;
                double bot = gray[r1 + x0] * (1 - wx) + gray[r1 + x1] * wx;
                byte v = (byte)(top * (1 - wy) + bot * wy + 0.5);
                int i = o + x * 4;
                dst[i] = v; dst[i + 1] = v; dst[i + 2] = v; dst[i + 3] = 255;
            }
        }
        return (dst, ow, oh);
    }

    // ───────── Kelime → satır → blok ─────────

    private static List<OcrWordBox> Dedupe(List<OcrWordBox> words)
    {
        var kept = new List<OcrWordBox>();
        foreach (var w in words.OrderByDescending(w => w.Text.Length))
        {
            bool dup = false;
            foreach (var k in kept)
            {
                double ix = Math.Min(w.Right, k.Right) - Math.Max(w.X, k.X);
                double iy = Math.Min(w.Bottom, k.Bottom) - Math.Max(w.Y, k.Y);
                if (ix <= 0 || iy <= 0) continue;
                double inter = ix * iy, small = Math.Min(w.W * w.H, k.W * k.H);
                if (inter > 0.5 * small) { dup = true; break; }
            }
            if (!dup) kept.Add(w);
        }
        return kept;
    }

    /// <summary>Satır: OCR kelimeleri (ham) ya da temizlendikten sonra kelime + simge dizisi.</summary>
    private sealed class Line
    {
        public readonly List<OcrWordBox> Words = new();
        /// <summary>Temiz satırda: soldan sağa öğeler; Icon = true ise tuş simgesi.</summary>
        public readonly List<(OcrWordBox Box, bool Icon)> Items = new();
        public int SideIcon;
        public double X = double.MaxValue, Y = double.MaxValue, Right = double.MinValue, Bottom = double.MinValue;
        public double H => Bottom - Y;
        public double CenterY => (Y + Bottom) / 2;
        public double CenterX => (X + Right) / 2;
        public double WordH => Words.Count == 0 ? 0 : Median(Words.Select(w => w.H));
        public string Text => string.Join(" ", Words.Select(w => w.Text));
        public int IconCount => Items.Count(i => i.Icon);

        public void Add(OcrWordBox w)
        {
            Words.Add(w);
            X = Math.Min(X, w.X); Y = Math.Min(Y, w.Y);
            Right = Math.Max(Right, w.Right); Bottom = Math.Max(Bottom, w.Bottom);
        }
    }

    private static List<Line> GroupLines(List<OcrWordBox> words)
    {
        var lines = new List<Line>();
        foreach (var w in words.OrderBy(w => w.X))
        {
            Line? best = null;
            double bestGap = double.MaxValue;
            foreach (var ln in lines)
            {
                double h = ln.WordH;
                double ratio = w.H / Math.Max(1, h);
                if (ratio < 0.55 || ratio > 1.9) continue;
                if (Math.Abs(w.CenterY - ln.CenterY) > 0.5 * Math.Max(h, w.H)) continue;
                double gap = w.X - ln.Right;
                // Kelime arası ~0.3-0.6 yazı boyu; arada tuş simgesi varsa ~1.5-2 → yine aynı satır
                if (gap < -0.5 * h || gap > 2.2 * Math.Min(h, w.H)) continue;
                if (gap < bestGap) { bestGap = gap; best = ln; }
            }
            if (best == null) { best = new Line(); lines.Add(best); }
            best.Add(w);
        }
        return lines;
    }

    private static bool IsWordLike(OcrWordBox w)
    {
        int letters = w.Text.Count(char.IsLetter);
        int other = w.Text.Count(c => !char.IsLetter(c) && c != '\'' && c != '.' && c != ',' && c != '!' && c != '?' && c != ':' && c != '-' && c != '"');
        return (letters >= 2 && other <= letters / 3) || (w.Text == "I" || w.Text == "a" || w.Text == "A");
    }

    /// <summary>
    /// Satırı temizler: kelimeleri ve tuş simgelerini ayırır. Simge = OCR'ın harf olmayan / boyu farklı okuması
    /// ("(m", "@", "©", "fi") ya da iki kelime arasındaki geniş boşluk. Satır başı/sonundaki simgeler kutuya
    /// dahil edilmez (SideIcon), aradakiler yer tutucu olur. OCR'ın tek harf hataları sözlükle düzeltilir.
    /// </summary>
    private static Line? CleanLine(Line ln)
    {
        var words = ln.Words.OrderBy(w => w.X).ToList();
        var core = words.Where(IsWordLike).ToList();
        if (core.Count == 0) return null;
        double medH = Median(core.Select(w => w.H));
        bool capsLine = core.Count(w => w.Text.Where(char.IsLetter).All(char.IsUpper)) * 2 > core.Count;

        bool IsWord(OcrWordBox w)
        {
            if (!IsWordLike(w)) return false;
            if (w.H > 1.6 * medH || w.H < 0.5 * medH) return false;
            string letters = new(w.Text.Where(char.IsLetter).ToArray());
            if (capsLine && letters.Length <= 2 && letters.All(char.IsLower)) return false; // "PHOTO MODE fi" (simge)
            return true;
        }

        int first = words.FindIndex(IsWord), last = words.FindLastIndex(IsWord);
        if (first < 0) return null;

        var clean = new Line();
        // Kenardaki simgeler: kutuya dahil değil, yalnızca hangi tarafta olduğu
        if (words.Take(first).Any(w => w.H >= 0.6 * medH && w.X > words[first].X - 3 * medH)) clean.SideIcon = -1;
        if (words.Skip(last + 1).Any(w => w.H >= 0.6 * medH && w.X < words[last].Right + 3 * medH)) clean.SideIcon = 1;

        OcrWordBox? prevWord = null;
        bool pendingIcon = false;
        OcrWordBox? pendingIconBox = null;
        for (int i = first; i <= last; i++)
        {
            var w = words[i];
            if (!IsWord(w))
            {
                if (w.H >= 0.6 * medH) { pendingIcon = true; pendingIconBox = pendingIconBox == null ? w : Union(pendingIconBox, w); }
                continue;
            }
            if (prevWord != null)
            {
                double gap = w.X - prevWord.Right;
                if (pendingIcon || gap > 1.0 * medH)
                {
                    // Simge kutusu: OCR okuduysa onun kutusu, yoksa iki kelime arasındaki boşluk
                    var ib = pendingIconBox ?? new OcrWordBox("", prevWord.Right + 0.15 * medH, w.CenterY - 0.65 * medH,
                        Math.Max(1, gap - 0.3 * medH), 1.3 * medH);
                    clean.Items.Add((ib, true));
                }
            }
            pendingIcon = false;
            pendingIconBox = null;

            string t = w.Text;
            string lettersOnly = new(t.Where(char.IsLetter).ToArray());
            bool simpleCase = lettersOnly.Length > 0 && (lettersOnly.All(char.IsUpper) || lettersOnly.All(char.IsLower));
            if (simpleCase && lettersOnly.Length == t.Length) t = EnglishWords.Correct(t);
            var fixedWord = w with { Text = t };
            clean.Add(fixedWord);
            clean.Items.Add((fixedWord, false));
            prevWord = w;
        }
        return clean;
    }

    private static OcrWordBox Union(OcrWordBox a, OcrWordBox b)
    {
        double l = Math.Min(a.X, b.X), t = Math.Min(a.Y, b.Y), r = Math.Max(a.Right, b.Right), btm = Math.Max(a.Bottom, b.Bottom);
        return new OcrWordBox(a.Text + b.Text, l, t, r - l, btm - t);
    }

    private sealed record BuiltBlock(string Text, string Tagged, PixelRect Bounds, double LineHeight, int LineCount,
        List<RectD> Icons, List<RectD> Runs, int SideIcon);

    private sealed class Block
    {
        public readonly List<Line> Lines = new();
        public double LineHeight => Median(Lines.Select(l => l.H));

        public BuiltBlock? Build(int W, int H)
        {
            var text = new StringBuilder();
            var tagged = new StringBuilder();
            var icons = new List<RectD>();
            var runs = new List<RectD>();
            double l = double.MaxValue, t = double.MaxValue, r = double.MinValue, b = double.MinValue;
            RectD? run = null;
            foreach (var ln in Lines)
            {
                foreach (var (box, icon) in ln.Items)
                {
                    if (icon)
                    {
                        icons.Add(new RectD(box.X, box.Y, box.W, box.H));
                        tagged.Append($" <x id=\"{icons.Count}\"/>");
                        if (run is { } done) runs.Add(done);
                        run = null;
                        continue;
                    }
                    if (text.Length > 0) text.Append(' ');
                    text.Append(box.Text);
                    tagged.Append(' ').Append(WebUtility.HtmlEncode(box.Text));
                    run = run is { } cur
                        ? new RectD(Math.Min(cur.X, box.X), Math.Min(cur.Y, box.Y), Math.Max(cur.Right, box.Right) - Math.Min(cur.X, box.X), Math.Max(cur.Bottom, box.Bottom) - Math.Min(cur.Y, box.Y))
                        : new RectD(box.X, box.Y, box.W, box.H);
                    l = Math.Min(l, box.X); t = Math.Min(t, box.Y); r = Math.Max(r, box.Right); b = Math.Max(b, box.Bottom);
                }
            }
            if (run is { } lastRun) runs.Add(lastRun);
            if (text.Length == 0) return null;
            var bounds = PixelRect.FromLTRB((int)Math.Floor(l), (int)Math.Floor(t), (int)Math.Ceiling(r), (int)Math.Ceiling(b)).ClampTo(W, H);
            if (bounds.IsEmpty) return null;
            int side = Lines.Count == 1 ? Lines[0].SideIcon : 0;
            return new BuiltBlock(text.ToString(), tagged.ToString().Trim(), bounds, LineHeight, Lines.Count, icons,
                Lines.Count == 1 ? runs : new List<RectD>(), side);
        }
    }

    private static List<Block> GroupBlocks(List<Line> lines)
    {
        var blocks = new List<Block>();
        foreach (var ln in lines.OrderBy(l => l.Y))
        {
            Block? target = null;
            foreach (var b in blocks)
            {
                var last = b.Lines[^1];
                double h = b.LineHeight;
                double ratio = ln.H / Math.Max(1, h);
                if (ratio < 0.75 || ratio > 1.33) continue;
                double gap = ln.Y - last.Bottom;
                if (gap < -0.25 * h || gap > 0.9 * h) continue;
                bool leftAligned = Math.Abs(ln.X - last.X) < 1.5 * h;
                bool centered = Math.Abs(ln.CenterX - last.CenterX) < 1.5 * h;
                if (!leftAligned && !centered) continue;
                // Paragraf mı, alt alta ayrı ipuçları mı? Üst satır uzun bir cümle olmalı ya da alt satır küçük harfle sürmeli.
                // Yanında tuş simgesi olan satırlar (tuş listesi) asla birleştirilmez
                if (last.SideIcon != 0 || ln.SideIcon != 0) continue;
                string lower = ln.Text.TrimStart();
                int realWords = last.Words.Count(w => w.Text.Count(char.IsLetter) >= 3);
                bool continues = realWords >= 4 ||
                                 (last.Words.Count >= 2 && lower.Length > 0 && char.IsLower(lower[0]));
                if (!continues) continue;
                char end = last.Text.TrimEnd().LastOrDefault();
                if (last.Words.Count < 4 && (end == '.' || end == '!' || end == '?')) continue;
                target = b;
                break;
            }
            if (target == null) { target = new Block(); blocks.Add(target); }
            target.Lines.Add(ln);
        }
        return blocks;
    }

    private static double Median(IEnumerable<double> values)
    {
        var a = values.OrderBy(v => v).ToArray();
        return a.Length == 0 ? 0 : a[a.Length / 2];
    }

    // ───────── Değişim takibi: kutudaki BEYAZ yazı deseni ─────────

    public const int MaskW = 32, MaskH = 6;

    /// <summary>
    /// Kutudaki açık renkli, renksiz (HUD yazısı) piksellerin kaba deseni. Arkadaki oyun dünyası hareket etse de
    /// değişmez; yazı değişince / kaybolunca bozulur → Türkçe kutu o anda kaldırılır.
    /// </summary>
    public static (bool[] Mask, int Count) TextMask(CapturedFrame f, PixelRect r)
    {
        r = r.ClampTo(f.Width, f.Height);
        var m = new bool[MaskW * MaskH];
        if (r.IsEmpty) return (m, 0);
        int count = 0;
        for (int j = 0; j < MaskH; j++)
        for (int i = 0; i < MaskW; i++)
        {
            bool on = false;
            for (int sy = 0; sy < 3 && !on; sy++)
            for (int sx = 0; sx < 3 && !on; sx++)
            {
                int x = r.X + (int)((i + (sx + 0.5) / 3.0) * r.Width / MaskW);
                int y = r.Y + (int)((j + (sy + 0.5) / 3.0) * r.Height / MaskH);
                int p = y * f.Stride + x * 4;
                int bch = f.Pixels[p], g = f.Pixels[p + 1], rch = f.Pixels[p + 2];
                int mx = Math.Max(rch, Math.Max(g, bch)), mn = Math.Min(rch, Math.Min(g, bch));
                on = mx >= 170 && mx - mn <= 60;
            }
            m[j * MaskW + i] = on;
            if (on) count++;
        }
        return (m, count);
    }

    /// <summary>İki desen arasındaki fark (Jaccard uzaklığı, 0 = aynı, 1 = tamamen farklı).</summary>
    public static double MaskDistance(bool[] a, bool[] b)
    {
        int inter = 0, uni = 0;
        for (int i = 0; i < a.Length && i < b.Length; i++)
        {
            if (a[i] && b[i]) inter++;
            if (a[i] || b[i]) uni++;
        }
        return uni == 0 ? 0 : 1.0 - (double)inter / uni;
    }

    /// <summary>Simgenin görüntüsü (BGRA) – Türkçe metnin içinde aynı simgeyi göstermek için.</summary>
    public static (byte[] Bgra, int W, int H) Crop(CapturedFrame f, RectD r)
    {
        var px = r.ToPixel().ClampTo(f.Width, f.Height);
        var c = FrameOps.Crop(f, px);
        return (c.Pixels, c.Width, c.Height);
    }
}

/// <summary>Ekran çevirisinde arka plan dokusundan çıkan anlamsız OCR okumalarını eler.</summary>
public static class ScreenTextFilter
{
    private const string AllowedAccents = "éèêëáàâäíìîïóòôöúùûüñçÉÈÁÀÍÓÚÜÑÇ";
    private const string Vowels = "aeiouyAEIOUYéèêëáàâäíìîïóòôöúùûüÉÈÁÀÍÓÚÜ";

    public static bool IsPlausible(string s)
    {
        s = s.Trim();
        int letters = 0, ascii = 0, nonSpace = 0, iLike = 0;
        foreach (char c in s)
        {
            if (char.IsWhiteSpace(c)) continue;
            nonSpace++;
            if (!char.IsLetter(c)) { if (c == '|') iLike++; continue; }
            letters++;
            if (c < 128) ascii++;
            else if (AllowedAccents.IndexOf(c) < 0) return false; // å, ß, Cyrillic vb. → dokudan çıkan çöp
            if (c == 'I' || c == 'l' || c == 'i') iLike++;
        }
        if (letters < 3) return false;
        if (letters < 0.5 * nonSpace) return false; // "$12.50 Reward" geçsin, "x7#@" geçmesin
        if (iLike > 0.45 * letters) return false; // "IIVO II", "lll Il"

        var tokens = s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => new string(t.Where(char.IsLetter).ToArray()))
            .Where(t => t.Length > 0).ToList();
        if (tokens.Count == 0) return false;

        int checkedTokens = 0, good = 0, longest = 0;
        foreach (var t in tokens)
        {
            longest = Math.Max(longest, t.Length);
            if (t.Length < 3) continue;
            checkedTokens++;
            if (LooksLikeWord(t)) good++;
        }
        if (longest < 3) return false;          // yalnızca "a I to" gibi kırıntılar
        if (tokens.Count == 1 && tokens[0].Length <= 3 && tokens[0].All(char.IsLower)) return false; // "tio" gibi tek kırıntı
        return good >= Math.Max(1, (int)Math.Ceiling(checkedTokens * 0.6));
    }

    private static bool LooksLikeWord(string t)
    {
        if (t.IndexOfAny(Vowels.ToCharArray()) < 0) return false; // ünlüsüz: "Hmm/Psst" dışında neredeyse hep çöp
        // Harf büyüklüğü düzeni: "word", "Word", "WORD" (karışık "wOrD" çöptür)
        bool allLower = t.All(char.IsLower), allUpper = t.All(char.IsUpper);
        bool capitalized = char.IsUpper(t[0]) && t.Skip(1).All(char.IsLower);
        if (!(allLower || allUpper || capitalized)) return false;
        // 6+ ünsüz yan yana (İngilizcede yok) → çöp
        int run = 0;
        foreach (char c in t.ToLowerInvariant())
        {
            run = Vowels.IndexOf(c) >= 0 ? 0 : run + 1;
            if (run >= 6) return false;
        }
        return true;
    }
}
