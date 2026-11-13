using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Ps5Altyazi.Logging;

namespace Ps5Altyazi.Capture;

/// <summary>
/// Yedek / teşhis yöntemi: GDI PrintWindow (PW_RENDERFULLCONTENT).
/// Bazı DirectX pencereleri bu yöntemle siyah gelebilir; WGC başarısız olursa karşılaştırma için kullanılır.
/// </summary>
public sealed class PrintWindowCaptureSource : ICaptureSource
{
    public string Name => "PrintWindow (GDI)";
    public event Action<CapturedFrame>? FrameArrived;
    public event Action<string>? Stopped;

    private CancellationTokenSource? _cts;

    public void Start(IntPtr hwnd, int maxFps)
    {
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
        int delayMs = 1000 / Math.Max(1, maxFps);
        int consecutiveErrors = 0;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                if (!NativeMethods.IsWindow(hwnd))
                {
                    Stopped?.Invoke("Yakalanan pencere kapandı.");
                    return;
                }

                try
                {
                    CapturedFrame? f = CaptureOnce(hwnd);
                    if (f != null) FrameArrived?.Invoke(f);
                    consecutiveErrors = 0;
                }
                catch (Exception ex)
                {
                    // Aynı hatayı saniyede onlarca kez loglamamak için seyrelt.
                    if (consecutiveErrors++ % 50 == 0)
                        Log.Error($"PrintWindow hatası: {ex.Message}");
                }

                try { await Task.Delay(delayMs, token); }
                catch (TaskCanceledException) { return; }
            }
        }, token);
    }

    private CapturedFrame? CaptureOnce(IntPtr hwnd)
    {
        if (NativeMethods.IsIconic(hwnd)) return null;
        if (!NativeMethods.GetWindowRect(hwnd, out var r)) return null;
        int w = r.Width, h = r.Height;
        if (w <= 0 || h <= 0) return null;

        IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
        IntPtr memDc = NativeMethods.CreateCompatibleDC(screenDc);
        IntPtr bmp = NativeMethods.CreateCompatibleBitmap(screenDc, w, h);
        IntPtr old = NativeMethods.SelectObject(memDc, bmp);
        bool selected = true;
        try
        {
            if (!NativeMethods.PrintWindow(hwnd, memDc, NativeMethods.PW_RENDERFULLCONTENT))
                throw new InvalidOperationException("PrintWindow çağrısı başarısız oldu.");

            // GetDIBits için bitmap bir DC'ye seçili olmamalı.
            NativeMethods.SelectObject(memDc, old);
            selected = false;

            var bi = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                biWidth = w,
                biHeight = -h, // negatif = üstten alta
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0 // BI_RGB
            };
            var pixels = new byte[w * h * 4];
            int lines = NativeMethods.GetDIBits(memDc, bmp, 0, (uint)h, pixels, ref bi, 0);
            if (lines == 0) throw new InvalidOperationException("GetDIBits başarısız oldu.");

            return new CapturedFrame(pixels, w, h, w * 4, DateTime.Now, Name, r.Left, r.Top);
        }
        finally
        {
            if (selected) NativeMethods.SelectObject(memDc, old);
            NativeMethods.DeleteDC(memDc);
            NativeMethods.DeleteObject(bmp);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        _cts = null;
    }
}
