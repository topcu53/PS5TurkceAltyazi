using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Ps5Altyazi.Translation;

// Kabul testleri: her senaryo "GEÇTİ/KALDI" yazar; biri kalırsa çıkış kodu 1.
string dir = args[0];
bool windows = OperatingSystem.IsWindows();
int fails = 0;
void Check(string name, bool ok, string detail)
{
    Console.WriteLine($"{(ok ? "GEÇTİ" : "KALDI")} | {name} | {detail}");
    if (!ok) fails++;
}

string[] samples = OfflineEngine.BenchmarkSamples;

// 1) CPU modu (Intel dahili grafik / ekran kartı yok senaryosu: GPU hiç denenmez)
var cpuR = OfflineEngine.Open(dir, OfflineMode.CpuOnly, gpuAvailable: false, 0, 0, 0, cpuFallback: true, benchmark: true);
Check("CPU modu", cpuR.Engine?.Device == MarianDevice.Cpu, cpuR.Message);
var cpuOut = samples.Select(s => cpuR.Engine!.Translate(s)).ToArray();
File.WriteAllLines(Path.Combine(dir, "samples.txt"), samples);
File.WriteAllLines(Path.Combine(dir, "cs_ids.txt"), samples.Select(s => string.Join(" ", cpuR.Engine!.Encode(s))));
for (int i = 0; i < samples.Length; i++) Console.WriteLine($"   CPU  {samples[i]}\n     → {cpuOut[i]}");

// 2) CPU iş parçacığı elle (2)
using (var t2 = new MarianOnnx(dir, MarianDevice.Cpu, 0, threads: 2))
    Check("CPU elle iş parçacığı = 2", t2.Threads == 2 && t2.Translate("Get in the car!").Length > 0, t2.DeviceName);

// 3) Otomatik, ekran kartı yok (CUDA/DirectX 12 yok) → CPU
var a1 = OfflineEngine.Open(dir, OfflineMode.Auto, gpuAvailable: false, 0, 0, 0, true, true);
Check("Otomatik + ekran kartı yok → CPU", a1.Engine?.Device == MarianDevice.Cpu && !a1.IsError, a1.Message);
a1.Engine?.Dispose();

// 4) Yalnız GPU + ekran kartı yok + CPU'ya dönüş kapalı → açık hata, çökme yok
var g1 = OfflineEngine.Open(dir, OfflineMode.GpuOnly, gpuAvailable: false, 0, 0, 0, cpuFallback: false, benchmark: false);
Check("Yalnız GPU + ekran kartı yok → açık hata", g1.Engine == null && g1.IsError && g1.Message.Contains("Yalnız ekran kartı"), g1.Message);

// 5) Model klasörü yok (internet yok, indirilmemiş) → hata, çökme yok
var miss = OfflineEngine.Open(Path.Combine(dir, "yok"), OfflineMode.Auto, true, 0, 0, 0, true, false);
Check("Model yok → çökmeden hata", miss.Engine == null && miss.IsError, miss.Message);

if (windows)
{
    // 6) Ekran kartında (DirectML; CI'da WARP yazılım aygıtı) gerçek çeviri, FP16
    var g = OfflineEngine.Open(dir, OfflineMode.GpuOnly, gpuAvailable: true, 0, 0, 0, cpuFallback: false, benchmark: true);
    Check("Ekran kartı (DirectML) modu", g.Engine?.Device == MarianDevice.Gpu, g.Message);
    if (g.Engine != null)
    {
        int same = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            string o = g.Engine.Translate(samples[i]);
            if (o == cpuOut[i]) same++;
            Console.WriteLine($"   GPU  {samples[i]}\n     → {o}{(o == cpuOut[i] ? "  (CPU ile aynı)" : "  (CPU: " + cpuOut[i] + ")")}");
        }
        // FP16 ile INT8 çıktıları birebir aynı olmak zorunda değil ama çoğu aynı / çok yakın olmalı
        Check("FP16 (GPU) ile INT8 (CPU) çıktı uyumu", same >= samples.Length / 2, $"{same}/{samples.Length} birebir aynı");
        g.Engine.Dispose();
    }

    // 7) Ekran kartı yükleme hatası (olmayan aygıt) → Otomatik CPU'ya döner
    var bad = OfflineEngine.Open(dir, OfflineMode.Auto, gpuAvailable: true, gpuIndex: 99, 0, 0, true, true);
    Check("GPU yükleme hatası → CPU'ya dönüş", bad.Engine?.Device == MarianDevice.Cpu && !bad.IsError, bad.Message);
    bad.Engine?.Dispose();

    // 8) Otomatik + ekran kartı var → hız testiyle seçim (iki ölçüm de raporlanır)
    var auto = OfflineEngine.Open(dir, OfflineMode.Auto, gpuAvailable: true, 0, 0, 0, true, true);
    Check("Otomatik: hız testiyle seçim", auto.Engine != null && auto.Cpu != null && auto.Gpu != null, auto.Message);
    auto.Engine?.Dispose();

    // 9) Ekran kartı çalışırken bellek hatası → bu altyazı CPU'da çevrilir, sonrası CPU
    OfflineTranslator.ModelDir = dir;
    OfflineTranslator.Configure(new OfflineConfig(OfflineMode.GpuOnly, GpuAvailable: true, GpuIndex: 0, CpuFallback: true));
    OfflineTranslator.Preload(benchmark: false)!.Wait();
    var before = OfflineTranslator.ActiveDevice;
    OfflineTranslator.FaultInjection = "gpu-run";
    string fb = new OfflineTranslator().TranslateAsync("Get in the car!", null, CancellationToken.None).Result;
    Check("GPU çalışırken bellek hatası → CPU'da sürer", before == MarianDevice.Gpu && OfflineTranslator.ActiveDevice == MarianDevice.Cpu && fb.Length > 0,
          $"önce {before}, sonra {OfflineTranslator.ActiveDevice}: \"{fb}\"");
}

cpuR.Engine?.Dispose();
Console.WriteLine(fails == 0 ? "TÜM TESTLER GEÇTİ" : $"{fails} TEST KALDI");
return fails == 0 ? 0 : 1;
