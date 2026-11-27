using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Ps5Altyazi.Translation;

/// <summary>Bilgisayarda çevirinin ayarları (ana pencere ayarlardan doldurur).</summary>
public sealed record OfflineConfig(
    OfflineMode Mode = OfflineMode.Auto,
    bool GpuAvailable = false,
    int GpuIndex = 0,
    int Threads = 0,
    int PhysicalCores = 0,
    bool CpuFallback = true);

/// <summary>
/// Bilgisayarda (internetsiz) çeviri: kota yok, hız sınırı yok → çeviri hiç durmaz. İşlemcide (Intel/AMD) ya da
/// ekran kartında (DirectML: NVIDIA/AMD/Intel) çalışır; Otomatik modda hız testiyle en hızlısı seçilir.
/// Ekran kartında çalışırken hata olursa (bellek yetersizliği vb.) o altyazı hemen işlemcide yeniden çevrilir ve
/// sonrası işlemcide sürer – çeviri kesilmez. Modeller bir kez indirilir:
/// %LOCALAPPDATA%\PS5TurkceAltyazi\models\opus-mt-tc-big-en-tr (CPU ~530 MB; ekran kartı için +~890 MB).
/// </summary>
public sealed class OfflineTranslator : ITranslator, IDisposable
{
    public const string ModelName = "opus-mt-tc-big-en-tr";
    private const string BaseUrl = "https://huggingface.co/onnx-community/opus-mt-tc-big-en-tr/resolve/main/";

    /// <summary>Model klasörü (testler başka klasör verebilir).</summary>
    public static string ModelDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PS5TurkceAltyazi", "models", ModelName);

    public static bool IsInstalled => MarianOnnx.HasFiles(ModelDir, MarianDevice.Cpu) || MarianOnnx.HasFiles(ModelDir, MarianDevice.Gpu);
    public static bool GpuFilesInstalled => MarianOnnx.HasFiles(ModelDir, MarianDevice.Gpu);

    public static OfflineConfig Config { get; private set; } = new();
    /// <summary>Son açılışın / seçimin açıklaması (ayarlar ekranında gösterilir).</summary>
    public static string Status { get; private set; } = "Henüz açılmadı.";
    public static OfflineBenchmark? LastCpuBenchmark { get; private set; }
    public static OfflineBenchmark? LastGpuBenchmark { get; private set; }
    public static MarianDevice? ActiveDevice => _shared?.Device;
    public static string? ActiveDeviceName => _shared?.DeviceName;
    /// <summary>Durum değiştiğinde (cihaz seçildi, CPU'ya dönüldü…) – ana pencere günlüğe yazar.</summary>
    public static event Action<string, bool>? Event;

    private static MarianOnnx? _shared;
    private static Task<MarianOnnx>? _loading;
    private static readonly object Gate = new();
    private static readonly SemaphoreSlim Busy = new(1, 1);

    public string Id => "offline";
    public string Name => _shared != null ? (_shared.Device == MarianDevice.Gpu ? "Bilgisayar (ekran kartı)" : "Bilgisayar (işlemci)") : "Bilgisayar (çevrimdışı)";

    /// <summary>Ayar değişince çağrılır: model yeniden (yeni cihazda) açılır.</summary>
    public static void Configure(OfflineConfig cfg)
    {
        lock (Gate)
        {
            if (cfg == Config && (_shared != null || _loading != null)) return;
            Config = cfg;
            var old = _shared;
            _shared = null;
            _loading = null;
            if (old != null) _ = Task.Run(async () => { await Busy.WaitAsync(); try { old.Dispose(); } finally { Busy.Release(); } });
        }
    }

    /// <summary>Modeli arka planda açar (ilk çeviri beklemesin). Otomatik modda hız testi de yapılır. Model yoksa null.</summary>
    public static Task<MarianOnnx>? Preload(bool benchmark = true)
    {
        if (!IsInstalled) return null;
        lock (Gate)
        {
            if (_shared != null) return Task.FromResult(_shared);
            var cfg = Config;
            return _loading ??= Task.Run(() =>
            {
                var r = OfflineEngine.Open(ModelDir, cfg.Mode, cfg.GpuAvailable, cfg.GpuIndex, cfg.Threads, cfg.PhysicalCores,
                    cfg.CpuFallback, benchmark && cfg.Mode == OfflineMode.Auto, m => Event?.Invoke(m, false));
                Status = r.Message;
                if (r.Cpu != null) LastCpuBenchmark = r.Cpu;
                if (r.Gpu != null) LastGpuBenchmark = r.Gpu;
                Event?.Invoke("💻 " + r.Message, r.IsError);
                if (r.Engine == null)
                {
                    lock (Gate) _loading = null;
                    throw new TranslationException(r.Message, TranslationErrorKind.Transient);
                }
                lock (Gate) _shared = r.Engine;
                return r.Engine;
            });
        }
    }

