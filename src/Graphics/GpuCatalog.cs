using System.Runtime.InteropServices;
using Vortice.DXGI;

namespace BlueSpectrum.Graphics;

public sealed record GpuAdapterInfo(string Id, string Name);

/// <summary>Hardware adapters, including cards with no connected display.</summary>
public static class GpuCatalog
{
    public static IReadOnlyList<GpuAdapterInfo> Enumerate()
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory2>();
        var adapters = OpenAdapters(factory);
        try { return adapters.Select(x => x.Info).ToArray(); }
        finally { foreach (var item in adapters) item.Adapter.Dispose(); }
    }

    internal sealed record Entry(GpuAdapterInfo Info, IDXGIAdapter1 Adapter);

    internal static List<Entry> OpenAdapters(IDXGIFactory1 factory)
    {
        var result = new List<Entry>();
        var duplicates = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            for (uint index = 0; factory.EnumAdapters1(index, out var adapter).Success; index++)
            {
                var description = adapter.Description1;
                if ((description.Flags & AdapterFlags.Software) != 0)
                {
                    adapter.Dispose();
                    continue;
                }
                // LUID identifies the live device, but changes after reboot. Persist PCI identity instead.
                var hardwareId = $"PCI:{description.VendorId:X4}:{description.DeviceId:X4}:{description.SubsystemId:X8}:{description.Revision:X2}";
                var address = TryGetPciAddress(description.Luid);
                if (address != null) hardwareId += ":" + address;
                duplicates.TryGetValue(hardwareId, out var occurrence);
                duplicates[hardwareId] = occurrence + 1;
                var id = occurrence == 0 ? hardwareId : $"{hardwareId}:INSTANCE{occurrence + 1}";
                result.Add(new Entry(new GpuAdapterInfo(id, description.Description.Trim()), adapter));
            }
            return result;
        }
        catch
        {
            foreach (var item in result) item.Adapter.Dispose();
            throw;
        }
    }

    internal static Entry SelectAutomatic(IReadOnlyList<Entry> adapters, nint hwnd)
    {
        if (adapters.Count == 0) throw new InvalidOperationException("사용할 수 있는 하드웨어 GPU가 없습니다.");
        var monitor = MonitorFromWindow(hwnd, 2);
        foreach (var entry in adapters)
        {
            for (uint index = 0; entry.Adapter.EnumOutputs(index, out var output).Success; index++)
            {
                using (output)
                    if (output.Description.Monitor == monitor) return entry;
            }
        }
        return adapters[0];
    }

    private static unsafe string? TryGetPciAddress(long luid)
    {
        var open = new OpenAdapter { LowPart = (uint)luid, HighPart = (int)(luid >> 32) };
        try
        {
            if (D3DKMTOpenAdapterFromLuid(ref open) < 0) return null;
            try
            {
                PciAddress address = default;
                var query = new QueryAdapter { Handle = open.Handle, Type = 6, Data = (nint)(&address), Size = (uint)sizeof(PciAddress) };
                if (D3DKMTQueryAdapterInfo(ref query) < 0) return null;
                return $"BUS{address.Bus:X2}:DEV{address.Device:X2}:FN{address.Function:X2}";
            }
            finally
            {
                var close = new CloseAdapter { Handle = open.Handle };
                D3DKMTCloseAdapter(ref close);
            }
        }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
    }

    [StructLayout(LayoutKind.Sequential)] private struct OpenAdapter { public uint LowPart; public int HighPart; public uint Handle; }
    [StructLayout(LayoutKind.Sequential)] private struct CloseAdapter { public uint Handle; }
    [StructLayout(LayoutKind.Sequential)] private struct PciAddress { public uint Bus, Device, Function; }
    [StructLayout(LayoutKind.Sequential)] private struct QueryAdapter { public uint Handle; public int Type; public nint Data; public uint Size; }
    [DllImport("gdi32.dll")] private static extern int D3DKMTOpenAdapterFromLuid(ref OpenAdapter data);
    [DllImport("gdi32.dll")] private static extern int D3DKMTCloseAdapter(ref CloseAdapter data);
    [DllImport("gdi32.dll")] private static extern int D3DKMTQueryAdapterInfo(ref QueryAdapter data);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
}
