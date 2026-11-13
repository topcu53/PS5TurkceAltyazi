using System;
using Ps5Altyazi.Capture;

namespace Ps5Altyazi.Vision;

public static class FrameOps
{
    /// <summary>Karenin bir bölümünü yeni bir kare olarak kopyalar.</summary>
    public static CapturedFrame Crop(CapturedFrame f, PixelRect r)
    {
        r = r.ClampTo(f.Width, f.Height);
        if (r.X == 0 && r.Y == 0 && r.Width == f.Width && r.Height == f.Height) return f;
        if (r.IsEmpty) throw new ArgumentException("Kırpma alanı boş.");

        int rowBytes = r.Width * 4;
        var dst = new byte[rowBytes * r.Height];
        for (int y = 0; y < r.Height; y++)
            Buffer.BlockCopy(f.Pixels, (r.Y + y) * f.Stride + r.X * 4, dst, y * rowBytes, rowBytes);

        return f with
        {
            Pixels = dst,
            Width = r.Width,
            Height = r.Height,
            Stride = rowBytes,
            OriginX = f.OriginX + r.X,
            OriginY = f.OriginY + r.Y
        };
    }

    /// <summary>Metin maskesini görüntülenebilir BGR32 piksellere çevirir (metin beyaz, arka plan koyu).</summary>
    public static byte[] MaskToBgr32(bool[] mask, int width, int height)
    {
        var px = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            byte v = mask[i] ? (byte)255 : (byte)28;
            int j = i * 4;
            px[j] = v; px[j + 1] = v; px[j + 2] = v; px[j + 3] = 255;
        }
        return px;
    }
}
