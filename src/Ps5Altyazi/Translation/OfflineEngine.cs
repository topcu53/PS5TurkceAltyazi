using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Ps5Altyazi.Translation;

/// <summary>Bilgisayarda çevirinin çalışma modu.</summary>
public enum OfflineMode
{
    /// <summary>Ekran kartı varsa dener, gerçek test çevirisi ve hız testiyle hangisi hızlıysa onu seçer; her hatada CPU'ya döner.</summary>
    Auto,
    /// <summary>Yalnızca işlemci.</summary>
    CpuOnly,
    /// <summary>Yalnızca ekran kartı; kullanılamıyorsa açık hata verir (CPU'ya dönmez, "CPU'ya dön" açık değilse).</summary>
    GpuOnly,
}

/// <summary>Hız testi sonucu.</summary>
public sealed record OfflineBenchmark(
    MarianDevice Device, string DeviceName, double LoadMs, double FirstMs, double AvgMs, double P50Ms, double P95Ms,
    double CpuPercent, long RamMb)
{
    public override string ToString() =>
        $"{DeviceName}: yükleme {LoadMs:F0} ms · ilk çeviri {FirstMs:F0} ms · ortalama {AvgMs:F0} ms · P50 {P50Ms:F0} ms · " +
        $"P95 {P95Ms:F0} ms · CPU %{CpuPercent:F0} · RAM {RamMb} MB";
}

/// <summary>
/// Çeviri donanımı yöneticisi (platformdan bağımsız): istenen moda göre modeli CPU ya da ekran kartında açar, gerçek bir
/// test çevirisiyle doğrular, Otomatik modda hız testiyle en düşük gecikmeli olanı seçer. Hiçbir durumda istisna
/// fırlatıp programı düşürmez (yalnız "Yalnız GPU" modunda GPU kullanılamazsa açık bir hata mesajı döner).
/// </summary>
public static class OfflineEngine
{
    /// <summary>Hız testinde kullanılan tipik oyun altyazıları (her iki cihazda aynı metinler).</summary>
    public static readonly string[] BenchmarkSamples =
    {
        "Hey, let me call him real quick.",
        "Where the hell have you been?",
        "Drive to the Speedshop.",
        "I saw the whole thing, do you need my statement or something?",
        "Okay, we're done.",
        "You heard of Venture Apartments? Up off Northbrook Drive in Rockridge.",
        "Bro, I just wanted to say I respect these kicks you just dropped, king.",
        "Get in the car!",
    };

    public sealed record Result(MarianOnnx? Engine, string Message, OfflineBenchmark? Cpu, OfflineBenchmark? Gpu, bool IsError);

