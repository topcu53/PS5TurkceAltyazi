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
        public int SameText = 1;      // aynı yazı üst üste kaç taramada birebir okundu (yarım okumalar çevrilmesin)
        public long LastScan;
        public bool Hidden;           // yazı değişti / kayboldu, sonraki taramayı bekliyor
        public bool[] Mask = Array.Empty<bool>();
        public int MaskCount;
        public int MaskBad;           // üst üste kaç karede yazı değişmiş göründü (titremesin diye hemen gizlenmez)
        public string[] IconPngs = Array.Empty<string>();
        public int FrameW, FrameH;
    }

    private const string ScreenContext =
        "Video game HUD text during gameplay: button prompts and interaction options (e.g. Greet, Antagonize, Press to skip), " +
        "mission objectives and hints. Translate briefly, as in a game's Turkish localization.";

    private static readonly Regex TagRx = new("<x id=\"(\\d+)\"\\s*/>", RegexOptions.Compiled);

    private static readonly System.Globalization.CultureInfo TrCulture = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");

    private enum CaseStyle { Normal, Upper, Title }

    private static CaseStyle StyleOf(string tagged)
    {
        var words = TagRx.Replace(tagged, " ").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string(w.Where(char.IsLetter).ToArray())).Where(w => w.Length > 0).ToList();
        if (words.Count == 0) return CaseStyle.Normal;
        if (words.All(w => w.All(char.IsUpper)) && words.Sum(w => w.Length) >= 4) return CaseStyle.Upper;
        int title = words.Count(w => char.IsUpper(w[0]));
        return words.Count >= 2 && title >= words.Count * 0.8 ? CaseStyle.Title : CaseStyle.Normal;
    }

    /// <summary>Etiketlerin dışındaki metne dönüşüm uygular (&lt;x id="1"/&gt; bozulmasın).</summary>
    private static string MapOutsideTags(string tagged, Func<string, string> f)
    {
        var sb = new StringBuilder();
        int last = 0;
        foreach (Match m in TagRx.Matches(tagged))
        {
            sb.Append(f(tagged[last..m.Index])).Append(m.Value);
            last = m.Index + m.Length;
        }
        sb.Append(f(tagged[last..]));
        return sb.ToString();
    }

    private static string NormalizeCase(string tagged)
    {
        if (StyleOf(tagged) == CaseStyle.Normal) return tagged;
        string lower = MapOutsideTags(tagged, t => Regex.Replace(t.ToLowerInvariant(), @"\bi\b", "I"));
        int i = 0;
        while (i < lower.Length && !char.IsLetter(lower[i])) { if (lower[i] == '<') { int e = lower.IndexOf('>', i); i = e < 0 ? lower.Length : e + 1; } else i++; }
        return i < lower.Length ? lower[..i] + char.ToUpperInvariant(lower[i]) + lower[(i + 1)..] : lower;
    }

    private static string RestoreCase(string originalTagged, string tr)
    {
        switch (StyleOf(originalTagged))
        {
            case CaseStyle.Upper:
                return MapOutsideTags(tr, t => t.ToUpper(TrCulture));
            case CaseStyle.Title:
                return MapOutsideTags(tr, t => Regex.Replace(t, @"(^|[\s\-(""'])(\p{Ll})",
                    m => m.Groups[1].Value + m.Groups[2].Value.ToUpper(TrCulture)));
            default:
                return tr;
        }
    }

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
        _settings.ScreenTranslateEnabled = false; // oyun içi yazı çevirisi şimdilik rafta
        ScreenCheck.IsChecked = _settings.ScreenTranslateEnabled;
        if (_settings.ScreenTranslateEnabled)
            Log.Info("Oyun içi yazıları sürekli çevirme AÇIK (deneysel). Ekranı bir kez çevirmek için F7.");
        if (!EnglishWords.IsLoaded) Log.Warn("İngilizce kelime listesi yüklenemedi; OCR düzeltmesi yapılmayacak.");
    }

    private void ScreenCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        SetScreenTranslation(ScreenCheck.IsChecked == true, "kutu");
    }

    /// <summary>F7: oyun içi yazı çevirisini aç / kapat.</summary>
    private void ToggleScreenTranslation() => ToggleScreenSnapshot();

    private void SetScreenTranslation(bool on, string source)
    {
        if (_settings.ScreenTranslateEnabled == on && ScreenCheck.IsChecked == on) return;
        _settings.ScreenTranslateEnabled = on;
        SaveSettings();
        if (ScreenCheck.IsChecked != on) ScreenCheck.IsChecked = on;
        if (!on) ClearScreenItems();
        _thumbAtScan = null;
        Notify(on ? "Oyun içi yazılar sürekli çevriliyor (deneysel)" : "Sürekli çeviri kapalı – ekranı çevirmek için F7");
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
        if (_snapItems != null) { WatchSnapshot(r.Game); return; }
        if (!ScreenOn || _snapCts != null || _ocr is not WindowsOcrEngine) return;
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
            bool gone = count < it.MaskCount * 0.35 || ScreenTextReader.MaskDistance(mask, it.Mask) > 0.4;
            // Arkadaki sahne (at, ışık) hareket ettikçe maske oynar: ancak ~0,1 sn boyunca değişik kalırsa gizle
            it.MaskBad = gone ? it.MaskBad + 1 : 0;
            if (it.MaskBad >= 6)
            {
                it.Hidden = true;
                it.MaskBad = 0;
                changed = true;
                _screenForceScan = true;
            }
            else if (gone) _screenForceScan = true; // değişmiş olabilir: yeniden okuyup doğrula
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
            // Yanında tuş simgesi olan yazılar (Fırçala □, Besle ✚ ...) tuş ipucu listesidir, menü sayılmaz
            var plain = res.Blocks.Where(b => b.SideIcon == 0 && b.Icons.Count == 0).ToList();
            // Alt alta, aynı kenara yaslı kısa yazılar (RDR2: Bilgi göster / Eyeri çıkar / Fırçala ...) tuş listesidir
            foreach (var col in PromptColumns(plain, game.Width)) plain.RemoveAll(col.Contains);
            int big = plain.Count(b => b.LineHeight >= 0.03 * game.Height);
            bool menu = plain.Count >= 7 || big >= 4;
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

    /// <summary>Sağ ya da sol kenarı aynı hizada, kısa (≤ 4 kelime) en az 3 satırlık yazı sütunları.</summary>
    private static IEnumerable<List<ScreenTextBlock>> PromptColumns(List<ScreenTextBlock> blocks, int W)
    {
        var shortOnes = blocks.Where(b => b.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 4).ToList();
        double tol = 0.02 * W;
        foreach (Func<ScreenTextBlock, double> edge in new Func<ScreenTextBlock, double>[] { b => b.Bounds.Right, b => b.Bounds.X })
        {
            var used = new HashSet<ScreenTextBlock>();
            foreach (var a in shortOnes)
            {
                if (used.Contains(a)) continue;
                var col = shortOnes.Where(o => !used.Contains(o) && Math.Abs(edge(o) - edge(a)) <= tol).ToList();
                if (col.Count >= 3)
                {
                    used.UnionWith(col);
                    yield return col;
                }
            }
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
                bool sameText = b.Tagged == item.English;
                if (sameText)
                {
                    item.Seen++;
                    item.SameText++;
                    item.Block = b;
                }
                else if (sim < 0.5)
                {
                    // Gerçekten başka yazı geldi (Greet → Defuse) → yeniden çevir
                    item.Seen = 1;
                    item.SameText = 1;
                    item.Turkish = null;
                    item.NoTranslation = false;
                    item.Block = b;
                }
                else
                {
                    // Aynı yazının farklı okunuşu ("ILT ACTION RIFLE" / "RIFLE FROM HORSE"): çeviri varsa koru.
                    // Ancak yeni okuma daha uzun ve en az o kadar anlamlıysa (yarım okuma tamamlandıysa) onu benimse.
                    item.Seen++;
                    bool better = b.Text.Length > item.Block.Text.Length + 2 && b.Known >= item.Block.Known;
                    if (item.Turkish == null || better)
                    {
                        item.SameText = 1;
                        item.Turkish = null;
                        item.NoTranslation = false;
                        item.Block = b;
                    }
                    // değilse eski (tam) okumayı ve kutusunu koru: yarım okuma kutuyu küçültüp İngilizceyi açığa çıkarmasın
                }
            }
            else
            {
                item = new ScreenItem { Block = b };
                _screenItems.Add(item);
            }

            item.LastScan = scan;
            (item.Mask, item.MaskCount) = ScreenTextReader.TextMask(game, item.Px);
            item.IconPngs = item.Block.Icons.Select(ic => IconPng(game, ic)).ToArray();
            item.FrameW = game.Width;
            item.FrameH = game.Height;
            matched.Add(item);
        }

        // Bu taramada görülmeyenler: bir kez görülmezse gizle, iki kez görülmezse kaldır
        // OCR bir taramada satırı okuyamayabilir: yazı ekranda hâlâ duruyorsa (maske tutuyor) gizleme → titreme olmaz
        foreach (var it in _screenItems.Where(i => !matched.Contains(i)).ToList())
        {
            it.Missed++;
            bool stillThere = false;
            if (it.MaskCount >= 4 && it.FrameW == game.Width && it.FrameH == game.Height)
            {
                var (mask, count) = ScreenTextReader.TextMask(game, it.Px);
                stillThere = count >= it.MaskCount * 0.6 && ScreenTextReader.MaskDistance(mask, it.Mask) <= 0.25;
            }
            if (!stillThere) it.Hidden = true;
            if (it.Missed >= (stillThere ? 3 : 2) || (it.Seen < 2 && !stillThere)) _screenItems.Remove(it);
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
                                i.SameText >= 2) // aynı yazı iki kez birebir okunmadan çevirme (yarım / hatalı okumalar kota yemesin, titremesin)
                    .Select(i => i.English).Distinct().Take(50).ToList();
                if (pending.Count == 0) break;
                attempted.UnionWith(pending);
                foreach (var it in _screenItems.Where(i => pending.Contains(i.English))) it.Translating = true;

                try
                {
                    // BÜYÜK HARF / Her Kelimesi Büyük yazılar (RDR2 küçük büyük harfleri) çeviriyi bozar ("Bolt Action" → "Ilt Eylem"):
                    // cümle düzenine çevirip gönder, sonucu oyundaki görünüme geri çevir
                    var request = pending.Select(NormalizeCase).ToList();
                    var (results, fromCache, engineName, ms) = await service.TranslateManyAsync(request, ScreenContext, CancellationToken.None);
                    for (int k = 0; k < pending.Count; k++)
                    {
                        string en = pending[k];
                        string? tr = results[k]?.Trim();
                        if (tr == null) continue; // çevrilemedi → sonraki taramada tekrar denenir
                        tr = RestoreCase(en, tr);
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
        // OCR kelime kutusu yazıdan biraz yüksek çıkar: Türkçe, İngilizceden iri görünmesin (komşu satırları kapatmasın)
        double factor = !hasLower ? 1.12 : hasDesc ? 0.92 : 1.08;
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
                double pr = run.H * 0.10; // büyük ilk harfler / alt çizgiler kutudan taşmasın
                pieces.Add(new ScreenPiece(run.X / W, (run.Y - pr) / H, run.W / W, (run.H + 2 * pr) / H, fs, text, align, false, 0, Array.Empty<string>()));
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
        double pad = b.LineHeight * 0.10; // İngilizce yazının üst/alt kenarı Türkçe kutunun dışında kalmasın
        pieces.Add(new ScreenPiece(b.Bounds.X / W, (b.Bounds.Y - pad) / H, b.Bounds.Width / W, (b.Bounds.Height + 2 * pad) / H, fs, body, al, wrap,
            wrap ? b.Bounds.Width * 1.15 / W : 0, icons.ToArray()));
        return pieces;
    }

    /// <summary>Gösterilecek öğeleri PC katmanına ve TV'ye gönderir (değişiklik yoksa göndermez).</summary>
    private void PushScreen(bool force = false)
    {
        bool muted = !_settings.SubtitleWindowEnabled;
        var show = muted ? new List<ScreenItem>()
            : _snapItems != null ? _snapItems.ToList()                          // F7 ile anlık çevrilmiş ekran (sabit)
            : ScreenOn && !_menuOpen ? _screenItems.Where(IsShowable).Where(i => i.FrameW > 0).ToList()
            : new List<ScreenItem>();

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
