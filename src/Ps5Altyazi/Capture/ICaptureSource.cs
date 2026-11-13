using System;

namespace Ps5Altyazi.Capture;

/// <summary>
/// Yakalanan tek kare. Piksel biçimi: BGRA 32 bit, üstten alta satırlar.
/// OriginX/OriginY: karenin sol üst köşesinin ekran koordinatı (oyun alanını hesaplamak için).
/// </summary>
public sealed record CapturedFrame(byte[] Pixels, int Width, int Height, int Stride, DateTime Timestamp, string Method,
    int OriginX = 0, int OriginY = 0);

public interface ICaptureSource : IDisposable
{
    string Name { get; }

    /// <summary>Yeni kare geldiğinde arka plan iş parçacığından tetiklenir.</summary>
    event Action<CapturedFrame>? FrameArrived;

    /// <summary>Yakalama kendiliğinden durduğunda (pencere kapandı vb.) tetiklenir.</summary>
    event Action<string>? Stopped;

    void Start(IntPtr hwnd, int maxFps);
}
