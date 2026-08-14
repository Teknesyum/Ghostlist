using Ghostlist.Core;
using Ghostlist.Tests.ClassifierTests;

namespace Ghostlist.Tests.ProviderTests;

public class ShellLinkReaderTests
{
    [Fact]
    public void LocalTargetIsReadFromTheLinkInfoStructure()
    {
        var link = ShellLinkReader.Read(ShellLinkBuilder.Build(@"C:\Ghost\", "app.exe"));

        Assert.NotNull(link);
        Assert.Equal(@"C:\Ghost\app.exe", link.LocalPath);
        Assert.False(link.IsNetworkTarget);
    }

    [Fact]
    public void NetworkOnlyLinkExposesNoLocalPath()
    {
        var link = ShellLinkReader.Read(ShellLinkBuilder.BuildNetworkLink());

        Assert.NotNull(link);
        Assert.Null(link.LocalPath);
        Assert.True(link.IsNetworkTarget);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new byte[] { 1, 2, 3 })]
    public void GarbageIsRejectedInsteadOfGuessed(byte[]? content) => Assert.Null(ShellLinkReader.Read(content));
}

public class ShortcutProviderTests
{
    private const string StartMenu = @"C:\Users\Ghost\Start Menu";
    private static readonly string DeadLink = Path.Combine(StartMenu, "Dead.lnk");

    [Fact]
    public void ShortcutWithAMissingTargetInAMissingFolderIsBroken()
    {
        var fileSystem = Directory().WithBytes(DeadLink, ShellLinkBuilder.Build(@"C:\Gone\", "app.exe"));

        var finding = Assert.Single(new ShortcutProvider(Paths(), fileSystem).Scan());

        Assert.Equal(EntryStatus.Broken, finding.Status);
        Assert.Equal(Categories.Shortcut, finding.ProviderId);
        Assert.Contains(finding.Evidence, x => x.Kind == EvidenceKinds.ShortcutTargetMissing);
        Assert.True(ConfidenceRules.IsAutoFixable(finding, Categories.Shortcut));
    }

    [Fact]
    public void ShortcutWhoseTargetStillExistsIsNotReported()
    {
        var fileSystem = Directory()
            .WithBytes(DeadLink, ShellLinkBuilder.Build(@"C:\App\", "app.exe"))
            .WithFile(@"C:\App\app.exe");

        Assert.Empty(new ShortcutProvider(Paths(), fileSystem).Scan());
    }

    [Fact]
    public void MissingTargetInAnExistingFolderIsOnlySuspicious()
    {
        var fileSystem = Directory()
            .WithBytes(DeadLink, ShellLinkBuilder.Build(@"C:\App\", "app.exe"))
            .WithDirectory(@"C:\App");

        var finding = Assert.Single(new ShortcutProvider(Paths(), fileSystem).Scan());

        Assert.Equal(EntryStatus.Suspicious, finding.Status);
    }

    [Fact]
    public void NetworkShortcutsAreSkippedBecauseUnreachableIsNotMissing()
    {
        var fileSystem = Directory().WithBytes(DeadLink, ShellLinkBuilder.BuildNetworkLink());

        Assert.Empty(new ShortcutProvider(Paths(), fileSystem).Scan());
    }

    [Fact]
    public void ShortcutsOnRemovableDrivesAreSkipped()
    {
        var fileSystem = Directory()
            .WithBytes(DeadLink, ShellLinkBuilder.Build(@"E:\Portable\", "app.exe"))
            .WithRemovableVolume(@"E:\");

        Assert.Empty(new ShortcutProvider(Paths(), fileSystem).Scan());
    }

    [Fact]
    public void UrlFilesAreNeverInspected()
    {
        var fileSystem = new FakeFileSystem()
            .WithDirectory(StartMenu)
            .WithListing(StartMenu, [Path.Combine(StartMenu, "Site.url")])
            .WithText(Path.Combine(StartMenu, "Site.url"), "[InternetShortcut]");

        Assert.Empty(new ShortcutProvider(Paths(), fileSystem).Scan());
    }

    [Fact]
    public void FixMovesTheShortcutIntoTheBackupInsteadOfDeletingIt()
    {
        var fileSystem = Directory().WithBytes(DeadLink, ShellLinkBuilder.Build(@"C:\Gone\", "app.exe"));
        var provider = new ShortcutProvider(Paths(), fileSystem);
        var finding = Assert.Single(provider.Scan());
        var sink = new RecordingBackupSink();

        var result = provider.Fix(finding, sink);

        Assert.True(result.Success);
        Assert.Equal(DeadLink, Assert.Single(sink.MovedFiles));
    }

    [Fact]
    public void SuspiciousShortcutIsNotEligibleForFixing()
    {
        var fileSystem = Directory()
            .WithBytes(DeadLink, ShellLinkBuilder.Build(@"C:\App\", "app.exe"))
            .WithDirectory(@"C:\App");
        var provider = new ShortcutProvider(Paths(), fileSystem);
        var sink = new RecordingBackupSink();

        var result = provider.Fix(Assert.Single(provider.Scan()), sink);

        Assert.Equal(FixResultKeys.NotEligible, result.ResultKey);
        Assert.Empty(sink.MovedFiles);
    }

    private static FakeEnvironmentPaths Paths() => new() { ShortcutDirectories = [StartMenu] };

    private static FakeFileSystem Directory() =>
        new FakeFileSystem().WithDirectory(StartMenu).WithListing(StartMenu, [DeadLink]);
}
