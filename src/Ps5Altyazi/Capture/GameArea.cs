using System;
using Ps5Altyazi.Vision;

namespace Ps5Altyazi.Capture;

/// <summary>
/// Yakalanan pencere karesinden oyun görüntüsünün bulunduğu alanı hesaplar:
/// 1) Başlık çubuğu ve kenarlıklar atılır (pencerenin istemci alanı).
/// 2) PS Remote Play görüntüyü 16:9 olarak ortalar; kalan siyah bantlar atılır.
/// </summary>
public static class GameArea
{
    public const double Aspect = 16.0 / 9.0;

    public static PixelRect Compute(IntPtr hwnd, CapturedFrame f, bool cropToGame)
    {
        var full = new PixelRect(0, 0, f.Width, f.Height);
        if (!cropToGame || hwnd == IntPtr.Zero) return full;

        try
        {
            if (!NativeMethods.IsWindow(hwnd)) return full;
            if (!NativeMethods.GetClientRect(hwnd, out var rc)) return full;
            var pt = new NativeMethods.POINT { X = 0, Y = 0 };
            if (!NativeMethods.ClientToScreen(hwnd, ref pt)) return full;

            var client = new PixelRect(pt.X - f.OriginX, pt.Y - f.OriginY, rc.Width, rc.Height).ClampTo(f.Width, f.Height);
            if (client.Width < 64 || client.Height < 64) return full;
            return FitAspect(client, Aspect);
        }
        catch
        {
            return full;
        }
    }

    public static PixelRect FitAspect(PixelRect c, double aspect)
    {
        double ar = (double)c.Width / c.Height;
        if (ar > aspect)
        {
            int w = (int)Math.Round(c.Height * aspect);
            return new PixelRect(c.X + (c.Width - w) / 2, c.Y, w, c.Height);
        }
        else
        {
            int h = (int)Math.Round(c.Width / aspect);
            return new PixelRect(c.X, c.Y + (c.Height - h) / 2, c.Width, h);
        }
    }
}
