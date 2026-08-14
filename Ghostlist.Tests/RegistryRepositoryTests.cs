using System.Text.Json;
using Microsoft.Win32;
using Ghostlist.Core;

namespace Ghostlist.Tests;

public class RegistryRepositoryTests
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static (InMemoryRegistryHiveAccessor Accessor, WindowsUninstallRepository Repository) CreateFakeRegistry()
    {
        var accessor = new InMemoryRegistryHiveAccessor();
        using var key = accessor.CreateKey(RegistryHive.CurrentUser, RegistryView.Registry64, $@"{UninstallPath}\GhostApp");
        key.SetValue("DisplayName", "Ghost App", RegistryValueKind.String);
        key.SetValue("UninstallString", @"""C:\Ghost\unins000.exe"" /S", RegistryValueKind.String);
        key.SetValue("InstallLocation", @"%ProgramFiles%\Ghost", RegistryValueKind.ExpandString);
        key.SetValue("Modules", new[] { "core", "ui" }, RegistryValueKind.MultiString);
        key.SetValue("EstimatedSize", 4096, RegistryValueKind.DWord);
        key.SetValue("InstallDate", 20260814123045L, RegistryValueKind.QWord);
        key.SetValue("Thumbprint", new byte[] { 0x00, 0x1F, 0xFF, 0x42 }, RegistryValueKind.Binary);
        key.SetValue("", "varsayılan", RegistryValueKind.String);

        using var level1 = key.CreateSubKey("Components");
        level1.SetValue("Count", 2, RegistryValueKind.DWord);
        level1.SetValue("Path", @"%ProgramFiles%\Ghost\bin", RegistryValueKind.ExpandString);

        using var level2 = level1.CreateSubKey("Renderer");
        level2.SetValue("Blob", new byte[] { 0x10, 0x20 }, RegistryValueKind.Binary);
        level2.SetValue("Flags", new[] { "a", "b", "c" }, RegistryValueKind.MultiString);

        return (accessor, new WindowsUninstallRepository(accessor));
    }

    [Fact]
    public void CaptureDeleteRestoreRebuildsWholeSubtree()
    {
        var (accessor, repository) = CreateFakeRegistry();
        var entry = repository.Scan().Single();

        var captured = repository.Capture(entry);
        var persisted = JsonSerializer.Deserialize<RegistryTreeBackup>(JsonSerializer.Serialize(captured, JsonOptions), JsonOptions)!;

        repository.Delete(entry.Location);
        Assert.Null(accessor.OpenKey(entry.Location.Hive, entry.Location.View, entry.Location.SubKeyPath));

        repository.Restore(persisted);
        var recaptured = repository.Capture(entry);

        AssertSameValues(captured.Values, recaptured.Values);
        AssertSameChildren(captured.ChildKeys, recaptured.ChildKeys);
    }

    [Fact]
    public void CaptureRecordsNestedSubKeysAndValueKinds()
    {
        var (_, repository) = CreateFakeRegistry();
        var backup = repository.Capture(repository.Scan().Single());

        var components = Assert.Single(backup.ChildKeys);
        Assert.Equal("Components", components.Name);
        var renderer = Assert.Single(components.ChildKeys);
        Assert.Equal("Renderer", renderer.Name);
        Assert.Equal(RegistryValueKind.QWord, backup.Values.Single(x => x.Name == "InstallDate").Kind);
        Assert.Equal(Convert.ToBase64String([0x10, 0x20]), renderer.Values.Single(x => x.Name == "Blob").Value);
        Assert.Equal(@"%ProgramFiles%\Ghost", backup.Values.Single(x => x.Name == "InstallLocation").Value);
    }

    [Fact]
    public void LegacyBackupWithoutChildrenIsStillRestorable()
    {
        var accessor = new InMemoryRegistryHiveAccessor();
        var repository = new WindowsUninstallRepository(accessor);
        var location = new RegistryLocation(RegistryHive.CurrentUser, RegistryView.Registry64, $@"{UninstallPath}\LegacyApp");
        var legacyJson = $$"""
        {
          "Location": { "Hive": {{(int)RegistryHive.CurrentUser}}, "View": {{(int)RegistryView.Registry64}}, "SubKeyPath": "{{location.SubKeyPath.Replace("\\", "\\\\")}}" },
          "DisplayName": "Legacy App",
          "CreatedAt": "2026-01-01T00:00:00+00:00",
          "Values": [ { "Name": "DisplayName", "Kind": 1, "Value": "Legacy App" } ]
        }
        """;

        var backup = JsonSerializer.Deserialize<RegistryTreeBackup>(legacyJson, JsonOptions)!;
        Assert.Empty(backup.ChildKeys);

        repository.Restore(backup);

        using var restored = accessor.OpenKey(location.Hive, location.View, location.SubKeyPath)!;
        Assert.Equal("Legacy App", restored.GetValue("DisplayName"));
        Assert.Empty(restored.GetSubKeyNames());
    }

    [Fact]
    public void DeleteRemovesSubKeysToo()
    {
        var (accessor, repository) = CreateFakeRegistry();
        var entry = repository.Scan().Single();

        repository.Delete(entry.Location);

        using var uninstallRoot = accessor.OpenKey(entry.Location.Hive, entry.Location.View, UninstallPath)!;
        Assert.Empty(uninstallRoot.GetSubKeyNames());
    }

    [Fact]
    public void ScanReadsUnexpandedValuesSoViewAwareExpansionCanRun()
    {
        var (_, repository) = CreateFakeRegistry();
        var entry = repository.Scan().Single();

        Assert.Equal("Ghost App", entry.DisplayName);
        Assert.Equal(@"%ProgramFiles%\Ghost", entry.InstallLocation);
    }

    private static void AssertSameChildren(IReadOnlyList<RegistryKeySnapshot> expected, IReadOnlyList<RegistryKeySnapshot> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        foreach (var item in expected)
        {
            var match = actual.Single(x => x.Name == item.Name);
            AssertSameValues(item.Values, match.Values);
            AssertSameChildren(item.ChildKeys, match.ChildKeys);
        }
    }

    private static void AssertSameValues(IReadOnlyList<RegistryValueSnapshot> expected, IReadOnlyList<RegistryValueSnapshot> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        foreach (var item in expected)
        {
            var match = actual.Single(x => x.Name == item.Name);
            Assert.Equal(item.Kind, match.Kind);
            Assert.Equal(Describe(item.Value), Describe(match.Value));
        }
    }

    private static string Describe(object? value) => value switch
    {
        null => "<null>",
        string[] items => string.Join('|', items),
        byte[] bytes => Convert.ToBase64String(bytes),
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "<null>"
    };
}
