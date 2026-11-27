using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Ps5Altyazi.TextRecognition;

public sealed record OcrOutput(string Text, string[] Lines, double Milliseconds);

/// <summary>OCR'ın bulduğu tek kelime ve görüntüdeki kutusu (piksel).</summary>
public sealed record OcrWordBox(string Text, double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;
    public double CenterY => Y + H / 2;
}

/// <summary>OCR motoru soyutlaması (ileride PaddleOCR / Tesseract eklenebilir).</summary>
public interface ISubtitleOcr
{
    string Name { get; }
    int MaxImageDimension { get; }
    Task<OcrOutput> RecognizeAsync(OcrInput input);
}

/// <summary>
/// Windows'un yerleşik OCR motoru (Windows.Media.Ocr). Ek kütüphane gerektirmez, CPU'da hızlıdır.
/// İngilizce OCR dil paketi gerekir; yoksa kullanıcı profilindeki dille (ör. Türkçe, Latin alfabesi) çalışır.
/// </summary>
public sealed class WindowsOcrEngine : ISubtitleOcr
{
    private readonly OcrEngine _engine;

    public string Name { get; }
    public int MaxImageDimension => (int)OcrEngine.MaxImageDimension;
    public bool IsEnglish { get; }

    private WindowsOcrEngine(OcrEngine engine, bool isEnglish)
    {
        _engine = engine;
        IsEnglish = isEnglish;
        Name = $"Windows OCR ({engine.RecognizerLanguage.LanguageTag})";
    }

    public static string AvailableLanguages()
    {
        try { return string.Join(", ", OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag)); }
        catch (Exception ex) { return $"okunamadı: {ex.Message}"; }
    }

    /// <summary>İngilizce motoru oluşturur; yoksa profil dilleriyle yedek motor döndürür. Hiçbiri yoksa null.</summary>
    public static WindowsOcrEngine? Create(out string status)
    {
        try
        {
            var english = OcrEngine.AvailableRecognizerLanguages
                .FirstOrDefault(l => l.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase));
            if (english != null)
            {
                var e = OcrEngine.TryCreateFromLanguage(english);
                if (e != null)
                {
                    status = $"İngilizce OCR hazır ({english.LanguageTag}).";
                    return new WindowsOcrEngine(e, true);
                }
            }

            var fallback = OcrEngine.TryCreateFromUserProfileLanguages();
            if (fallback != null)
            {
                status = $"İngilizce OCR dil paketi YOK; yedek olarak {fallback.RecognizerLanguage.LanguageTag} kullanılıyor " +
                         "(İngilizce metni okur ama doğruluk düşebilir). \"İngilizce OCR paketini kur\" düğmesini kullanın.";
                return new WindowsOcrEngine(fallback, false);
            }

            status = "Windows OCR için yüklü dil yok. \"İngilizce OCR paketini kur\" düğmesini kullanın.";
            return null;
        }
        catch (Exception ex)
        {
            status = $"Windows OCR başlatılamadı: {ex.Message}";
            return null;
        }
    }

    public async Task<OcrOutput> RecognizeAsync(OcrInput input)
    {
        var sw = Stopwatch.StartNew();
        using var bmp = SoftwareBitmap.CreateCopyFromBuffer(input.Bgra.AsBuffer(), BitmapPixelFormat.Bgra8,
            input.Width, input.Height, BitmapAlphaMode.Premultiplied);
        var result = await _engine.RecognizeAsync(bmp);
        sw.Stop();
        var lines = result.Lines.Select(l => l.Text).ToArray();
        return new OcrOutput(string.Join("\n", lines), lines, sw.Elapsed.TotalMilliseconds);
    }

    /// <summary>Görüntüdeki bütün kelimeleri kutularıyla döndürür (ekran çevirisi için).</summary>
    public async Task<List<OcrWordBox>> RecognizeWordsAsync(byte[] bgra, int width, int height)
    {
        using var bmp = SoftwareBitmap.CreateCopyFromBuffer(bgra.AsBuffer(), BitmapPixelFormat.Bgra8,
            width, height, BitmapAlphaMode.Premultiplied);
        var result = await _engine.RecognizeAsync(bmp);
        var words = new List<OcrWordBox>();
        foreach (var line in result.Lines)
            foreach (var w in line.Words)
            {
                var r = w.BoundingRect;
                if (!string.IsNullOrWhiteSpace(w.Text) && r.Width > 0 && r.Height > 0)
                    words.Add(new OcrWordBox(w.Text, r.X, r.Y, r.Width, r.Height));
            }
        return words;
    }

    /// <summary>
    /// İngilizce OCR dil paketini Windows'un resmi yöntemiyle kurar (yönetici onayı ister).
    /// </summary>
    public static void StartEnglishPackInstall()
    {
        const string cmd =
            "Write-Host 'Ingilizce OCR dil paketi kuruluyor, lutfen bekleyin...';" +
            "Add-WindowsCapability -Online -Name 'Language.OCR~~~en-US~0.0.1.0';" +
            "Write-Host 'Tamamlandi. Altyazi+ uygulamasini yeniden baslatin.';" +
            "Read-Host 'Kapatmak icin Enter'";
        Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{cmd}\"")
        {
            UseShellExecute = true,
            Verb = "runas"
        });
    }
}
