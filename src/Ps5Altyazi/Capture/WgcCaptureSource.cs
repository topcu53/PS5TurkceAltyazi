using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using Ps5Altyazi.Logging;
using Windows.Foundation.Metadata;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using WinRT;

namespace Ps5Altyazi.Capture;

/// <summary>
/// Windows Graphics Capture (Windows 10 1903+) ile tek bir pencereyi yakalar.
/// Pencere başka pencerelerin arkasında kalsa bile çalışır; küçültülmüş (simge durumundaki) pencereleri yakalayamaz.
/// </summary>
public sealed class WgcCaptureSource : ICaptureSource
{
    private const DirectXPixelFormat PixelFormat = DirectXPixelFormat.B8G8R8A8UIntNormalized;

    public string Name => "Windows Graphics Capture";
    public event Action<CapturedFrame>? FrameArrived;
    public event Action<string>? Stopped;

    private IntPtr _hwnd;
    private IDirect3DDevice? _device;
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private SizeInt32 _poolSize;
    private long _minIntervalTicks;
    private long _lastTicks;
    private int _busy;
    private volatile bool _disposed;

    public static bool IsSupported()
    {
        try { return GraphicsCaptureSession.IsSupported(); }
        catch { return false; }
    }

    public void Start(IntPtr hwnd, int maxFps)
    {
        if (!IsSupported())
            throw new NotSupportedException("Bu Windows sürümü Windows Graphics Capture'ı desteklemiyor (Windows 10 1903 veya üzeri gerekir).");

        _minIntervalTicks = Stopwatch.Frequency / Math.Max(1, maxFps);
        _hwnd = hwnd;

        _device = Direct3DHelper.CreateDevice(out string deviceDesc);
        Log.Info($"Direct3D aygıtı oluşturuldu: {deviceDesc}");

        _item = CaptureItemHelper.CreateForWindow(hwnd);
        _item.Closed += (_, _) => Stopped?.Invoke("Yakalanan pencere kapandı.");

        _poolSize = _item.Size;
        if (_poolSize.Width <= 0 || _poolSize.Height <= 0)
            throw new InvalidOperationException(
                $"Pencere boyutu geçersiz ({_poolSize.Width}x{_poolSize.Height}). Pencere simge durumunda (küçültülmüş) olabilir.");

        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device, PixelFormat, 2, _poolSize);
        _pool.FrameArrived += OnFrameArrived;

