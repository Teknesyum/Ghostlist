using Microsoft.Win32;
using Ghostlist.Core;
using Ghostlist.Tests.ClassifierTests;

namespace Ghostlist.Tests.ProviderTests;

public class StartupProviderTests
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunOnceKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string StartupFolder = @"C:\Users\Ghost\Startup";

    [Fact]
    public void RunValueWithAMissingTargetIsBroken()
    {
        var accessor = Registry(RunKey, "GhostAgent", @"""C:\Gone\agent.exe"" -silent");

        var finding = Assert.Single(Provider(accessor, new FakeFileSystem()).Scan());

        Assert.Equal(Categories.Startup, finding.ProviderId);
        Assert.Equal(EntryStatus.Broken, finding.Status);
        Assert.Contains(finding.Evidence, x => x.Kind == EvidenceKinds.StartupTargetMissing);
    }

    [Fact]
    public void RunOnceIsScannedTogetherWithRun()
    {
        var accessor = Registry(RunKey, "GhostAgent", @"C:\Gone\agent.exe");
        using (var key = accessor.CreateKey(RegistryHive.CurrentUser, RegistryView.Registry64, RunOnceKey))
            key.SetValue("GhostSetup", @"C:\Gone\setup.exe", RegistryValueKind.String);

        var findings = Provider(accessor, new FakeFileSystem()).Scan();

        Assert.Equal(2, findings.Count);
    }

    [Fact]
    public void UnresolvableStartupCommandIsSkippedInsteadOfReported()
    {
        var accessor = Registry(RunKey, "GhostAgent", "some unresolvable text");

        Assert.Empty(Provider(accessor, new FakeFileSystem()).Scan());
    }

    [Fact]
    public void ExistingStartupTargetIsNotReported()
    {
        var accessor = Registry(RunKey, "GhostAgent", @"C:\App\agent.exe");
        var fileSystem = new FakeFileSystem().WithFile(@"C:\App\agent.exe").WithDirectory(@"C:\App");

        Assert.Empty(Provider(accessor, fileSystem).Scan());
    }

    [Fact]
    public void FixTakesAValueLevelBackupAndRemovesOnlyThatValue()
    {
        var accessor = Registry(RunKey, "GhostAgent", @"C:\Gone\agent.exe");
        using (var key = accessor.CreateKey(RegistryHive.CurrentUser, RegistryView.Registry64, RunKey))
            key.SetValue("KeepMe", @"C:\App\other.exe", RegistryValueKind.String);
        var provider = Provider(accessor, new FakeFileSystem());
        var finding = provider.Scan().Single(x => x.Title == "GhostAgent");
        var sink = new RecordingBackupSink();

        var result = provider.Fix(finding, sink);

        Assert.True(result.Success);
        var saved = Assert.Single(sink.SavedValues);
        Assert.Equal("GhostAgent", saved.Value.Name);
        Assert.Equal(@"C:\Gone\agent.exe", saved.Value.Value);
        using var reopened = accessor.OpenKey(RegistryHive.CurrentUser, RegistryView.Registry64, RunKey)!;
        Assert.Equal(["KeepMe"], reopened.GetValueNames());
    }

    [Fact]
    public void StartupFolderShortcutWithAMissingTargetIsReportedAndMoved()
    {
        var shortcut = Path.Combine(StartupFolder, "Ghost.lnk");
        var fileSystem = new FakeFileSystem()
            .WithDirectory(StartupFolder)
            .WithListing(StartupFolder, [shortcut])
            .WithBytes(shortcut, ShellLinkBuilder.Build(@"C:\Gone\", "agent.exe"));
        var provider = new StartupProvider(new InMemoryRegistryHiveAccessor(),
            new FakeEnvironmentPaths { StartupDirectories = [StartupFolder] }, fileSystem);
        var finding = Assert.Single(provider.Scan());
        var sink = new RecordingBackupSink();

        Assert.True(provider.Fix(finding, sink).Success);
        Assert.Equal(shortcut, Assert.Single(sink.MovedFiles));
        Assert.Empty(sink.SavedValues);
    }

    private static StartupProvider Provider(InMemoryRegistryHiveAccessor accessor, FakeFileSystem fileSystem) =>
        new(accessor, new FakeEnvironmentPaths(), fileSystem);

    private static InMemoryRegistryHiveAccessor Registry(string path, string name, string command)
    {
        var accessor = new InMemoryRegistryHiveAccessor();
        using var key = accessor.CreateKey(RegistryHive.CurrentUser, RegistryView.Registry64, path);
        key.SetValue(name, command, RegistryValueKind.String);
        return accessor;
    }
}
