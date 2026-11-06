using AkikazuHardwareCheck.Core.Hardware;
using AkikazuHardwareCheck.Core.Models;
using AkikazuHardwareCheck.Core.Monitoring;

namespace AkikazuHardwareCheck.Core.Tests;

public class MonitoringTests
{
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    private static SensorReading Reading(
        string sensorId,
        double? value,
        double atSeconds = 0,
        SensorStatus status = SensorStatus.Ok,
        string name = "CPU Package",
        string type = "Temperature",
        string hardwareType = "Cpu",
        string hardwareName = "CPU") =>
        new("/hw", hardwareName, hardwareType, sensorId, name, type, value, "°C", status, "test", DateTimeOffset.UnixEpoch.AddSeconds(atSeconds));

    private static List<PhasedReading> Idle(params SensorReading[] readings) =>
        readings.Select(reading => new PhasedReading(TestPhase.Idle, reading)).ToList();

    private static Dictionary<TestPhase, double> IdleSeconds(double seconds) => new() { [TestPhase.Idle] = seconds };

    [Fact]
    public void MinMaxAndAverageUseOnlyValidSamples()
    {
        var samples = Idle(
            Reading("/t", 40, 0),
            Reading("/t", 50, 1),
            Reading("/t", 0, 2, SensorStatus.Invalid),
            Reading("/t", null, 3, SensorStatus.NoValue));

        var result = Assert.Single(SensorStatisticsCalculator.Calculate(samples, IdleSeconds(4), Second));

        Assert.Equal(40, result.Minimum);
        Assert.Equal(50, result.Maximum);
        Assert.Equal(45, result.Average);
        Assert.Equal(2, result.ValidSamples);
        Assert.Equal(4, result.TotalSamples);
        Assert.Equal(4, result.ExpectedSamples);
        Assert.True(result.IsSufficient);
    }

    [Fact]
    public void DifferentSensorsAreNeverMerged()
    {
        var samples = Idle(Reading("/package", 40), Reading("/tctl", 80, name: "Core (Tctl/Tdie)"));

        var results = SensorStatisticsCalculator.Calculate(samples, IdleSeconds(1), Second);

        Assert.Equal(2, results.Count);
        Assert.Equal(40, results.Single(result => result.SensorId == "/package").Average);
        Assert.Equal(80, results.Single(result => result.SensorId == "/tctl").Average);
    }

    [Fact]
    public void PhasesAreStatisticallySeparate()
    {
        List<PhasedReading> samples =
        [
            new(TestPhase.Idle, Reading("/t", 40)),
            new(TestPhase.CpuStress, Reading("/t", 90)),
        ];
        var seconds = new Dictionary<TestPhase, double> { [TestPhase.Idle] = 1, [TestPhase.CpuStress] = 1 };

        var results = SensorStatisticsCalculator.Calculate(samples, seconds, Second);

        Assert.Equal(40, results.Single(result => result.Phase == TestPhase.Idle).Maximum);
        Assert.Equal(90, results.Single(result => result.Phase == TestPhase.CpuStress).Maximum);
    }

    [Fact]
    public void TooFewValidSamplesAreInsufficient()
    {
        var samples = Idle(Reading("/t", 40, 0), Reading("/t", 41, 1));

        var result = Assert.Single(SensorStatisticsCalculator.Calculate(samples, IdleSeconds(60), Second));

        Assert.Equal(60, result.ExpectedSamples);
        Assert.False(result.IsSufficient);
    }

    [Fact]
    public void SensorWithOnlyInvalidSamplesHasNoValues()
    {
        var samples = Idle(Reading("/t", 0, 0, SensorStatus.Invalid));

        var result = Assert.Single(SensorStatisticsCalculator.Calculate(samples, IdleSeconds(1), Second));

        Assert.Null(result.Average);
        Assert.False(result.IsSufficient);
    }

    [Fact]
    public void LongestGapIsMeasuredBetweenValidSamples()
    {
        var samples = Idle(Reading("/t", 40, 0), Reading("/t", 40, 1), Reading("/t", 40, 6));

        var result = Assert.Single(SensorStatisticsCalculator.Calculate(samples, IdleSeconds(7), Second));

        Assert.Equal(5, result.LongestGapSeconds);
    }

