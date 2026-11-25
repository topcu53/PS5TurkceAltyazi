using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ps5Altyazi.Ps5;

/// <summary>Ağda bulunan PS5.</summary>
public sealed record Ps5Console(string Host, string Name, string Id, bool Awake, string App)
{
    public override string ToString() => $"{Name} – {Host} ({(Awake ? "açık" : "dinlenmede")})";
}

/// <summary>Eşlenmiş PS5: bağlanmak için gereken anahtarlar (onaltılık).</summary>
public sealed class Ps5Pairing
{
    public string Host { get; set; } = "";
    public string Name { get; set; } = "";
    public string Key { get; set; } = "";      // 16 bayt (kayıt anahtarı), onaltılık
    public string Morning { get; set; } = "";  // 16 bayt, onaltılık
    public string Mac { get; set; } = "";      // PS5'in MAC adresi (ağda yeniden bulmak için)

    /// <summary>Uyandırma kimliği: kayıt anahtarının ASCII metni (onaltılık sayı).</summary>
    public string WakeCredential
    {
        get
        {
            try
            {
                var bytes = Convert.FromHexString(Key);
                int end = Array.IndexOf(bytes, (byte)0);
                return Encoding.ASCII.GetString(bytes, 0, end < 0 ? bytes.Length : end);
            }
            catch { return ""; }
        }
    }
}

/// <summary>
/// ps5-baglanti.exe yardımcı programını (programın içinde gelir) çalıştırır. Yardımcı ayrı bir programdır; PS5'in
/// Remote Play protokolünü chiaki-ng ile konuşur. Altyazı+ onunla yalnız komut satırı ve standart giriş/çıkışla konuşur.
/// </summary>
public static class Ps5Helper
{
    public static readonly string LogPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PS5TurkceAltyazi", "ps5-baglanti.log");

    private static readonly object LogLock = new();
    private static string? _exe;
    private static bool _checked;

    /// <summary>Yardımcının diskteki yolu (ilk çağrıda programın içinden çıkarılır). Yoksa null.</summary>
    public static string? ExePath
    {
        get
        {
            if (_checked) return _exe;
            _checked = true;
            try
            {
                using var res = Assembly.GetExecutingAssembly().GetManifestResourceStream("ps5-baglanti.exe");
                if (res == null) return null;
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PS5TurkceAltyazi", "bin");
                Directory.CreateDirectory(dir);
                string ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0";
                string path = Path.Combine(dir, $"ps5-baglanti-{ver}-{res.Length}.exe");
                if (!File.Exists(path) || new FileInfo(path).Length != res.Length)
                {
                    string tmp = path + ".tmp";
                    using (var f = File.Create(tmp)) res.CopyTo(f);
                    File.Move(tmp, path, overwrite: true);
                }
                foreach (var old in Directory.EnumerateFiles(dir, "ps5-baglanti-*.exe").Where(p => p != path))
                    try { File.Delete(old); } catch { }
                _exe = path;
            }
            catch (Exception ex)
            {
                Logging.Log.Warn("PS5 bağlantı yardımcısı hazırlanamadı: " + ex.Message);
            }
            return _exe;
        }
    }

    public static bool Available => ExePath != null;

    internal static void WriteLog(string line)
    {
        try
        {
            lock (LogLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                var fi = new FileInfo(LogPath);
                if (fi.Exists && fi.Length > 4_000_000) File.Delete(LogPath);
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {line}\n");
            }
        }
        catch { }
    }

    internal static Process StartProcess(params string[] args)
    {
        string exe = ExePath ?? throw new InvalidOperationException("PS5 bağlantı yardımcısı (ps5-baglanti.exe) bu sürümde yok.");
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        WriteLog("> ps5-baglanti " + string.Join(' ', args.Select((a, i) => i >= 2 && a.Length >= 16 ? "…" : a)));
        return Process.Start(psi) ?? throw new InvalidOperationException("PS5 bağlantı yardımcısı başlatılamadı.");
    }

