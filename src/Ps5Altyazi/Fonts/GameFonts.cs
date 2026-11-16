using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Ps5Altyazi.Capture;
using Ps5Altyazi.Logging;
using Ps5Altyazi.Vision;

namespace Ps5Altyazi.Fonts;

/// <summary>Programla gelen (ya da kullanıcının eklediği) bir yazı tipi ve ölçülmüş özellikleri.</summary>
public sealed class FontEntry
{
    /// <summary>Dosya adı (ör. "barlow-600.ttf" ya da "custom-....ttf").</summary>
    public string File { get; set; } = "";
    /// <summary>Yazı tipinin içindeki aile adı (WPF "#Aile" için).</summary>
    public string Family { get; set; } = "";
    public string Style { get; set; } = "Regular";
    public int Weight { get; set; } = 400;
    public bool Serif { get; set; }
    // Ölçülmüş özellikler (küçük harfli metin): x-yüksekliği/üst uzantı, çizgi kalınlığı/x-yük., harf genişliği/x-yük.
    public double R { get; set; } = 0.72;
    public double S { get; set; } = 0.17;
    public double W { get; set; } = 0.82;
    // BÜYÜK HARFLİ metin: çizgi/büyük harf boyu, genişlik/büyük harf boyu
    public double Cs { get; set; } = 0.13;
    public double Cw { get; set; } = 0.74;
    /// <summary>x-yüksekliği / yazı boyu (em) – aynı ölçüm yöntemiyle; Türkçe yazı boyunu bulmak için.</summary>
    public double XhPerEm { get; set; } = 0.53;
    public double CapPerEm { get; set; } = 0.71;
    /// <summary>Çizgi kontrastı: yatay çizgi kalınlığı / dikey çizgi kalınlığı (tırnaklı fontlarda düşük). 0 = bilinmiyor.</summary>
    public double K { get; set; }
    public double Ck { get; set; }
    public bool Custom { get; set; }

    public string Key => System.IO.Path.GetFileNameWithoutExtension(File);
    public bool IsBold => Style.Contains("Bold", StringComparison.OrdinalIgnoreCase);
    public string DisplayName => Custom ? $"{Family} (eklenen dosya)" : (IsBold ? $"{Family} Bold" : Family);
    /// <summary>TV / telefon için CSS aile adı.</summary>
    public string CssFamily => "ap-" + Key;
}

/// <summary>
/// Yazı tipi kataloğu: programa gömülü açık lisanslı Türkçe karakterli fontlar + kullanıcının eklediği fontlar.
/// Fontlar %LOCALAPPDATA%\PS5TurkceAltyazi\fonts klasörüne çıkarılır (PC penceresi ve TV bunları kullanır).
/// </summary>
public static class FontCatalog
{
    public static string Folder { get; } = System.IO.Path.Combine(Log.AppDataDir, "fonts");
    private static List<FontEntry>? _entries;

    public static IReadOnlyList<FontEntry> All => _entries ??= Load();

