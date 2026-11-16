using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ps5Altyazi.Capture;
using Ps5Altyazi.Logging;
using Ps5Altyazi.TextRecognition;
using Ps5Altyazi.Translation;
using Ps5Altyazi.Tv;
using Ps5Altyazi.Vision;

namespace Ps5Altyazi;

/// <summary>
/// Ekran çevirisi (F7) – YALNIZCA OYNANIŞ EKRANI: tuş ipuçları ("Greet Ⓛ2", "Press Ⓧ to …"), görev / ipucu kutuları,
/// kenardaki HUD yazıları. Menüler (duraklatma, harita, ayarlar) çevrilmez.
///
/// İşleyiş (v0.13):
/// - Karenin ortası okunmaz (oyun dünyası), altyazı alanı ayrıca işlenir.
/// - Bir taramada çok sayıda yazı varsa (menü / harita açık) hiçbir şey gösterilmez.
/// - Tuş simgeleri ASLA örtülmez: Türkçe, simgenin solundaki/sağındaki İngilizce kelimelerin yerine yazılır ve
///   simgeden uzağa doğru büyür ("Press Ⓧ to skip" → "Atlamak için Ⓧ tuşuna basın", Ⓧ yerinde kalır).
/// - Her karede kutudaki beyaz yazı deseni kontrol edilir: yazı değiştiği / kaybolduğu anda Türkçe kalkar ve
///   hemen yeni tarama yapılır.
/// - Bekleyen bütün yazılar tek DeepL isteğinde çevrilir; aynı yazı tekrar çıkınca hafızadan anında gelir.
/// </summary>
public partial class MainWindow
{
    private sealed class ScreenItem
    {
        public required ScreenTextBlock Block;
        public string English => Block.Tagged;
        public string? Turkish;
        public bool NoTranslation;   // çeviri = orijinal (isim vb.) → kutu koyma
        public bool Translating;
        public PixelRect Px => Block.Bounds;
        public int Seen = 1, Missed;
        public long LastScan;
        public bool Hidden;           // yazı değişti / kayboldu, sonraki taramayı bekliyor
        public bool[] Mask = Array.Empty<bool>();
        public int MaskCount;
        public string[] IconPngs = Array.Empty<string>();
        public int FrameW, FrameH;
    }

    private const string ScreenContext =
        "Video game HUD text during gameplay: button prompts and interaction options (e.g. Greet, Antagonize, Press to skip), " +
        "mission objectives and hints. Translate briefly, as in a game's Turkish localization.";

    private static readonly Regex TagRx = new("<x id=\"(\\d+)\"\\s*/>", RegexOptions.Compiled);

    private readonly List<ScreenItem> _screenItems = new();
    private readonly Dictionary<string, string?> _screenTr = new(StringComparer.Ordinal); // İngilizce(etiketli) → Türkçe (null = çevrilmez)
    private ScreenOverlayWindow? _screenWin;
    private WindowsOcrEngine? _screenOcr;
    private bool _screenBusy, _screenTrBusy, _screenForceScan, _menuOpen;
    private DateTime _lastScreenScan, _lastScreenLog;
    private Rect _lastGameScreenPx;
    private string _lastScreenKey = "";
    private long _screenScans, _screenTranslated, _screenScanSeq;
    private double _screenOcrMsSum;

    // Ekran değişimi takibi (küçük gri özet)
    private const int ThumbW = 64, ThumbH = 36;
    private byte[]? _thumbPrev, _thumbAtScan;
    private int _stableFrames;

    private bool ScreenOn => _settings.ScreenTranslateEnabled;

    private void InitScreenTranslation()
    {
        ScreenCheck.IsChecked = _settings.ScreenTranslateEnabled;
        if (_settings.ScreenTranslateEnabled)
            Log.Info("Oyun içi yazı çevirisi AÇIK (tuş ipuçları, görev / ipucu yazıları; menüler hariç). Kapatmak için F7.");
        if (!EnglishWords.IsLoaded) Log.Warn("İngilizce kelime listesi yüklenemedi; OCR düzeltmesi yapılmayacak.");
    }

