using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LibreCode.Features.Marketplace;

/// <summary>
/// Detects GPU hardware information. Uses DXGI on Windows with WMI fallback.
/// Returns a stub result on Linux/macOS where these APIs are unavailable.
/// </summary>
public sealed class GpuDetectionService
{
    private GpuInfo? _cached;

    private static readonly Guid IidDxgiFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");

    /// <summary>
    /// Returns the primary GPU info (highest VRAM), cached after first detection.
    /// </summary>
    public GpuInfo GetGpuInfo()
    {
        if (_cached is not null) return _cached;

        if (!OperatingSystem.IsWindows())
        {
            _cached = new GpuInfo { Name = "GPU (non-Windows)", VramGb = 0 };
            return _cached;
        }

        try
        {
            _cached = DetectViaDxgi() ?? DetectViaWmi();
        }
        catch
        {
            _cached = new GpuInfo { Name = "GPU detection failed", VramGb = 0 };
        }

        return _cached;
    }

    private static GpuInfo? DetectViaDxgi()
    {
        nint factoryPtr = 0;

        try
        {
            int hr = CreateDXGIFactory1(in IidDxgiFactory1, out factoryPtr);
            if (hr < 0 || factoryPtr == 0) return null;

            double maxVramBytes = 0;
            string bestName = "Unknown GPU";

            for (uint i = 0; ; i++)
            {
                hr = DxgiFactoryEnumAdapters1(factoryPtr, i, out nint adapterPtr);
                if (hr < 0 || adapterPtr == 0) break;

                try
                {
                    var desc = new DxgiAdapterDesc1();
                    hr = DxgiAdapterGetDesc1(adapterPtr, ref desc);
                    if (hr < 0) continue;

                    if ((desc.Flags & 0x02) != 0) continue;

                    if (desc.DedicatedVideoMemory > (nuint)maxVramBytes)
                    {
                        maxVramBytes = desc.DedicatedVideoMemory;
                        bestName = desc.DescriptionString;
                    }
                }
                finally
                {
                    Marshal.Release(adapterPtr);
                }
            }

            if (maxVramBytes < 1) return null;

            double vramGb = maxVramBytes / (1024.0 * 1024.0 * 1024.0);
            return new GpuInfo { Name = bestName, VramGb = Math.Round(vramGb, 1) };
        }
        finally
        {
            if (factoryPtr != 0) Marshal.Release(factoryPtr);
        }
    }

    [SupportedOSPlatform("windows")]
    private static GpuInfo DetectViaWmi()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT Name, AdapterRAM FROM Win32_VideoController");

            double maxVram = 0;
            string gpuName = "Unknown GPU";

            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? "Unknown GPU";
                var adapterRam = Convert.ToUInt64(obj["AdapterRAM"] ?? 0UL);
                var vramGb = adapterRam / (1024.0 * 1024.0 * 1024.0);

                if (vramGb > maxVram)
                {
                    maxVram = vramGb;
                    gpuName = name;
                }
            }

            return new GpuInfo { Name = gpuName, VramGb = Math.Round(maxVram, 1) };
        }
        catch
        {
            return new GpuInfo { Name = "Unknown GPU", VramGb = 0 };
        }
    }

    /// <summary>Clears the cached GPU info to force re-detection.</summary>
    public void ClearCache() => _cached = null;

    #region DXGI P/Invoke (Windows only)

    [DllImport("dxgi.dll", EntryPoint = "CreateDXGIFactory1", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(in Guid riid, out nint ppFactory);

    private static int DxgiFactoryEnumAdapters1(nint factory, uint index, out nint adapter)
    {
        nint vtable = Marshal.ReadIntPtr(factory);
        nint fnPtr = Marshal.ReadIntPtr(vtable, 12 * nint.Size);
        var fn = Marshal.GetDelegateForFunctionPointer<EnumAdapters1Delegate>(fnPtr);
        return fn(factory, index, out adapter);
    }

    private static int DxgiAdapterGetDesc1(nint adapter, ref DxgiAdapterDesc1 desc)
    {
        nint vtable = Marshal.ReadIntPtr(adapter);
        nint fnPtr = Marshal.ReadIntPtr(vtable, 10 * nint.Size);
        var fn = Marshal.GetDelegateForFunctionPointer<GetDesc1Delegate>(fnPtr);
        return fn(adapter, ref desc);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1Delegate(nint self, uint index, out nint adapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesc1Delegate(nint self, ref DxgiAdapterDesc1 desc);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiAdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;

        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public long AdapterLuid;
        public uint Flags;

        public readonly string DescriptionString =>
            Description?.TrimEnd('\0') ?? "Unknown GPU";
    }

    #endregion
}
