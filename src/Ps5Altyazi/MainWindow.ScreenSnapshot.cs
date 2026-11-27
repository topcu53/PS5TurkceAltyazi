using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Ps5Altyazi.Capture;
using Ps5Altyazi.Logging;
using Ps5Altyazi.TextRecognition;
using Ps5Altyazi.Vision;

namespace Ps5Altyazi;

/// <summary>
/// F7 = "Ekranı çevir" (anlık): oyunun şu anki karesi bir kez okunur, ekrandaki bütün yazılar (tuş ipuçları, görev,
/// bilgi kutuları…) çevrilip kendi yerlerine konur ve birkaç saniye SABİT kalır. Yeniden F7'ye basınca hemen kalkar.
/// Sürekli takip eden moddaki titreme / gidip gelme bu modda olmaz (okuma tek kare, sonuç değişmez).
/// </summary>
public partial class MainWindow
{
    private List<ScreenItem>? _snapItems;
    private readonly Dictionary<ScreenItem, byte[]> _snapSig = new();

    /// <summary>Kutunun kaba gri görüntüsü (24×4): yazının rengi ne olursa olsun (kırmızı menü, beyaz ipucu) değişimi yakalar.</summary>
    private static byte[] BoxSignature(CapturedFrame f, PixelRect r)
    {
        const int GW = 24, GH = 4;
        var sig = new byte[GW * GH];
        for (int j = 0; j < GH; j++)
        for (int i = 0; i < GW; i++)
        {
            int x0 = r.X + i * r.Width / GW, x1 = Math.Max(x0 + 1, r.X + (i + 1) * r.Width / GW);
            int y0 = r.Y + j * r.Height / GH, y1 = Math.Max(y0 + 1, r.Y + (j + 1) * r.Height / GH);
            long sum = 0; int n = 0;
            for (int y = Math.Max(0, y0); y < Math.Min(f.Height, y1); y += 2)
            for (int x = Math.Max(0, x0); x < Math.Min(f.Width, x1); x += 2)
            {
                int p = y * f.Stride + x * 4;
                sum += (f.Pixels[p] * 29 + f.Pixels[p + 1] * 150 + f.Pixels[p + 2] * 77) >> 8;
                n++;
            }
            sig[j * GW + i] = (byte)(n == 0 ? 0 : sum / n);
        }
        return sig;
    }