    [Fact]
    public void SessionTagsSamplesWithCurrentPhaseAndSkipsUnusedTypes()
    {
        var session = new MonitoringSession();
        session.BeginPhase(TestPhase.Idle);
        session.Record([Reading("/t", 40), Reading("/v", 1, type: "Voltage")]);
        session.BeginPhase(TestPhase.CpuStress);
        session.Record([Reading("/t", 80)]);

        var samples = session.Samples();

        Assert.Equal(2, samples.Count);
        Assert.Equal(TestPhase.Idle, samples[0].Phase);
        Assert.Equal(TestPhase.CpuStress, samples[1].Phase);
    }

    [Fact]
    public void SamplerSurvivesReadFailures()
    {
        using var source = new FlakySource();
        var session = new MonitoringSession();
        using var sampler = new SensorSampler(source, session, TimeSpan.FromMilliseconds(5));

        sampler.Start();
        Assert.True(source.ThirdRead.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        sampler.Stop();

        Assert.True(session.ReadFailures >= 1);
        Assert.True(session.ReadCount >= 1);
        Assert.Equal("broken", session.LastFailure);
    }

    [Theory]
    [InlineData("CPU Package", "Core Max", "CPU Package")]
    [InlineData("Core (Tctl/Tdie)", "Core Max", "Core (Tctl/Tdie)")]
    [InlineData("Core Max", "Core #1", "Core Max")]
    [InlineData("Core #1", "Core #2", null)]
    public void CpuTemperaturePrefersTheDocumentedSensors(string first, string second, string? expected)
    {
        SensorReading[] readings = [Reading("/1", 40, name: first), Reading("/2", 41, name: second)];

        Assert.Equal(expected, SensorSelector.CpuTemperatureName(readings));
    }

    [Fact]
    public void InvalidCpuTemperatureIsNotSelected()
    {
        SensorReading[] readings = [Reading("/1", 0, status: SensorStatus.Invalid)];

        Assert.Null(SensorSelector.CpuTemperature(readings));
    }

    [Fact]
    public void GpuHotSpotIsNotUsedAsCoreTemperature()
    {
        SensorReading[] readings =
        [
            Reading("/hot", 95, name: "GPU Hot Spot", hardwareType: "GpuNvidia", hardwareName: "NVIDIA RTX"),
            Reading("/core", 70, name: "GPU Core", hardwareType: "GpuNvidia", hardwareName: "NVIDIA RTX"),
        ];

        Assert.Equal(70, SensorSelector.GpuTemperature(readings, "NVIDIA RTX"));
    }

    [Fact]
    public void GpuSensorsBelongToTheMatchingModelOnly()
    {
        SensorReading[] readings =
        [
            Reading("/a", 60, name: "GPU Core", hardwareType: "GpuIntel", hardwareName: "Intel(R) Arc(TM) Pro B60 Graphics"),
            Reading("/b", 90, name: "GPU Core", hardwareType: "GpuNvidia", hardwareName: "NVIDIA RTX"),
        ];

        Assert.Equal(60, SensorSelector.GpuTemperature(readings, "Intel Arc Pro B60 Graphics"));
    }

    [Fact]
    public void IdenticalCardsUseTheHottestForProtection()
    {
        SensorReading[] readings =
        [
            Reading("/a", 60, name: "GPU Core", hardwareType: "GpuIntel", hardwareName: "Arc"),
            Reading("/b", 75, name: "GPU Core", hardwareType: "GpuIntel", hardwareName: "Arc"),
        ];

        Assert.Equal(75, SensorSelector.GpuTemperature(readings, "Arc"));
    }

    [Fact]
    public void LiveVoltageClockAndFansUseWhatCanBeRead()
    {
        SensorReading Sensor(string type, string hardware, string name, double? value, string hardwareName = "CPU", double? maximum = null) =>
            new($"/{hardware}", hardwareName, hardware, $"/{hardware}/{type}/{name}", name, type, value, "", value is null ? SensorStatus.NoValue : SensorStatus.Ok, "test", DateTimeOffset.UnixEpoch, maximum);

        SensorReading[] desktop =
        [
            Sensor("Voltage", "Cpu", "CPU Core", 1.25),
            Sensor("Clock", "Cpu", "P-Core #1", 5200),
            Sensor("Clock", "Cpu", "E-Core #1", 4400),
            Sensor("Clock", "Cpu", "Bus Speed", 100),
            Sensor("Clock", "GpuNvidia", "GPU Core", 2500, "RTX"),
            Sensor("Voltage", "GpuNvidia", "GPU Core", 1.05, "RTX"),
            Sensor("Fan", "SuperIO", "CPU Fan", 1100, maximum: 1300),
            Sensor("Fan", "SuperIO", "AIO Pump", 2800, maximum: 3000),
            Sensor("Fan", "SuperIO", "Chassis Fan #1", 900, maximum: 1200),
            Sensor("Fan", "SuperIO", "Fan #2", 0),
            Sensor("Fan", "GpuNvidia", "GPU Fan", 0, "RTX", 1000),
        ];

        Assert.Equal(1.25, SensorSelector.CpuVoltage(desktop));
        Assert.Equal(5200, SensorSelector.CpuClock(desktop));
        Assert.Equal(2500, SensorSelector.GpuClock(desktop, "RTX"));
        Assert.Equal(1.05, SensorSelector.GpuVoltage(desktop, "RTX"));
        Assert.Equal(new FanSpeeds(1100, 2800, 0, 900), SensorSelector.Fans(desktop));
        Assert.Equal(new FanMaxima(1300, 3000, 1000, 1200), SensorSelector.FanMaximums(desktop));

        // Laptop controllers name both fans, and unread clocks read 0.
        SensorReading[] laptop = [Sensor("Fan", "EmbeddedController", "CPU Fan", 3100), Sensor("Fan", "EmbeddedController", "GPU Fan", 2900), Sensor("Clock", "Cpu", "Core #1", 0)];
        Assert.Equal(new FanSpeeds(3100, null, 2900, null), SensorSelector.Fans(laptop));
        Assert.Null(SensorSelector.CpuClock(laptop));

        Assert.Equal(new FanSpeeds(null, null, null, null), SensorSelector.Fans([]));
        Assert.Equal(new FanMaxima(null, null, null, null), SensorSelector.FanMaximums([]));
    }

    [Fact]
    public void OnlyRealGraphicsCardsAreStressTargets()
    {
        GpuInfo[] gpus =
        [
            new("Microsoft", "Basic", 0, null, GpuKind.Software, "0:1"),
            new("Intel", "Integrated", 128, null, GpuKind.Integrated, "0:2"),
            new("Intel", "Arc", 8UL << 30, null, GpuKind.Unknown, "0:3"),
        ];

        Assert.Equal(["Arc"], GpuInfoCollector.StressTargets(gpus).Select(gpu => gpu.Model));
        Assert.Null(GpuInfoCollector.DefaultStressTarget(gpus[..2]));
    }

    [Fact]
    public void DiscreteGpuIsPreferredEvenWithLessMemory()
    {
        GpuInfo[] gpus =
        [
            new("AMD", "APU", 16UL << 30, null, GpuKind.Integrated, "0:1"),
            new("NVIDIA", "RTX", 8UL << 30, null, GpuKind.Discrete, "0:2"),
        ];

        Assert.Equal("RTX", GpuInfoCollector.DefaultStressTarget(gpus)?.Model);
    }

    [Theory]
    [InlineData(true, null, GpuKind.Software)]
    [InlineData(true, true, GpuKind.Software)]
    [InlineData(false, true, GpuKind.Integrated)]
    [InlineData(false, false, GpuKind.Discrete)]
    [InlineData(false, null, GpuKind.Unknown)]
    public void GpuKindFollowsUnifiedMemory(bool isSoftware, bool? hasUnifiedMemory, GpuKind expected)
    {
        Assert.Equal(expected, GpuInfoCollector.ClassifyKind(isSoftware, hasUnifiedMemory));
    }

    private sealed class FlakySource : ISensorSource
    {
        private int _reads;

        public ManualResetEventSlim ThirdRead { get; } = new();

        public IReadOnlyList<SensorReading> Read()
        {
            if (Interlocked.Increment(ref _reads) >= 3)
            {
                ThirdRead.Set();
            }

            return _reads % 2 == 1 ? throw new InvalidOperationException("broken") : [];
        }

        public void Dispose() => ThirdRead.Dispose();
    }
}
