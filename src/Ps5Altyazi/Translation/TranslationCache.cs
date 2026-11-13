using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Ps5Altyazi.Logging;
using Ps5Altyazi.TextRecognition;

namespace Ps5Altyazi.Translation;

/// <summary>
/// Kalıcı çeviri önbelleği (SQLite): %LOCALAPPDATA%\PS5TurkceAltyazi\ceviri-onbellek.db
/// Aynı altyazı için ikinci kez API çağrısı yapılmaz. OCR'ın küçük farklarına karşı
/// %92+ benzer metinler de önbellekten karşılanır.
/// </summary>
public sealed class TranslationCache : IDisposable
{
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);
    private readonly object _gate = new();
    private readonly SqliteConnection _conn;
    private readonly Dictionary<string, string> _mem = new();

    public static string FilePath => Path.Combine(Log.AppDataDir, "ceviri-onbellek.db");
    public int Count { get { lock (_gate) return _mem.Count; } }

    public TranslationCache()
    {
        Directory.CreateDirectory(Log.AppDataDir);
        _conn = new SqliteConnection($"Data Source={FilePath}");
        _conn.Open();
        Exec("""
             CREATE TABLE IF NOT EXISTS translations(
               key     TEXT PRIMARY KEY,
               source  TEXT NOT NULL,
               target  TEXT NOT NULL,
               engine  TEXT,
               created TEXT,
               hits    INTEGER NOT NULL DEFAULT 0)
             """);
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT key, target FROM translations";
        using var r = cmd.ExecuteReader();
        while (r.Read()) _mem[r.GetString(0)] = r.GetString(1);
    }

    public static string Key(string text) =>
        Spaces.Replace(text.Replace('’', '\'').Replace('‘', '\'').Trim(), " ").ToLowerInvariant();

    public bool TryGet(string source, out string target, out bool fuzzy)
    {
        string key = Key(source);
        lock (_gate)
        {
            fuzzy = false;
            if (_mem.TryGetValue(key, out var exact))
            {
                target = exact;
                Touch(key);
                return true;
            }

            // OCR farkları için benzer metin araması (uzunluğu yakın olanlar arasında)
            if (key.Length >= 8)
            {
                int min = (int)(key.Length * 0.85), max = (int)Math.Ceiling(key.Length * 1.15);
                string? bestKey = null;
                double best = 0.92;
                foreach (var k in _mem.Keys)
                {
                    if (k.Length < min || k.Length > max) continue;
                    double sim = SubtitleText.Similarity(k, key);
                    if (sim >= best) { best = sim; bestKey = k; }
                }
                if (bestKey != null)
                {
                    target = _mem[bestKey];
                    fuzzy = true;
                    Touch(bestKey);
                    return true;
                }
            }
            target = "";
            return false;
        }
    }

    public void Put(string source, string target, string engine)
    {
        string key = Key(source);
        lock (_gate)
        {
            _mem[key] = target;
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                              INSERT INTO translations(key, source, target, engine, created, hits)
                              VALUES($k, $s, $t, $e, $c, 0)
                              ON CONFLICT(key) DO UPDATE SET target = excluded.target, engine = excluded.engine
                              """;
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$s", source);
            cmd.Parameters.AddWithValue("$t", target);
            cmd.Parameters.AddWithValue("$e", engine);
            cmd.Parameters.AddWithValue("$c", DateTime.Now.ToString("s"));
            cmd.ExecuteNonQuery();
        }
    }

    private void Touch(string key)
    {
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "UPDATE translations SET hits = hits + 1 WHERE key = $k";
            cmd.Parameters.AddWithValue("$k", key);
            cmd.ExecuteNonQuery();
        }
        catch { /* istatistik; önemli değil */ }
    }

    private void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        lock (_gate) _conn.Dispose();
    }
}