        _session = _pool.CreateCaptureSession(_item);
        TryConfigureSession(_session);
        _session.StartCapture();
    }

    private static void TryConfigureSession(GraphicsCaptureSession session)
    {
        const string type = "Windows.Graphics.Capture.GraphicsCaptureSession";
        try
        {
            if (ApiInformation.IsPropertyPresent(type, "IsCursorCaptureEnabled"))
                session.IsCursorCaptureEnabled = false;
        }
        catch (Exception ex) { Log.Info($"İmleç gizleme ayarlanamadı: {ex.Message}"); }

        try
        {
            // Windows 11'de sarı yakalama çerçevesini kapatır. Windows 10'da desteklenmez (sorun değil).
            if (ApiInformation.IsPropertyPresent(type, "IsBorderRequired"))
                session.IsBorderRequired = false;
        }
        catch (Exception ex) { Log.Info($"Sarı yakalama çerçevesi kapatılamadı (zararsız): {ex.Message}"); }
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        if (_disposed) return;
        try
        {
            using Direct3D11CaptureFrame? frame = sender.TryGetNextFrame();
            if (frame == null) return;

            SizeInt32 content = frame.ContentSize;
            if (content.Width != _poolSize.Width || content.Height != _poolSize.Height)
            {
                // Pencere yeniden boyutlandı: havuzu yeni boyuta göre kur, bu kareyi atla.
                if (content.Width > 0 && content.Height > 0 && _device != null)
                {
                    _poolSize = content;
                    sender.Recreate(_device, PixelFormat, 2, content);
                    Log.Info($"Pencere boyutu değişti: {content.Width}x{content.Height}");
                }
                return;
            }

            long now = Stopwatch.GetTimestamp();
            if (now - _lastTicks < _minIntervalTicks) return;
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;

            try
            {
                _lastTicks = now;
                // GPU yüzeyini CPU belleğine kopyala (bu iş parçacığı serbest iş parçacığı; beklemek güvenli).
                using SoftwareBitmap bmp = SoftwareBitmap
                    .CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Ignore)
                    .AsTask().GetAwaiter().GetResult();

                using SoftwareBitmap bgra = bmp.BitmapPixelFormat == BitmapPixelFormat.Bgra8
                    ? SoftwareBitmap.Copy(bmp)
                    : SoftwareBitmap.Convert(bmp, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);

                int w = bgra.PixelWidth, h = bgra.PixelHeight;
                var pixels = new byte[w * h * 4];
                bgra.CopyToBuffer(pixels.AsBuffer());

                // WGC pencere karesi, DWM'nin görünür pencere sınırlarına karşılık gelir.
                var bounds = WindowFinder.GetBounds(_hwnd);
                FrameArrived?.Invoke(new CapturedFrame(pixels, w, h, w * 4, DateTime.Now, Name, bounds.Left, bounds.Top));
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }
        catch (Exception ex)
        {
            if (!_disposed) Log.Error($"WGC kare işleme hatası: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { if (_pool != null) _pool.FrameArrived -= OnFrameArrived; } catch { }
        try { _session?.Dispose(); } catch { }
        try { _pool?.Dispose(); } catch { }
        try { _device?.Dispose(); } catch { }
        _session = null; _pool = null; _device = null; _item = null;
    }
}

/// <summary>HWND'den GraphicsCaptureItem oluşturur (IGraphicsCaptureItemInterop).</summary>
internal static class CaptureItemHelper
{
    private static readonly Guid GraphicsCaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    [ComImport]
    [System.Runtime.InteropServices.Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [ComVisible(true)]
    internal interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
        IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
    }

    public static GraphicsCaptureItem CreateForWindow(IntPtr hwnd)
    {
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        Guid iid = GraphicsCaptureItemIid;
        IntPtr ptr = interop.CreateForWindow(hwnd, ref iid);
        try
        {
            return GraphicsCaptureItem.FromAbi(ptr);
        }
        finally
        {
            Marshal.Release(ptr);
        }
    }
}

/// <summary>WinRT IDirect3DDevice oluşturur (önce donanım, olmazsa WARP yazılım aygıtı).</summary>
internal static class Direct3DHelper
{
    private const int D3D_DRIVER_TYPE_HARDWARE = 1;
    private const int D3D_DRIVER_TYPE_WARP = 5;
    private const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
    private const uint D3D11_SDK_VERSION = 7;
    private static readonly Guid IidIdxgiDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(IntPtr pAdapter, int driverType, IntPtr software, uint flags,
        IntPtr pFeatureLevels, uint featureLevels, uint sdkVersion,
        out IntPtr ppDevice, out int pFeatureLevel, out IntPtr ppImmediateContext);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    public static IDirect3DDevice CreateDevice(out string description)
    {
        description = "Donanım (GPU)";
        int hr = D3D11CreateDevice(IntPtr.Zero, D3D_DRIVER_TYPE_HARDWARE, IntPtr.Zero, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            IntPtr.Zero, 0, D3D11_SDK_VERSION, out IntPtr d3dDevice, out _, out IntPtr context);
        if (hr < 0)
        {
            description = "WARP (yazılım/CPU)";
            hr = D3D11CreateDevice(IntPtr.Zero, D3D_DRIVER_TYPE_WARP, IntPtr.Zero, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                IntPtr.Zero, 0, D3D11_SDK_VERSION, out d3dDevice, out _, out context);
        }
        Marshal.ThrowExceptionForHR(hr);

        IntPtr dxgiDevice = IntPtr.Zero, inspectable = IntPtr.Zero;
        try
        {
            Guid iid = IidIdxgiDevice;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3dDevice, ref iid, out dxgiDevice));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out inspectable));
            return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        finally
        {
            if (inspectable != IntPtr.Zero) Marshal.Release(inspectable);
            if (dxgiDevice != IntPtr.Zero) Marshal.Release(dxgiDevice);
            if (context != IntPtr.Zero) Marshal.Release(context);
            if (d3dDevice != IntPtr.Zero) Marshal.Release(d3dDevice);
        }
    }
}