    private void ScreenCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        SetScreenTranslation(ScreenCheck.IsChecked == true, "kutu");
    }

    /// <summary>F7: oyun içi yazı çevirisini aç / kapat.</summary>
    private void ToggleScreenTranslation() => SetScreenTranslation(!_settings.ScreenTranslateEnabled, "F7");

    private void SetScreenTranslation(bool on, string source)
    {
        if (_settings.ScreenTranslateEnabled == on && ScreenCheck.IsChecked == on) return;
        _settings.ScreenTranslateEnabled = on;
        SaveSettings();
        if (ScreenCheck.IsChecked != on) ScreenCheck.IsChecked = on;
        if (!on) ClearScreenItems();
        _thumbAtScan = null;
        Notify(on ? "Oyun içi yazı çevirisi açık (F7)" : "Oyun içi yazı çevirisi kapalı (F7)");
        Log.Info($"[{source}] Oyun içi yazı çevirisi {(on ? "AÇILDI (tuş ipuçları, görev / ipucu yazıları; menüler hariç)" : "KAPATILDI")}.");
        if (on && _ocr is not WindowsOcrEngine)
            Log.Warn("Oyun içi yazı çevirisi için Windows OCR (İngilizce paket) gerekir.");
        if (on && _capture == null)
            Log.Info("Oyun içi yazı çevirisi, çeviri başlatıldığında (F6) çalışır.");
    }

    private void ClearScreenItems()
    {
        _screenItems.Clear();
        PushScreen(force: true);
    }

    /// <summary>Her canlı karede çağrılır (arayüz iş parçacığı).</summary>
    private void ScreenTick(FrameResult r)
    {
        if (!ScreenOn || _ocr is not WindowsOcrEngine) return;
        _screenOcr ??= WindowsOcrEngine.Create(out _); // altyazı OCR'ı ile aynı anda çalışabilsin diye ayrı motor
        if (_screenOcr is not { } engine) return;

        var game = r.Game;
        var gameRect = new Rect(game.OriginX, game.OriginY, game.Width, game.Height);
        bool moved = gameRect != _lastGameScreenPx;
        _lastGameScreenPx = gameRect;

        // 1) Gösterilen kutulardaki yazı değişti / kayboldu mu? (Greet → Defuse, ipucu kapandı) → hemen kaldır + tara
        bool changed = moved;
        foreach (var it in _screenItems)
        {
            if (it.Hidden || it.MaskCount < 4 || it.FrameW != game.Width || it.FrameH != game.Height) continue;
            var (mask, count) = ScreenTextReader.TextMask(game, it.Px);
            if (count < it.MaskCount * 0.4 || ScreenTextReader.MaskDistance(mask, it.Mask) > 0.45)
            {
                it.Hidden = true;
                changed = true;
                _screenForceScan = true;
            }
        }
        if (changed) PushScreen();

        // 2) Tam (kenar) tarama: değişim olduysa hemen, ekran değişip durulduysa hemen, değilse aralıklarla
        var thumb = Thumb(game);
        double motion = _thumbPrev == null ? 99 : ThumbDiff(thumb, _thumbPrev);
        _thumbPrev = thumb;
        _stableFrames = motion < 2.5 ? _stableFrames + 1 : 0;
        double sinceScan = _thumbAtScan == null ? 99 : ThumbDiff(thumb, _thumbAtScan);
        double ms = (DateTime.Now - _lastScreenScan).TotalMilliseconds;
        int interval = Math.Clamp(_settings.ScreenScanIntervalMs, 400, 5000);

        bool settledChange = sinceScan > 5 && _stableFrames >= 2 && ms > 150;
        bool forced = _screenForceScan && ms > 100;
        if (_screenBusy || !(forced || settledChange || ms >= interval)) return;

        _screenForceScan = false;
        _thumbAtScan = thumb;
        PixelRect? exclude = OcrCheck.IsChecked == true ? r.RegionPx : null; // altyazı alanı ayrı işleniyor
        _ = ScanScreenAsync(engine, game, exclude);
    }

    private static byte[] Thumb(CapturedFrame f)
    {
        var t = new byte[ThumbW * ThumbH];
        for (int j = 0; j < ThumbH; j++)
        {
            int y = (int)((j + 0.5) * f.Height / ThumbH);
            for (int i = 0; i < ThumbW; i++)
            {
                int x = (int)((i + 0.5) * f.Width / ThumbW);
                int p = y * f.Stride + x * 4;
                t[j * ThumbW + i] = (byte)((f.Pixels[p] * 29 + f.Pixels[p + 1] * 150 + f.Pixels[p + 2] * 77) >> 8);
            }
        }
        return t;
    }

    private static double ThumbDiff(byte[] a, byte[] b)
    {
        long s = 0;
        for (int i = 0; i < a.Length; i++) s += Math.Abs(a[i] - b[i]);
        return (double)s / a.Length;
    }

    private async Task ScanScreenAsync(WindowsOcrEngine engine, CapturedFrame game, PixelRect? exclude)
    {
        _screenBusy = true;
        _lastScreenScan = DateTime.Now;
        try
        {
            var res = await Task.Run(() => ScreenTextReader.ReadAsync(engine, game, exclude));
            if (!ScreenOn || _capture == null) return;
            _screenScans++;
            _screenOcrMsSum += res.Milliseconds;

            // Menü / harita açık mı? (çok sayıda yazı ya da büyük yazılı liste) → gösterme, çevirme
            int big = res.Blocks.Count(b => b.LineHeight >= 0.03 * game.Height);
            bool menu = res.Blocks.Count >= 7 || big >= 4;
            if (menu != _menuOpen)
            {
                _menuOpen = menu;
                Log.Info(menu ? $"[Ekran] Menü / harita algılandı ({res.Blocks.Count} yazı): menüler çevrilmiyor."
                              : "[Ekran] Oyuna dönüldü: tuş ipuçları ve HUD yazıları çevriliyor.");
            }
            if (menu)
            {
                if (_screenItems.Count > 0) ClearScreenItems();
                return;
            }

            MergeScreenBlocks(game, res.Blocks);

            if ((DateTime.Now - _lastScreenLog).TotalSeconds >= 15)
            {
                _lastScreenLog = DateTime.Now;
                int shown = _screenItems.Count(IsShowable);
                Log.Info($"[Ekran] {res.Blocks.Count} yazı bulundu ({res.WordCount} kelime, {res.RejectedCount} anlamsız okuma elendi), " +
                         $"{shown} tanesi Türkçe gösteriliyor. OCR {res.Milliseconds:F0} ms " +
                         $"(ortalama {_screenOcrMsSum / Math.Max(1, _screenScans):F0} ms). " +
                         $"Okunanlar: {string.Join(" | ", res.Blocks.Take(12).Select(b => Short(b.Tagged, 40)))}");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Oyun içi yazı OCR hatası: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _screenBusy = false;
        }
    }

    private void MergeScreenBlocks(CapturedFrame game, List<ScreenTextBlock> blocks)
    {
        long scan = ++_screenScanSeq;
        var matched = new HashSet<ScreenItem>();
        foreach (var b in blocks)
        {
            if (b.Text.Length < 3) continue;

            // Aynı yer = aynı öğe
            ScreenItem? item = null;
            double best = 0;
            foreach (var it in _screenItems)
            {
                if (matched.Contains(it)) continue;
                double ov = Overlap(it.Px, b.Bounds);
                if (ov > best) { best = ov; item = it; }
            }

            if (item != null && best >= 0.5)
            {
                double sim = SubtitleText.Similarity(item.Block.Text, b.Text);
                item.Missed = 0;
                item.Hidden = false;
                if (sim < 0.8 || (b.Tagged != item.English && b.Known > item.Block.Known))
                {
                    // Başka yazı geldi (Greet → Defuse) ya da daha iyi okundu → yeniden çevir
                    if (sim < 0.8) item.Seen = 1; else item.Seen++;
                    item.Turkish = null;
                    item.NoTranslation = false;
                }
                else item.Seen++;
                item.Block = b;
            }
            else
            {
                item = new ScreenItem { Block = b };
                _screenItems.Add(item);
            }

            item.LastScan = scan;
            (item.Mask, item.MaskCount) = ScreenTextReader.TextMask(game, b.Bounds);
            item.IconPngs = b.Icons.Select(ic => IconPng(game, ic)).ToArray();
            item.FrameW = game.Width;
            item.FrameH = game.Height;
            matched.Add(item);
        }

        // Bu taramada görülmeyenler: bir kez görülmezse gizle, iki kez görülmezse kaldır
        foreach (var it in _screenItems.Where(i => !matched.Contains(i)).ToList())
        {
            it.Missed++;
            it.Hidden = true;
            if (it.Missed >= 2 || it.Seen < 2) _screenItems.Remove(it);
        }

        // Üst üste binen kopyaları temizle (en son görüleni tut)
        var keep = new List<ScreenItem>();
        foreach (var it in _screenItems.OrderByDescending(i => i.LastScan))
            if (!keep.Any(k => Overlap(k.Px, it.Px) >= 0.15)) keep.Add(it);
        _screenItems.RemoveAll(i => !keep.Contains(i));
        if (_screenItems.Count > 30) _screenItems.RemoveRange(0, _screenItems.Count - 30);

        foreach (var it in _screenItems) ApplyKnownTranslation(it);
        PushScreen();
        _ = TranslatePendingScreenItemsAsync();
    }

    private static string IconPng(CapturedFrame game, RectD r)
    {
        try
        {
            var (bgra, w, h) = ScreenTextReader.Crop(game, new RectD(r.X - 1, r.Y - 1, r.W + 2, r.H + 2));
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return Convert.ToBase64String(ms.ToArray());
        }
        catch
        {
            return "";
        }
    }

    private static double Overlap(PixelRect a, PixelRect b)
    {
        int ix = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
        int iy = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
        if (ix <= 0 || iy <= 0) return 0;
        double inter = (double)ix * iy;
        return inter / Math.Max(1, Math.Min((double)a.Width * a.Height, (double)b.Width * b.Height));
    }

    private void ApplyKnownTranslation(ScreenItem it)
    {
        if (it.Turkish != null || it.NoTranslation) return;
        if (_screenTr.TryGetValue(it.English, out var known))
        {
            if (known == null) it.NoTranslation = true;
            else it.Turkish = known;
        }
    }

    /// <summary>Bekleyen bütün yazıları tek istekte çevirir; bitince yenileri varsa tekrar çalışır.</summary>
    private async Task TranslatePendingScreenItemsAsync()
    {
        if (_screenTrBusy) return;
        var service = _translation;
        if (service == null || TranslateCheck.IsChecked != true) return;

        _screenTrBusy = true;
        var attempted = new HashSet<string>();
        try
        {
            while (true)
            {
                // Sözlükteki yazılar hemen; şüpheliler ikinci görülüşte (kota harcanmasın)
                var pending = _screenItems
                    .Where(i => i.Turkish == null && !i.NoTranslation && !i.Translating && !attempted.Contains(i.English) &&
                                (i.Seen >= 2 || i.Block.Known >= 0.6 || SubtitleText.IsPlausible(i.Block.Text)))
                    .Select(i => i.English).Distinct().Take(50).ToList();
                if (pending.Count == 0) break;
                attempted.UnionWith(pending);
                foreach (var it in _screenItems.Where(i => pending.Contains(i.English))) it.Translating = true;

                try
                {
                    var (results, fromCache, engineName, ms) = await service.TranslateManyAsync(pending, ScreenContext, CancellationToken.None);
                    for (int k = 0; k < pending.Count; k++)
                    {
                        string en = pending[k];
                        string? tr = results[k]?.Trim();
                        if (tr == null) continue; // çevrilemedi → sonraki taramada tekrar denenir
                        string plainTr = WebUtility.HtmlDecode(TagRx.Replace(tr, " ")).Trim();
                        string plainEn = WebUtility.HtmlDecode(TagRx.Replace(en, " ")).Trim();
                        bool same = plainTr.Length == 0 || string.Equals(Simplify(plainTr), Simplify(plainEn), StringComparison.OrdinalIgnoreCase);
                        _screenTr[en] = same ? null : tr;
                    }
                    int apiCount = pending.Count - fromCache;
                    if (apiCount > 0)
                    {
                        _screenTranslated += apiCount;
                        Log.Info($"[Ekran] {pending.Count} yazı çevrildi ({apiCount} yeni, {engineName}, {ms:F0} ms): " +
                                 string.Join(" | ", pending.Take(8).Select(en => $"{Short(en, 40)} → {Short(_screenTr.GetValueOrDefault(en) ?? "(aynı)", 40)}")));
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn($"[Ekran] Çeviri hatası: {ex.GetType().Name}: {ex.Message}");
                    break;
                }
                finally
                {
                    foreach (var it in _screenItems) it.Translating = false;
                }

                foreach (var it in _screenItems) ApplyKnownTranslation(it);
                PushScreen();
            }
        }
        finally
        {
            _screenTrBusy = false;
        }
    }

    private static string Simplify(string s) => new(s.Where(char.IsLetterOrDigit).ToArray());
    private static string Short(string s, int max = 70) => s.Length > max ? s[..max] + "…" : s;

    private static bool IsShowable(ScreenItem i) => i.Turkish != null && !i.Hidden && !i.NoTranslation;

    /// <summary>
    /// Türkçe yazı boyu (piksel): OCR kutusu harflerin mürekkep yüksekliğidir. BÜYÜK HARF yazıda bu, yazı
    /// boyunun ~%72'si; küçük harfli ve "g, p, y" gibi aşağı uzayan harfli yazıda ~%95'idir.
    /// </summary>
    private static double FontPxFor(ScreenItem i)
    {
        string text = i.Block.Text;
        bool hasLower = text.Any(char.IsLower);
        bool hasDesc = text.IndexOfAny("gjpqy".ToCharArray()) >= 0;
        double factor = !hasLower ? 1.35 : hasDesc ? 1.02 : 1.28;
        return i.Block.LineHeight * factor;
    }

    /// <summary>
    /// Bir öğenin çizim parçaları. Satır içinde tuş simgesi varsa Türkçe, simgelerin arasındaki İngilizce kelime
    /// gruplarının yerine parça parça yazılır: simgeden önceki parça simgeye sağdan dayanır (sola doğru büyür),
    /// sonraki parça simgeye soldan dayanır (sağa doğru büyür). Simge hiç örtülmez.
    /// Çok satırlı ipucu kutularında (ya da çeviri yer tutucuyu kaybettiyse) simgenin birebir kopyası Türkçe
    /// metnin içinde, kelimenin yanında gösterilir.
    /// </summary>
    private static List<ScreenPiece> BuildPieces(ScreenItem it)
    {
        var pieces = new List<ScreenPiece>();
        var b = it.Block;
        double W = it.FrameW, H = it.FrameH;
        double fs = FontPxFor(it) / H;
        string tr = it.Turkish!;
        int k = b.Icons.Count;

        var ids = TagRx.Matches(tr).Select(m => int.Parse(m.Groups[1].Value)).ToList();
        string[] parts = TagRx.Split(tr).Where((_, idx) => idx % 2 == 0).ToArray(); // Split, yakalanan grubu da döndürür
        bool ordered = ids.SequenceEqual(Enumerable.Range(1, k));

        // Yazı ekranın hangi yanında? (sağdaki tuş listesi sağa, soldaki ipucu kutusu sola yaslanır)
        bool rightSide = (b.Bounds.X + b.Bounds.Width / 2.0) > W / 2;

        if (b.LineCount == 1 && k > 0 && ordered && parts.Length == k + 1 && b.Runs.Count == k + 1)
        {
            for (int i = 0; i <= k; i++)
            {
                var run = b.Runs[i];
                string text = WebUtility.HtmlDecode(parts[i]).Trim();
                if (text.Length == 0) text = " ";
                char align = i == 0 ? 'r' : 'l';
                pieces.Add(new ScreenPiece(run.X / W, run.Y / H, run.W / W, run.H / H, fs, text, align, false, 0, Array.Empty<string>()));
            }
            return pieces;
        }

        // Tek parça: simgeler metnin içinde kopya olarak (\u0001 + sıra)
        var icons = new List<string>();
        string body;
        if (k == 0)
        {
            body = WebUtility.HtmlDecode(TagRx.Replace(tr, " ")).Trim();
        }
        else
        {
            var sb = new StringBuilder();
            int last = 0;
            foreach (Match m in TagRx.Matches(tr))
            {
                sb.Append(WebUtility.HtmlDecode(tr[last..m.Index]));
                int id = int.Parse(m.Groups[1].Value);
                if (id >= 1 && id <= it.IconPngs.Length && it.IconPngs[id - 1].Length > 0)
                {
                    sb.Append('\u0001').Append((char)('0' + icons.Count));
                    icons.Add(it.IconPngs[id - 1]);
                }
                last = m.Index + m.Length;
            }
            sb.Append(WebUtility.HtmlDecode(tr[last..]));
            // Çeviri yer tutucuyu kaybettiyse simgeleri sona ekle (hangi tuş olduğu kaybolmasın)
            for (int i = 0; i < it.IconPngs.Length && icons.Count < 10; i++)
                if (!ids.Contains(i + 1) && it.IconPngs[i].Length > 0)
                {
                    sb.Append(" \u0001").Append((char)('0' + icons.Count));
                    icons.Add(it.IconPngs[i]);
                }
            body = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        }

        bool wrap = b.LineCount > 1;
        char al = !wrap && (b.SideIcon == 1 || (b.SideIcon == 0 && rightSide)) ? 'r' : 'l';
        pieces.Add(new ScreenPiece(b.Bounds.X / W, b.Bounds.Y / H, b.Bounds.Width / W, b.Bounds.Height / H, fs, body, al, wrap,
            wrap ? b.Bounds.Width * 1.15 / W : 0, icons.ToArray()));
        return pieces;
    }

    /// <summary>Gösterilecek öğeleri PC katmanına ve TV'ye gönderir (değişiklik yoksa göndermez).</summary>
    private void PushScreen(bool force = false)
    {
        bool muted = !_settings.SubtitleWindowEnabled;
        var show = ScreenOn && !muted && !_menuOpen ? _screenItems.Where(IsShowable).Where(i => i.FrameW > 0).ToList() : new List<ScreenItem>();

        string key = _lastGameScreenPx + "|" + string.Join("|", show.Select(i => $"{i.Px}:{i.Turkish}"));
        if (!force && key == _lastScreenKey) return;
        _lastScreenKey = key;

        var pieces = show.SelectMany(BuildPieces).ToList();
        _shownPieces = pieces;

        // PC
        if (pieces.Count > 0 && _screenWin == null) _screenWin = new ScreenOverlayWindow();
        _screenWin?.SetPieces(_lastGameScreenPx, pieces);

        // TV / telefon
        _server?.Screen(pieces);
    }

    private void LogScreenSummary()
    {
        if (_screenScans == 0) return;
        Log.Info($"Oyun içi yazı çevirisi özeti: {_screenScans} tarama (ortalama OCR {_screenOcrMsSum / _screenScans:F0} ms), " +
                 $"{_screenTranslated} yeni yazı çevrildi, {_screenTr.Count} yazı hafızada.");
        _screenScans = 0;
        _screenOcrMsSum = 0;
        _screenTranslated = 0;
    }
}