    public static FontEntry? Find(string? key) =>
        key == null ? null : All.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));

    private static List<FontEntry> Load()
    {
        var list = new List<FontEntry>();
        try
        {
            Directory.CreateDirectory(Folder);
            var asm = typeof(FontCatalog).Assembly;
            using (var s = asm.GetManifestResourceStream("fonts/catalog.json"))
            {
                if (s != null)
                {
                    var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    list.AddRange(JsonSerializer.Deserialize<List<FontEntry>>(s, opts) ?? new());
                }
            }
            foreach (var e in list)
            {
                string path = System.IO.Path.Combine(Folder, e.File);
                using var rs = asm.GetManifestResourceStream("fonts/" + e.File);
                if (rs == null) continue;
                if (!System.IO.File.Exists(path) || new FileInfo(path).Length != rs.Length)
                {
                    using var fs = System.IO.File.Create(path);
                    rs.CopyTo(fs);
                }
            }
            // Kullanıcının eklediği fontlar
            foreach (var path in Directory.GetFiles(Folder, "custom-*.*"))
            {
                var info = TtfInfo.Read(path);
                if (info != null) list.Add(info);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Yazı tipleri hazırlanamadı: {ex.Message}");
        }
        return list;
    }

    /// <summary>Kullanıcının seçtiği .ttf / .otf dosyasını kataloğa ekler (kopyalar).</summary>
    public static FontEntry AddCustom(string sourcePath)
    {
        var info = TtfInfo.Read(sourcePath) ?? throw new InvalidOperationException("Bu dosya okunamadı (yalnızca .ttf / .otf).");
        string ext = System.IO.Path.GetExtension(sourcePath).ToLowerInvariant();
        string safe = new string(info.Family.ToLowerInvariant().Where(char.IsLetterOrDigit).Take(24).ToArray());
        string name = $"custom-{(safe.Length == 0 ? "font" : safe)}-{info.Weight}{ext}";
        Directory.CreateDirectory(Folder);
        System.IO.File.Copy(sourcePath, System.IO.Path.Combine(Folder, name), overwrite: true);
        info.File = name;
        _entries = null; // yeniden yükle
        return Find(info.Key) ?? info;
    }

    public static string PathOf(FontEntry e) => System.IO.Path.Combine(Folder, e.File);

    /// <summary>TV'nin indirebileceği font dosyası mı? (yalnızca bu klasördeki, adı güvenli dosyalar)</summary>
    public static string? ServeablePath(string fileName)
    {
        if (fileName.Length > 80 || fileName.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-' || c == '.'))) return null;
        if (fileName.Contains("..")) return null;
        string ext = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
        if (ext != ".ttf" && ext != ".otf") return null;
        string path = System.IO.Path.Combine(Folder, fileName);
        return System.IO.File.Exists(path) ? path : null;
    }
}

/// <summary>TTF/OTF dosyasından aile adı, kalınlık ve x-yüksekliği / büyük harf boyu (OS/2 tablosu).</summary>
public static class TtfInfo
{
    public static FontEntry? Read(string path)
    {
        try
        {
            byte[] d = System.IO.File.ReadAllBytes(path);
            if (d.Length < 12) return null;
            int numTables = U16(d, 4);
            int head = -1, os2 = -1, name = -1;
            for (int i = 0; i < numTables; i++)
            {
                int rec = 12 + i * 16;
                if (rec + 16 > d.Length) return null;
                string tag = Encoding.ASCII.GetString(d, rec, 4);
                int off = (int)U32(d, rec + 8);
                if (tag == "head") head = off;
                else if (tag == "OS/2") os2 = off;
                else if (tag == "name") name = off;
            }
            if (head < 0 || os2 < 0) return null;
            int upm = U16(d, head + 18);
            if (upm <= 0) return null;
            int weight = U16(d, os2 + 4);
            int version = U16(d, os2);
            double xh = 0.52, cap = 0.70;
            if (version >= 2 && os2 + 90 <= d.Length)
            {
                int sx = (short)U16(d, os2 + 86), sc = (short)U16(d, os2 + 88);
                if (sx > 0) xh = sx / (double)upm;
                if (sc > 0) cap = sc / (double)upm;
            }
            string family = (name >= 0 ? ReadName(d, name, 1) : null) ?? System.IO.Path.GetFileNameWithoutExtension(path);
            string style = (name >= 0 ? ReadName(d, name, 2) : null) ?? "Regular";
            // Katalogdaki ölçümle uyum: kenar yumuşatma nedeniyle ölçülen değer tablodan ~%4 büyük çıkar
            return new FontEntry
            {
                File = System.IO.Path.GetFileName(path), Family = family, Style = style, Weight = weight,
                XhPerEm = xh * 1.04, CapPerEm = cap * 1.0, Custom = true
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadName(byte[] d, int tbl, int id)
    {
        int count = U16(d, tbl + 2), strOff = tbl + U16(d, tbl + 4);
        string? fallback = null;
        for (int i = 0; i < count; i++)
        {
            int r = tbl + 6 + i * 12;
            int plat = U16(d, r), enc = U16(d, r + 2), nid = U16(d, r + 6), len = U16(d, r + 8), off = U16(d, r + 10);
            if (nid != id || strOff + off + len > d.Length) continue;
            if (plat == 3 && (enc == 1 || enc == 10)) return Encoding.BigEndianUnicode.GetString(d, strOff + off, len);
            if (plat == 1 && enc == 0) fallback ??= Encoding.Latin1.GetString(d, strOff + off, len);
        }
        return fallback;
    }

    private static int U16(byte[] d, int o) => (d[o] << 8) | d[o + 1];
    private static uint U32(byte[] d, int o) => (uint)((d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3]);
}

/// <summary>Bir altyazı satırının ölçümü.</summary>
public sealed record LineMeasure(double Xh, double Asc, double R, double S, double W, bool Caps, byte Red, byte Green, byte Blue, bool Outline, double K = 0);

/// <summary>Oyunun altyazısına göre seçilen yazı tipi ve Türkçe yazının boyu.</summary>
public sealed record GameFontChoice(FontEntry Font, double EmNorm, string ColorHex, bool Outline, int Samples)
{
    /// <summary>Yazı boyu (em) piksel olarak, verilen kare yüksekliği için.</summary>
    public double EmPx(double frameHeight) => EmNorm * frameHeight;
}

/// <summary>
/// Oyunun altyazı yazısını ölçer ve katalogdaki en benzer yazı tipini seçer.
/// Ölçüm (Python'da da aynısı ile katalog üretildi):
///   - Satır parlaklık (V) kırpıntısı → Otsu eşiği → yazı pikselleri (altyazı zeminden açıktır)
///   - Satır yoğunluk profili: yoğun bant = x-yüksekliği (BÜYÜK HARF metinde büyük harf boyu), en üst = üst uzantı
///   - Bandın ortasında yatay koşu uzunluklarının ortancası = çizgi kalınlığı
///   - Yazı genişliği / karakter sayısı = harf genişliği
/// Oranlar (kalınlık/x-yük., genişlik/x-yük., x-yük./üst uzantı) katalogla karşılaştırılır.
/// Yazı boyu: ölçülen x-yüksekliği / seçilen fontun (x-yüksekliği / em) oranı → orijinalle aynı harf boyu.
/// </summary>
public sealed class SubtitleFontMatcher
{
    private readonly List<LineMeasure> _lower = new(), _caps = new();
    private const int Keep = 24;
    public GameFontChoice? Current { get; private set; }
    /// <summary>Kullanıcının sabitlediği yazı tipi (null = otomatik seçim). Boy ve renk yine ölçülür.</summary>
    public FontEntry? Fixed { get; set; }
    /// <summary>Oyuna hazır aile (ör. "eb-garamond"): otomatik seçim yalnızca bu ailenin kalınlıkları arasından yapılır.</summary>
    public string? FamilyHint { get; set; }
    private double _currentDist = double.MaxValue;

    public void Reset()
    {
        _lower.Clear();
        _caps.Clear();
        Current = null;
        _currentDist = double.MaxValue;
    }

    /// <summary>Önceden öğrenilmiş seçimi yükler (program açılınca ilk altyazıdan itibaren doğru font/boy).</summary>
    public void Seed(GameFontChoice choice)
    {
        Current = choice;
        _currentDist = double.MaxValue;
    }

    /// <summary>OCR'ı tutan bir altyazının satırlarını ölçer. Seçim değiştiyse true.</summary>
    public bool Add(CapturedFrame game, IReadOnlyList<PixelRect> lines, IReadOnlyList<string> texts)
    {
        if (lines.Count == 0 || lines.Count != texts.Count) return false;
        for (int i = 0; i < lines.Count; i++)
        {
            var m = Measure(game, lines[i], texts[i]);
            if (m == null) continue;
            var list = m.Caps ? _caps : _lower;
            list.Add(m);
            if (list.Count > Keep) list.RemoveAt(0);
            DetectStyleChange(list);
        }
        return Choose(game.Height);
    }

    private bool _restyled;
    private DateTime _lastRestyle = DateTime.MinValue;

    /// <summary>
    /// Oyun (ya da altyazı stili) değişti mi? Son 4 ölçüm öncekilerden belirgin farklıysa eski ölçümler silinir;
    /// yeni oyunun yazısı birkaç altyazıda öğrenilir (kullanıcının oyun seçmesine gerek kalmaz).
    /// </summary>
    private void DetectStyleChange(List<LineMeasure> list)
    {
        const int recent = 5;
        if (list.Count < recent + 6) return;
        if (DateTime.UtcNow - _lastRestyle < TimeSpan.FromMinutes(3)) return; // aynı oyunda sık sık sıfırlanmasın
        var old = list.Take(list.Count - recent).ToList();
        var now = list.Skip(list.Count - recent).ToList();
        double xo = Median(old.Select(m => m.Xh)), xn = Median(now.Select(m => m.Xh));
        double so = Median(old.Select(m => m.S)), sn = Median(now.Select(m => m.S));
        double wo = Median(old.Select(m => m.W)), wn = Median(now.Select(m => m.W));
        var ko = old.Where(m => m.K > 0).Select(m => m.K).ToList(); var kn = now.Where(m => m.K > 0).Select(m => m.K).ToList();
        double dk = ko.Count > 0 && kn.Count > 0 ? Math.Abs(Median(ko) - Median(kn)) : 0;
        // Her yeni ölçüm aynı yöne sapmalı (tek tük OCR hatası stil değişimi sayılmasın)
        bool allBigger = now.All(m => m.Xh > xo * 1.12), allSmaller = now.All(m => m.Xh < xo * 0.88);
        bool sizeJump = (allBigger || allSmaller) && Math.Abs(xn - xo) > xo * 0.15;
        // Kısa satırlar (ör. "Of all the days...") ölçüyü oynatır: şekil değişimi için en az iki ölçü birlikte belirgin değişmeli
        int shapeVotes = (Math.Abs(sn - so) > 0.06 ? 1 : 0) + (Math.Abs(wn - wo) > 0.14 ? 1 : 0) + (dk > 0.28 ? 1 : 0);
        bool shapeJump = shapeVotes >= 2;
        if (!sizeJump && !shapeJump) return;
        list.RemoveRange(0, list.Count - recent);
        (ReferenceEquals(list, _lower) ? _caps : _lower).Clear(); // diğer listede de eski oyunun ölçümleri var
        _restyled = true;
        _lastRestyle = DateTime.UtcNow;
        Log.Info("Altyazı yazısı değişti (başka oyun?): yazı tipi ve boy yeniden öğreniliyor.");
    }

    private bool Choose(double frameH)
    {
        if (frameH <= 0) return false;
        // Küçük harfli satırlar daha güvenilir (x-yüksekliği); büyük harfe yalnızca küçük harf ölçümü yoksa geçilir.
        // (Eskiden sayıya göre gidip gelince yazı boyu bir büyüyüp bir küçülüyordu.)
        bool useCaps = _lower.Count < 3 && _caps.Count >= 3;
        var src = useCaps ? _caps : _lower;
        if (src.Count < 3) return false;

        double r = Median(src.Select(m => m.R)), s = Median(src.Select(m => m.S)), w = Median(src.Select(m => m.W));
        double k = Median(src.Where(m => m.K > 0).Select(m => m.K));
        double xh = Median(src.Select(m => m.Xh));
        FontEntry? best = null;
        double bestD = double.MaxValue;
        foreach (var f in FontCatalog.All)
        {
            if (f.Custom) continue; // tırnaklı (serif) fontlar da aday: çizgi kontrastıyla ayırt edilir
            if (FamilyHint != null && !f.Key.StartsWith(FamilyHint + "-", StringComparison.OrdinalIgnoreCase)) continue;
            double d = Distance(f, useCaps, r, s, w, k);
            if (d < bestD) { bestD = d; best = f; }
        }
        if (best == null) return false;

        // Renk ve kenar çizgisi: son ölçümlerin ortancası / çoğunluğu
        byte cr = (byte)Median(src.Select(m => (double)m.Red)), cg = (byte)Median(src.Select(m => (double)m.Green)), cb = (byte)Median(src.Select(m => (double)m.Blue));
        if (cr > 200 && cg > 200 && cb > 200 && Math.Max(cr, Math.Max(cg, cb)) - Math.Min(cr, Math.Min(cg, cb)) < 30) { cr = cg = cb = 255; }
        string color = $"#{cr:X2}{cg:X2}{cb:X2}";
        bool outline = src.Count(m => m.Outline) * 2 > src.Count;

        // Seçili fontu korumayı tercih et: yeni font ancak belirgin şekilde daha iyiyse
        FontEntry chosen = Fixed ?? best;
        if (Fixed == null && Current != null && FontCatalog.Find(Current.Font.Key) != null && Current.Font.Key != best.Key)
        {
            var cur = Current.Font;
            double curD = Distance(cur, useCaps, r, s, w, k);
            bool clearlyWrong = curD > 2.5 && bestD < curD * 0.6; // ör. önceki oyundan kalan font buna hiç benzemiyor
            if (!_restyled && !clearlyWrong && (bestD > curD * 0.75 || src.Count < 6)) chosen = cur;
        }

        double perEm = useCaps ? chosen.CapPerEm : chosen.XhPerEm;
        double em = xh / Math.Max(0.2, perEm) / frameH;
        // Boy kararlılığı: yeterli ölçüm varsa küçük oynamalarda eski boy korunur (yazı her altyazıda aynı boyda kalsın)
        if (!_restyled && Current != null && Current.EmNorm > 0 && Current.Font.Key == chosen.Key && Current.Samples >= 6 &&
            Math.Abs(Current.EmNorm - em) < Current.EmNorm * 0.07)
            em = Current.EmNorm;
        var next = new GameFontChoice(chosen, em, color, outline, src.Count);
        bool changed = Current == null || Current.Font.Key != next.Font.Key || Math.Abs(Current.EmNorm - em) > Current.EmNorm * 0.03 ||
                       Current.ColorHex != color || Current.Outline != outline;
        Current = next;
        _currentDist = bestD;
        _restyled = false;
        return changed;
    }

    /// <summary>Ölçülen yazı ile bir font arasındaki fark (küçük = daha benzer).</summary>
    private static double Distance(FontEntry f, bool caps, double r, double s, double w, double k)
    {
        double fk = caps ? f.Ck : f.K;
        double dk = fk > 0 && k > 0 ? Math.Abs(k - fk) / 0.10 : 0;
        return caps
            ? Math.Abs(s - f.Cs) / 0.03 + Math.Abs(w - f.Cw) / 0.05 + dk
            : Math.Abs(s - f.S) / 0.03 + Math.Abs(w - f.W) / 0.05 + Math.Abs(r - f.R) / 0.04 + dk;
    }

    /// <summary>
    /// Yazı tipini sabitler (null = otomatik). Ölçüm varsa boy yeni fonta göre hemen yeniden hesaplanır.
    /// </summary>
    public void SetFixed(FontEntry? font, double frameH)
    {
        Fixed = font;
        if (!Choose(frameH) && font != null && Current != null)
        {
            // Yeterli ölçüm yok: eski boyu yeni fontun oranına göre çevir
            double scale = Current.Font.XhPerEm / Math.Max(0.2, font.XhPerEm);
            Current = Current with { Font = font, EmNorm = Current.EmNorm * scale };
        }
    }

    // ───────── Ölçüm ─────────

    public static LineMeasure? Measure(CapturedFrame f, PixelRect line, string text)
    {
        text = text.Trim();
        if (text.Length < 4) return null;
        int padY = (int)(line.Height * 0.25), padX = (int)(line.Height * 0.3);
        var r = PixelRect.FromLTRB(line.X - padX, line.Y - padY, line.Right + padX, line.Bottom + padY).ClampTo(f.Width, f.Height);
        if (r.Width < 20 || r.Height < 8) return null;

        int w = r.Width, h = r.Height;
        var v = new byte[w * h];
        var hist = new int[256];
        for (int y = 0; y < h; y++)
        {
            int row = (r.Y + y) * f.Stride + r.X * 4;
            for (int x = 0; x < w; x++)
            {
                int p = row + x * 4;
                byte val = Math.Max(f.Pixels[p], Math.Max(f.Pixels[p + 1], f.Pixels[p + 2]));
                v[y * w + x] = val;
                hist[val]++;
            }
        }
        int t = Otsu(hist, v.Length);
        var ink = new bool[w * h];
        var rows = new int[h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (v[y * w + x] > t) { ink[y * w + x] = true; rows[y]++; }

        int peak = rows.Max();
        if (peak < 3) return null;
        int xTop = -1, baseRow = -1, top = -1;
        for (int y = 0; y < h; y++)
        {
            if (top < 0 && rows[y] >= 0.04 * peak) top = y;
            if (rows[y] >= 0.45 * peak) { if (xTop < 0) xTop = y; baseRow = y + 1; }
        }
        int xh = baseRow - xTop, asc = baseRow - top;
        if (xh < 5 || asc <= 0) return null;

        var runs = new List<int>();
        for (int y = xTop + (int)(xh * 0.3); y < xTop + (int)Math.Ceiling(xh * 0.7); y++)
        {
            int n = 0;
            for (int x = 0; x < w; x++)
            {
                if (ink[y * w + x]) n++;
                else if (n > 0) { runs.Add(n); n = 0; }
            }
            if (n > 0) runs.Add(n);
        }
        if (runs.Count == 0) return null;
        runs.Sort();
        double stroke = runs.Count % 2 == 1 ? runs[runs.Count / 2] : (runs[runs.Count / 2 - 1] + runs[runs.Count / 2]) / 2.0;

        // Çizgi kontrastı: x-yüksekliği bandındaki yatay çizgilerin kalınlığı (sütunlardaki kısa dikey koşular)
        var hruns = new List<int>();
        for (int x = 0; x < w; x++)
        {
            int n = 0;
            for (int y = xTop; y < baseRow; y++)
            {
                if (ink[y * w + x]) n++;
                else if (n > 0) { if (n < 0.5 * xh) hruns.Add(n); n = 0; }
            }
        }
        double hstroke = stroke;
        if (hruns.Count > 0)
        {
            hruns.Sort();
            hstroke = hruns.Count % 2 == 1 ? hruns[hruns.Count / 2] : (hruns[hruns.Count / 2 - 1] + hruns[hruns.Count / 2]) / 2.0;
        }
        // Küçük yazıda (x-yüksekliği < 14 px) piksel yuvarlaması kontrastı bozar → kullanma
        double contrast = xh >= 14 && stroke > 0 ? hstroke / stroke : 0;

        int c0 = -1, c1 = -1;
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                if (ink[y * w + x]) { if (c0 < 0) c0 = x; c1 = x; break; }
        int width = c1 - c0 + 1;

        var letters = text.Where(char.IsLetter).ToList();
        bool caps = letters.Count > 0 && letters.All(char.IsUpper);

        // Renk: yazının iç pikselleri; kenar çizgisi: yazının hemen dışındaki piksellerin koyu olma oranı
        var rs = new List<byte>(); var gs = new List<byte>(); var bs = new List<byte>();
        int edge = 0, darkEdge = 0;
        for (int y = 1; y < h - 1; y++)
        {
            for (int x = 1; x < w - 1; x++)
            {
                int i = y * w + x;
                if (ink[i])
                {
                    if (ink[i - 1] && ink[i + 1] && ink[i - w] && ink[i + w])
                    {
                        int p = (r.Y + y) * f.Stride + (r.X + x) * 4;
                        bs.Add(f.Pixels[p]); gs.Add(f.Pixels[p + 1]); rs.Add(f.Pixels[p + 2]);
                    }
                }
                else if (ink[i - 1] || ink[i + 1] || ink[i - w] || ink[i + w])
                {
                    edge++;
                    if (v[i] < 70) darkEdge++;
                }
            }
        }
        if (rs.Count < 10) return null;
        // Kenar çizgisi yalnızca zemin açıkken anlaşılır (koyu zeminde her şey koyu)
        long bgSum = 0; int bgN = 0;
        for (int i = 0; i < v.Length; i++) if (!ink[i]) { bgSum += v[i]; bgN++; }
        double bg = bgN == 0 ? 0 : bgSum / (double)bgN;
        bool outline = edge > 0 && bg > 80 && darkEdge > edge * 0.55;

        return new LineMeasure(xh, asc, xh / (double)asc, stroke / xh, width / (double)text.Length / xh, caps,
            MedianByte(rs), MedianByte(gs), MedianByte(bs), outline, contrast);
    }

    private static int Otsu(int[] hist, int total)
    {
        double sumAll = 0;
        for (int i = 0; i < 256; i++) sumAll += i * (double)hist[i];
        double wb = 0, sb = 0, best = 0;
        int t = 128;
        for (int i = 0; i < 256; i++)
        {
            wb += hist[i];
            if (wb == 0) continue;
            double wf = total - wb;
            if (wf == 0) break;
            sb += i * (double)hist[i];
            double mb = sb / wb, mf = (sumAll - sb) / wf;
            double between = wb * wf * (mb - mf) * (mb - mf);
            if (between > best) { best = between; t = i; }
        }
        return t;
    }

    private static double Median(IEnumerable<double> values)
    {
        var a = values.OrderBy(x => x).ToArray();
        if (a.Length == 0) return 0;
        return a.Length % 2 == 1 ? a[a.Length / 2] : (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2;
    }

    private static byte MedianByte(List<byte> l)
    {
        l.Sort();
        return l[l.Count / 2];
    }
}
