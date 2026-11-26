using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ps5Altyazi;

/// <summary>
/// Güncelleme: GitHub'daki son sürüme bakar; yeni sürüm varsa kurulum dosyasını (AltyaziPlus-Setup.exe) indirir ve sessiz
/// kurar. Kurulum programı kapatır, yeni sürümü kurar ve yeniden açar. Ayarlar ve eşlemeler korunur.
/// </summary>
public static class Updater
{
    private const string LatestUrl = "https://api.github.com/repos/topcu53/PS5TurkceAltyazi/releases/latest";
    private const string SetupName = "AltyaziPlus-Setup.exe";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("AltyaziPlus-Updater");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    public sealed record Release(Version Version, string Tag, string SetupUrl, long SetupSize, string Notes)
    {
        /// <summary>Kurulu sürümden bu yana CHANGELOG.md'deki değişiklikler (düz metin).</summary>
        public string ChangeNotes { get; init; } = "";
    }

    public static Version Current
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    /// <summary>Daha yeni bir sürüm varsa onu döndürür; yoksa (ya da internet yoksa) null.</summary>
    public static async Task<Release?> CheckAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        using var res = await Http.GetAsync(LatestUrl, cts.Token);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(cts.Token));
        var root = doc.RootElement;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var ver)) return null;
        ver = new Version(ver.Major, ver.Minor, Math.Max(0, ver.Build));
        if (ver <= Current) return null;
        var asset = root.GetProperty("assets").EnumerateArray()
            .FirstOrDefault(a => string.Equals(a.GetProperty("name").GetString(), SetupName, StringComparison.OrdinalIgnoreCase));
        if (asset.ValueKind != JsonValueKind.Object) return null;
        string notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        var rel = new Release(ver, tag, asset.GetProperty("browser_download_url").GetString() ?? "", asset.GetProperty("size").GetInt64(), notes);
        try { rel = rel with { ChangeNotes = await ChangesSinceAsync(tag, cts.Token) }; } catch { }
        return rel;
    }

    /// <summary>CHANGELOG.md'den kurulu sürümden yeni olan bölümler ("## vX.Y – Başlık" + maddeler), düz metin.</summary>
    private static async Task<string> ChangesSinceAsync(string tag, CancellationToken ct)
    {
        string url = $"https://raw.githubusercontent.com/topcu53/PS5TurkceAltyazi/{tag}/CHANGELOG.md";
        string md = await Http.GetStringAsync(url, ct);
        var lines = new System.Collections.Generic.List<string>();
        bool take = false;
        foreach (var raw in md.Split('\n'))
        {
            string line = raw.TrimEnd();
            if (line.StartsWith("## v"))
            {
                string v = line[4..].Split(' ', '–', '-')[0].Trim();
                take = Version.TryParse(v.Count(c => c == '.') == 1 ? v + ".0" : v, out var lv) && lv > Current;
                if (take) { if (lines.Count > 0) lines.Add(""); lines.Add(line[3..].Trim()); }
                continue;
            }
            if (!take) continue;
            if (line.StartsWith("- ")) lines.Add("• " + line[2..].Replace("**", ""));
            else if (line.StartsWith("  ") && lines.Count > 0) lines[^1] += " " + line.Trim().Replace("**", "");
        }
        return string.Join("\n", lines).Trim();
    }

    /// <summary>Kurulumu indirir (ilerleme 0–1) ve sessiz başlatır. Sonra program kapanmalıdır.</summary>
    public static async Task DownloadAndRunAsync(Release r, IProgress<double> progress, CancellationToken ct = default)
    {
        if (!r.SetupUrl.StartsWith("https://github.com/topcu53/PS5TurkceAltyazi/releases/download/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Güncelleme adresi beklenmedik bir yerde; indirilmedi.");
        string dir = Path.Combine(Path.GetTempPath(), "AltyaziPlus-guncelleme");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"AltyaziPlus-Setup-{r.Version}.exe");

        using (var res = await Http.GetAsync(r.SetupUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            res.EnsureSuccessStatusCode();
            long total = res.Content.Headers.ContentLength ?? r.SetupSize;
            await using var src = await res.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(path);
            var buf = new byte[1 << 16];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (total > 0) progress.Report((double)done / total);
            }
        }
        if (r.SetupSize > 0 && new FileInfo(path).Length != r.SetupSize)
            throw new InvalidOperationException("İndirilen dosya eksik; tekrar deneyin.");

        Process.Start(new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /SP-",
        });
    }
}
