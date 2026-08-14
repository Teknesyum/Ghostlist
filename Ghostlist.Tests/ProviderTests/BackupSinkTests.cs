using Microsoft.Win32;
using Ghostlist.Core;
using Ghostlist.Tests.ClassifierTests;

namespace Ghostlist.Tests.ProviderTests;

public class BackupSinkTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GhostlistTests", Guid.NewGuid().ToString("N"));
    private readonly string backupDirectory;
    private readonly string workspace;
    private readonly InMemoryRegistryHiveAccessor accessor = new();

    public BackupSinkTests()
    {
        backupDirectory = Path.Combine(root, "Backups");
        workspace = Path.Combine(root, "Workspace");
        Directory.CreateDirectory(workspace);
    }

    [Fact]
    public void FileFixIsAMoveIntoTheBackupAndRestorePutsItBack()
    {
        var sink = Sink();
        var file = Path.Combine(workspace, "Dead.lnk");
        File.WriteAllText(file, "shortcut");

        var manifest = sink.MoveFileToBackup(file, "Dead");

        Assert.False(File.Exists(file));
        Assert.True(File.Exists(manifest));

        sink.Restore(manifest);

        Assert.True(File.Exists(file));
        Assert.Equal("shortcut", File.ReadAllText(file));
        Assert.False(File.Exists(manifest));
    }

    [Fact]
    public void DirectoryFixIsAMoveAndRestoreRebuildsTheWholeFolder()
    {
        var sink = Sink();
        var folder = Path.Combine(workspace, "GhostApp");
        Directory.CreateDirectory(Path.Combine(folder, "data"));
        File.WriteAllText(Path.Combine(folder, "data", "readme.txt"), "leftover");

        var manifest = sink.MoveDirectoryToBackup(folder, "GhostApp");

        Assert.False(Directory.Exists(folder));

        sink.Restore(manifest);

        Assert.Equal("leftover", File.ReadAllText(Path.Combine(folder, "data", "readme.txt")));
    }

    [Fact]
    public void RegistryValueBackupIsRestoredAsASingleValue()
    {
        var sink = Sink();
        var location = new RegistryLocation(RegistryHive.CurrentUser, RegistryView.Registry64, @"SOFTWARE\Ghost\Run");
        var snapshot = new RegistryValueSnapshot("GhostAgent", RegistryValueKind.String, @"C:\Gone\agent.exe");

        var path = sink.SaveRegistryValue(new RegistryValueBackup(location, snapshot, DateTimeOffset.Now), "GhostAgent");
        sink.Restore(path);

        using var key = accessor.OpenKey(location.Hive, location.View, location.SubKeyPath)!;
        Assert.Equal(@"C:\Gone\agent.exe", key.GetValue("GhostAgent"));
    }

    [Fact]
    public void BackupsOutsideTheGhostlistDirectoryAreRefused()
    {
        var outside = Path.Combine(root, "elsewhere.json");
        File.WriteAllText(outside, "{}");

        Assert.Throws<InvalidOperationException>(() => Sink().Restore(outside));
    }

    [Fact]
    public void EveryProviderIsRegisteredAndScanCollectsAllCategories()
    {
        var service = CleanupService.CreateDefault(backupDirectory);

        var categories = service.Providers.Select(x => x.Category).ToList();

        Assert.Equal(
            [Categories.Uninstall, Categories.Shortcut, Categories.Startup, Categories.Task, Categories.Folder, Categories.Msix],
            categories);
        Assert.Equal(categories.Count, categories.Distinct().Count());
    }

    private FileBackupSink Sink() => new(backupDirectory, new UnusedRepository(), accessor);

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private sealed class UnusedRepository : IUninstallRepository
    {
        public IReadOnlyList<UninstallEntry> Scan() => [];
        public RegistryTreeBackup Capture(UninstallEntry entry) => throw new NotSupportedException();
        public void Delete(RegistryLocation location) => throw new NotSupportedException();
        public void Restore(RegistryTreeBackup backup) => throw new NotSupportedException();
    }
}
