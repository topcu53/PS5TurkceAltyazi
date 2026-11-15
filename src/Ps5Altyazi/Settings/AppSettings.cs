using System;
using System.IO;
using System.Text.Json;
using Ps5Altyazi.Logging;
using Ps5Altyazi.Vision;

namespace Ps5Altyazi.Settings;

/// <summary>
/// Kullanıcı ayarları: %LOCALAPPDATA%\PS5TurkceAltyazi\settings.json
/// (Çeviri önbelleği 4. aşamada SQLite ile ayrıca tutulacak.)
/// </summary>
public sealed class AppSettings
{
    public int CaptureMethod { get; set; } = 0;
    public int Fps { get; set; } = 30;
    public bool CropToGame { get; set; } = true;

    /// <summary>Oyun alanına göre oransal altyazı alanı: [x, y, genişlik, yükseklik].</summary>
    public double[]? SubtitleRegion { get; set; }
    public string? SubtitleRegionSource { get; set; }
    public DateTime? SubtitleRegionUpdated { get; set; }

    /// <summary>DeepL API anahtarı, DPAPI ile şifreli (SecretStore).</summary>
    public string? DeepLKeyProtected { get; set; }
    public bool TranslationEnabled { get; set; } = true;

    /// <summary>Yedek 1: Azure Translator (isteğe bağlı), anahtar DPAPI ile şifreli.</summary>
    public string? AzureKeyProtected { get; set; }
    public string? AzureRegion { get; set; }

    /// <summary>Yedek 2: MyMemory (anahtarsız). E-posta verilirse günlük sınır ~10 kat artar.</summary>
    public bool MyMemoryEnabled { get; set; } = true;
    public string? MyMemoryEmail { get; set; }

    // ── Türkçe altyazı penceresi (Aşama 5) ──
    public bool SubtitleWindowEnabled { get; set; } = true;

    // Ekran çevirisi (menüler, HUD, görev yazıları) – F7
    public bool ScreenTranslateEnabled { get; set; } = false;
    public int ScreenScanIntervalMs { get; set; } = 700;
    public double SubtitleFontSize { get; set; } = 42;
    public double SubtitleBackgroundOpacity { get; set; } = 0.6;
    public double SubtitleWidthFraction { get; set; } = 0.7;
    public bool SubtitleTopmost { get; set; } = true;
    public double? SubtitleLeft { get; set; }
    public double? SubtitleBottom { get; set; }
    /// <summary>Türkçe kutuyu oyunun İngilizce altyazısının tam üstüne yerleştir (onu örter).</summary>
    public bool SubtitleCoverOriginal { get; set; } = false;
    /// <summary>Örtme modunda (PC ve TV) Türkçe yazı boyutu orijinal İngilizce altyazıyla aynı olsun.</summary>
    public bool SubtitleMatchOriginalSize { get; set; } = true;

    // Oyunun yazı tipi (oyun başına): "auto" = altyazıya en benzer font otomatik seçilir; ya da sabit bir font anahtarı
    public string ActiveGame { get; set; } = "Genel";
    public System.Collections.Generic.Dictionary<string, GameFontProfile> GameFonts { get; set; } = new();
    public bool AutoStart { get; set; } = true;

    // ── TV modu / ağ yayını (Aşama 8) ──
    public bool TvServerEnabled { get; set; } = false;
    public string TvPlatform { get; set; } = "webos";
    public string? TvIp { get; set; }
    public string? TvKeyProtected { get; set; }
    public string? TvPassphraseProtected { get; set; }
    public int TvHdmi { get; set; } = 1;
    public bool TvAutoLaunch { get; set; } = true;
    /// <summary>TV'de İngilizce altyazının üstünü Türkçe gelene kadar siyah kutuyla ört.</summary>
    public bool TvMaskEnglish { get; set; } = true;
    public DateTime? TvLastExtend { get; set; }
    /// <summary>TV'lerin SSH sunucu anahtarı parmak izleri (ip → SHA256), ilk bağlantıda öğrenilir.</summary>
    public System.Collections.Generic.Dictionary<string, string> TvHostKeys { get; set; } = new();

    /// <summary>Ayar dosyası sürümü (eski ayarları güncellemek için).</summary>
    public int SettingsVersion { get; set; } = 0;

    /// <summary>Kotası dolan motorların yeniden deneneceği zaman (motor kimliği → zaman).</summary>
    public System.Collections.Generic.Dictionary<string, DateTime> EngineCooldowns { get; set; } = new();

    public static string FilePath => Path.Combine(Log.AppDataDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public NormRect? GetRegion()
    {
        if (SubtitleRegion is not { Length: 4 } a) return null;
        var r = new NormRect(a[0], a[1], a[2], a[3]);
        return r.IsValid ? r : null;
    }

    public void SetRegion(NormRect? r, string? source)
    {
        SubtitleRegion = r is { } v ? new[] { v.X, v.Y, v.Width, v.Height } : null;
        SubtitleRegionSource = r is null ? null : source;
        SubtitleRegionUpdated = r is null ? null : DateTime.Now;
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions);
                if (s != null)
                {
                    Log.Info($"Ayarlar yüklendi: {FilePath}");
                    return s;
                }
            }
            Log.Info("Ayar dosyası yok; varsayılan ayarlar kullanılıyor.");
        }
        catch (Exception ex)
        {
            Log.Warn($"Ayarlar okunamadı, varsayılanlar kullanılıyor: {ex.Message}");
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Log.AppDataDir);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error($"Ayarlar kaydedilemedi: {ex.Message}");
        }
    }
}

/// <summary>Bir oyunun yazı tipi ayarı ve öğrenilmiş altyazı görünümü.</summary>
public sealed class GameFontProfile
{
    /// <summary>"auto" ya da katalogdaki font anahtarı (ör. "barlow-600", "custom-...").</summary>
    public string FontKey { get; set; } = "auto";
    public string? LearnedKey { get; set; }
    public double LearnedEm { get; set; }
    public string LearnedColor { get; set; } = "#FFFFFF";
    public bool LearnedOutline { get; set; }
    public int LearnedSamples { get; set; }
}
