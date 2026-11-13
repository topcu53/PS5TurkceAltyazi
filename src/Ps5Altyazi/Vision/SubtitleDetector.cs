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

            void Visit(int j)
            {
                if (dil[j] && !seen[j]) { seen[j] = true; queue[tail++] = j; }
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
