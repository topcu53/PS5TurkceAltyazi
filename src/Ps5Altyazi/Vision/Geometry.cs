using System;
using System.Globalization;

namespace Ps5Altyazi.Vision;

/// <summary>Piksel cinsinden dikdörtgen.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static PixelRect FromLTRB(int left, int top, int right, int bottom) =>
        new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));

    public PixelRect ClampTo(int width, int height)
    {
        int l = Math.Clamp(X, 0, width), t = Math.Clamp(Y, 0, height);
        int r = Math.Clamp(Right, 0, width), b = Math.Clamp(Bottom, 0, height);
        return FromLTRB(l, t, r, b);
    }

    public PixelRect Union(PixelRect o) =>
        FromLTRB(Math.Min(X, o.X), Math.Min(Y, o.Y), Math.Max(Right, o.Right), Math.Max(Bottom, o.Bottom));

    public override string ToString() => $"x={X}, y={Y}, {Width}x{Height}";
}

/// <summary>Oyun alanına göre 0–1 aralığında oransal dikdörtgen (pencere boyutu değişse de geçerli kalır).</summary>
public readonly record struct NormRect(double X, double Y, double Width, double Height)
{
    public bool IsValid =>
        Width > 0.01 && Height > 0.005 && X >= 0 && Y >= 0 && X + Width <= 1.0001 && Y + Height <= 1.0001;

    public PixelRect ToPixels(int width, int height) =>
        new PixelRect((int)Math.Round(X * width), (int)Math.Round(Y * height),
                      (int)Math.Round(Width * width), (int)Math.Round(Height * height)).ClampTo(width, height);

    public static NormRect FromPixels(PixelRect r, int width, int height) =>
        new((double)r.X / width, (double)r.Y / height, (double)r.Width / width, (double)r.Height / height);

    public override string ToString() => string.Format(CultureInfo.GetCultureInfo("tr-TR"),
        "sol %{0:F1}, üst %{1:F1}, genişlik %{2:F1}, yükseklik %{3:F1}", X * 100, Y * 100, Width * 100, Height * 100);
}
