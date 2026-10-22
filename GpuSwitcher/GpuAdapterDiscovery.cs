using System.Runtime.InteropServices;

namespace GpuSwitcher;

internal sealed record GpuAdapter(long Luid, string Name, int WindowsIndex, bool IsHighPerformance);

internal static unsafe class GpuAdapterDiscovery
{
    private static readonly Guid Factory6Id = new("c1b6694f-ff09-44a9-b03c-77900a0a1d17");
    private static readonly Guid AdapterId = new("2411e7e1-12ac-4ccf-bd14-9798e8534dc0");

    public static IReadOnlyList<GpuAdapter> Discover()
    {
        Marshal.ThrowExceptionForHR(CreateDXGIFactory1(in Factory6Id, out var factory));
        try
        {
            var highPerformanceLuid = GetFirstHighPerformanceLuid(factory);
            var adapters = new List<GpuAdapter>();
            var enumerate = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Method(factory, 12);
            for (uint index = 0; index < 32; index++)
            {
                nint adapter = 0;
                if (enumerate(factory, index, &adapter) < 0) break;
                try
                {
                    var desc = Describe(adapter);
                    var name = new string(desc.Description).TrimEnd('\0');
                    if (name.Contains("Basic Render Driver", StringComparison.OrdinalIgnoreCase)) continue;
                    adapters.Add(new GpuAdapter(desc.AdapterLuid, name, (int)index,
                        desc.AdapterLuid == highPerformanceLuid));
                }
                finally { Marshal.Release(adapter); }
            }
            return adapters;
        }
        finally { Marshal.Release(factory); }
    }

    private static long GetFirstHighPerformanceLuid(nint factory)
    {
        var enumerate = (delegate* unmanaged[Stdcall]<nint, uint, uint, Guid*, nint*, int>)Method(factory, 29);
        var adapterId = AdapterId;
        nint adapter = 0;
        if (enumerate(factory, 0, 2, &adapterId, &adapter) < 0) return long.MinValue;
        try { return Describe(adapter).AdapterLuid; }
        finally { Marshal.Release(adapter); }
    }

    private static DxgiAdapterDesc Describe(nint adapter)
    {
        DxgiAdapterDesc desc = default;
        var getDesc = (delegate* unmanaged[Stdcall]<nint, DxgiAdapterDesc*, int>)Method(adapter, 8);
        Marshal.ThrowExceptionForHR(getDesc(adapter, &desc));
        return desc;
    }

    private static nint Method(nint instance, int slot) => (*(nint**)instance)[slot];

    [StructLayout(LayoutKind.Sequential)]
    private struct DxgiAdapterDesc
    {
        public fixed char Description[128];
        public uint VendorId, DeviceId, SubSysId, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public long AdapterLuid;
    }

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(in Guid riid, out nint factory);
}
