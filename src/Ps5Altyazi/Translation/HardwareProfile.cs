using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Ps5Altyazi.Translation;

/// <summary>Bir ekran kartı (DXGI sırası = DirectML aygıt numarası).</summary>
public sealed record GpuInfo(int Index, string Name, uint VendorId, long DedicatedMb, long SharedMb, bool Integrated)
{
    public string Vendor => VendorId switch { 0x10DE => "NVIDIA", 0x1002 or 0x1022 => "AMD", 0x8086 => "Intel", 0x1414 => "Microsoft", _ => $"0x{VendorId:X4}" };
    public bool IsNvidia => VendorId == 0x10DE;
    public override string ToString() =>
        $"#{Index} {Name} · {(Integrated ? $"dahili (paylaşılan {SharedMb / 1024.0:F1} GB)" : $"VRAM {DedicatedMb / 1024.0:F1} GB")}";
}

/// <summary>NVIDIA kartın anlık durumu (nvidia-smi).</summary>
public sealed record NvidiaStatus(string Name, int TempC, int UtilPercent, long UsedMb, long TotalMb, string Driver);

/// <summary>
/// Bilgisayarın donanım profili: işlemci modeli, fiziksel çekirdek / mantıksal işlemci sayısı, toplam ve boş RAM,
/// ekran kartları (DXGI ile: ad, üretici, VRAM, dahili mi) ve NVIDIA için sürücü / sıcaklık / kullanım (nvidia-smi).
/// </summary>
public sealed class HardwareProfile
{
    public string CpuName { get; private init; } = "?";
    public int PhysicalCores { get; private init; }
    public int LogicalProcessors { get; private init; } = Environment.ProcessorCount;
    public long TotalRamMb { get; private init; }
    public long FreeRamMb { get; private init; }
    public IReadOnlyList<GpuInfo> Gpus { get; private init; } = Array.Empty<GpuInfo>();
    public string? NvidiaDriver { get; private init; }

    /// <summary>Çeviri için en uygun ekran kartı: en çok VRAM'li ayrık kart; yoksa dahili; yoksa null.</summary>
    public GpuInfo? BestGpu => Gpus.Where(g => !g.Integrated).OrderByDescending(g => g.DedicatedMb).FirstOrDefault()
                               ?? Gpus.FirstOrDefault();

    /// <summary>Ekran kartı modelinin (~0,9 GB FP16) rahatça sığacağı ayrık kart var mı (≥ 2 GB VRAM)?</summary>
    public bool HasSuitableDiscreteGpu => Gpus.Any(g => !g.Integrated && g.DedicatedMb >= 2048);

    public static HardwareProfile Detect()
    {
        string cpu = "?";
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            cpu = (k?.GetValue("ProcessorNameString") as string)?.Trim() ?? "?";
        }
        catch { }

