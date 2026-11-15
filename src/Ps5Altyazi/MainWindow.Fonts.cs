using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using Ps5Altyazi.Capture;
using Ps5Altyazi.Fonts;
using Ps5Altyazi.Logging;
using Ps5Altyazi.Settings;
using Ps5Altyazi.Vision;

namespace Ps5Altyazi;

/// <summary>
/// Türkçe altyazıyı oyunun kendi altyazı yazısına benzetmek: oyunun altyazısı ölçülür, programla gelen açık lisanslı
/// fontlardan en benzeri seçilir (ya da kullanıcı oyun için bir font / kendi font dosyasını seçer) ve Türkçe yazı
/// orijinal harflerle aynı yükseklikte, aynı renkte yazılır. Öğrenilen görünüm oyun başına kaydedilir:
/// program bir sonraki açılışta ilk altyazıdan itibaren doğru font ve boyla başlar.
/// </summary>
public partial class MainWindow
{
    private readonly SubtitleFontMatcher _fontMatcher = new();
    private int _lastFrameH;
    private string? _lastFontLog;

    private GameFontProfile ActiveFontProfile
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_settings.ActiveGame)) _settings.ActiveGame = "Genel";
            if (!_settings.GameFonts.TryGetValue(_settings.ActiveGame, out var p))
            {
                p = new GameFontProfile();
                _settings.GameFonts[_settings.ActiveGame] = p;
            }
            return p;
        }
    }

    /// <summary>Etkin oyunun yazı tipi ayarını yükler (açılışta ve oyun değişince).</summary>
    private void InitGameFont()
    {
        var p = ActiveFontProfile;
        _fontMatcher.Reset();
        _fontMatcher.Fixed = p.FontKey == "auto" ? null : FontCatalog.Find(p.FontKey);
        var learned = FontCatalog.Find(p.LearnedKey);
        var font = _fontMatcher.Fixed ?? learned;
        if (font != null)
        {
            double em = p.LearnedEm;
            if (em > 0 && learned != null && font != learned) em *= learned.XhPerEm / Math.Max(0.2, font.XhPerEm);
            _fontMatcher.Seed(new GameFontChoice(font, Math.Max(0, em), p.LearnedColor, p.LearnedOutline, p.LearnedSamples));
        }
        _lastFontLog = null;
        ApplyGameFont();
        Log.Info($"Oyun: \"{_settings.ActiveGame}\" – yazı tipi: " +
                 (_fontMatcher.Fixed != null ? $"{_fontMatcher.Fixed.DisplayName} (sabit)" :
                  learned != null ? $"otomatik (öğrenilmiş: {learned.DisplayName})" : "otomatik (ilk altyazılardan öğrenilecek)"));
    }

    /// <summary>Kullanıcı aynı oyun için fontu değiştirdi (otomatik / sabit / eklenen dosya).</summary>
    private void OnFontSettingChanged(bool gameChanged)
    {
        if (gameChanged) { InitGameFont(); return; }
        var p = ActiveFontProfile;
        var fixedFont = p.FontKey == "auto" ? null : FontCatalog.Find(p.FontKey);
        _fontMatcher.SetFixed(fixedFont, _lastFrameH);
        ApplyGameFont();
        SaveLearnedFont();
    }

    /// <summary>OCR'ı tutan her altyazıdan sonra çağrılır: yazıyı ölçer, gerekirse fontu / boyu günceller.</summary>
    private void LearnGameFont(CapturedFrame game, List<TextLine> lines, IReadOnlyList<string> ocrLines)
    {
        try
        {
            _lastFrameH = game.Height;
            var rects = lines.Select(l => l.Bounds).OrderBy(b => b.Y).ToList();
            var texts = ocrLines.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            if (_fontMatcher.Add(game, rects, texts))
            {
                ApplyGameFont();
                SaveLearnedFont();
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Yazı tipi ölçülemedi: {ex.Message}");
        }
    }

    private void SaveLearnedFont()
    {
        var c = _fontMatcher.Current;
        if (c == null || c.EmNorm <= 0) return;
        var p = ActiveFontProfile;
        p.LearnedKey = c.Font.Key;
        p.LearnedEm = Math.Round(c.EmNorm, 5);
        p.LearnedColor = c.ColorHex;
        p.LearnedOutline = c.Outline;
        p.LearnedSamples = c.Samples;
        SaveSettings();
    }

    /// <summary>Seçili yazı tipini PC altyazı penceresine ve TV'ye uygular.</summary>
    private void ApplyGameFont()
    {
        var c = _fontMatcher.Current;
        if (c == null)
        {
            _subWin?.SetGameFont(null, true, 0, null);
        }
        else
        {
            FontFamily? family = null;
            try
            {
                var baseUri = new Uri(FontCatalog.Folder.TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar);
                family = new FontFamily(baseUri, "./" + c.Font.File + "#" + c.Font.Family);
            }
            catch (Exception ex)
            {
                Log.Warn($"Yazı tipi yüklenemedi ({c.Font.File}): {ex.Message}");
            }
            Color? color = null;
            try { color = (Color)ColorConverter.ConvertFromString(c.ColorHex); } catch { }
            double emPx = _lastFrameH > 0 && c.EmNorm > 0 ? c.EmPx(_lastFrameH) : 0;
            _subWin?.SetGameFont(family, c.Font.IsBold, emPx, color);

            string desc = $"{c.Font.DisplayName}|{c.EmNorm:F4}|{c.ColorHex}";
            if (desc != _lastFontLog && c.EmNorm > 0)
            {
                _lastFontLog = desc;
                Log.Success($"Altyazı yazı tipi ({_settings.ActiveGame}): {c.Font.DisplayName}" +
                            (_fontMatcher.Fixed != null ? " (sabit)" : " (oyunun altyazısına en benzer)") +
                            $" • yazı boyu {c.EmNorm * 1080:F0} px (1080p) • renk {c.ColorHex}" +
                            (c.Samples > 0 ? $" • {c.Samples} satırdan ölçüldü" : ""));
            }
        }
        SendStyleToScreens();
    }

    /// <summary>TV'ye gönderilecek yazı tipi bilgisi (null = varsayılan yazı).</summary>
    private object? GameFontForScreens()
    {
        var c = _fontMatcher.Current;
        if (c == null) return null;
        return new
        {
            css = c.Font.CssFamily,
            file = c.Font.File,
            bold = c.Font.IsBold,
            em = Math.Round(c.EmNorm, 5),
            color = c.ColorHex
        };
    }

    /// <summary>Altyazı Görünümü penceresinde gösterilen açıklama.</summary>
    private string DescribeGameFont()
    {
        var c = _fontMatcher.Current;
        if (c == null) return "Henüz ölçülmedi: birkaç altyazıdan sonra oyunun yazısına en benzer font seçilir.";
        string size = c.EmNorm > 0 ? $"yazı boyu {c.EmNorm * 1080:F0} px (1080p)" : "yazı boyu henüz ölçülmedi";
        return $"Şu an: {c.Font.DisplayName} • {size} • renk {c.ColorHex}" + (c.Samples > 0 ? $" • {c.Samples} satırdan ölçüldü" : "");
    }
}
