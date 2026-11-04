using System.Reflection;
using System.Text;
using AkikazuHardwareCheck.Core.Hardware;
using AkikazuHardwareCheck.Core.Localization;
using AkikazuHardwareCheck.Core.Models;
using AkikazuHardwareCheck.Core.Reporting;
using AkikazuHardwareCheck.Core.Testing;

namespace AkikazuHardwareCheck.Core.Tests;

/// <summary>Guards against garbled text: all four languages must display on any Windows language or code page.</summary>
public class EncodingTests
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static CheckReport Report() => new(
        CheckReport.CURRENT_SCHEMA_VERSION,
        "1.0.0",
        DateTimeOffset.UnixEpoch,
        new HardwareInventory(
            new ProbeEnvironment(new OperatingSystemInfo("Windows 11 专业版", "25H2", "1", "X64"), WindowsActivation.Activated, true, true),
            [],
            [],
            new MotherboardInfo("華碩", "マザーボード 메모", null, null),
            null,
            [],
            [],
            []),
        [],
        new CoolingEstimate(CoolingOutcome.NoData, null, null, 0, []));

    [Theory]
    [MemberData(nameof(LocalizationTests.Languages), MemberType = typeof(LocalizationTests))]
    public void LanguageFilesAreStrictUtf8WithoutReplacementCharacters(string language)
    {
        using var stream = typeof(Translator).Assembly.GetManifestResourceStream($"i18n.{language}.json");
        Assert.NotNull(stream);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);

        var text = StrictUtf8.GetString(copy.ToArray());

        Assert.DoesNotContain('�', text);
        Assert.False(text.StartsWith('﻿'));
    }

    [Theory]
    [MemberData(nameof(LocalizationTests.Languages), MemberType = typeof(LocalizationTests))]
    public void ReportDeclaresUtf8AndKeepsEveryScript(string language)
    {
        var html = new HtmlReportGenerator(Translator.Load(language)).Generate(Report());

        Assert.Contains("<meta charset=\"utf-8\">", html);
        Assert.Contains($"<html lang=\"{language}\">", html);
        Assert.Contains("マザーボード", html);
        Assert.Contains("메모", html);
    }

    [Fact]
    public void SavedFilesAreUtf8AndReadBackUnchanged()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, $"test-output-{Guid.NewGuid():N}");
        try
        {
            var saved = ReportStore.Save(Report(), folder, Translator.Load(AppLanguage.JAPANESE));

            Assert.Contains("マザーボード 메모", StrictUtf8.GetString(File.ReadAllBytes(saved.HtmlPath)));
            Assert.Contains("Windows 11 专业版", StrictUtf8.GetString(File.ReadAllBytes(saved.JsonPath)));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void BuiltInInstallerMatchesThePinnedHash()
    {
        var app = Assembly.GetAssembly(typeof(PawnIoSetup));
        using var stream = app?.GetManifestResourceStream("pawnio.setup.exe");
        Assert.NotNull(stream);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);

        Assert.True(PawnIoSetup.MatchesPinnedInstaller(copy.ToArray()));
        Assert.False(PawnIoSetup.MatchesPinnedInstaller([1, 2, 3]));
    }

    [Theory]
    [InlineData(0, DriverSetupResult.Installed)]
    [InlineData(3010, DriverSetupResult.RebootRequired)]
    [InlineData(5, DriverSetupResult.Failed)]
    public void InstallerExitCodesAreMapped(int code, DriverSetupResult expected)
    {
        Assert.Equal(expected, PawnIoSetup.MapExitCode(code));
    }

    [Fact]
    public void DriverIsNeverInstalledTwiceOrWithoutRights()
    {
        Assert.Equal(DriverSetupResult.AlreadyInstalled, PawnIoSetup.Install(isAdministrator: true, isInstalled: true).Result);
        Assert.Equal(DriverSetupResult.NeedsAdministrator, PawnIoSetup.Install(isAdministrator: false, isInstalled: false).Result);
    }

    [Theory]
    [InlineData(1u, "1.2")]
    [InlineData(2u, "2.0")]
    [InlineData(9u, null)]
    public void TpmVersionsAreMapped(uint version, string? expected)
    {
        Assert.Equal(expected, SecurityCollector.MapTpmVersion(version));
    }

    [Fact]
    public void TestToneIsAValidStereoWave()
    {
        var wave = TestTone.CreateWave();

        Assert.Equal("RIFF", Encoding.ASCII.GetString(wave, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wave, 8, 4));
        Assert.Equal((short)2, BitConverter.ToInt16(wave, 22));
        Assert.Equal(wave.Length - 8, BitConverter.ToInt32(wave, 4));
        Assert.Equal(wave.Length - 44, BitConverter.ToInt32(wave, 40));

        // The first tone is on the left channel only.
        var leftMiddle = BitConverter.ToInt16(wave, 44 + (4 * 1000));
        var rightMiddle = BitConverter.ToInt16(wave, 44 + (4 * 1000) + 2);
        Assert.NotEqual((short)0, leftMiddle);
        Assert.Equal((short)0, rightMiddle);
    }
}
