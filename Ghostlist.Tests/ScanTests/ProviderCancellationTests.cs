using Microsoft.Win32;
using Ghostlist.Core;
using Ghostlist.Tests.ClassifierTests;
using Ghostlist.Tests.ProviderTests;
using Xunit;

namespace Ghostlist.Tests.ScanTests;

public class ProviderCancellationTests
{
    private const string StartMenu = @"C:\Users\Ghost\Start Menu";
    private const string StartupFolder = @"C:\Users\Ghost\Startup";
    private const string ProgramFiles = @"C:\Program Files";
    private const string TaskRoot = @"C:\Windows\System32\Tasks";
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    private static readonly CancellationToken Cancelled = Token();

    [Fact]
    public void ShortcutProviderStopsWhenCancelled()
    {
        var link = Path.Combine(StartMenu, "Dead.lnk");
        var fileSystem = new FakeFileSystem()
            .WithListing(StartMenu, [link])
            .WithBytes(link, ShellLinkBuilder.Build(@"C:\Gone\", "app.exe"));
        var provider = new ShortcutProvider(new FakeEnvironmentPaths { ShortcutDirectories = [StartMenu] }, fileSystem);

        Assert.Single(provider.Scan());
        Assert.Throws<OperationCanceledException>(() => provider.Scan(Cancelled));
    }

    [Fact]
    public void StartupProviderStopsWhenCancelled()
    {
        var accessor = new InMemoryRegistryHiveAccessor();
        using (var key = accessor.CreateKey(RegistryHive.CurrentUser, RegistryView.Registry64, RunKey))
            key.SetValue("GhostAgent", @"C:\Gone\agent.exe", RegistryValueKind.String);
        var provider = new StartupProvider(accessor,
            new FakeEnvironmentPaths { StartupDirectories = [StartupFolder] }, new FakeFileSystem());

        Assert.NotEmpty(provider.Scan());
        Assert.Throws<OperationCanceledException>(() => provider.Scan(Cancelled));
    }

    [Fact]
    public void ScheduledTaskProviderStopsWhenCancelled()
    {
        var task = Path.Combine(TaskRoot, "GhostUpdater");
        var fileSystem = new FakeFileSystem()
            .WithListing(TaskRoot, [task])
            .WithText(task, TaskXml(@"C:\Gone\updater.exe"));
        var provider = new ScheduledTaskProvider(
            new FakeEnvironmentPaths { ScheduledTaskRoot = TaskRoot }, fileSystem, new RecordingTaskRemover());

        Assert.Single(provider.Scan());
        Assert.Throws<OperationCanceledException>(() => provider.Scan(Cancelled));
    }

    [Fact]
    public void LeftoverFolderProviderStopsWhenCancelled()
    {
        var leftover = Path.Combine(ProgramFiles, "GhostApp");
        var now = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
        var file = Path.Combine(leftover, "readme.txt");
        var fileSystem = new FakeFileSystem()
            .WithDirectory(ProgramFiles)
            .WithDirectory(leftover)
            .WithListing(ProgramFiles, [leftover])
            .WithListing(leftover, [file])
            .WithLastWrite(leftover, now.AddDays(-400))
            .WithLastWrite(file, now.AddDays(-400));
        var provider = new LeftoverFolderProvider(
            new FakeEnvironmentPaths { ProgramDirectories = [ProgramFiles] },
            fileSystem, new EmptyRepository(), new FixedTime(now));

        Assert.Single(provider.Scan());
        Assert.Throws<OperationCanceledException>(() => provider.Scan(Cancelled));
    }

    [Fact]
    public void UninstallEntryProviderStopsWhenCancelled()
    {
        var provider = new UninstallEntryProvider(new SingleEntryRepository(), new EntryClassifier(new FakeFileSystem()));

        Assert.Single(provider.Scan());
        Assert.Throws<OperationCanceledException>(() => provider.Scan(Cancelled));
    }

    [Fact]
    public void AppxProviderStopsWhenCancelled()
    {
        var provider = new AppxProvider(new OneStagedPackage(), new FakeFileSystem());

        Assert.NotEmpty(provider.Scan());
        Assert.Throws<OperationCanceledException>(() => provider.Scan(Cancelled));
    }

    private static CancellationToken Token()
    {
        var source = new CancellationTokenSource();
        source.Cancel();
        return source.Token;
    }

    private static string TaskXml(string command) =>
        $"""
         <?xml version="1.0" encoding="UTF-16"?>
         <Task xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
           <RegistrationInfo><Author>Ghost Software</Author></RegistrationInfo>
           <Actions><Exec><Command>{command}</Command></Exec></Actions>
         </Task>
         """;

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class EmptyRepository : IUninstallRepository
    {
        public IReadOnlyList<UninstallEntry> Scan() => [];
        public RegistryTreeBackup Capture(UninstallEntry entry) => throw new NotSupportedException();
        public void Delete(RegistryLocation location) => throw new NotSupportedException();
        public void Restore(RegistryTreeBackup backup) => throw new NotSupportedException();
    }

    private sealed class SingleEntryRepository : IUninstallRepository
    {
        private static readonly RegistryLocation Location =
            new(RegistryHive.CurrentUser, RegistryView.Registry64, @"SOFTWARE\Test\Broken");

        public IReadOnlyList<UninstallEntry> Scan() =>
            [new UninstallEntry("id", "Broken App", null, null, @"C:\Gone\unins000.exe", null, null, false, false, Location)];

        public RegistryTreeBackup Capture(UninstallEntry entry) => throw new NotSupportedException();
        public void Delete(RegistryLocation location) => throw new NotSupportedException();
        public void Restore(RegistryTreeBackup backup) => throw new NotSupportedException();
    }

    private sealed class OneStagedPackage : IAppxCatalog
    {
        public IReadOnlyList<AppxPackage> GetStagedPackages() =>
            [new AppxPackage("Ghost.App_1.0.0.0_x64__abc", @"C:\Gone\WindowsApps\Ghost.App")];
    }
}