    /// <summary>Kısa komutları çalıştırır: stdout'taki JSON satırları ve stderr satırları.</summary>
    private static async Task<(List<JsonElement> Lines, List<string> Log)> RunAsync(string? stdin, TimeSpan timeout, params string[] args)
    {
        using var p = StartProcess(args);
        var log = new List<string>();
        var errTask = Task.Run(async () =>
        {
            string? l;
            while ((l = await p.StandardError.ReadLineAsync()) != null) { lock (log) log.Add(l); WriteLog("  " + l); }
        });
        if (stdin != null) await p.StandardInput.WriteAsync(stdin);
        p.StandardInput.Close();
        var outTask = p.StandardOutput.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeout);
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch { } }
        string output = await outTask;
        await errTask;
        var lines = new List<JsonElement>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try { lines.Add(JsonDocument.Parse(line).RootElement.Clone()); } catch (JsonException) { }
        }
        WriteLog("< " + output.Trim());
        return (lines, log);
    }

    /// <summary>Bu bilgisayarın IPv4 ağlarındaki adresler (/22'ye kadar) ve genel yayın.</summary>
    public static List<string> LocalTargets()
    {
        var list = new List<string>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var u in nic.GetIPProperties().UnicastAddresses)
                {
                    if (u.Address.AddressFamily != AddressFamily.InterNetwork || u.PrefixLength < 22 || u.PrefixLength > 30) continue;
                    uint ip = ToUInt(u.Address), mask = u.PrefixLength == 0 ? 0 : uint.MaxValue << (32 - u.PrefixLength);
                    uint net = ip & mask, count = ~mask;
                    for (uint i = 1; i < count; i++) if (net + i != ip) list.Add(FromUInt(net + i));
                }
            }
        }
        catch { }
        list.Add("255.255.255.255");
        return list.Distinct().ToList();
    }

    private static uint ToUInt(IPAddress a) { var b = a.GetAddressBytes(); return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]); }
    private static string FromUInt(uint v) => $"{v >> 24}.{(v >> 16) & 255}.{(v >> 8) & 255}.{v & 255}";

    /// <summary>PS5 arar. hosts null ise bütün yerel ağ.</summary>
    public static async Task<List<Ps5Console>> DiscoverAsync(IEnumerable<string>? hosts = null, int timeoutMs = 2500)
    {
        var targets = (hosts ?? LocalTargets()).ToList();
        var (lines, _) = await RunAsync(string.Join("\n", targets) + "\n", TimeSpan.FromMilliseconds(timeoutMs + 8000), "discover", timeoutMs.ToString());
        var result = new List<Ps5Console>();
        foreach (var e in lines)
        {
            if (!e.TryGetProperty("host", out var h)) continue;
            result.Add(new Ps5Console(h.GetString() ?? "", Str(e, "name"), Str(e, "id"),
                e.TryGetProperty("awake", out var a) && a.ValueKind == JsonValueKind.True, Str(e, "app")));
        }
        return result.GroupBy(c => c.Host).Select(g => g.First()).ToList();
    }

    public static async Task<bool> WakeAsync(Ps5Pairing p)
    {
        if (p.WakeCredential.Length == 0) return false;
        var (lines, _) = await RunAsync(null, TimeSpan.FromSeconds(10), "wake", p.Host, p.WakeCredential);
        return lines.Any(e => e.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True);
    }

    /// <summary>PS5'in "Cihazı Bağla" koduyla eşleştirir. Başarısızsa Türkçe açıklamalı istisna.</summary>
    public static async Task<Ps5Pairing> RegisterAsync(string host, byte[] accountId, string pin)
    {
        var (lines, log) = await RunAsync(null, TimeSpan.FromSeconds(60), "register", host, Convert.ToHexString(accountId).ToLowerInvariant(), pin);
        var r = lines.LastOrDefault();
        if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
            return new Ps5Pairing { Host = host, Name = Str(r, "name"), Key = Str(r, "key"), Morning = Str(r, "morning"), Mac = Str(r, "mac") };
        throw new InvalidOperationException(ExplainRegister(log));
    }

    private static string ExplainRegister(List<string> log)
    {
        string all = string.Join("\n", log);
        if (all.Contains("80108b02", StringComparison.OrdinalIgnoreCase))
            return "PS5 hesabı tanımadı: kod, programda giriş yaptığınız (ikinci) hesabın PS5 kullanıcısında alınmalı. PS5'te o kullanıcıya geçip kodu yeniden alın.";
        if (all.Contains("80108b09", StringComparison.OrdinalIgnoreCase))
            return "PS5 kodu kabul etmedi: kod yanlış ya da süresi dolmuş. 'Cihazı Bağla' ekranındaki güncel kodu girin.";
        if (all.Contains("80108b10", StringComparison.OrdinalIgnoreCase))
            return "Uzaktan Oynatma şu an başka bir cihazda kullanılıyor (PS Remote Play açık mı?). Kapatıp tekrar deneyin.";
        if (all.Contains("search", StringComparison.OrdinalIgnoreCase))
            return "PS5 eşleştirme isteğine yanıt vermedi. PS5 açık mı, aynı ağda mı, 'Cihazı Bağla' ekranı açık mı?";
        return "Eşleştirme başarısız. Ayrıntı: " + LogPath;
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
