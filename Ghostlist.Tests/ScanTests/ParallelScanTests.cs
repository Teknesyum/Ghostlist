using System.Reflection;
using Ghostlist.Core;
using Xunit;

namespace Ghostlist.Tests.ScanTests;

public class ParallelScanTests
{
    [Fact]
    public void EveryProviderContributesFindingsInProviderOrder()
    {
        var service = Service(new StubProvider("a"), new StubProvider("b"), new StubProvider("c"));

        var outcome = service.Scan();

        Assert.Equal(["a", "b", "c"], outcome.Findings.Select(x => x.ProviderId));
        Assert.False(outcome.HasFailures);
    }

    [Fact]
    public void OneFailingProviderDoesNotStopTheOthers()
    {
        var service = Service(new StubProvider("a"), new ThrowingProvider("bad"), new StubProvider("c"));

        var outcome = service.Scan();

        Assert.Equal(["a", "c"], outcome.Findings.Select(x => x.ProviderId));
        var failure = Assert.Single(outcome.Failures);
        Assert.Equal("bad", failure.ProviderId);
        Assert.Contains("boom", failure.Message);
    }

    [Fact]
    public async Task FailureIsReportedThroughProgressAndIsNotSwallowed()
    {
        var reports = new List<ScanProgress>();
        var service = Service(new StubProvider("a"), new ThrowingProvider("bad"));

        var outcome = await service.ScanAsync(new CollectingProgress(reports));

        Assert.Contains(reports, x => x.ProviderId == "bad" && x.StateKey == ScanStates.Failed && x.Error is not null);
        Assert.Contains(reports, x => x.ProviderId == "a" && x.StateKey == ScanStates.Completed);
        Assert.True(outcome.HasFailures);
    }

    [Fact]
    public async Task ProgressReportsEveryCategoryWithATotal()
    {
        var reports = new List<ScanProgress>();
        var service = Service(new StubProvider("a"), new StubProvider("b"));

        await service.ScanAsync(new CollectingProgress(reports));

        Assert.All(reports, x => Assert.Equal(2, x.Total));
        Assert.Equal(2, reports.Count(x => x.StateKey == ScanStates.Completed));
        Assert.Equal(2, reports.Where(x => x.StateKey == ScanStates.Completed).Max(x => x.Completed));
    }

    [Fact]
    public void CancellationThrowsAndDiscardsPartialFindings()
    {
        using var source = new CancellationTokenSource();
        var service = Service(new StubProvider("a"), new BlockingProvider("slow", source));

        Assert.Throws<OperationCanceledException>(() => service.Scan(source.Token));
    }

    [Fact]
    public void CancellationBeforeStartProducesNoFindings()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var service = Service(new StubProvider("a"), new StubProvider("b"));