    public async Task<string> TranslateAsync(string text, string? context, CancellationToken ct)
    {
        var load = Preload() ?? throw new TranslationException("Çevrimdışı çeviri modeli indirilmemiş.", TranslationErrorKind.Auth);
        MarianOnnx m;
        try { m = await load.WaitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (TranslationException) { throw; }
        catch (Exception ex) { throw new TranslationException("Çevrimdışı model açılamadı: " + ex.Message, TranslationErrorKind.Transient); }

        await Busy.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            try
            {
                if (FaultInjection == "gpu-run" && m.Device == MarianDevice.Gpu) { FaultInjection = null; throw new OutOfMemoryException("test: ekran kartı belleği yetersiz"); }
                return await Task.Run(() => m.Translate(text), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && m.Device == MarianDevice.Gpu && Config.CpuFallback)
            {
                // Ekran kartı çalışırken hata (bellek yetersiz, sürücü sıfırlandı…): işlemciye dön ve bu altyazıyı orada çevir
                Event?.Invoke($"⚠ Ekran kartında çeviri hatası ({ex.Message}). İşlemciye geçiliyor; çeviri sürüyor.", true);
                var cpu = await Task.Run(() => new MarianOnnx(ModelDir, MarianDevice.Cpu, 0, Config.Threads, Config.PhysicalCores), ct).ConfigureAwait(false);
                lock (Gate)
                {
                    _shared = cpu;
                    _loading = Task.FromResult(cpu);
                    Config = Config with { Mode = OfflineMode.CpuOnly };
                    Status = $"⚠ Ekran kartı hata verdi, {cpu.DeviceName} kullanılıyor.";
                }
                _ = Task.Run(m.Dispose);
                return await Task.Run(() => cpu.Translate(text), ct).ConfigureAwait(false);
            }
        }
        finally { Busy.Release(); }
    }

    /// <summary>Yalnız testler için: "gpu-run" → ekran kartındaki ilk çeviride bellek hatası taklidi.</summary>
    public static string? FaultInjection { get; set; }

    /// <summary>İndirilecek dosyalar: CPU profili her zaman; ekran kartı profili istenirse.</summary>
    public static string[] FilesToDownload(bool includeGpu) =>
        MarianOnnx.CommonFiles.Concat(MarianOnnx.CpuFiles).Concat(includeGpu ? MarianOnnx.GpuFiles : Array.Empty<string>()).ToArray();

    /// <summary>Modeli indirir (eksik dosyaları; yarıda kalanı baştan). progress: (indirilen bayt, toplam bayt).</summary>
    public static async Task DownloadAsync(IProgress<(long Done, long Total)>? progress, CancellationToken ct, bool includeGpu = false)
    {
        var files = FilesToDownload(includeGpu);
        Directory.CreateDirectory(Path.Combine(ModelDir, "onnx"));
        using var http = new HttpClient { Timeout = TimeSpan.FromHours(3) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("AltyaziPlus");

        long total = 0;
        var sizes = new long[files.Length];
        for (int i = 0; i < files.Length; i++)
        {
            using var head = new HttpRequestMessage(HttpMethod.Head, BaseUrl + files[i]);
            using var r = await http.SendAsync(head, ct).ConfigureAwait(false);
            sizes[i] = r.Content.Headers.ContentLength ?? 0;
            total += sizes[i];
        }

        long done = 0;
        var buf = new byte[1 << 16];
        for (int i = 0; i < files.Length; i++)
        {
            string rel = files[i];
            string dest = Path.Combine(ModelDir, rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(dest) && (sizes[i] == 0 || new FileInfo(dest).Length == sizes[i]))
            {
                done += sizes[i];
                progress?.Report((done, total));
                continue;
            }
            string part = dest + ".part";
            using (var resp = await http.GetAsync(BaseUrl + rel, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var dst = File.Create(part);
                int n;
                while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                    done += n;
                    progress?.Report((done, total));
                }
            }
            File.Move(part, dest, overwrite: true);
        }
    }

    public void Dispose() { }
}
