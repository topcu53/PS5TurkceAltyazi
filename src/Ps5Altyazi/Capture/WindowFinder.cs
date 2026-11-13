using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Ps5Altyazi.Capture;

public sealed record WindowInfo(IntPtr Handle, string Title, string ProcessName, int ProcessId, int Width, int Height)
{
    /// <summary>Sony PS Remote Play'in yürütülebilir dosyası RemotePlay.exe, pencere başlığı "PS Remote Play".</summary>
    public bool IsRemotePlay =>
        ProcessName.Equals("RemotePlay", StringComparison.OrdinalIgnoreCase) ||
        Title.Contains("PS Remote Play", StringComparison.OrdinalIgnoreCase);

    public override string ToString() =>
        $"{(IsRemotePlay ? "★ " : "")}{Title}  [{ProcessName}]  {Width}x{Height}";
}

public static class WindowFinder
{
    /// <summary>Yakalanabilir, görünür üst düzey pencereleri listeler (bu uygulamanınkiler hariç).</summary>
    public static List<WindowInfo> EnumerateWindows()
    {
        var result = new List<WindowInfo>();
        int ownPid = Environment.ProcessId;
        var nameCache = new Dictionary<uint, string>();

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            try
            {
                if (!NativeMethods.IsWindowVisible(hwnd)) return true;
                if (IsCloaked(hwnd)) return true;

                int len = NativeMethods.GetWindowTextLength(hwnd);
                if (len <= 0) return true;
                var sb = new StringBuilder(len + 1);
                NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
                string title = sb.ToString();

                NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == ownPid) return true;

                if (!nameCache.TryGetValue(pid, out var pname))
                {
                    try { pname = Process.GetProcessById((int)pid).ProcessName; }
                    catch { pname = "?"; }
                    nameCache[pid] = pname;
                }

                var r = GetBounds(hwnd);
                bool minimized = NativeMethods.IsIconic(hwnd);
                // Küçültülmüş pencereler de listelensin (kullanıcı uyarılacak); çok küçük araç pencereleri atlansın.
                if (!minimized && (r.Width < 100 || r.Height < 80)) return true;

                result.Add(new WindowInfo(hwnd, title, pname, (int)pid, r.Width, r.Height));
            }
            catch
            {
                // Tek bir pencere sorunu listeyi bozmasın.
            }
            return true;
        }, IntPtr.Zero);

        return result
            .OrderByDescending(w => w.IsRemotePlay)
            .ThenBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>PS Remote Play penceresini bulur. Birden fazla varsa en büyüğünü (akış penceresi) seçer.</summary>
    public static WindowInfo? FindRemotePlay()
    {
        return EnumerateWindows()
            .Where(w => w.IsRemotePlay)
            .OrderByDescending(w => (long)w.Width * w.Height)
            .FirstOrDefault();
    }

    internal static NativeMethods.RECT GetBounds(IntPtr hwnd)
    {
        // DWM sınırları görünmez kenarlıkları hariç tutar.
        if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                out NativeMethods.RECT r, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.RECT>()) == 0)
            return r;
        NativeMethods.GetWindowRect(hwnd, out r);
        return r;
    }

    public static bool IsCloaked(IntPtr hwnd)
    {
        return NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0
               && cloaked != 0;
    }

    /// <summary>
    /// Pencerenin ekran yakalama koruması (SetWindowDisplayAffinity). 0 = koruma yok.
    /// Koruma varsa yakalanan görüntü siyah gelir; bu uygulama korumayı AŞMAZ, yalnızca raporlar.
    /// </summary>
    public static uint GetDisplayAffinity(IntPtr hwnd)
    {
        try
        {
            return NativeMethods.GetWindowDisplayAffinity(hwnd, out uint aff) ? aff : 0;
        }
        catch
        {
            return 0;
        }
    }
}
