using System;

namespace Ps5Altyazi.Capture;

public enum FrameVerdict
{
    Gecerli,   // Ayrıntılı, değişen görüntü
    Siyah,     // Tamamen / neredeyse siyah
    TekRenk,   // Düz renk, boş kare
    Donmus,    // Belirli süredir hiç değişmiyor
    CokKucuk   // Pencere çok küçük / geçersiz
}

public sealed record FrameAnalysis(
    FrameVerdict Verdict,
    double MeanLuma,
    double StdDev,
    double DarkRatio,
    double EdgeDensity,
    double Motion,
    TimeSpan SinceLastChange);

/// <summary>
/// Yakalanan karenin gerçekten görüntü içerip içermediğini sezgisel olarak denetler:
/// siyah ekran, tek renk / boş kare ve donmuş görüntüyü ayırt eder; hareketi ölçer.
/// Tek bir iş parçacığından çağrılmalıdır.
/// </summary>
public sealed class FrameAnalyzer
{
    private const int ThumbW = 64, ThumbH = 36;
    private double[]? _prevThumb;
    private DateTime _lastChange = DateTime.MinValue;

    public TimeSpan FrozenThreshold { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Küçük resim farkı bu değerin üstündeyse "değişti" sayılır (0-255 ölçeği).</summary>
    public double ChangeThreshold { get; set; } = 0.8;

    public void Reset()
    {
        _prevThumb = null;
        _lastChange = DateTime.MinValue;
    }

    public FrameAnalysis Analyze(CapturedFrame f)
    {
        DateTime now = DateTime.Now;
        if (f.Width < 64 || f.Height < 64)
            return new FrameAnalysis(FrameVerdict.CokKucuk, 0, 0, 0, 0, 0, TimeSpan.Zero);

        byte[] p = f.Pixels;
        int stride = f.Stride;

        // 1) Genel istatistikler (en fazla ~320x180 örnek)
        int stepX = Math.Max(1, f.Width / 320);
        int stepY = Math.Max(1, f.Height / 180);
        double sum = 0, sumSq = 0;
        long n = 0, dark = 0, edges = 0, edgeN = 0;

        for (int y = 0; y < f.Height; y += stepY)
        {
            int row = y * stride;
            double prevL = -1;
            for (int x = 0; x < f.Width; x += stepX)
            {
                int i = row + x * 4;
                double l = 0.114 * p[i] + 0.587 * p[i + 1] + 0.299 * p[i + 2];
                sum += l;
                sumSq += l * l;
                n++;
                if (l < 16) dark++;
                if (prevL >= 0)
                {
                    edgeN++;
                    if (Math.Abs(l - prevL) > 24) edges++;
                }
                prevL = l;
            }
        }

        double mean = sum / n;
        double std = Math.Sqrt(Math.Max(0, sumSq / n - mean * mean));
        double darkRatio = (double)dark / n;
        double edgeDensity = edgeN > 0 ? (double)edges / edgeN : 0;

        // 2) Hareket: 64x36 küçük resim, her hücrede 4 örnek ortalaması
        var thumb = new double[ThumbW * ThumbH];
        double cellW = (double)f.Width / ThumbW, cellH = (double)f.Height / ThumbH;
        for (int ty = 0; ty < ThumbH; ty++)
        {
            for (int tx = 0; tx < ThumbW; tx++)
            {
                double acc = 0;
                for (int s = 0; s < 4; s++)
                {
                    int sx = (int)((tx + 0.25 + 0.5 * (s & 1)) * cellW);
                    int sy = (int)((ty + 0.25 + 0.5 * (s >> 1)) * cellH);
                    sx = Math.Min(sx, f.Width - 1);
                    sy = Math.Min(sy, f.Height - 1);
                    int i = sy * stride + sx * 4;
                    acc += 0.114 * p[i] + 0.587 * p[i + 1] + 0.299 * p[i + 2];
                }
                thumb[ty * ThumbW + tx] = acc / 4;
            }
        }

        double motion = 0;
        if (_prevThumb != null)
        {
            double d = 0;
            for (int i = 0; i < thumb.Length; i++) d += Math.Abs(thumb[i] - _prevThumb[i]);
            motion = d / thumb.Length;
        }

        if (_prevThumb == null || motion > ChangeThreshold) _lastChange = now;
        _prevThumb = thumb;
        TimeSpan sinceChange = now - _lastChange;

        FrameVerdict verdict;
        if (mean < 8 && std < 5) verdict = FrameVerdict.Siyah;
        else if (std < 3) verdict = FrameVerdict.TekRenk;
        else if (sinceChange > FrozenThreshold) verdict = FrameVerdict.Donmus;
        else verdict = FrameVerdict.Gecerli;

        return new FrameAnalysis(verdict, mean, std, darkRatio, edgeDensity, motion, sinceChange);
    }

    public static string Describe(FrameVerdict v) => v switch
    {
        FrameVerdict.Gecerli => "Görüntü geliyor",
        FrameVerdict.Siyah => "Siyah ekran",
        FrameVerdict.TekRenk => "Boş / tek renk kare",
        FrameVerdict.Donmus => "Görüntü sabit",
        FrameVerdict.CokKucuk => "Pencere çok küçük",
        _ => v.ToString()
    };
}
