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
        return FixDigitLetters(Clean(s));
    }

    private static readonly Regex MixedToken = new(@"\b(?=[\p{L}0-9']*\p{L})(?=[\p{L}0-9']*[0-9])[\p{L}0-9']{3,}\b", RegexOptions.Compiled);
    private static readonly Regex NumberLike = new(@"^\d+(st|nd|rd|th|s|k|m|mm|am|pm|x|g|kg|km|mph|th)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Harf yerine rakam okunmuş kelimeler: "100k" → "look", "6ut" → "but"/"out", "c0me" → "come". Yalnızca düzeltilmiş
    /// hali sözlükte varsa değiştirilir ("2nd", "9mm", "80s" gibi gerçek sayılara dokunulmaz).
    /// </summary>
    private static string FixDigitLetters(string s)
    {
        if (!EnglishWords.IsLoaded) return s;
        return MixedToken.Replace(s, m =>
        {
            string w = m.Value;
            if (NumberLike.IsMatch(w)) return w;
            foreach (var map in DigitMaps)
            {
                var sb = new StringBuilder(w.Length);
                foreach (char c in w) sb.Append(map.TryGetValue(c, out char rch) ? rch : c);
                string cand = sb.ToString();
                if (EnglishWords.Contains(cand.Trim('\'')))
                    return char.IsUpper(w.FirstOrDefault(char.IsLetter)) && !char.IsUpper(cand[0]) ? char.ToUpper(cand[0]) + cand[1..] : cand;
            }
            return w;
        });
    }

    private static readonly Dictionary<char, char>[] DigitMaps =
    {
        new() { ['0'] = 'o', ['1'] = 'l', ['5'] = 's', ['3'] = 'e', ['4'] = 'a', ['7'] = 't', ['8'] = 'b', ['6'] = 'o' },
        new() { ['0'] = 'o', ['1'] = 'i', ['5'] = 's', ['3'] = 'e', ['4'] = 'a', ['7'] = 't', ['8'] = 'b', ['6'] = 'b' },
    };

    // Altyazıda hiç bulunmayan, OCR'ın kenar çizgisinden / simgeden uydurduğu işaretler
    private const string JunkChars = "•·€¯«»¤°©®™¦§¶†‡※■□▪▫◦●○◆◇★☆✓✔✗✘¬±×÷^`~_={}<>\\$#%&*+@¨´¸¹²³¼½¾";
    private static readonly Regex PunctOnlyToken = new(@"(?<=^|\s)(?!\.\.\.|…|-)[^\p{L}\p{N}\s]{1,4}(?=\s|$)", RegexOptions.Compiled);
    private static readonly Regex LeadingDash = new(@"(^|(?<=[.!?…]\s))[-–—~]+\s*(?=\S)", RegexOptions.Compiled);
    private static readonly Regex StrayAfterEnd = new(@"(?:([!?…])[lI1|'.,;:]+|(?<!\.)(\.)[lI1|',;:]+)$", RegexOptions.Compiled);
    private static readonly Regex InnerJunkPunct = new(@"(?<=\p{L})[.,;:]{1,2}(?=\p{L}{2})", RegexOptions.Compiled);

    /// <summary>
    /// Metindeki anlamsız işaretleri temizler: •, „, €, ¯ gibi simgeler; tek başına duran noktalama ("you ' know");
    /// konuşma tireleri ("-Fuck off!"); cümle sonundaki fazlalık ("die!l", "you?.'"); kelime içine düşmüş nokta/virgül
    /// ("corner„stores", "w.aytoday"); eşi olmayan tırnak. Türkçe çeviri çıktısına da uygulanır.
    /// </summary>
    public static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        s = s.Replace('„', ',').Replace('‚', ',').Replace('¡', ' ').Replace('¿', ' ');
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (JunkChars.IndexOf(c) >= 0) { sb.Append(' '); continue; }
            if (char.IsControl(c)) continue;
            sb.Append(c);
        }
        s = Spaces.Replace(sb.ToString(), " ").Trim();
        s = InnerJunkPunct.Replace(s, " ");
        s = PunctOnlyToken.Replace(s, "");
        s = LeadingDash.Replace(s, "");
        s = StrayAfterEnd.Replace(s, "$1$2");
        s = Regex.Replace(s, @"([!?])[,;:]", "$1");
        if (s.Count(c => c == '"') % 2 == 1) s = s.Replace("\"", "");
        s = Regex.Replace(s, @"\s+([.,!?;:])", "$1");
        s = Regex.Replace(s, @"([,;:])(?=\p{L})", "$1 ");
        s = Regex.Replace(s, @"[,;:]+([.!?])", "$1");
        return Spaces.Replace(s, " ").Trim().Trim(',', ';', ':').Trim();
    }

    private static readonly Regex LangLabel = new(
        @"(?:^|(?<=[.!?…]\s)|(?<=\s-\s?))[\[(]?\s*(?:[Il1]n|Speaking)\s+(Spanish|French|Portuguese|Italian|German|Russian|Chinese|Mandarin|Cantonese|Japanese|Korean|Arabic|Haitian Creole|Creole|Vietnamese|Tagalog|Hindi|Polish|Ukrainian|Hebrew|Greek|Dutch|Swedish|Turkish|Persian|Farsi|Armenian|Romanian|Albanian)\s*[:;\])]\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Dictionary<string, string> LangTr = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Spanish"] = "İspanyolca", ["French"] = "Fransızca", ["Portuguese"] = "Portekizce", ["Italian"] = "İtalyanca",
        ["German"] = "Almanca", ["Russian"] = "Rusça", ["Chinese"] = "Çince", ["Mandarin"] = "Çince", ["Cantonese"] = "Kantonca",
        ["Japanese"] = "Japonca", ["Korean"] = "Korece", ["Arabic"] = "Arapça", ["Haitian Creole"] = "Haiti Kreolü",
        ["Creole"] = "Kreol", ["Vietnamese"] = "Vietnamca", ["Tagalog"] = "Tagalogca", ["Hindi"] = "Hintçe", ["Polish"] = "Lehçe",
        ["Ukrainian"] = "Ukraynaca", ["Hebrew"] = "İbranice", ["Greek"] = "Yunanca", ["Dutch"] = "Felemenkçe", ["Swedish"] = "İsveççe",
        ["Turkish"] = "Türkçe", ["Persian"] = "Farsça", ["Farsi"] = "Farsça", ["Armenian"] = "Ermenice", ["Romanian"] = "Rumence",
        ["Albanian"] = "Arnavutça",
    };

    /// <summary>Çeviri çıktısı için temizlik (Türkçede kelime sonunda kesme işareti kalmaz: "Lanet olsun'").</summary>
    public static string CleanTurkish(string s)
    {
        s = Clean(s);
        // İngilizcedeki kekelemeden kalan tek harf + tire ("f- Ayakkabılarımı") Türkçede anlamsız
        s = Regex.Replace(s, @"(^|(?<=[.!?…]\s))\p{L}{1,2}-\s+", "");
        return Regex.Replace(s, @"(?<=\p{L})'(?=\s|$|[.,!?])", "");
    }

    /// <summary>Yabancı dil işaretinden (Türkçe metinde) sonraki kısım TV'de farklı renkte gösterilir.</summary>
    public const char ForeignMark = '\u2063';

    /// <summary>
    /// "In Spanish: Listen." gibi başka dilde konuşulduğunu belirten etiketi bulur. Dönüş: etiketten önceki kısım,
    /// dilin Türkçe adı ve sonraki kısım; etiket yoksa null.
    /// </summary>
    public static (string Before, string LangTr, string After)? SplitLanguageLabel(string s)
    {
        var m = LangLabel.Match(s);
        if (!m.Success) return null;
        string lang = Regex.Replace(m.Groups[1].Value, @"\s+", " ");
        string tr = LangTr.TryGetValue(lang, out var t) ? t : lang;
        return (s[..m.Index].Trim().TrimEnd('-').Trim(), tr, s[(m.Index + m.Length)..].Trim());
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
        if (IsButtonPrompt(s)) return false;
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

    private static readonly Regex PromptToken = new(@"^(?:[@©®•○●◯◎⊙]|[O0oe]|\(\S{0,3}\)?|\(\S{0,3})$", RegexOptions.Compiled);

    /// <summary>
    /// Oyunun etkileşim istemi mi ("@ APPLE", "O WARDROBE O DRESS", "(.0) JACK DRIVER", "@ TURN OFF")?
    /// Tuş simgesi (OCR'da @, O, (D…) + BÜYÜK HARFLİ kısa eylem adı; altyazı değildir, çevrilmez.
    /// </summary>
    public static bool IsButtonPrompt(string s)
    {
        var toks = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (toks.Length == 0 || toks.Length > 8) return false;
        int icons = 0;
        var words = new List<string>();
        foreach (var t in toks)
        {
            if (PromptToken.IsMatch(t) || t.All(c => !char.IsLetter(c))) { icons++; continue; }
            words.Add(t);
        }
        if (icons == 0 || words.Count == 0 || words.Count > 5) return false;
        // Kalan kelimelerin hepsi BÜYÜK HARF ve cümle noktalaması yok
        bool allCaps = words.All(w => w.Where(char.IsLetter).All(char.IsUpper) && w.Count(char.IsLetter) >= 2);
        return allCaps && !words.Any(w => w.Any(c => c is '.' or '?' or '!' or ','));
    }

    /// <summary><paramref name="small"/> (harf/rakam anahtarı) <paramref name="big"/> içinde geçiyor mu? (kısmi okuma)</summary>
    public static bool ContainsKey(string big, string small)
    {
        string k = Key(small);
        return k.Length >= 2 && Key(big).Contains(k, StringComparison.Ordinal);
    }

    private static readonly Regex SentenceBreak = new(@"(?<=[.!?…])[""')\]]*\s+(?=[""'¿¡(\-]*[A-Z0-9])", RegexOptions.Compiled);
    private static readonly Regex Abbrev = new(@"(?:\b(?:Mr|Mrs|Ms|Dr|St|Jr|Sr|vs|Lt|Sgt|Capt)\.)$", RegexOptions.Compiled);

    /// <summary>
    /// Metni cümlelere böler ("Okay, we're done. I have nothing for you." → 2 parça). Kısaltmalarda (Dr., Mr.) bölmez.
    /// Her cümle ayrı çevrilip önbelleğe girer; aynı cümle başka satırla birlikte okunduğunda yeniden çevrilmez.
    /// </summary>
    public static List<string> Sentences(string text)
    {
        var raw = SentenceBreak.Split(text.Trim());
        var list = new List<string>();
        foreach (var r in raw)
        {
            string p = r.Trim();
            if (p.Length == 0) continue;
            if (list.Count > 0 && (Abbrev.IsMatch(list[^1]) || p.Count(char.IsLetter) < 2)) list[^1] += " " + p;
            else list.Add(p);
        }
        return list;
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
