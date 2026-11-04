using System.Management;

namespace AkikazuHardwareCheck.Core.Hardware;

internal static class Wmi
{
    public const string CIM_SCOPE = @"root\cimv2";
    public const string STORAGE_SCOPE = @"root\microsoft\windows\storage";
    public const string MONITOR_SCOPE = @"root\wmi";

    private const int QUERY_TIMEOUT_SECONDS = 15;

    public static List<T> Query<T>(string scope, string wql, Func<ManagementBaseObject, T> map)
    {
        var options = new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(QUERY_TIMEOUT_SECONDS) };
        using var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(wql), options);
        using var results = searcher.Get();

        var items = new List<T>();
        foreach (var result in results)
        {
            using (result)
            {
                items.Add(map(result));
            }
        }

        return items;
    }

    public static string? Text(ManagementBaseObject item, string property) =>
        HardwareText.Clean(item[property] as string);

    public static ulong? Number(ManagementBaseObject item, string property) =>
        item[property] is { } value ? Convert.ToUInt64(value) : null;

    public static int? Integer(ManagementBaseObject item, string property) =>
        item[property] is { } value ? Convert.ToInt32(value) : null;
}