        Assert.Throws<OperationCanceledException>(() => service.Scan(source.Token));
    }

    [Fact]
    public async Task ConcurrencyNeverExceedsTheConfiguredLimit()
    {
        var watcher = new ConcurrencyWatcher();
        var providers = Enumerable.Range(0, 8).Select(i => (IIssueProvider)new WatchedProvider($"p{i}", watcher)).ToList();
        var service = new CleanupService(providers, new NullBackupSink());

        await service.ScanAsync(null, new ScanOptions(2));

        Assert.True(watcher.Peak <= 2, $"peak was {watcher.Peak}");
    }

    [Fact]
    public void DefaultConcurrencyIsCappedAtFour()
    {
        Assert.True(ScanOptions.DefaultConcurrency <= ScanOptions.ConcurrencyCeiling);
        Assert.True(ScanOptions.DefaultConcurrency >= 1);
        Assert.Equal(4, ScanOptions.ConcurrencyCeiling);
    }

    [Fact]
    public void ConcurrencyLimitMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScanOptions(0));
    }

    [Fact]
    public void EveryProviderChecksTheCancellationToken()
    {
        var providers = typeof(IIssueProvider).Assembly.GetTypes()
            .Where(x => x is { IsAbstract: false, IsClass: true } && typeof(IIssueProvider).IsAssignableFrom(x))
            .ToList();

        Assert.NotEmpty(providers);
        foreach (var provider in providers)
        {
            var scan = provider.GetMethod(nameof(IIssueProvider.Scan), BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(scan);
            Assert.Contains(scan!.GetParameters(), x => x.ParameterType == typeof(CancellationToken));
        }
    }

    [Fact]
    public void FixIsNotCancellable()
    {
        var fix = typeof(IIssueProvider).GetMethod(nameof(IIssueProvider.Fix))!;
        Assert.DoesNotContain(fix.GetParameters(), x => x.ParameterType == typeof(CancellationToken));

        var serviceFix = typeof(CleanupService).GetMethod(nameof(CleanupService.Fix))!;
        Assert.DoesNotContain(serviceFix.GetParameters(), x => x.ParameterType == typeof(CancellationToken));
    }

    [Fact]
    public void FixCompletesEvenWhenTheScanTokenIsAlreadyCancelled()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var provider = new StubProvider("a");
        var service = new CleanupService([provider], new NullBackupSink());
        var finding = provider.Scan().Single();

        var result = service.Fix(finding);

        Assert.True(result.Success);
    }

    private static CleanupService Service(params IIssueProvider[] providers) =>
        new(providers, new NullBackupSink());

    private static Finding FindingFor(string id) =>
        new(id, id, id, EntryStatus.Broken, 95, [new Evidence("test", id, 95)], id, id);

    private sealed class StubProvider(string id) : IIssueProvider
    {
        public string Id => id;
        public string Category => id;
        public IReadOnlyList<Finding> Scan(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return [FindingFor(id)];
        }
        public FixResult Fix(Finding finding, IBackupSink backup) => FixResult.Fixed("none");
    }

    private sealed class ThrowingProvider(string id) : IIssueProvider
    {
        public string Id => id;
        public string Category => id;
        public IReadOnlyList<Finding> Scan(CancellationToken token = default) => throw new InvalidOperationException("boom");
        public FixResult Fix(Finding finding, IBackupSink backup) => FixResult.Fixed("none");
    }

    private sealed class BlockingProvider(string id, CancellationTokenSource source) : IIssueProvider
    {
        public string Id => id;
        public string Category => id;
        public IReadOnlyList<Finding> Scan(CancellationToken token = default)
        {
            source.Cancel();
            token.ThrowIfCancellationRequested();
            return [FindingFor(id)];
        }
        public FixResult Fix(Finding finding, IBackupSink backup) => FixResult.Fixed("none");
    }

    private sealed class ConcurrencyWatcher
    {
        private int current;
        private int peak;
        public int Peak => Volatile.Read(ref peak);
        public void Enter()
        {
            var now = Interlocked.Increment(ref current);
            int seen;
            while (now > (seen = Volatile.Read(ref peak)))
                Interlocked.CompareExchange(ref peak, now, seen);
        }
        public void Leave() => Interlocked.Decrement(ref current);
    }

    private sealed class WatchedProvider(string id, ConcurrencyWatcher watcher) : IIssueProvider
    {
        public string Id => id;
        public string Category => id;
        public IReadOnlyList<Finding> Scan(CancellationToken token = default)
        {
            watcher.Enter();
            Thread.Sleep(40);
            watcher.Leave();
            return [FindingFor(id)];
        }
        public FixResult Fix(Finding finding, IBackupSink backup) => FixResult.Fixed("none");
    }

    private sealed class CollectingProgress(List<ScanProgress> reports) : IProgress<ScanProgress>
    {
        private readonly object gate = new();
        public void Report(ScanProgress value) { lock (gate) reports.Add(value); }
    }

    private sealed class NullBackupSink : IBackupSink
    {
        public string SaveRegistryTree(RegistryTreeBackup backup, string label) => "none";
        public string SaveRegistryValue(RegistryValueBackup backup, string label) => "none";
        public string MoveFileToBackup(string sourcePath, string label) => "none";
        public string MoveDirectoryToBackup(string sourcePath, string label) => "none";
        public string SaveText(string content, string label, string extension) => "none";
        public void Restore(string backupPath) { }
        public IReadOnlyList<string> List() => [];
    }
}
