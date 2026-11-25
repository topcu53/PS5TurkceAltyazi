using System;
using Ps5Altyazi.Ps5;

namespace Ps5Altyazi.Capture;

/// <summary>
/// Görüntüyü PS Remote Play penceresinden değil, PS5'ten doğrudan alır (ps5-baglanti yardımcısı üzerinden).
/// Ekranda pencere açılmaz; PS5 dinlenmedeyse uyandırılır, bağlantı koparsa kendiliğinden yeniden bağlanılır.
/// Ses ve kol girdisi kapalıdır: oyunu TV'de, kendi kolunuzla oynamaya devam edersiniz.
/// </summary>
public sealed class Ps5DirectCaptureSource : ICaptureSource
{
    private readonly Ps5Connection _conn;

    public Ps5DirectCaptureSource(Ps5Pairing pairing, byte[] accountId, string? passcode, int fps)
    {
        _conn = new Ps5Connection(pairing, accountId, passcode, fps);
        _conn.FrameReceived += (px, w, h) =>
            FrameArrived?.Invoke(new CapturedFrame(px, w, h, w * 4, DateTime.Now, Name));
        _conn.StateChanged += (s, t) => StateChanged?.Invoke(s, t);
        _conn.Moved += p => Moved?.Invoke(p);
        _conn.GaveUp += reason => Stopped?.Invoke(reason);
    }

    public string Name => "PS5'e doğrudan bağlantı";
    public string ConsoleName => _conn.ConsoleName;

    public event Action<CapturedFrame>? FrameArrived;
    public event Action<string>? Stopped;
    public event Action<Ps5State, string>? StateChanged;
    public event Action<Ps5Pairing>? Moved;

    public void Start(IntPtr hwnd, int maxFps) => _conn.Start();

    public void Dispose() => _conn.Stop();
}
