using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Ps5Altyazi.TextRecognition;

/// <summary>Altyazı metni yardımcıları: temizleme, geçerlilik, benzerlik, görüntü parmak izi.</summary>
public static class SubtitleText
{
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    /// <summary>OCR çıktısını tek satırlık temiz metne çevirir; anlamsız satırları (ör. "~ .") atar.</summary>
    public static string Normalize(string[] lines)
    {
        var kept = lines
            .Select(l => Spaces.Replace(l, " ").Trim())
            .Where(l => l.Count(char.IsLetter) >= 2)
            .Select(FixCommon);
        return Spaces.Replace(string.Join(" ", kept), " ").Trim();
    }

    private static string FixCommon(string s)
    {
        // Sık OCR karışıklıkları
        s = s.Replace('|', 'I').Replace('’', '\'').Replace('‘', '\'').Replace('“', '"').Replace('”', '"');
        s = Regex.Replace(s, @"\s+([.,!?;:])", "$1");
        return s;
    }

    private const string AllowedAccents = "éèêëáàâäíìîïóòôöúùûüñçÉÈÁÀÍÓÚÜÑÇ";
    private const string EndPunctuation = ".?!…,:;\"'-—)";
    private static readonly HashSet<string> CommonWords = new(
        ("a i an am are as at be but by do go he hi her him his how if in is it its me my no not of oh ok on or our out so " +
         "that the them then there they this to up us was we well what when where who why will with yes yeah you your hey uh um " +
         "ah okay alright sir mr mrs ma'am all can come get got good just know let like look now one see some take tell think " +
         "time want way yet ain't i'm it's that's don't can't you're we're").Split(' '));

    /// <summary>
    /// Metin gerçek bir İngilizce altyazı gibi mi? Arka plandaki çit/ot gibi dokulardan gelen OCR çöplerini
    /// ("IIVO II", "Idåi", "I tio") eler. Şartlar: en az 2 harf; İngilizcede olmayan harf yok (ß, å…);
    /// ve satır noktalama ile bitiyor VEYA en az 3 kelime VEYA yaygın bir İngilizce kelime içeriyor.
    /// Şimdiye kadarki 137 gerçek altyazının hepsini kabul edip 6 çöp okumayı eleyecek şekilde denendi.
    /// </summary>
    public static bool IsPlausible(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim();
        int letters = s.Count(char.IsLetter);
        if (letters < 2) return false;
        if (s.Any(c => char.IsLetter(c) && c > 127 && AllowedAccents.IndexOf(c) < 0)) return false;
        int good = s.Count(c => char.IsLetter(c) || c == ' ');
        if (good < s.Length * 0.5) return false;
        var tokens = s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.ToLowerInvariant().Trim('.', ',', '!', '?', ';', ':', '\'', '"', '…', '-'))
            .Where(t => t.Length > 0).ToList();
        // Sözlük denetimi: kelimelerin çoğu İngilizce değilse (OCR'ın dokudan/simgeden uydurduğu harfler) çöp sayılır.
        // Özel adlar (Arthur, Dutch…) için eşik düşük tutuldu; kısa satırlar aşağıdaki kurallarla değerlendirilir.
        if (tokens.Count >= 3 && EnglishWords.IsLoaded && EnglishWords.KnownRatio(s) < 0.4) return false;
        if (tokens.Any(t => t.Length >= 5 && t.All(char.IsLetter) && !t.Any(c => "aeiouy".IndexOf(c) >= 0))) return false;
        if (EndPunctuation.IndexOf(s[^1]) >= 0) return true;
        if (tokens.Count >= 3) return true;
        return tokens.Any(t => t.Length >= 2 && CommonWords.Contains(t));
    }

    /// <summary>0–1 arası benzerlik (yalnızca harf/rakamlar, büyük-küçük harf duyarsız, Levenshtein).</summary>
    public static double Similarity(string a, string b)
    {
        string x = Key(a), y = Key(b);
        if (x.Length == 0 && y.Length == 0) return 1;
        if (x.Length == 0 || y.Length == 0) return 0;
        int[] prev = new int[y.Length + 1], cur = new int[y.Length + 1];
        for (int j = 0; j <= y.Length; j++) prev[j] = j;
        for (int i = 1; i <= x.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= y.Length; j++)
            {
                int cost = x[i - 1] == y[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return 1.0 - (double)prev[y.Length] / Math.Max(x.Length, y.Length);
    }

    private static string Key(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s) if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    // ── Görüntü parmak izi: metin maskesinin 64x8 ızgara özeti ──

    public const int FpCols = 64, FpRows = 8;

    /// <summary>
    /// Maskenin (genişlik width) yalnızca [sx, sy, sw, sh] bölümünün özeti. Bölüm olarak bulunan altyazı
    /// satırlarının kutusu verilir; böylece arka plandaki hareket parmak izini bozmaz.
    /// </summary>
    public static bool[] Fingerprint(bool[] mask, int width, int sx, int sy, int sw, int sh)
    {
        var fp = new bool[FpCols * FpRows];
        if (sw < FpCols || sh < FpRows) return fp;
        for (int gy = 0; gy < FpRows; gy++)
        {
            int y0 = sy + gy * sh / FpRows, y1 = sy + (gy + 1) * sh / FpRows;
            for (int gx = 0; gx < FpCols; gx++)
            {
                int x0 = sx + gx * sw / FpCols, x1 = sx + (gx + 1) * sw / FpCols;
                int cnt = 0, total = Math.Max(1, (y1 - y0) * (x1 - x0));
                for (int y = y0; y < y1; y++)
                {
                    int row = y * width;
                    for (int x = x0; x < x1; x++) if (mask[row + x]) cnt++;
                }
                fp[gy * FpCols + gx] = cnt > total * 0.02;
            }
        }
        return fp;
    }

    /// <summary>İki parmak izi arasındaki fark (Jaccard uzaklığı, 0 = aynı, 1 = tamamen farklı).</summary>
    public static double Distance(bool[] a, bool[] b)
    {
        int inter = 0, union = 0;
        for (int i = 0; i < a.Length && i < b.Length; i++)
        {
            if (a[i] || b[i]) union++;
            if (a[i] && b[i]) inter++;
        }
        return union == 0 ? 0 : 1.0 - (double)inter / union;
    }
}
