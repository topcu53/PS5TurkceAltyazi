using System;
using System.Collections.Generic;
using System.Linq;
using Ps5Altyazi.Capture;

namespace Ps5Altyazi.Vision;

public sealed record TextLine(PixelRect Bounds, int PixelCount, double Fill);

public sealed record TextScan(PixelRect Search, bool[]? Mask, List<TextLine> Lines)
{
    public bool HasText => Lines.Count > 0;
}

public sealed record AutoDetectResult(PixelRect TextBounds, int LineCount, NormRect SuggestedRegion);

/// <summary>
/// Altyazı benzeri metni bulur: koyu zemin üzerinde açık renkli, renksiz (beyaz/gri), ince çizgili,
/// yatay olarak dizilmiş karakter grupları. OCR kullanmaz; hızlı bir ön tespittir.
///
/// Eşikler RDR2 örnek karesi (1767x987) ve küçültülmüş (960x536) sürümüyle denendi:
/// altyazı bulundu, altyazısız karede mini harita / tuş ipuçları yanlış pozitif vermedi.
/// </summary>
public static class SubtitleDetector
{
    public const int BrightThreshold = 120;   // metin pikseli en az bu parlaklıkta
    public const int ContrastThreshold = 70;  // yakınında en az bu kadar daha koyu piksel olmalı
    public const int MaxSaturation = 70;      // renkli (HUD simgeleri vb.) pikselleri ele

