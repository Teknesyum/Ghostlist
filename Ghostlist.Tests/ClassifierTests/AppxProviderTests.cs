using Microsoft.Win32;
using Ghostlist.Core;

namespace Ghostlist.Tests.ClassifierTests;

public class AppxProviderTests
{
    private const string PackageRepository =
        @"SOFTWARE\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\PackageRepository\Packages";
    private const string FullName = "Teknesyum.Ghost_1.0.0.0_x64__8wekyb3d8bbwe";
    private const string StoreRoot = @"C:\Program Files\WindowsApps";
    private static readonly string PackageFolder = Path.Combine(StoreRoot, FullName);

    [Fact]
    public void StagedPackageWithAMissingFolderInAnIntactStoreIsBroken()
    {
        var fileSystem = new FakeFileSystem().WithDirectory(StoreRoot);

        var finding = Assert.Single(Provider(fileSystem).Scan());

        Assert.Equal(EntryStatus.Broken, finding.Status);
        Assert.Equal(95, finding.Confidence);
        Assert.Contains(finding.Evidence, x => x.Kind == EvidenceKinds.AppxInstallFolderMissing);
        Assert.Contains(finding.Evidence, x => x.Kind == EvidenceKinds.AppxStoreRootPresent);
    }

    [Fact]
    public void MissingFolderInAnUnreadableStoreStaysSuspicious()
    {
        var fileSystem = new FakeFileSystem().WithDirectory(StoreRoot, ProbeResult.Unknown);

        var finding = Assert.Single(Provider(fileSystem).Scan());

        Assert.Equal(EntryStatus.Suspicious, finding.Status);
        Assert.Contains(finding.Evidence, x => x.Kind == EvidenceKinds.AppxStoreRootUnreadable);
    }

    [Fact]
    public void UnreadablePackageFolderProducesNoFindingAtAll()
    {
        var fileSystem = new FakeFileSystem().WithDirectory(PackageFolder, ProbeResult.Unknown);

        Assert.Empty(Provider(fileSystem).Scan());
    }

    [Fact]
    public void HealthyPackageProducesNoFinding()
    {
        var fileSystem = new FakeFileSystem()
            .WithDirectory(StoreRoot)
            .WithDirectory(PackageFolder)
            .WithFile(Path.Combine(PackageFolder, "AppxManifest.xml"));

        Assert.Empty(Provider(fileSystem).Scan());
    }

    [Fact]
    public void EmptyPackageFolderWithoutAManifestIsBroken()
    {
        var fileSystem = new FakeFileSystem()
            .WithDirectory(StoreRoot)
            .WithDirectory(PackageFolder)
            .WithListing(PackageFolder, []);

        var finding = Assert.Single(Provider(fileSystem).Scan());

        Assert.Equal(EntryStatus.Broken, finding.Status);
        Assert.Contains(finding.Evidence, x => x.Kind == EvidenceKinds.AppxManifestMissing);
        Assert.Contains(finding.Evidence, x => x.Kind == EvidenceKinds.AppxPackageFolderEmpty);
    }

    [Fact]
    public void PopulatedFolderWithoutAManifestIsOnlySuspicious()
    {
        var fileSystem = new FakeFileSystem()
            .WithDirectory(StoreRoot)
            .WithDirectory(PackageFolder)
            .WithListing(PackageFolder, [Path.Combine(PackageFolder, "ghost.exe")]);

        var finding = Assert.Single(Provider(fileSystem).Scan());

        Assert.Equal(EntryStatus.Suspicious, finding.Status);
    }

    [Fact]
    public void FixOnlyShowsTheRemovalCommandAndTouchesNothing()
    {
        var accessor = new InMemoryRegistryHiveAccessor();
        var provider = Provider(new FakeFileSystem().WithDirectory(StoreRoot), accessor);
        var finding = Assert.Single(provider.Scan());
        var sink = new RecordingBackupSink();

        var result = provider.Fix(finding, sink);

        Assert.False(result.Success);
        Assert.Equal(FixResultKeys.ManualCommandRequired, result.ResultKey);
        Assert.Equal($"Remove-AppxPackage -Package {FullName}", result.ManualCommand);
        Assert.False(sink.Used);
        using var repository = accessor.OpenKey(RegistryHive.LocalMachine, RegistryView.Registry64, $@"{PackageRepository}\{FullName}");
        Assert.NotNull(repository);
    }

    [Fact]
    public void CategoryAndProviderIdAreTheLanguageIndependentMsixKey()
    {
        var provider = Provider(new FakeFileSystem());

        Assert.Equal(Categories.Msix, provider.Id);
        Assert.Equal(Categories.Msix, provider.Category);
    }

    private static AppxProvider Provider(FakeFileSystem fileSystem, InMemoryRegistryHiveAccessor? accessor = null)
    {
        accessor ??= new InMemoryRegistryHiveAccessor();
        using var key = accessor.CreateKey(RegistryHive.LocalMachine, RegistryView.Registry64, $@"{PackageRepository}\{FullName}");
        key.SetValue("Path", PackageFolder, RegistryValueKind.String);
        return new AppxProvider(new RegistryAppxCatalog(accessor), fileSystem);
    }

    private sealed class RecordingBackupSink : IBackupSink
    {
        public bool Used { get; private set; }
        public string SaveRegistryTree(RegistryTreeBackup backup, string label) { Used = true; return string.Empty; }
        public string MoveFileToBackup(string sourcePath, string label) { Used = true; return string.Empty; }
        public string SaveText(string content, string label, string extension) { Used = true; return string.Empty; }
        public void Restore(string backupPath) => Used = true;
        public IReadOnlyList<string> List() => [];
    }
}