    private static double SigDiff(byte[] a, byte[] b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++) s += Math.Abs(a[i] - b[i]);
        return s / a.Length;
    }
    private CancellationTokenSource? _snapCts;
    private DispatcherTimer? _snapTimer;

    private async void ToggleScreenSnapshot()
    {
        // Çeviri sürüyorsa iptal, ekrandaysa kaldır
        if (_snapCts != null) { _snapCts.Cancel(); ClearSnapshot("iptal"); return; }
        if (_snapItems != null) { ClearSnapshot("F7"); return; }

        var game = _last?.Game;
        if (_capture == null || game == null) { Notify("Önce çeviriyi başlatın (F6)"); Log.Warn("Ekranı çevir: yakalama çalışmıyor (F6 ile başlatın)."); return; }
        if (_ocr is not WindowsOcrEngine) { Log.Warn("Ekranı çevir için Windows OCR (İngilizce paket) gerekir."); return; }
        _screenOcr ??= WindowsOcrEngine.Create(out _);
        if (_screenOcr is not { } engine) return;
        var service = _translation;
        if (service == null) { Log.Warn("Ekranı çevir: çeviri servisi yok (Ayarlar → Çeviri)."); return; }

        var cts = new CancellationTokenSource();
        _snapCts = cts;
        Notify("Ekran çevriliyor… (F7)");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            PixelRect? exclude = OcrCheck.IsChecked == true ? _last?.RegionPx : null; // altyazı ayrıca çevriliyor
            var res = await Task.Run(() => ScreenTextReader.ReadAsync(engine, game, exclude, fullFrame: true), cts.Token);
            if (cts.IsCancellationRequested) return;

            // Üst üste binen okumalar (aynı yazı iki kez / iki satırı kapsayan blok): tek satırlıyı ve anlamlıyı tut.
            // Sözlükte olmayan okumalar (oyuncunun koyduğu at adı "Ay Parçası" → "AY PARGASI") çevrilmez.
            var blocks = new List<ScreenTextBlock>();
            foreach (var b in res.Blocks.Where(b => b.Known >= 0.5)
                                        .OrderBy(b => b.LineCount).ThenByDescending(b => b.Known).ThenBy(b => b.Bounds.Y))
                if (!blocks.Any(k => Overlap(k.Bounds, b.Bounds) >= 0.25)) blocks.Add(b);
            var items = blocks.Select(b => new ScreenItem
            {
                Block = b,
                FrameW = game.Width,
                FrameH = game.Height,
                IconPngs = b.Icons.Select(ic => IconPng(game, ic)).ToArray()
            }).ToList();
            // Her yazının "parmak izi": ekran değişince (menü kaydırıldı, ipucu kapandı) o çeviri kaldırılır
            foreach (var it in items) _snapSig[it] = BoxSignature(game, it.Px);
            if (items.Count == 0)
            {
                Notify("Ekranda çevrilecek yazı bulunamadı");
                Log.Info($"[Ekranı çevir] Yazı bulunamadı (OCR {res.Milliseconds:F0} ms).");
                return;
            }

            // Hafızada olanlar hemen, kalanlar tek istekte
            foreach (var it in items) ApplyKnownTranslation(it);
            var pending = items.Where(i => i.Turkish == null && !i.NoTranslation).Select(i => i.English).Distinct().ToList();
            if (pending.Count > 0)
            {
                var request = pending.Select(NormalizeCase).ToList();
                var (results, _, engineName, ms) = await service.TranslateManyAsync(request, ScreenContext, cts.Token);
                if (cts.IsCancellationRequested) return;
                for (int k = 0; k < pending.Count; k++)
                {
                    string? tr = results[k]?.Trim();
                    if (tr == null) continue;
                    tr = RestoreCase(pending[k], tr);
                    bool same = string.Equals(WebUtility.HtmlDecode(TagRx.Replace(tr, "")).Trim(),
                                              WebUtility.HtmlDecode(TagRx.Replace(pending[k], "")).Trim(), StringComparison.OrdinalIgnoreCase);
                    _screenTr[pending[k]] = same ? null : tr;
                }
                foreach (var it in items) ApplyKnownTranslation(it);
            }

            _snapItems = items.Where(IsShowable).ToList();
            int chars = _snapItems.Sum(i => i.Turkish!.Length);
            // Yazı çoksa daha uzun kalır; F7 ile hemen kaldırılabilir
            int showMs = Math.Clamp(7000 + chars * 60, 8000, 40000);
            _lastGameScreenPx = new System.Windows.Rect(game.OriginX, game.OriginY, game.Width, game.Height);
            PushScreen(force: true);

            _snapTimer?.Stop();
            _snapTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(showMs) };
            _snapTimer.Tick += (_, _) => ClearSnapshot("süre doldu");
            _snapTimer.Start();
            Log.Info($"[Ekranı çevir] {_snapItems.Count} yazı çevrildi, {showMs / 1000.0:F0} sn ekranda (toplam {sw.ElapsedMilliseconds} ms). " +
                     string.Join(" | ", _snapItems.Take(12).Select(i => $"{Short(i.Block.Text, 30)} → {Short(i.Turkish!, 30)}")));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error($"Ekran çevrilemedi: {ex.Message}");
            Notify("Ekran çevrilemedi");
        }
        finally
        {
            if (_snapCts == cts) _snapCts = null;
        }
    }

    /// <summary>
    /// Çeviri ekrandayken her karede: yazısı değişen / kaybolan öğe kaldırılır (menü kaydırıldı, ipucu kapandı).
    /// Öğelerin çoğu değiştiyse ekran tamamen değişmiştir → çevirinin hepsi kalkar (yeniden F7).
    /// </summary>
    private void WatchSnapshot(CapturedFrame game)
    {
        if (_snapItems == null || _snapItems.Count == 0) return;
        bool changed = false;
        int gone = 0;
        foreach (var it in _snapItems.ToList())
        {
            if (it.FrameW != game.Width || it.FrameH != game.Height) { ClearSnapshot("pencere değişti"); return; }
            if (!_snapSig.TryGetValue(it, out var sig0)) continue;
            bool diff = SigDiff(BoxSignature(game, it.Px), sig0) > 18; // ortalama 18 gri seviyesinden fazla değişti
            it.MaskBad = diff ? it.MaskBad + 1 : 0;
            if (it.MaskBad >= 4) // ~0,07 sn sürekli farklı: gerçekten değişti
            {
                _snapItems.Remove(it);
                gone++;
                changed = true;
            }
        }
        if (_snapItems.Count == 0 || (gone > 0 && _snapItems.Count <= gone)) { ClearSnapshot("ekran değişti"); return; }
        if (changed) PushScreen(force: true);
    }

    private void ClearSnapshot(string why)
    {
        _snapTimer?.Stop();
        _snapTimer = null;
        if (_snapItems == null) return;
        _snapItems = null;
        _snapSig.Clear();
        PushScreen(force: true);
        if (why == "F7") Notify("Ekran çevirisi kaldırıldı");
    }
}
