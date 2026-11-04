using System.Globalization;
using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Models;

namespace AkikazuHardwareCheck.Core.Reporting;

/// <summary>Turns memory and drive test results into the lines shown on screen and in the report.</summary>
public static class ToolTestSummary
{
    public static bool HasProblem(MemoryTestResult? result) => result?.End is MemoryTestEnd.ErrorsFound or MemoryTestEnd.Failed;

    public static bool HasProblem(DiskTestResult? result) => result?.End is DiskTestEnd.ErrorsFound or DiskTestEnd.Failed;

    public static string Text(Translator t, MemoryTestResult result)
    {
        var size = DisplayNames.MemorySize(result.TestedBytes);
        return result.End switch
        {
            MemoryTestEnd.Completed => t["memoryTest.completed", size, result.Passes],
            MemoryTestEnd.ErrorsFound => t["memoryTest.errorsFound", result.Errors, size],
            MemoryTestEnd.Stopped => t["memoryTest.stopped", size, result.Passes],
            MemoryTestEnd.Unsupported => t["memoryTest.unsupported"],
            _ => t["memoryTest.failed", result.Detail ?? ""],
        };
    }

    public static string Text(Translator t, DiskTestResult result)
    {
        return result.End switch
        {
            DiskTestEnd.Completed => t["diskTest.completed", result.Drive, Speed(result.SequentialReadMbps), Speed(result.SequentialWriteMbps), Speed(result.RandomReadMbps)],
            DiskTestEnd.ErrorsFound => t["diskTest.errorsFound", result.Drive, result.VerificationErrors],
            DiskTestEnd.Stopped => t["diskTest.stopped", result.Drive],
            DiskTestEnd.NotEnoughSpace => t["diskTest.notEnoughSpace", result.Drive],
            _ => t["diskTest.failed", result.Drive, result.Detail ?? ""],
        };

        string Speed(double? value) => value is { } speed ? speed.ToString("N0", CultureInfo.InvariantCulture) : t["common.unavailable"];
    }
}