        long total = 0, free = 0;
        try
        {
            var ms = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref ms)) { total = (long)(ms.ullTotalPhys / 1048576); free = (long)(ms.ullAvailPhys / 1048576); }
        }
        catch { }

        var gpus = new List<GpuInfo>();
        try { gpus = EnumerateGpus(); } catch { }
        string? driver = null;
        if (gpus.Any(g => g.IsNvidia)) driver = QueryNvidia().FirstOrDefault()?.Driver;

        return new HardwareProfile
        {
            CpuName = cpu,
            PhysicalCores = CountPhysicalCores(),
            TotalRamMb = total,
            FreeRamMb = free,
            Gpus = gpus,
            NvidiaDriver = driver,
        };
    }

    public string Describe()
    {
        var lines = new List<string>
        {
            $"İşlemci: {CpuName} · {PhysicalCores} fiziksel çekirdek / {LogicalProcessors} mantıksal işlemci",
            $"RAM: {TotalRamMb / 1024.0:F1} GB toplam · {FreeRamMb / 1024.0:F1} GB boş",
        };
        if (Gpus.Count == 0) lines.Add("Ekran kartı: DirectX 12 destekli ekran kartı bulunamadı (çeviri işlemcide yapılır).");
        foreach (var g in Gpus)
            lines.Add($"Ekran kartı {g}{(g.IsNvidia && NvidiaDriver != null ? $" · sürücü {NvidiaDriver}" : "")}");
        return string.Join("\n", lines);
    }

    // ───────── NVIDIA (nvidia-smi) ─────────

    /// <summary>NVIDIA kartların sıcaklık / kullanım / bellek bilgisi. nvidia-smi yoksa boş liste.</summary>
    public static List<NvidiaStatus> QueryNvidia()
    {
        var list = new List<NvidiaStatus>();
        try
        {
            var psi = new ProcessStartInfo("nvidia-smi",
                "--query-gpu=name,temperature.gpu,utilization.gpu,memory.used,memory.total,driver_version --format=csv,noheader,nounits")
            {
                RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return list;
            string outp = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(3000)) { try { p.Kill(); } catch { } return list; }
            foreach (var line in outp.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var f = line.Split(',').Select(x => x.Trim()).ToArray();
                if (f.Length < 6) continue;
                int I(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : -1;
                list.Add(new NvidiaStatus(f[0], I(f[1]), I(f[2]), I(f[3]), I(f[4]), f[5]));
            }
        }
        catch { /* NVIDIA sürücüsü yok */ }
        return list;
    }

    // ───────── DXGI ─────────

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1Fn(IntPtr self, uint index, out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesc1Fn(IntPtr self, out DXGI_ADAPTER_DESC1 desc);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow; public int LuidHigh;
        public uint Flags;
    }

    private static T Slot<T>(IntPtr com, int slot) where T : Delegate
    {
        IntPtr vtbl = Marshal.ReadIntPtr(com);
        return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(vtbl, slot * IntPtr.Size));
    }

    /// <summary>DXGI ekran kartı listesi (DirectML aygıt numarası bu sıradır). Yazılım aygıtları (WARP) hariç.</summary>
    private static List<GpuInfo> EnumerateGpus()
    {
        var list = new List<GpuInfo>();
        var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); // IDXGIFactory1
        if (CreateDXGIFactory1(ref iid, out IntPtr factory) < 0 || factory == IntPtr.Zero) return list;
        try
        {
            var enumAdapters1 = Slot<EnumAdapters1Fn>(factory, 12);
            for (uint i = 0; i < 16; i++)
            {
                if (enumAdapters1(factory, i, out IntPtr adapter) < 0 || adapter == IntPtr.Zero) break;
                try
                {
                    if (Slot<GetDesc1Fn>(adapter, 10)(adapter, out var d) < 0) continue;
                    bool software = (d.Flags & 2) != 0 || d.VendorId == 0x1414;
                    if (software) continue;
                    long ded = (long)(d.DedicatedVideoMemory.ToUInt64() / 1048576);
                    long shared = (long)(d.SharedSystemMemory.ToUInt64() / 1048576);
                    // Dahili: ayrılmış belleği çok az (≤ 512 MB) ya da Intel UHD/Iris (Arc hariç)
                    bool integrated = ded <= 512 || (d.VendorId == 0x8086 && !d.Description.Contains("Arc", StringComparison.OrdinalIgnoreCase));
                    list.Add(new GpuInfo((int)i, d.Description.Trim(), d.VendorId, ded, shared, integrated));
                }
                finally { Marshal.Release(adapter); }
            }
        }
        finally { Marshal.Release(factory); }
        return list;
    }

    // ───────── İşlemci / RAM ─────────

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint length);

    /// <summary>Fiziksel çekirdek sayısı (RelationProcessorCore kayıtları).</summary>
    private static int CountPhysicalCores()
    {
        try
        {
            uint len = 0;
            GetLogicalProcessorInformationEx(0, IntPtr.Zero, ref len);
            if (len == 0) return Math.Max(1, Environment.ProcessorCount / 2);
            IntPtr buf = Marshal.AllocHGlobal((int)len);
            try
            {
                if (!GetLogicalProcessorInformationEx(0, buf, ref len)) return Math.Max(1, Environment.ProcessorCount / 2);
                int count = 0, off = 0;
                while (off < len)
                {
                    int size = Marshal.ReadInt32(buf, off + 4); // Relationship (4) + Size (4)
                    if (size <= 0) break;
                    count++;
                    off += size;
                }
                return Math.Max(1, count);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch { return Math.Max(1, Environment.ProcessorCount / 2); }
    }
}
