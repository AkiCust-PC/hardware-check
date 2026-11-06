using System.Text.RegularExpressions;
using AkikazuHardwareCheck.Core.Localization;

namespace AkikazuHardwareCheck.Core.Tests;

/// <summary>
/// The four language files can agree with each other and still miss a key that the code asks for. A missing key throws
/// at runtime, so every literal indexer argument under src is checked against the language files here.
/// </summary>
public partial class LocalizationUsageTests
{
    [Fact]
    public void EveryLiteralKeyUsedInSourceExistsInEveryLanguage()
    {
        var root = FindRepositoryRoot();
        var sources = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        var used = sources
            .SelectMany(path => KeyPattern().Matches(File.ReadAllText(path)).Select(match => match.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(used);
        foreach (var language in AppLanguage.Supported)
        {
            var translator = Translator.Load(language);
            var keys = translator.Keys.ToHashSet(StringComparer.Ordinal);
            var missing = used.Where(key => !keys.Contains(key)).Order(StringComparer.Ordinal).ToArray();
            Assert.True(missing.Length == 0, $"{language} is missing: {string.Join(", ", missing)}");
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AkikazuHardwareCheck.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"Could not find the repository root above {AppContext.BaseDirectory}.");
    }

    [GeneratedRegex(@"(?:\b_t|\bt|translator)\[""([^""]+)""\]")]
    private static partial Regex KeyPattern();
}
