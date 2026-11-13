using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Ps5Altyazi.TextRecognition;

/// <summary>
/// Gömülü İngilizce kelime listesi (~38.000 sık kelime + oyun menüsü kelimeleri, sıklık sırasıyla).
/// Ekran çevirisinde: (1) OCR'ın tek harf hatalarını düzeltmek ("UNLINE" → "ONLINE"),
/// (2) okunan yazının gerçekten İngilizce olup olmadığını anlamak için kullanılır.
/// Kelime listesi: wordfreq (CC BY-SA 4.0) verisinden üretildi.
/// </summary>
public static class EnglishWords
{
    private static readonly Lazy<Dictionary<string, int>> Ranks = new(Load);

    private static Dictionary<string, int> Load()
    {
        var dict = new Dictionary<string, int>(40000, StringComparer.Ordinal);
        try
        {
            using var s = typeof(EnglishWords).Assembly.GetManifestResourceStream("en_words.txt.gz");
            if (s == null) return dict;
            using var gz = new GZipStream(s, CompressionMode.Decompress);
            using var r = new StreamReader(gz, Encoding.UTF8);
            string? line;
            int rank = 0;
            while ((line = r.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length > 0 && !dict.ContainsKey(line)) dict[line] = rank++;
            }
        }
        catch { /* liste yüklenemezse düzeltme yapılmaz */ }
        return dict;
    }

    public static bool IsLoaded => Ranks.Value.Count > 0;

    public static bool Contains(string word) => Ranks.Value.ContainsKey(word.ToLowerInvariant());

    /// <summary>
    /// Sözlükte olmayan bir kelimeyi, tek harf farkla (değiştirme / ekleme / silme) en sık kullanılan
    /// sözlük kelimesine düzeltir. Büyük/küçük harf düzeni korunur. Düzeltme yoksa kelimeyi aynen döndürür.
    /// </summary>
    public static string Correct(string word)
    {
        var d = Ranks.Value;
        if (d.Count == 0 || word.Length < 4 || !word.All(char.IsLetter) || word.Any(c => c > 127)) return word;
        string low = word.ToLowerInvariant();
        if (d.ContainsKey(low)) return word;

        string? best = null;
        int bestRank = int.MaxValue;
        foreach (var cand in Edits1(low))
            if (d.TryGetValue(cand, out int rank) && rank < bestRank) { best = cand; bestRank = rank; }
        // Çok nadir kelimelere "düzeltme" yapma (yanlış düzeltme riski)
        if (best == null || bestRank > 25000) return word;
        return ApplyCase(word, best);
    }

    /// <summary>Metindeki harf kelimelerinden (2+ harf) sözlükte olanların oranı.</summary>
    public static double KnownRatio(string text)
    {
        int total = 0, known = 0;
        foreach (var tok in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string core = new(tok.Where(char.IsLetter).ToArray());
            if (core.Length < 2) continue;
            total++;
            if (Contains(core)) known++;
        }
        return total == 0 ? 0 : (double)known / total;
    }

    private static IEnumerable<string> Edits1(string w)
    {
        const string letters = "abcdefghijklmnopqrstuvwxyz";
        for (int i = 0; i < w.Length; i++)
        {
            yield return w.Remove(i, 1); // silme
            foreach (char c in letters)
                if (c != w[i]) yield return w[..i] + c + w[(i + 1)..]; // değiştirme
        }
        for (int i = 0; i <= w.Length; i++)
            foreach (char c in letters)
                yield return w.Insert(i, c.ToString()); // ekleme
    }

    private static string ApplyCase(string original, string fixedLower)
    {
        if (original.All(char.IsUpper)) return fixedLower.ToUpperInvariant();
        if (char.IsUpper(original[0])) return char.ToUpperInvariant(fixedLower[0]) + fixedLower[1..];
        return fixedLower;
    }
}
