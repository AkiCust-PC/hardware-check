using System.Collections.Concurrent;
using AkikazuHardwareCheck.Core.Models;
using Microsoft.Win32;

namespace AkikazuHardwareCheck.Core.Hardware;

/// <summary>
/// Reads every hardware source once. Independent sources run together so a slow WMI query does not hold up the rest;
/// one unreadable source is reported as a gap and never stops the others. A caller can receive the core inventory
/// first and the slower diagnostic groups afterwards.
/// </summary>
public static class HardwareInfoService
{
    private const string PAWN_IO_UNINSTALL_KEY = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";

    public static HardwareInventory Read(Action<HardwareInventory>? onCoreReady = null)
    {
        var diagnostics = new ConcurrentBag<DiagnosticEntry>();

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

        var environmentTask = Task.Run(() => new ProbeEnvironment(
            Collect<OperatingSystemInfo?>("OperatingSystem", null, OperatingSystemCollector.ReadOperatingSystem),
            Collect("Activation", WindowsActivation.Unknown, OperatingSystemCollector.ReadActivation),
            Collect("Administrator", false, Elevation.IsAdministrator),
            Collect("PawnIO", false, IsPawnIoInstalled)));

        var gpusTask = Task.Run(() => Collect<IReadOnlyList<GpuInfo>>("GPU", [], GpuInfoCollector.ReadGpus));
        var disksTask = Task.Run(() => Collect<IReadOnlyList<DiskInfo>>("Storage", [], SystemInfoCollector.ReadDisks));
        var cpusTask = Task.Run(() => Collect<IReadOnlyList<CpuInfo>>("CPU", [], SystemInfoCollector.ReadCpus));
        var motherboardTask = Task.Run(() => Collect<MotherboardInfo?>("Motherboard", null, SystemInfoCollector.ReadMotherboard));
        var memoryTask = Task.Run(() => Collect<MemoryInfo?>("Memory", null, SystemInfoCollector.ReadMemory));
        var networkCardsTask = Task.Run(() => Collect<IReadOnlyList<string>>("NetworkAdapters", [], PeripheralCollector.ReadNetworkAdapters));
        var networkTask = Task.Run(() => Collect<NetworkConnection?>("Network", null, NetworkCollector.Read));
        var systemDriveTask = Task.Run(() => Collect<SystemDriveSpace?>("SystemDrive", null, ReadSystemDrive));
        var peripheralsTask = Task.Run(() => Collect<IReadOnlyList<Peripheral>?>("Peripherals", null, PeripheralCollector.Read));
        var computerTask = Task.Run(() => Collect<ComputerInfo?>("Computer", null, ComputerCollector.Read));
        var batteryTask = Task.Run(() => Collect<BatteryInfo?>("Battery", null, ComputerCollector.ReadBattery));
        var securityTask = Task.Run(() => Collect<SecurityInfo?>("Security", null, SecurityCollector.Read));
        var localeTask = Task.Run(() => Collect<LocaleInfo?>("Locale", null, LocaleCollector.Read));
        var reliabilityTask = Task.Run(() => Collect<ReliabilityInfo?>("Reliability", null, ReliabilityCollector.Read));

        // These depend on a previous result, so they start after it but still overlap the other work.
        var displayTask = Task.Run(() => Collect<IReadOnlyList<DisplayInfo>>("Display", [], () => DisplayInfoCollector.ReadDisplays(gpusTask.Result)));
        var diskHealthTask = Task.Run(() => Collect<IReadOnlyList<DiskHealth>?>("DiskHealth", null, () => DiskHealthCollector.Read(disksTask.Result, environmentTask.Result.IsAdministrator)));

        HardwareInventory Snapshot()
        {
            var environment = environmentTask.Result;
            var networkCards = networkCardsTask.Result;
            return new HardwareInventory(
                environment,
                cpusTask.Result,
                gpusTask.Result,
                motherboardTask.Result,
                memoryTask.Result,
                disksTask.Result,
                displayTask.Result,
                diagnostics.OrderBy(entry => entry.Source, StringComparer.Ordinal).ToList(),
                networkTask.Result,
                systemDriveTask.Result,
                diskHealthTask.IsCompletedSuccessfully ? diskHealthTask.Result : null,
                peripheralsTask.IsCompletedSuccessfully ? peripheralsTask.Result : null,
                networkCards.Where(card => !NetworkCollector.IsWirelessCard(card)).ToList(),
                networkCards.Where(NetworkCollector.IsWirelessCard).ToList(),
                computerTask.IsCompletedSuccessfully ? computerTask.Result : null,
                batteryTask.IsCompletedSuccessfully ? batteryTask.Result : null,
                securityTask.IsCompletedSuccessfully ? securityTask.Result : null,
                localeTask.IsCompletedSuccessfully ? localeTask.Result : null,
                reliabilityTask.IsCompletedSuccessfully ? reliabilityTask.Result : null);
        }

        Task.WaitAll(environmentTask, gpusTask, disksTask, cpusTask, motherboardTask, memoryTask, networkCardsTask,
            networkTask, systemDriveTask, displayTask);
        onCoreReady?.Invoke(Snapshot());

        Task.WaitAll(peripheralsTask, computerTask, batteryTask, securityTask, localeTask, reliabilityTask, diskHealthTask);
        return Snapshot();
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

    public static bool IsPawnIoInstalled()
    {
        using var key = Registry.LocalMachine.OpenSubKey(PAWN_IO_UNINSTALL_KEY);
        return key is not null;
    }
}
