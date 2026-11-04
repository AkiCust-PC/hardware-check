using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Models;
using AkikazuHardwareCheck.Core.Reporting;
using AkikazuHardwareCheck.Core.Testing;

namespace AkikazuHardwareCheck.Core.Tests;

public class ToolTestTests
{
    private const int WORDS = 1024;

    [Fact]
    public void OwnAddressFindsAFlippedBit()
    {
        var words = new ulong[WORDS];
        MemoryTest.FillOwnAddress(words, 100);
        Assert.Equal(0, MemoryTest.CountOwnAddressErrors(words, 100));

        words[7] ^= 1UL << 12;

        Assert.Equal(1, MemoryTest.CountOwnAddressErrors(words, 100));
    }

    [Fact]
    public void RandomSequenceIsReproducibleAndFindsAFlippedBit()
    {
        var words = new ulong[WORDS];
        MemoryTest.FillRandom(words, 42);
        Assert.Equal(0, MemoryTest.CountRandomErrors(words, 42));

        words[500] ^= 1;

        Assert.Equal(1, MemoryTest.CountRandomErrors(words, 42));
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(ulong.MaxValue)]
    public void MovingInversionPassesOnHealthyMemoryAndLeavesTheComplement(ulong pattern)
    {
        var words = new ulong[WORDS];

        Assert.Equal(0, MemoryTest.MovingInversion(words, pattern));
        Assert.All(words, word => Assert.Equal(~pattern, word));
    }

    [Fact]
    public void PlanUsesTheFractionAndWholeChunks()
    {
        const ulong GIB = 1UL << 30;

        Assert.Equal(7 * GIB, MemoryTest.PlanBytes(10 * GIB, 0.7) / (256UL << 20) * (256UL << 20));
        Assert.Equal(0UL, MemoryTest.PlanBytes(100, 0.7));
    }

    [Fact]
    public async Task MemoryTestRunsAndStops()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var task = new MemoryTest(TimeSpan.FromSeconds(30), availableFraction: 0.01).RunAsync(null, stop.Token);

        stop.CancelAfter(TimeSpan.FromMilliseconds(300));
        var result = await task;

        Assert.Contains(result.End, new[] { MemoryTestEnd.Stopped, MemoryTestEnd.Unsupported });
        Assert.Equal(0, result.Errors);
    }

    [Theory]
    [InlineData(10L * (1L << 30), 1L << 30, true)]
    [InlineData(5L * (1L << 30), 1L << 30, false)]
    public void DiskTestKeepsSpareRoom(long free, long size, bool expected)
    {
        Assert.Equal(expected, DiskTest.HasRoomFor(free, size));
    }

    [Fact]
    public void BlockVerificationCatchesWrongBlockAndWrongData()
    {
        var pattern = new byte[64];
        new Random(1).NextBytes(pattern);
        var block = (byte[])pattern.Clone();
        BitConverter.TryWriteBytes(block, 5L);

        Assert.True(DiskTest.IsBlockValid(block, 5, pattern));
        Assert.False(DiskTest.IsBlockValid(block, 6, pattern));

        block[40] ^= 1;
        Assert.False(DiskTest.IsBlockValid(block, 5, pattern));
    }

    [Theory]
    [InlineData(@"C:\", @"C:\Users\A\Temp", @"C:\Users\A\Temp")]
    [InlineData(@"D:\", @"C:\Users\A\Temp", @"D:\")]
    public void DiskTestPrefersTheTempFolderOnItsOwnDrive(string root, string temp, string expected)
    {
        Assert.Equal(expected, DiskTest.FolderForDrive(root, temp));
    }

    [Fact]
    public async Task DiskTestWritesVerifiesAndLeavesNoFile()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, $"test-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var result = await new DiskTest(folder, sizeBytes: 8L * 1024 * 1024).RunAsync(null, TestContext.Current.CancellationToken);

            Assert.True(result.End == DiskTestEnd.Completed, $"{result.End}: {result.Detail}");
            Assert.Equal(0, result.VerificationErrors);
            Assert.NotNull(result.SequentialWriteMbps);
            Assert.NotNull(result.SequentialReadMbps);
            Assert.NotNull(result.RandomReadMbps);
            Assert.Empty(Directory.GetFiles(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task StoppingTheDiskTestLeavesNoFile()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, $"test-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            using var stop = new CancellationTokenSource();
            await stop.CancelAsync();

            var result = await new DiskTest(folder, sizeBytes: 8L * 1024 * 1024).RunAsync(null, stop.Token);

            Assert.Equal(DiskTestEnd.Stopped, result.End);
            Assert.Empty(Directory.GetFiles(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task BadFolderIsReportedRatherThanThrown()
    {
        var result = await new DiskTest(@"Q:\no-such-folder-here", 1024).RunAsync(null, TestContext.Current.CancellationToken);

        Assert.Contains(result.End, new[] { DiskTestEnd.Failed, DiskTestEnd.NotEnoughSpace });
    }

    [Theory]
    [MemberData(nameof(LocalizationTests.Languages), MemberType = typeof(LocalizationTests))]
    public void EveryResultRendersInEveryLanguage(string language)
    {
        var t = Translator.Load(language);
        foreach (var end in Enum.GetValues<MemoryTestEnd>())
        {
            Assert.NotEmpty(ToolTestSummary.Text(t, new MemoryTestResult(end, 1UL << 30, 2, 1, 1, "x")));
        }

        foreach (var end in Enum.GetValues<DiskTestEnd>())
        {
            Assert.NotEmpty(ToolTestSummary.Text(t, new DiskTestResult(end, @"C:\", 1, 1, 2, 3, 1, "x")));
        }
    }

    [Fact]
    public void MemoryErrorsMakeTheReportNeedReview()
    {
        var t = Translator.Load(AppLanguage.ENGLISH);
        var hardware = new HardwareInventory(new ProbeEnvironment(null, WindowsActivation.Unknown, false, false), [], [], null, null, [], [], []);
        var report = new CheckReport(3, "1", DateTimeOffset.UnixEpoch, hardware, [], new CoolingEstimate(CoolingOutcome.NoData, null, null, 0, []), MemoryTest: new MemoryTestResult(MemoryTestEnd.ErrorsFound, 1UL << 30, 1, 3, 1, null));

        var html = new HtmlReportGenerator(t).Generate(report);

        Assert.Contains(t["report.conclusion.needsReview"], html);
        Assert.Contains(t["ui.memoryTest"], html);
    }

    [Fact]
    public void ACleanMemoryTestAloneDoesNotClaimAFullCheck()
    {
        var t = Translator.Load(AppLanguage.ENGLISH);
        var hardware = new HardwareInventory(new ProbeEnvironment(null, WindowsActivation.Unknown, false, false), [], [], null, null, [], [], []);
        var report = new CheckReport(3, "1", DateTimeOffset.UnixEpoch, hardware, [], new CoolingEstimate(CoolingOutcome.NoData, null, null, 0, []), MemoryTest: new MemoryTestResult(MemoryTestEnd.Completed, 1UL << 30, 1, 0, 1, null));

        var html = new HtmlReportGenerator(t).Generate(report);

        Assert.Contains(t["report.conclusion.notRun"], html);
        Assert.DoesNotContain(t["report.conclusion.noObviousIssue"], html);
    }
}