    /// <summary>
    /// Modeli açar. <paramref name="gpuAvailable"/>: bilgisayarda uygun ekran kartı ve GPU dosyaları var mı.
    /// </summary>
    public static Result Open(string dir, OfflineMode mode, bool gpuAvailable, int gpuIndex, int threads, int physicalCores,
                              bool cpuFallback, bool benchmark, Action<string>? log = null)
    {
        bool cpuFiles = MarianOnnx.HasFiles(dir, MarianDevice.Cpu);
        bool gpuFiles = MarianOnnx.HasFiles(dir, MarianDevice.Gpu);

        MarianOnnx? gpu = null;
        OfflineBenchmark? gpuBench = null;
        string? gpuError = null;

        if (mode != OfflineMode.CpuOnly)
        {
            if (!gpuAvailable) gpuError = "Uygun ekran kartı (DirectX 12) bulunamadı.";
            else if (!gpuFiles) gpuError = "Ekran kartı model dosyaları henüz indirilmedi.";
            else
            {
                try
                {
                    log?.Invoke($"Ekran kartı #{gpuIndex} deneniyor…");
                    gpu = new MarianOnnx(dir, MarianDevice.Gpu, gpuIndex, threads, physicalCores);
                    gpuBench = Verify(gpu, benchmark);
                    if (gpuBench == null) { gpu.Dispose(); gpu = null; gpuError = "Ekran kartında test çevirisi boş çıktı."; }
                }
                catch (Exception ex)
                {
                    gpu?.Dispose();
                    gpu = null;
                    gpuError = "Ekran kartında açılamadı: " + Short(ex);
                }
            }
            if (gpuError != null) log?.Invoke(gpuError);
        }

        if (mode == OfflineMode.GpuOnly && gpu != null)
            return new Result(gpu, $"✔ {gpu.DeviceName} kullanılıyor. {gpuBench}", null, gpuBench, false);
        if (mode == OfflineMode.GpuOnly && !cpuFallback)
            return new Result(null, "✖ Yalnız ekran kartı seçili ama kullanılamıyor: " + gpuError, null, null, true);

        // CPU (her zaman kullanılabilir yedek)
        if (!cpuFiles)
        {
            if (gpu != null) return new Result(gpu, $"✔ {gpu.DeviceName} kullanılıyor (CPU dosyaları yok). {gpuBench}", null, gpuBench, false);
            return new Result(null, "✖ Model dosyaları bulunamadı.", null, null, true);
        }
        MarianOnnx? cpu = null;
        OfflineBenchmark? cpuBench = null;
        bool needCpu = gpu == null || (mode == OfflineMode.Auto && benchmark);
        if (needCpu)
        {
            try
            {
                cpu = new MarianOnnx(dir, MarianDevice.Cpu, 0, threads, physicalCores);
                cpuBench = Verify(cpu, benchmark || gpu != null);
            }
            catch (Exception ex)
            {
                cpu?.Dispose();
                cpu = null;
                if (gpu == null) return new Result(null, "✖ İşlemcide de açılamadı: " + Short(ex), null, null, true);
            }
        }

        if (gpu != null && cpu != null)
        {
            // En düşük gerçek gecikme (P50, eşitse P95) kazanır
            bool gpuWins = gpuBench!.P50Ms < cpuBench!.P50Ms * 0.95 ||
                           (Math.Abs(gpuBench.P50Ms - cpuBench.P50Ms) <= cpuBench.P50Ms * 0.05 && gpuBench.P95Ms < cpuBench.P95Ms);
            var win = gpuWins ? gpu : cpu;
            (gpuWins ? cpu : gpu).Dispose();
            return new Result(win, $"✔ Otomatik: {win.DeviceName} seçildi (daha hızlı).\n  {cpuBench}\n  {gpuBench}", cpuBench, gpuBench, false);
        }
        if (cpu != null)
        {
            string why = mode == OfflineMode.CpuOnly ? "" : gpuError != null ? $" (ekran kartı: {gpuError})" : "";
            string fb = mode == OfflineMode.GpuOnly ? "⚠ Ekran kartı kullanılamadı, CPU'ya dönüldü" : "✔";
            return new Result(cpu, $"{fb} {cpu.DeviceName} kullanılıyor{why}. {cpuBench}", cpuBench, gpuBench, false);
        }
        return new Result(gpu, $"✔ {gpu!.DeviceName} kullanılıyor. {gpuBench}", cpuBench, gpuBench, false);
    }

    /// <summary>Gerçek test çevirisi (+ isteğe bağlı hız testi). Çeviri boş çıkarsa null.</summary>
    public static OfflineBenchmark? Verify(MarianOnnx m, bool fullBenchmark)
    {
        var proc = Process.GetCurrentProcess();
        var cpu0 = proc.TotalProcessorTime;
        var wall = Stopwatch.StartNew();
        var sw = Stopwatch.StartNew();
        string first = m.Translate(BenchmarkSamples[0]);
        double firstMs = sw.Elapsed.TotalMilliseconds;
        if (string.IsNullOrWhiteSpace(first)) return null;
        var times = new List<double> { firstMs };
        if (fullBenchmark)
        {
            times.Clear();
            foreach (var s in BenchmarkSamples)
            {
                sw.Restart();
                m.Translate(s);
                times.Add(sw.Elapsed.TotalMilliseconds);
            }
        }
        proc.Refresh();
        double cpuPct = (proc.TotalProcessorTime - cpu0).TotalMilliseconds / Math.Max(1, wall.Elapsed.TotalMilliseconds) / Environment.ProcessorCount * 100;
        var sorted = times.OrderBy(x => x).ToArray();
        double P(double q) => sorted[Math.Clamp((int)Math.Ceiling(q * sorted.Length) - 1, 0, sorted.Length - 1)];
        return new OfflineBenchmark(m.Device, m.DeviceName, m.LoadTime.TotalMilliseconds, firstMs, times.Average(), P(0.5), P(0.95),
            cpuPct, proc.WorkingSet64 / 1048576);
    }

    private static string Short(Exception ex)
    {
        var e = ex;
        while (e.InnerException != null) e = e.InnerException;
        string m = e.Message.Replace('\n', ' ');
        return m.Length > 180 ? m[..180] + "…" : m;
    }
}
