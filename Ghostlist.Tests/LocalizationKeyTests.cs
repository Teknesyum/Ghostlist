using System.Text.Json;

namespace Ghostlist.Tests;

public class LocalizationKeyTests
{
    private static string LocalizationDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Ghostlist.App", "Localization");
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Ghostlist.App/Localization was not found above the test output directory.");
    }

    private static Dictionary<string, string> Load(string language) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(LocalizationDirectory(), $"{language}.json")))!;

    [Fact]
    public void TurkishAndEnglishCarryTheSameKeys()
    {
        var turkish = Load("tr");
        var english = Load("en");

        Assert.Empty(turkish.Keys.Except(english.Keys, StringComparer.Ordinal));
        Assert.Empty(english.Keys.Except(turkish.Keys, StringComparer.Ordinal));
    }

    [Fact]
    public void NoTranslationIsEmpty()
    {
        foreach (var language in new[] { "tr", "en" })
            Assert.All(Load(language), pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), $"{language}:{pair.Key}"));
    }

    [Fact]
    public void PlaceholdersMatchBetweenLanguages()
    {
        var turkish = Load("tr");
        var english = Load("en");

        foreach (var (key, text) in turkish)
        {
            var expected = Placeholders(text);
            var actual = Placeholders(english[key]);
            Assert.True(expected.SetEquals(actual), $"{key}: tr [{string.Join(",", expected)}] vs en [{string.Join(",", actual)}]");
        }
    }

    [Fact]
    public void EveryBackupAndHistoryKeyExistsInBothLanguages()
    {
        var required = new[]
        {
            "tab.findings", "tab.backups",
            "backup.kind.registry_tree", "backup.kind.registry_value",
            "backup.kind.file", "backup.kind.directory", "backup.kind.unreadable",
            "backup.state.restorable", "backup.state.restored",
            "backup.state.payloadMissing", "backup.state.unreadable",
            "history.operation.fix", "history.operation.restore",
            "history.title", "history.reveal"
        };

        var turkish = Load("tr");
        var english = Load("en");
        foreach (var key in required)
        {
            Assert.True(turkish.ContainsKey(key), $"tr is missing {key}");
            Assert.True(english.ContainsKey(key), $"en is missing {key}");
        }
    }

    private static HashSet<string> Placeholders(string text) =>
        System.Text.RegularExpressions.Regex.Matches(text, @"\{(\w+)\}")
            .Select(x => x.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
}
