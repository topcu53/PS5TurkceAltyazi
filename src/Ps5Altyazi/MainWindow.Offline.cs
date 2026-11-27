using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ps5Altyazi.Logging;
using Ps5Altyazi.Translation;

namespace Ps5Altyazi;

/// <summary>
/// Bilgisayarda çeviri: donanım profili, işlemci / ekran kartı seçimi, ekran kartı modelinin indirilmesi.
/// </summary>
public partial class MainWindow
{
    /// <summary>Algılanan donanım (ilk açılışta bir kez, arka planda).</summary>
    internal static HardwareProfile? Hardware { get; private set; }
    private Task? _hwDetect;
    private bool _offlineEventsHooked;
    private bool _gpuDownloading;

    /// <summary>Donanım algılanmadıysa algılar; bittiğinde çeviri zincirini yeniden kurar. true: hazır.</summary>
    private bool EnsureHardwareProfile()
    {
        if (Hardware != null) return true;
        _hwDetect ??= Task.Run(() =>
        {
            var hw = HardwareProfile.Detect();
            Hardware = hw;
            foreach (var line in hw.Describe().Split('\n')) Log.Info("🖥 " + line);
            Dispatcher.InvokeAsync(() => ApplyTranslators(checkUsage: false));
        });
        return false;
    }

    /// <summary>Ayarlardan bilgisayarda çeviri yapılandırması.</summary>
    internal static OfflineConfig BuildOfflineConfig(Ps5Altyazi.Settings.AppSettings s, HardwareProfile? hw)
    {
        var mode = s.OfflineDevice switch { "cpu" => OfflineMode.CpuOnly, "gpu" => OfflineMode.GpuOnly, _ => OfflineMode.Auto };
        var gpus = hw?.Gpus ?? Array.Empty<GpuInfo>();
        var gpu = gpus.FirstOrDefault(g => g.Index == s.OfflineGpuIndex) ?? hw?.BestGpu;
        // Otomatik modda dahili grafik (Intel UHD vb.) genelde işlemciden yavaştır ve oyun görüntüsüyle yarışır:
        // yalnız ayrık kart denenir. "Yalnız ekran kartı" seçildiyse dahili kart da kullanılabilir.
        bool usable = gpu != null && OfflineTranslator.GpuFilesInstalled && (mode == OfflineMode.GpuOnly || !gpu.Integrated);
        return new OfflineConfig(mode, usable, gpu?.Index ?? 0, Math.Clamp(s.OfflineThreads, 0, 64), hw?.PhysicalCores ?? 0, s.OfflineCpuFallback);
    }

    /// <summary>ApplyTranslators içinden: cihazı ayarla, modeli arka planda aç, gerekirse ekran kartı modelini indir.</summary>
    private void ConfigureOffline()
    {
        if (!_offlineEventsHooked)
        {
            _offlineEventsHooked = true;
            OfflineTranslator.Event += (msg, isError) => Dispatcher.InvokeAsync(() =>
            {
                if (isError) Log.Warn(msg); else Log.Info(msg);
                UpdateTranslationStatus();
            });
        }
        if (!EnsureHardwareProfile()) return; // algılanınca yeniden çağrılır
        OfflineTranslator.Configure(BuildOfflineConfig(_settings, Hardware));
        _ = OfflineTranslator.Preload();
        _ = EnsureGpuModelAsync();
    }

    /// <summary>
    /// Uygun ayrık ekran kartı varsa (≥ 2 GB VRAM) ve "Yalnız işlemci" seçili değilse ekran kartı modelini
    /// (FP16, ~890 MB, bir kez) arka planda indirir; bitince Otomatik mod hız testiyle en hızlı cihazı seçer.
    /// </summary>
    private async Task EnsureGpuModelAsync()
    {
        var hw = Hardware;
        if (_gpuDownloading || hw == null || OfflineTranslator.GpuFilesInstalled || !OfflineTranslator.IsInstalled) return;
        if (_settings.OfflineDevice == "cpu") return;
        if (!hw.HasSuitableDiscreteGpu && _settings.OfflineDevice != "gpu") return;
        if (hw.Gpus.Count == 0) return;
        _gpuDownloading = true;
        try
        {
            int lastTen = -1;
            var prog = new Progress<(long Done, long Total)>(p =>
            {
                int pct = p.Total > 0 ? (int)(100 * p.Done / p.Total) : 0;
                OfflineStatus = $"💻 Ekran kartı modeli indiriliyor %{pct} (çeviri bu sırada işlemcide sürüyor)";
                if (pct / 10 != lastTen) { lastTen = pct / 10; UpdateTranslationStatus(); }
            });
            Log.Info($"Ekran kartı bulundu ({hw.BestGpu?.Name}). Daha hızlı çeviri için ekran kartı modeli indiriliyor (bir kez, ~890 MB).");
            await OfflineTranslator.DownloadAsync(prog, CancellationToken.None, includeGpu: true);
            OfflineStatus = null;
            Log.Success("Ekran kartı modeli hazır; hız testiyle en hızlı cihaz seçiliyor.");
            ApplyTranslators(checkUsage: false);
            RefreshSettingsTexts();
        }
        catch (Exception ex)
        {
            OfflineStatus = null;
            Log.Warn($"Ekran kartı modeli indirilemedi ({ex.Message}); çeviri işlemcide sürüyor. Ayarlar → Çeviri'den yeniden deneyebilirsiniz.");
        }
        finally
        {
            _gpuDownloading = false;
            UpdateTranslationStatus();
        }
    }
}
