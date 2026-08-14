using Ghostlist.Core;
using Microsoft.Win32;

namespace Ghostlist.Tests;

public sealed class BackupCatalogTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ghostlist-catalog-" + Guid.NewGuid().ToString("N"));

    private string Directory => Path.Combine(root, "Backups");

    private FileBackupSink Sink() => new(Directory, new NullUninstallRepository(), new InMemoryRegistryHiveAccessor());

    public void Dispose()
    {
        try { if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, recursive: true); }
        catch { }
    }

    [Fact]
    public void MissingDirectoryYieldsEmptyList() => Assert.Empty(new BackupCatalog(Directory).List());

    [Fact]
    public void RegistryTreeBackupIsDescribedFromContent()
    {
        var location = new RegistryLocation(RegistryHive.LocalMachine, RegistryView.Registry64, "Software\\Ghost\\App");
        Sink().SaveRegistryTree(new RegistryTreeBackup(location, "Hayalet Uygulama", DateTimeOffset.Now, []), "hayalet");

        var entry = Assert.Single(new BackupCatalog(Directory).List());
        Assert.Equal(BackupKinds.RegistryTree, entry.KindKey);
        Assert.Equal("Hayalet Uygulama", entry.DisplayName);
        Assert.Contains("Software\\Ghost\\App", entry.Target);
        Assert.True(entry.CanRestore);
        Assert.NotNull(entry.CreatedAt);
        Assert.True(entry.SizeBytes > 0);
    }

    [Fact]
    public void RegistryValueBackupIsDistinguishedFromTreeBackup()
    {
        var location = new RegistryLocation(RegistryHive.CurrentUser, RegistryView.Registry64, "Software\\Microsoft\\Windows\\CurrentVersion\\Run");
        Sink().SaveRegistryValue(
            new RegistryValueBackup(location, new RegistryValueSnapshot("GhostAgent", RegistryValueKind.String, "C:\\yok.exe"), DateTimeOffset.Now),
            "ghostagent");

        var entry = Assert.Single(new BackupCatalog(Directory).List());
        Assert.Equal(BackupKinds.RegistryValue, entry.KindKey);
        Assert.Equal("GhostAgent", entry.DisplayName);
        Assert.True(entry.CanRestore);
    }

    [Fact]
    public void FileBackupReportsOriginalPathAndCountsPayloadSize()
    {
        var source = Path.Combine(root, "kaynak", "hayalet.lnk");
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, new string('x', 500));

        Sink().MoveFileToBackup(source, "hayalet");

        var entry = Assert.Single(new BackupCatalog(Directory).List());
        Assert.Equal(BackupKinds.File, entry.KindKey);
        Assert.Equal(source, entry.Target);
        Assert.Equal("hayalet.lnk", entry.DisplayName);
        Assert.True(entry.CanRestore);
        Assert.True(entry.SizeBytes > 500);
    }

    [Fact]
    public void DirectoryBackupIsRecognised()
    {
        var source = Path.Combine(root, "kaynak", "HayaletKlasor");
        System.IO.Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "veri.txt"), "abc");

        Sink().MoveDirectoryToBackup(source, "hayaletklasor");

        var entry = Assert.Single(new BackupCatalog(Directory).List());
        Assert.Equal(BackupKinds.Directory, entry.KindKey);
        Assert.Equal(source, entry.Target);
        Assert.True(entry.CanRestore);
    }

    [Fact]
    public void MissingPayloadMakesEntryUnrestorableButStillListed()
    {
        var source = Path.Combine(root, "kaynak", "hayalet.lnk");
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "x");
        Sink().MoveFileToBackup(source, "hayalet");

        foreach (var file in System.IO.Directory.GetFiles(Path.Combine(Directory, "payload"))) File.Delete(file);

        var entry = Assert.Single(new BackupCatalog(Directory).List());
        Assert.Equal(BackupKinds.File, entry.KindKey);
        Assert.False(entry.CanRestore);
    }

    [Fact]
    public void CorruptBackupIsListedAsUnreadableInsteadOfCrashing()
    {
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(Path.Combine(Directory, "20260101-000000-bozuk-abc.json"), "{ bu json degil");
        File.WriteAllText(Path.Combine(Directory, "20260101-000001-bos-def.json"), string.Empty);

        var entries = new BackupCatalog(Directory).List();
        Assert.Equal(2, entries.Count);
        Assert.All(entries, x =>
        {
            Assert.Equal(BackupKinds.Unreadable, x.KindKey);
            Assert.False(x.CanRestore);
            Assert.False(x.IsReadable);
        });
    }

    [Fact]
    public void CorruptBackupDoesNotHideHealthyOnes()
    {
        var location = new RegistryLocation(RegistryHive.LocalMachine, RegistryView.Registry64, "Software\\Ghost");
        Sink().SaveRegistryTree(new RegistryTreeBackup(location, "Saglam", DateTimeOffset.Now, []), "saglam");
        File.WriteAllText(Path.Combine(Directory, "20260101-000000-bozuk-abc.json"), "{{{");

        var entries = new BackupCatalog(Directory).List();
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, x => x.DisplayName == "Saglam" && x.CanRestore);
    }

    [Fact]
    public void FileNameIsNotTrustedForKindOrName()
    {
        System.IO.Directory.CreateDirectory(Directory);
        var lying = Path.Combine(Directory, "20260101-000000-YanlisAd-abc.ghost.json");
        var manifest = new PathBackupManifest(
            FileBackupSink.FileKind, "C:\\Gercek\\hedef.lnk", Path.Combine(Directory, "payload", "yok.lnk"), DateTimeOffset.Now);
        File.WriteAllText(lying, System.Text.Json.JsonSerializer.Serialize(manifest));

        var entry = Assert.Single(new BackupCatalog(Directory).List());
        Assert.Equal(BackupKinds.File, entry.KindKey);
        Assert.Equal("hedef.lnk", entry.DisplayName);
        Assert.Equal("C:\\Gercek\\hedef.lnk", entry.Target);
    }

    [Fact]
    public void ListIsOrderedNewestFirst()
    {
        System.IO.Directory.CreateDirectory(Directory);
        Write("eski.json", new RegistryTreeBackup(Location(), "Eski", DateTimeOffset.Now.AddDays(-10), []));
        Write("yeni.json", new RegistryTreeBackup(Location(), "Yeni", DateTimeOffset.Now, []));

        var entries = new BackupCatalog(Directory).List();
        Assert.Equal("Yeni", entries[0].DisplayName);
        Assert.Equal("Eski", entries[1].DisplayName);
    }

    [Fact]
    public void TotalSizeAddsUpEveryEntry()
    {
        System.IO.Directory.CreateDirectory(Directory);
        Write("bir.json", new RegistryTreeBackup(Location(), "Bir", DateTimeOffset.Now, []));
        Write("iki.json", new RegistryTreeBackup(Location(), "Iki", DateTimeOffset.Now, []));

        var catalog = new BackupCatalog(Directory);
        Assert.Equal(catalog.List().Sum(x => x.SizeBytes), catalog.TotalSize());
    }

    [Fact]
    public void IsOlderThanUsesContentDate()
    {
        System.IO.Directory.CreateDirectory(Directory);
        Write("eski.json", new RegistryTreeBackup(Location(), "Eski", DateTimeOffset.Now.AddDays(-120), []));

        var entry = Assert.Single(new BackupCatalog(Directory).List());
        Assert.True(entry.IsOlderThan(TimeSpan.FromDays(BackupCatalog.StaleDays), DateTimeOffset.Now));
        Assert.False(entry.IsOlderThan(TimeSpan.FromDays(365), DateTimeOffset.Now));
    }

    [Fact]
    public void DeleteRemovesManifestAndPayload()
    {
        var source = Path.Combine(root, "kaynak", "hayalet.lnk");
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "x");
        Sink().MoveFileToBackup(source, "hayalet");

        var catalog = new BackupCatalog(Directory);
        var entry = Assert.Single(catalog.List());
        catalog.Delete(entry);

        Assert.Empty(catalog.List());
        Assert.Empty(System.IO.Directory.GetFiles(Path.Combine(Directory, "payload")));
    }

    [Fact]
    public void DeleteRefusesPathsOutsideTheBackupDirectory()
    {
        System.IO.Directory.CreateDirectory(Directory);
        var outside = Path.Combine(root, "disarida.json");
        File.WriteAllText(outside, "{}");

        var entry = new BackupCatalogEntry(outside, BackupKinds.RegistryTree, DateTimeOffset.Now, "x", "y", true, 2);
        Assert.Throws<InvalidOperationException>(() => new BackupCatalog(Directory).Delete(entry));
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void LargeBackupIsDescribedWithoutReadingEveryValue()
    {
        System.IO.Directory.CreateDirectory(Directory);
        var values = Enumerable.Range(0, 20000)
            .Select(i => new RegistryValueSnapshot($"Deger{i}", RegistryValueKind.String, new string('y', 200)))
            .ToList();
        Write("buyuk.json", new RegistryTreeBackup(Location(), "Buyuk Yedek", DateTimeOffset.Now, values));

        var entry = Assert.Single(new BackupCatalog(Directory).List());
        Assert.Equal(BackupKinds.RegistryTree, entry.KindKey);
        Assert.Equal("Buyuk Yedek", entry.DisplayName);
        Assert.True(entry.SizeBytes > 1_000_000);
    }

    private static RegistryLocation Location() =>
        new(RegistryHive.LocalMachine, RegistryView.Registry64, "Software\\Ghost");

    private void Write(string name, RegistryTreeBackup backup) =>
        File.WriteAllText(Path.Combine(Directory, name),
            System.Text.Json.JsonSerializer.Serialize(backup, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
}

internal sealed class NullUninstallRepository : IUninstallRepository
{
    public IReadOnlyList<UninstallEntry> Scan() => [];

    public RegistryTreeBackup Capture(UninstallEntry entry) =>
        new(entry.Location, entry.DisplayName, DateTimeOffset.Now, []);

    public void Delete(RegistryLocation location) { }

    public void Restore(RegistryTreeBackup backup) { }
}