    /// <summary>
    /// <paramref name="f"/> oyun alanı karesidir; <paramref name="search"/> bu karenin içinde aranacak bölge.
    /// centerX: verilirse satırın yatay merkezi bu noktaya en fazla centerTol*W uzaklıkta olmalı
    /// (altyazılar ortalıdır; arka plandaki parlak dokuları elemek için).
    /// </summary>
    public static TextScan Scan(CapturedFrame f, PixelRect search, double? centerX, double centerTol, double minWidthFrac, bool keepMask,
        double minAspect = 3.0)
    {
        int W = f.Width, H = f.Height;
        search = search.ClampTo(W, H);
        var lines = new List<TextLine>();
        int sw = search.Width, sh = search.Height;
        if (sw < 8 || sh < 8) return new TextScan(search, keepMask ? new bool[Math.Max(0, sw * sh)] : null, lines);

        int n = sw * sh;
        byte[] p = f.Pixels;
        int stride = f.Stride;
        var lum = new byte[n];
        var satOk = new bool[n];

        for (int y = 0; y < sh; y++)
        {
            int src = (search.Y + y) * stride + search.X * 4;
            int dst = y * sw;
            for (int x = 0; x < sw; x++, src += 4)
            {
                int b = p[src], g = p[src + 1], r = p[src + 2];
                lum[dst + x] = (byte)((299 * r + 587 * g + 114 * b) / 1000);
                int mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
                satOk[dst + x] = mx - mn <= MaxSaturation;
            }
        }

        // Yatay ve dikey minimum (artı biçimli komşuluk)
        int rad = Math.Max(2, (int)Math.Round(H / 300.0));
        var minH = new byte[n];
        var minV = new byte[n];
        for (int y = 0; y < sh; y++)
        {
            int row = y * sw;
            for (int x = 0; x < sw; x++)
            {
                byte m = 255;
                int a = Math.Max(0, x - rad), e = Math.Min(sw - 1, x + rad);
                for (int k = a; k <= e; k++) if (lum[row + k] < m) m = lum[row + k];
                minH[row + x] = m;
            }
        }
        for (int x = 0; x < sw; x++)
        {
            for (int y = 0; y < sh; y++)
            {
                byte m = 255;
                int a = Math.Max(0, y - rad), e = Math.Min(sh - 1, y + rad);
                for (int k = a; k <= e; k++) { byte v = lum[k * sw + x]; if (v < m) m = v; }
                minV[y * sw + x] = m;
            }
        }

        var mask = new bool[n];
        for (int i = 0; i < n; i++)
        {
            int l = lum[i];
            mask[i] = l >= BrightThreshold && satOk[i] && Math.Min(minH[i], minV[i]) <= l - ContrastThreshold;
        }

        // Hücre ızgarası
        int cs = Math.Max(2, (int)Math.Round(H / 270.0));
        int gw = sw / cs, gh = sh / cs;
        if (gw < 2 || gh < 1) return new TextScan(search, keepMask ? mask : null, lines);

        var cells = new int[gw * gh];
        for (int y = 0; y < gh * cs; y++)
        {
            int row = y * sw, crow = (y / cs) * gw;
            for (int x = 0; x < gw * cs; x++)
                if (mask[row + x]) cells[crow + x / cs]++;
        }

        // Harfleri / kelimeleri birleştirmek için yatay genişletme
        int kx = Math.Max(2, (int)Math.Round(0.012 * W / cs));
        var dil = new bool[gw * gh];
        for (int gy = 0; gy < gh; gy++)
        {
            int row = gy * gw;
            for (int gx = 0; gx < gw; gx++)
            {
                if (cells[row + gx] == 0) continue;
                int a = Math.Max(0, gx - kx), e = Math.Min(gw - 1, gx + kx);
                for (int k = a; k <= e; k++) dil[row + k] = true;
            }
        }

        // Bağlı bileşenler (4-komşuluk)
        var seen = new bool[gw * gh];
        var queue = new int[gw * gh];
        for (int start = 0; start < gw * gh; start++)
        {
            if (!dil[start] || seen[start]) continue;
            int head = 0, tail = 0;
            queue[tail++] = start;
            seen[start] = true;
            int t = int.MaxValue, bt = -1, l = int.MaxValue, rt = -1, cnt = 0, act = 0;
            while (head < tail)
            {
                int idx = queue[head++];
                int cy = idx / gw, cx = idx % gw;
                if (cy < t) t = cy; if (cy > bt) bt = cy;
                if (cx < l) l = cx; if (cx > rt) rt = cx;
                if (cells[idx] > 0) { cnt += cells[idx]; act++; }

                if (cx > 0) Visit(idx - 1);
                if (cx < gw - 1) Visit(idx + 1);
                if (cy > 0) Visit(idx - gw);
                if (cy < gh - 1) Visit(idx + gw);
            }

            int top = search.Y + t * cs, bottom = search.Y + (bt + 1) * cs;
            int left = search.X + l * cs, right = search.X + (rt + 1) * cs;
            int hh = bottom - top, ww = right - left;
            double fill = (double)act / ((bt - t + 1) * (rt - l + 1));
            double lineCenter = (left + right) / 2.0;

            bool ok = cnt >= 20
                      && hh >= 0.012 * H && hh <= 0.07 * H
                      && ww >= minWidthFrac * W
                      && ww >= minAspect * hh
                      && fill >= 0.12 && fill <= 0.85
                      && (centerX is not { } cx0 || Math.Abs(lineCenter - cx0) <= centerTol * W);
            if (ok) lines.Add(new TextLine(PixelRect.FromLTRB(left, top, right, bottom), cnt, fill));
            else if (hh > 0.07 * H && hh <= 0.24 * H && cnt >= 40)
            {
                // Üst üste duran iki (ya da üç) altyazı satırı (aynı anda konuşan iki kişi) birbirine değince tek
                // ve çok yüksek bir parça oluyordu → tamamen eleniyordu, yalnız tek satırlık altyazılar okunuyordu.
                // Satır satır böl: yatay izdüşümde boş / seyrek satırlardan ayır.
                SplitBlock(t, bt, l, rt);
            }

            void Visit(int j)
            {
                if (dil[j] && !seen[j]) { seen[j] = true; queue[tail++] = j; }
            }
        }

        void SplitBlock(int t0, int b0, int l0, int r0)
        {
            int rows = b0 - t0 + 1;
            var act = new int[rows];
            int max = 0;
            for (int gy = t0; gy <= b0; gy++)
            {
                int c = 0;
                for (int gx = l0; gx <= r0; gx++) if (cells[gy * gw + gx] > 0) c++;
                act[gy - t0] = c;
                if (c > max) max = c;
            }
            if (max == 0) return;
            int thr = Math.Max(1, (int)(max * 0.12));
            int expH = Math.Max(3, (int)Math.Round(0.045 * H / cs)); // tek satırın beklenen yüksekliği (hücre)
            int y = 0;
            while (y < rows)
            {
                while (y < rows && act[y] < thr) y++;
                if (y >= rows) break;
                int s0 = y;
                while (y < rows && act[y] >= thr) y++;
                int s1 = y - 1;
                // Boşluksuz bitişik satırlar: beklenen satır yüksekliğine göre en seyrek satırlardan böl
                int segStart = s0;
                while (s1 - segStart + 1 > (int)(expH * 1.6))
                {
                    int best = -1, bestV = int.MaxValue;
                    for (int k = segStart + (int)(expH * 0.6); k <= Math.Min(s1 - 2, segStart + (int)(expH * 1.4)); k++)
                        if (act[k] < bestV) { bestV = act[k]; best = k; }
                    if (best < 0) break;
                    AddPart(segStart, best - 1);
                    segStart = best + 1;
                }
                AddPart(segStart, s1);
            }

            void AddPart(int r0s, int r1s)
            {
                if (r1s < r0s) return;
                int pl = int.MaxValue, pr = -1, cnt2 = 0, act2 = 0;
                for (int gy = t0 + r0s; gy <= t0 + r1s; gy++)
                    for (int gx = l0; gx <= r0; gx++)
                    {
                        int v = cells[gy * gw + gx];
                        if (v == 0) continue;
                        cnt2 += v; act2++;
                        if (gx < pl) pl = gx;
                        if (gx > pr) pr = gx;
                    }
                if (pr < 0) return;
                int top2 = search.Y + (t0 + r0s) * cs, bottom2 = search.Y + (t0 + r1s + 1) * cs;
                int left2 = search.X + pl * cs, right2 = search.X + (pr + 1) * cs;
                int h2 = bottom2 - top2, w2 = right2 - left2;
                double fill2 = (double)act2 / ((r1s - r0s + 1) * (pr - pl + 1));
                double center2 = (left2 + right2) / 2.0;
                bool ok2 = cnt2 >= 20
                           && h2 >= 0.012 * H && h2 <= 0.07 * H
                           && w2 >= minWidthFrac * W
                           && w2 >= minAspect * h2
                           && fill2 >= 0.10 && fill2 <= 0.9
                           && (centerX is not { } cx1 || Math.Abs(center2 - cx1) <= centerTol * W);
                if (ok2) lines.Add(new TextLine(PixelRect.FromLTRB(left2, top2, right2, bottom2), cnt2, fill2));
            }
        }

        return new TextScan(search, keepMask ? mask : null, lines);
    }

