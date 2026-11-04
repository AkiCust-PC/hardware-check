using System.Security.Principal;
using AkikazuHardwareCheck.Core.Models;
using Microsoft.Win32;

namespace AkikazuHardwareCheck.Core.Hardware;

/// <summary>Reads every hardware source once. One unreadable source is reported as a gap and never stops the others.</summary>
public static class HardwareInfoService
{
    private const string PAWN_IO_UNINSTALL_KEY = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";

    public static HardwareInventory Read()
    {
        var diagnostics = new List<DiagnosticEntry>();

        T Collect<T>(string source, T fallback, Func<T> read)
        {
            try
            {
                return read();
            }
            catch (Exception exception)
            {
                diagnostics.Add(new DiagnosticEntry(source, exception.Message));
                return fallback;
            }
        }

        var environment = new ProbeEnvironment(
            Collect<OperatingSystemInfo?>("OperatingSystem", null, OperatingSystemCollector.ReadOperatingSystem),
            Collect("Activation", WindowsActivation.Unknown, OperatingSystemCollector.ReadActivation),
            IsAdministrator(),
            Collect("PawnIO", false, IsPawnIoInstalled));

        var gpus = Collect<IReadOnlyList<GpuInfo>>("GPU", [], GpuInfoCollector.ReadGpus);
        var disks = Collect<IReadOnlyList<DiskInfo>>("Storage", [], SystemInfoCollector.ReadDisks);
        var networkCards = Collect<IReadOnlyList<string>>("NetworkAdapters", [], PeripheralCollector.ReadNetworkAdapters);

        return new HardwareInventory(
            environment,
            Collect<IReadOnlyList<CpuInfo>>("CPU", [], SystemInfoCollector.ReadCpus),
            gpus,
            Collect<MotherboardInfo?>("Motherboard", null, SystemInfoCollector.ReadMotherboard),
            Collect<MemoryInfo?>("Memory", null, SystemInfoCollector.ReadMemory),
            disks,
            Collect<IReadOnlyList<DisplayInfo>>("Display", [], () => DisplayInfoCollector.ReadDisplays(gpus)),
            diagnostics,
            Collect<NetworkConnection?>("Network", null, NetworkCollector.Read),
            Collect<SystemDriveSpace?>("SystemDrive", null, ReadSystemDrive),
            Collect<IReadOnlyList<DiskHealth>?>("DiskHealth", null, () => DiskHealthCollector.Read(disks, environment.IsAdministrator)),
            Collect<IReadOnlyList<Peripheral>?>("Peripherals", null, PeripheralCollector.Read),
            networkCards.Where(card => !NetworkCollector.IsWirelessCard(card)).ToList(),
            networkCards.Where(NetworkCollector.IsWirelessCard).ToList(),
            Collect<ComputerInfo?>("Computer", null, ComputerCollector.Read),
            Collect<BatteryInfo?>("Battery", null, ComputerCollector.ReadBattery),
            Collect<SecurityInfo?>("Security", null, SecurityCollector.Read),
            Collect<LocaleInfo?>("Locale", null, LocaleCollector.Read),
            Collect<ReliabilityInfo?>("Reliability", null, ReliabilityCollector.Read));
    }

    private static SystemDriveSpace? ReadSystemDrive()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory);
        if (root is null)
        {
            return null;
        }

        var drive = new DriveInfo(root);
        return new SystemDriveSpace((ulong)(drive.TotalSize - drive.TotalFreeSpace), (ulong)drive.TotalSize);
    }

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool IsPawnIoInstalled()
    {
        using var key = Registry.LocalMachine.OpenSubKey(PAWN_IO_UNINSTALL_KEY);
        return key is not null;
    }
}
