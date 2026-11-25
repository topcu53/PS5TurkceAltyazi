// SPDX-License-Identifier: AGPL-3.0-only
using System;
using CouchSubs.RemotePlay;
using Ps5Altyazi.RemotePlayLink;

namespace Ps5Altyazi.Capture;

/// <summary>
/// Görüntüyü PS Remote Play penceresinden değil, PS5'ten doğrudan alır (Remote Play protokolü, chiaki-ng; CouchSubs'ın
/// yöntemi). Ekranda pencere açılmaz; PS5 dinlenmedeyse uyandırılır, bağlantı koparsa kendiliğinden yeniden bağlanılır.
/// Ses ve kol girdisi kapalıdır: oyunu TV'de, kendi kolunuzla oynamaya devam edersiniz.
/// </summary>
public sealed class Ps5DirectCaptureSource : ICaptureSource
{
    private readonly RegisteredConsole _console;
    private readonly byte[] _accountId;
    private readonly string? _passcode;
    private StreamSession? _session;

    public Ps5DirectCaptureSource(RegisteredConsole console, byte[] accountId, string? passcode)
    {
        _console = console;
        _accountId = accountId;
        _passcode = passcode;
    }

    public string Name => "PS5'e doğrudan bağlantı";
    public string ConsoleName => _console.Name;

    public event Action<CapturedFrame>? FrameArrived;
    public event Action<string>? Stopped;
    /// <summary>Bağlanıyor / bağlı / koptu (oturumun iş parçacığında).</summary>
    public event Action<SessionState>? StateChanged;
    /// <summary>PS5 ağda yeni adreste bulundu; kayıt güncellenmeli.</summary>
    public event Action<RegisteredConsole>? ConsoleMoved;

    public void Start(IntPtr hwnd, int maxFps)
    {
        var s = new StreamSession(_console, _accountId, _passcode);
        s.FrameReceived += OnFrame;
        s.StateChanged += OnState;
        s.ConsoleMoved += c => ConsoleMoved?.Invoke(c);
        _session = s;
        s.Start();
    }

    private void OnFrame(Frame f) =>
        FrameArrived?.Invoke(new CapturedFrame(f.Pixels, f.Width, f.Height, f.Stride, DateTime.Now, Name));

    private void OnState(SessionState st)
    {
        StateChanged?.Invoke(st);
        if (st.Phase == SessionPhase.Disconnected && _session?.HasGivenUp == true)
            Stopped?.Invoke(Ps5Link.Describe(st.Reason));
    }

    public void Dispose()
    {
        var s = _session;
        _session = null;
        if (s == null) return;
        s.FrameReceived -= OnFrame;
        s.StateChanged -= OnState;
        s.Dispose();
    }
}
