using Ghostlist.Core;
using Ghostlist.Tests.ClassifierTests;

namespace Ghostlist.Tests.ProviderTests;

public class ScheduledTaskProviderTests
{
    private const string TaskRoot = @"C:\Windows\System32\Tasks";
    private static readonly string GhostTask = Path.Combine(TaskRoot, "GhostUpdater");
    private static readonly string MicrosoftTask = Path.Combine(TaskRoot, "Microsoft", "Windows", "Defender");

    [Fact]
    public void TaskPointingAtAMissingExecutableIsBroken()
    {
        var provider = Provider(new FakeFileSystem()
            .WithListing(TaskRoot, [GhostTask])
            .WithText(GhostTask, TaskXml(@"C:\Gone\updater.exe")));

        var finding = Assert.Single(provider.Scan());

        Assert.Equal(Categories.Task, finding.ProviderId);
        Assert.Equal(EntryStatus.Broken, finding.Status);
        Assert.Equal(@"\GhostUpdater", finding.Title);
    }

    [Fact]
    public void MicrosoftTasksAreNeverEvenListed()
    {
        var provider = Provider(new FakeFileSystem()
            .WithListing(TaskRoot, [MicrosoftTask])
            .WithText(MicrosoftTask, TaskXml(@"C:\Gone\defender.exe")));

        Assert.Empty(provider.Scan());
    }

    [Fact]
    public void MicrosoftAuthoredTasksOutsideTheMicrosoftBranchAreAlsoSkipped()
    {
        var provider = Provider(new FakeFileSystem()
            .WithListing(TaskRoot, [GhostTask])
            .WithText(GhostTask, TaskXml(@"C:\Gone\updater.exe", author: "Microsoft Corporation")));

        Assert.Empty(provider.Scan());
    }

    [Fact]
    public void TaskWithALiveExecutableIsNotReported()
    {
        var provider = Provider(new FakeFileSystem()
            .WithListing(TaskRoot, [GhostTask])
            .WithText(GhostTask, TaskXml(@"C:\App\updater.exe"))
            .WithFile(@"C:\App\updater.exe")
            .WithDirectory(@"C:\App"));

        Assert.Empty(provider.Scan());
    }

    [Fact]
    public void NonTaskFilesAndUnparsableXmlAreIgnored()
    {
        var provider = Provider(new FakeFileSystem()
            .WithListing(TaskRoot, [GhostTask])
            .WithText(GhostTask, "not a task file"));

        Assert.Empty(provider.Scan());
    }

    [Fact]
    public void FixBacksUpTheTaskXmlBeforeCallingSchtasks()
    {
        var fileSystem = new FakeFileSystem()
            .WithListing(TaskRoot, [GhostTask])
            .WithText(GhostTask, TaskXml(@"C:\Gone\updater.exe"));
        var remover = new RecordingTaskRemover();
        var provider = new ScheduledTaskProvider(Paths(), fileSystem, remover);
        var sink = new RecordingBackupSink();

        var result = provider.Fix(Assert.Single(provider.Scan()), sink);

        Assert.True(result.Success);
        Assert.Contains(@"C:\Gone\updater.exe", Assert.Single(sink.SavedTexts));
        Assert.Equal(@"\GhostUpdater", Assert.Single(remover.Deleted));
    }

    [Fact]
    public void FailedDeletionStillKeepsTheBackupAndReportsFailure()
    {
        var fileSystem = new FakeFileSystem()
            .WithListing(TaskRoot, [GhostTask])
            .WithText(GhostTask, TaskXml(@"C:\Gone\updater.exe"));
        var provider = new ScheduledTaskProvider(Paths(), fileSystem, new RecordingTaskRemover(succeeds: false));
        var sink = new RecordingBackupSink();

        var result = provider.Fix(Assert.Single(provider.Scan()), sink);

        Assert.False(result.Success);
        Assert.Equal(FixResultKeys.Failed, result.ResultKey);
        Assert.NotNull(result.BackupPath);
    }

    private static ScheduledTaskProvider Provider(FakeFileSystem fileSystem) =>
        new(Paths(), fileSystem, new RecordingTaskRemover());

    private static FakeEnvironmentPaths Paths() => new() { ScheduledTaskRoot = TaskRoot };

    private static string TaskXml(string command, string author = "Teknesyum") => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Author>{author}</Author>
          </RegistrationInfo>
          <Actions Context="Author">
            <Exec>
              <Command>{command}</Command>
              <Arguments>/silent</Arguments>
            </Exec>
          </Actions>
        </Task>
        """;
}
