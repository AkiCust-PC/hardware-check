using System.Globalization;
using System.Text;
using AkikazuHardwareCheck.Core.Hardware;
using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Models;
using AkikazuHardwareCheck.Core.Monitoring;
using AkikazuHardwareCheck.Core.Reporting;
using AkikazuHardwareCheck.Core.Testing;
using AkikazuHardwareCheck.Probe;

const int EXIT_USAGE_ERROR = 2;
const int EXIT_JSON_SAVE_FAILED = 3;
const char LIST_SEPARATOR = ',';
const int PROFILE_FIELDS = 3;

Console.OutputEncoding = Encoding.UTF8;

var language = AppLanguage.FromCulture(CultureInfo.CurrentUICulture.Name);
string? jsonPath = null;
string? testParts = null;
var profile = TestProfile.Standard;
var allowUnmonitored = false;
double? memoryTestSeconds = null;
string? diskTestFolder = null;
var isUsageValid = args.Length % 2 == 0;
for (var index = 0; isUsageValid && index < args.Length; index += 2)
{
    switch (args[index])
    {
        case "--json":
            jsonPath = args[index + 1];
            break;
        case "--lang" when AppLanguage.Parse(args[index + 1]) is { } selected:
            language = selected;
            break;
        case "--test":
            testParts = args[index + 1];
            break;
        case "--seconds" when ParseSeconds(args[index + 1], profile) is { } custom:
            profile = custom;
            break;
        case "--memtest" when double.TryParse(args[index + 1], CultureInfo.InvariantCulture, out var seconds) && seconds > 0:
            memoryTestSeconds = seconds;
            break;
        case "--disktest":
            diskTestFolder = args[index + 1];
            break;
        case "--allow-unmonitored":
            allowUnmonitored = args[index + 1] == "yes";
            break;
        default:
            isUsageValid = false;
            break;
    }
}

var translator = Translator.Load(language);
if (!isUsageValid)
{
    Console.Error.WriteLine(translator["app.usage"]);
    return EXIT_USAGE_ERROR;
}

var hardware = HardwareInfoService.Read();
var diagnostics = hardware.Diagnostics.ToList();
IReadOnlyList<SensorReading> sensors = [];
try
{
    sensors = LhmSensorSource.ReadOnce();
}
catch (Exception exception)
{
    diagnostics.Add(new DiagnosticEntry("Sensors", exception.Message));
}

TestRunResult? testRun = null;
if (testParts is not null)
{
    testRun = await RunTestAsync(testParts.Split(LIST_SEPARATOR), profile, allowUnmonitored, hardware.Gpus);
}

MemoryTestResult? memoryResult = null;
if (memoryTestSeconds is { } memorySeconds)
{
    memoryResult = await new MemoryTest(TimeSpan.FromSeconds(memorySeconds)).RunAsync(null, CancellationToken.None);
}

DiskTestResult? diskResult = null;
if (diskTestFolder is not null)
{
    diskResult = await new DiskTest(diskTestFolder).RunAsync(null, CancellationToken.None);
}

var report = new CheckReport(
    CheckReport.CURRENT_SCHEMA_VERSION,
    CheckReport.CurrentToolVersion,
    DateTimeOffset.Now,
    hardware with { Diagnostics = diagnostics },
    sensors,
    CoolingEstimator.Estimate(sensors, hardware.Environment.IsSensorAccessLimited),
    testRun,
    memoryResult,
    diskResult);

new ConsoleReport(translator, Console.Out).Write(report);
if (memoryResult is not null)
{
    Console.WriteLine(ToolTestSummary.Text(translator, memoryResult));
}

if (diskResult is not null)
{
    Console.WriteLine(ToolTestSummary.Text(translator, diskResult));
}


if (jsonPath is not null)
{
    Console.WriteLine();
    if (JsonReportWriter.TrySave(report, jsonPath) is { } failure)
    {
        Console.Error.WriteLine(translator["app.jsonSaveFailed", jsonPath, failure]);
        return EXIT_JSON_SAVE_FAILED;
    }

    Console.WriteLine($"JSON: {Path.GetFullPath(jsonPath)}");
}

return 0;

static async Task<TestRunResult> RunTestAsync(string[] parts, TestProfile profile, bool allowUnmonitored, IReadOnlyList<GpuInfo> gpus)
{
    using var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, keyEvent) =>
    {
        keyEvent.Cancel = true;
        stop.Cancel();
    };

    using ISensorSource sensorSource = new LhmSensorSource();
    var controller = new TestController(sensorSource, gpu => new GpuStressLoad(gpu.AdapterLuid!), new CpuStressLoad())
    {
        ConfirmUnmonitored = (_, _) => Task.FromResult(allowUnmonitored),
    };
    controller.ProgressChanged += progress => Console.Error.WriteLine(
        $"{progress.Phase} {progress.PhaseElapsedSeconds:0}/{progress.PhasePlannedSeconds:0}s CPU {progress.CpuTemperature:0.#}°C {progress.CpuLoad:0}% GPU {progress.GpuTemperature:0.#}°C {progress.GpuLoad:0}%");

    var plan = new TestPlan(profile, parts.Contains("idle"), parts.Contains("gpu"), parts.Contains("cpu"), GpuInfoCollector.DefaultStressTarget(gpus));
    return await controller.RunAsync(plan, stop.Token);
}

static TestProfile? ParseSeconds(string text, TestProfile profile)
{
    var values = text.Split(LIST_SEPARATOR).Select(value => double.TryParse(value, CultureInfo.InvariantCulture, out var number) ? number : (double?)null).ToList();
    if (values.Count != PROFILE_FIELDS || values.Any(value => value is null or <= 0))
    {
        return null;
    }

    return profile with { IdleSeconds = values[0]!.Value, GpuStressSeconds = values[1]!.Value, CpuStressSeconds = values[2]!.Value };
}
