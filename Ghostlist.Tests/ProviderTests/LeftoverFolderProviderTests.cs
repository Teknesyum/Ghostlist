using Microsoft.Win32;
using Ghostlist.Core;
using Ghostlist.Tests.ClassifierTests;

namespace Ghostlist.Tests.ProviderTests;

public class LeftoverFolderProviderTests
{
    private const string ProgramFiles = @"C:\Program Files";
    private const string Leftover = @"C:\Program Files\GhostApp";
    private static readonly DateTimeOffset Now = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LongAgo = Now.AddDays(-400);

    [Fact]
    public void UnownedStaleFolderWithoutExecutablesIsReportedButCappedBelowAutoFix()
    {
        var finding = Assert.Single(Provider(StaleLeftover()).Scan());

        Assert.Equal(Categories.Folder, finding.ProviderId);
        Assert.Equal(EntryStatus.Broken, finding.Status);
        Assert.Equal(ConfidenceRules.LeftoverFolderCeiling, finding.Confidence);
        Assert.False(ConfidenceRules.IsAutoFixable(finding, Categories.Folder));
    }

    [Fact]
    public void FolderOwnedByAnInstalledEntryIsNeverReported()
    {
        var repository = new FakeRepository(installLocation: Leftover);

        Assert.Empty(Provider(StaleLeftover(), repository).Scan());
    }

    [Fact]
    public void FolderOwnedThroughTheUninstallerPathIsNeverReported()
    {
        var repository = new FakeRepository(uninstallString: $@"""{Leftover}\unins000.exe"" /S");

        Assert.Empty(Provider(StaleLeftover(), repository).Scan());
    }

    [Fact]
    public void FolderContainingAnExecutableIsNeverReported()
    {
        var fileSystem = StaleLeftover([Path.Combine(Leftover, "readme.txt"), Path.Combine(Leftover, "app.exe")]);

        Assert.Empty(Provider(fileSystem).Scan());
    }

    [Fact]
    public void RecentlyTouchedFolderIsNeverReported()
    {
        var fileSystem = StaleLeftover();
        fileSystem.WithLastWrite(Leftover, Now.AddDays(-10));

        Assert.Empty(Provider(fileSystem).Scan());
    }

    [Fact]
    public void UnreadableFolderProducesNoFindingInsteadOfAFalsePositive()
    {
        var fileSystem = new FakeFileSystem()
            .WithDirectory(ProgramFiles)
            .WithDirectory(Leftover)
            .WithListing(ProgramFiles, [Leftover])
            .WithListing(Leftover, null);

        Assert.Empty(Provider(fileSystem).Scan());
    }

    [Fact]
    public void NoProviderRootEverPointsAtUserData()
    {
        var paths = new WindowsEnvironmentPaths();
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var forbidden = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            profile,
            Path.Combine(profile, "Saved Games")
        };

        var roots = paths.ProgramDirectories
            .Concat(paths.ShortcutDirectories)
            .Concat(paths.StartupDirectories)
            .Append(paths.ScheduledTaskRoot);

        foreach (var root in roots)
            foreach (var directory in forbidden)
                Assert.False(string.Equals(root.TrimEnd('\\'), directory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase),
                    $"{root} kullanıcı verisi klasörü");
    }

    [Fact]
    public void ProgramRootsAreExactlyTheThreeAllowedLocations()
    {
        var paths = new WindowsEnvironmentPaths();

        Assert.Equal(
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
        ], paths.ProgramDirectories);
    }

    [Fact]
    public void OnlyTheFirstLevelOfEachProgramRootIsConsidered()
    {
        var nested = Path.Combine(Leftover, "plugins");
        var fileSystem = StaleLeftover();
        fileSystem.WithDirectory(nested).WithListing(nested, []);

        var finding = Assert.Single(Provider(fileSystem).Scan());

        Assert.Equal(Leftover, finding.Subtitle);
    }

    [Fact]
    public void FixMovesTheFolderIntoTheBackupInsteadOfDeletingIt()
    {
        var provider = Provider(StaleLeftover());
        var finding = Assert.Single(provider.Scan());
        var sink = new RecordingBackupSink();

        var result = provider.Fix(finding, sink);

        Assert.True(result.Success);
        Assert.Equal(Leftover, Assert.Single(sink.MovedDirectories));
    }

    private static FakeFileSystem StaleLeftover(IReadOnlyList<string>? files = null)
    {
        files ??= [Path.Combine(Leftover, "readme.txt")];
        var fileSystem = new FakeFileSystem()
            .WithDirectory(ProgramFiles)
            .WithDirectory(Leftover)
            .WithListing(ProgramFiles, [Leftover])
            .WithListing(Leftover, files)
            .WithLastWrite(Leftover, LongAgo);
        foreach (var file in files) fileSystem.WithLastWrite(file, LongAgo);
        return fileSystem;
    }

    private static LeftoverFolderProvider Provider(FakeFileSystem fileSystem, FakeRepository? repository = null) =>
        new(new FakeEnvironmentPaths { ProgramDirectories = [ProgramFiles] },
            fileSystem, repository ?? new FakeRepository(), new FixedTimeProvider(Now));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeRepository(string? installLocation = null, string? uninstallString = null) : IUninstallRepository
    {
        private readonly RegistryLocation location = new(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Test\App");

        public IReadOnlyList<UninstallEntry> Scan() =>
            installLocation is null && uninstallString is null
                ? []
                : [new UninstallEntry("id", "App", null, null, uninstallString, installLocation, null, false, false, location)];

        public RegistryTreeBackup Capture(UninstallEntry entry) => throw new NotSupportedException();
        public void Delete(RegistryLocation location) => throw new NotSupportedException();
        public void Restore(RegistryTreeBackup backup) => throw new NotSupportedException();
    }
}