    /// <summary>Oyun karesinin alt yarısında ortalanmış altyazıyı arar ve önerilen altyazı alanını döndürür.</summary>
    public static AutoDetectResult? AutoDetect(CapturedFrame f)
    {
        int W = f.Width, H = f.Height;
        var search = new PixelRect((int)(W * 0.05), (int)(H * 0.50), (int)(W * 0.90), (int)(H * 0.48));
        var scan = Scan(f, search, centerX: W / 2.0, centerTol: 0.10, minWidthFrac: 0.05, keepMask: false);
        if (!scan.HasText) return null;

        var best = scan.Lines.OrderByDescending(x => x.PixelCount).First();
        PixelRect text = best.Bounds;
        int lineH = best.Bounds.Height;
        int count = 1;
        foreach (var other in scan.Lines.OrderBy(x => Math.Abs(x.Bounds.Y - best.Bounds.Y)))
        {
            if (other == best) continue;
            int gap = Math.Max(other.Bounds.Y - text.Bottom, text.Y - other.Bounds.Bottom);
            if (gap <= lineH) { text = text.Union(other.Bounds); count++; }
        }

        // Altyazı uzunluğu ve satır sayısı değişebileceği için cömert bir alan öner:
        // yatayda en az %20–%80, dikeyde hem üstte hem altta ~1,6 satır pay (iki satırlık altyazılar).
        int left = (int)Math.Min(text.X - 2 * lineH, W * 0.20);
        int right = (int)Math.Max(text.Right + 2 * lineH, W * 0.80);
        int top = (int)(text.Y - 1.6 * lineH);
        int bottom = (int)(text.Bottom + 1.6 * lineH);
        var region = PixelRect.FromLTRB(left, top, right, bottom).ClampTo(W, H);

        return new AutoDetectResult(text, count, NormRect.FromPixels(region, W, H));
    }
}
