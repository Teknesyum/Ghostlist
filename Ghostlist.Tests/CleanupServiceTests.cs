using Microsoft.Win32;
using Ghostlist.Core;

namespace Ghostlist.Tests;

public class CleanupServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "GhostlistTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void BrokenEntryIsBackedUpBeforeDeletionAndCanBeRestored()
    {
        var repository = new FakeRepository();
        var service = new CleanupService(repository, new EntryClassifier(new MissingFileSystem()), directory);
        var entry = service.Scan().Single();

        var backup = service.RemoveBrokenEntry(entry);

        Assert.True(File.Exists(backup));
        Assert.True(repository.Deleted);
        service.Restore(backup);
        Assert.True(repository.Restored);
    }

    [Fact]
    public void HealthyOrUnclassifiedEntryCannotBeRemoved()
    {
        var repository = new FakeRepository();
        var service = new CleanupService(repository, new EntryClassifier(new MissingFileSystem()), directory);
        Assert.Throws<InvalidOperationException>(() => service.RemoveBrokenEntry(repository.Entry));
        Assert.False(repository.Deleted);
    }

    [Fact]
    public void BackupOutsideGhostlistDirectoryCannotBeRestored()
    {
        var repository = new FakeRepository();
        var service = new CleanupService(repository, new EntryClassifier(new MissingFileSystem()), directory);
        var outside = Path.Combine(Path.GetDirectoryName(directory)!, "GhostlistTests-Outside", "backup.json");

        Assert.Throws<InvalidOperationException>(() => service.Restore(outside));
        Assert.False(repository.Restored);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    private sealed class MissingFileSystem : IFileSystem { public bool FileExists(string path) => false; }

    private sealed class FakeRepository : IUninstallRepository
    {
        private readonly RegistryLocation location = new(RegistryHive.CurrentUser, RegistryView.Registry64, @"SOFTWARE\Test\Broken");
        public UninstallEntry Entry => new("id", "Broken App", null, null, "C:\\Gone\\unins000.exe", null, null, false, false, location);
        public bool Deleted { get; private set; }
        public bool Restored { get; private set; }
        public IReadOnlyList<UninstallEntry> Scan() => [Entry];
        public RegistryTreeBackup Capture(UninstallEntry entry) => new(location, entry.DisplayName, DateTimeOffset.Now, [new("DisplayName", RegistryValueKind.String, entry.DisplayName)]);
        public void Delete(RegistryLocation _) => Deleted = true;
        public void Restore(RegistryTreeBackup _) => Restored = true;
    }
}
