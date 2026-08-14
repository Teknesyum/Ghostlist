using Microsoft.Win32;
using Ghostlist.Core;
using Ghostlist.Tests.ClassifierTests;

namespace Ghostlist.Tests;

public class CleanupServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "GhostlistTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void BrokenEntryIsBackedUpBeforeDeletionAndCanBeRestored()
    {
        var repository = new FakeRepository();
        var service = Create(repository);
        var finding = service.Scan().Single();

        Assert.Equal(EntryStatus.Broken, finding.Status);
        var result = service.Fix(finding);

        Assert.True(result.Success);
        Assert.True(File.Exists(result.BackupPath));
        Assert.True(repository.Deleted);
        service.Restore(result.BackupPath!);
        Assert.True(repository.Restored);
    }

    [Fact]
    public void HealthyOrSuspiciousFindingIsNotEligibleForFixing()
    {
        var repository = new FakeRepository();
        var service = Create(repository);
        var finding = service.Scan().Single() with { Status = EntryStatus.Suspicious };

        var result = service.Fix(finding);

        Assert.False(result.Success);
        Assert.Equal(FixResultKeys.NotEligible, result.ResultKey);
        Assert.False(repository.Deleted);
    }

    [Fact]
    public void BackupOutsideGhostlistDirectoryCannotBeRestored()
    {
        var repository = new FakeRepository();
        var service = Create(repository);
        var outside = Path.Combine(Path.GetDirectoryName(directory)!, "GhostlistTests-Outside", "backup.json");

        Assert.Throws<InvalidOperationException>(() => service.Restore(outside));
        Assert.False(repository.Restored);
    }

    [Fact]
    public void ScanCollectsEveryRegisteredProviderAndKeepsCategoriesSeparate()
    {
        var repository = new FakeRepository();
        var service = new CleanupService(
            [new UninstallEntryProvider(repository, Classifier()), new AppxProvider(new EmptyAppxCatalog(), new FakeFileSystem())],
            new FileBackupSink(directory, repository));

        var findings = service.Scan();

        Assert.Equal(2, service.Providers.Count);
        Assert.Equal(Categories.Uninstall, service.CategoryOf(findings.Single()));
    }

    [Fact]
    public void AutoFixOnlyPicksFindingsThatPassTheThresholdAndCategoryGate()
    {
        var repository = new FakeRepository();
        var service = Create(repository);
        var finding = service.Scan().Single();

        Assert.True(service.IsAutoFixable(finding));
        Assert.Empty(service.AutoFixable([finding with { Confidence = ConfidenceRules.AutoFixThreshold - 1 }]));
        Assert.Empty(service.AutoFixable([finding with { Evidence = [finding.Evidence[0]] }]));
    }

    private CleanupService Create(FakeRepository repository) =>
        new([new UninstallEntryProvider(repository, Classifier())], new FileBackupSink(directory, repository));

    private static EntryClassifier Classifier() => new(new FakeFileSystem());

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    private sealed class EmptyAppxCatalog : IAppxCatalog
    {
        public IReadOnlyList<AppxPackage> GetStagedPackages() => [];
    }

    private sealed class FakeRepository : IUninstallRepository
    {
        private readonly RegistryLocation location = new(RegistryHive.CurrentUser, RegistryView.Registry64, @"SOFTWARE\Test\Broken");
        public UninstallEntry Entry => new("id", "Broken App", null, null, @"C:\Gone\unins000.exe", null, null, false, false, location);
        public bool Deleted { get; private set; }
        public bool Restored { get; private set; }
        public IReadOnlyList<UninstallEntry> Scan() => [Entry];
        public RegistryTreeBackup Capture(UninstallEntry entry) => new(location, entry.DisplayName, DateTimeOffset.Now, [new("DisplayName", RegistryValueKind.String, entry.DisplayName)]);
        public void Delete(RegistryLocation _) => Deleted = true;
        public void Restore(RegistryTreeBackup _) => Restored = true;
    }
}
