using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using AkikazuHardwareCheck.Core.Models;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace AkikazuHardwareCheck.Core.Hardware;

/// <summary>
/// DXGI gives 64-bit memory sizes, the software adapter flag and the LUID the stress test targets.
/// Win32_VideoController.AdapterRAM is a 32-bit field that saturates at 4 GB, so it only supplies the driver version.
/// </summary>
public static class GpuInfoCollector
{
    public static List<GpuInfo> ReadGpus()
    {
        var drivers = Wmi.Query(
            Wmi.CIM_SCOPE,
            "SELECT PNPDeviceID, DriverVersion FROM Win32_VideoController",
            item => (DeviceId: Wmi.Text(item, "PNPDeviceID") ?? "", Version: Wmi.Text(item, "DriverVersion")));

        var packages = ReadDriverPackages();
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        var adapterIds = new List<string>();
        for (uint index = 0; factory.EnumAdapters1(index, out var adapter).Success; index++)
        {
            using (adapter)
            {
                var description = adapter.Description1;
                adapterIds.Add($"VEN_{description.VendorId:X4}&DEV_{description.DeviceId:X4}");
            }
        }

        var kept = KeepPhysicalAdapters(adapterIds, id => drivers.Count(entry => entry.DeviceId.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase) && entry.DeviceId.Contains(id, StringComparison.OrdinalIgnoreCase))).ToHashSet();
        var gpus = new List<GpuInfo>();
        for (uint index = 0; factory.EnumAdapters1(index, out var adapter).Success; index++)
        {
            using (adapter)
            {
                if (!kept.Contains((int)index))
                {
                    continue;
                }

                var description = adapter.Description1;
                var hardwareId = adapterIds[(int)index];
                var driver = drivers.FirstOrDefault(entry => entry.DeviceId.Contains(hardwareId, StringComparison.OrdinalIgnoreCase));
                var package = packages.FirstOrDefault(entry => entry.DeviceId.Contains(hardwareId, StringComparison.OrdinalIgnoreCase));
                var isSoftware = (description.Flags & AdapterFlags.Software) != 0;
                gpus.Add(new GpuInfo(
                    MapVendor((uint)description.VendorId),
                    HardwareText.Clean(description.Description),
                    (ulong)description.DedicatedVideoMemory,
                    driver.Version,
                    ClassifyKind(isSoftware, isSoftware ? null : ReadUnifiedMemory(adapter)),
                    FormatLuid(description.Luid.HighPart, description.Luid.LowPart),
                    package.Provider,
                    package.Date));
            }
        }

        return gpus;
    }

    /// <summary>Who published each display driver and when. A failed read only loses these details, never the GPU list.</summary>
    private static List<(string DeviceId, string? Provider, string? Date)> ReadDriverPackages()
    {
        try
        {
            return Wmi.Query(
                Wmi.CIM_SCOPE,
                "SELECT DeviceID, DriverProviderName, DriverDate FROM Win32_PnPSignedDriver WHERE DeviceClass = 'DISPLAY'",
                item => (Wmi.Text(item, "DeviceID") ?? "", Wmi.Text(item, "DriverProviderName"), SystemInfoCollector.FormatWmiDate(Wmi.Text(item, "DriverDate"))));
        }
        catch (Exception exception) when (exception is ManagementException or COMException or UnauthorizedAccessException)
        {
            Trace.TraceWarning(exception.Message);
            return [];
        }
    }

    /// <summary>
    /// Virtual display software can make DXGI list one physical GPU twice. Each ID keeps only as many adapters as Windows
    /// has PCI devices for it, so two real identical cards stay. An ID with no PCI device is left alone rather than guessed at.
    /// </summary>
    public static List<int> KeepPhysicalAdapters(IReadOnlyList<string> adapterIds, Func<string, int> physicalCount)
    {
        var seen = new Dictionary<string, int>();
        var kept = new List<int>();
        for (var index = 0; index < adapterIds.Count; index++)
        {
            var id = adapterIds[index];
            seen[id] = seen.GetValueOrDefault(id) + 1;
            var limit = physicalCount(id);
            if (limit == 0 || seen[id] <= limit)
            {
                kept.Add(index);
            }
        }

        return kept;
    }

    public static string FormatLuid(int highPart, uint lowPart) => $"{highPart:X8}:{lowPart:X8}";

    /// <summary>Unified memory means the GPU shares system RAM, which is how integrated GPUs work.</summary>
    public static GpuKind ClassifyKind(bool isSoftware, bool? hasUnifiedMemory) => (isSoftware, hasUnifiedMemory) switch
    {
        (true, _) => GpuKind.Software,
        (_, true) => GpuKind.Integrated,
        (_, false) => GpuKind.Discrete,
        _ => GpuKind.Unknown,
    };

    /// <summary>Real graphics cards. An integrated GPU shares the CPU's power and cooling, so the CPU test already covers it.</summary>
    public static List<GpuInfo> StressTargets(IEnumerable<GpuInfo> gpus) =>
        gpus.Where(gpu => gpu.Kind is not (GpuKind.Software or GpuKind.Integrated) && gpu.AdapterLuid is not null).ToList();

    /// <summary>A card known to be discrete comes first, then the most memory.</summary>
    public static GpuInfo? DefaultStressTarget(IEnumerable<GpuInfo> gpus) => StressTargets(gpus)
        .OrderBy(gpu => gpu.Kind == GpuKind.Discrete ? 0 : 1)
        .ThenByDescending(gpu => gpu.DedicatedMemoryBytes ?? 0)
        .FirstOrDefault();

    private static bool? ReadUnifiedMemory(IDXGIAdapter1 adapter)
    {
        var result = D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.None, null, out ID3D11Device? device);
        if (result.Failure || device is null)
        {
            return null;
        }

        using (device)
        {
            var options = device.CheckFeatureSupport<FeatureDataD3D11Options2>(Vortice.Direct3D11.Feature.D3D11Options2);
            return options.UnifiedMemoryArchitecture;
        }
    }

    private static string? MapVendor(uint vendorId) => vendorId switch
    {
        0x10DE => "NVIDIA",
        0x1002 => "AMD",
        0x8086 => "Intel",
        0x1414 => "Microsoft",
        _ => null,
    };
}