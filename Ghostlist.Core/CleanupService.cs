namespace Ghostlist.Core;

public sealed class CleanupService(IReadOnlyList<IIssueProvider> providers, IBackupSink backup)
{
    public IReadOnlyList<IIssueProvider> Providers => providers;

    public ScanOutcome Scan(CancellationToken token = default) =>
        ScanAsync(null, null, token).GetAwaiter().GetResult();

    public async Task<ScanOutcome> ScanAsync(
        IProgress<ScanProgress>? progress = null,
        ScanOptions? options = null,
        CancellationToken token = default)
    {
        var total = providers.Count;
        if (total == 0) return ScanOutcome.Empty;

        var results = new IReadOnlyList<Finding>?[total];
        var failures = new ScanFailure?[total];
        var completed = 0;
        using var gate = new SemaphoreSlim((options ?? ScanOptions.Default).MaxConcurrency);

        var tasks = new Task[total];
        for (var i = 0; i < total; i++)
        {
            var index = i;
            var provider = providers[index];
            tasks[index] = Task.Run(async () =>
            {
                await gate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    token.ThrowIfCancellationRequested();
                    progress?.Report(new ScanProgress(
                        provider.Id, provider.Category, ScanStates.Running, Volatile.Read(ref completed), total, null));
                    results[index] = provider.Scan(token);
                    progress?.Report(new ScanProgress(
                        provider.Id, provider.Category, ScanStates.Completed, Interlocked.Increment(ref completed), total, null));
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failures[index] = new ScanFailure(provider.Id, provider.Category, ex.Message);
                    progress?.Report(new ScanProgress(
                        provider.Id, provider.Category, ScanStates.Failed, Interlocked.Increment(ref completed), total, ex.Message));
                }
                finally
                {
                    gate.Release();
                }
            }, token);
        }

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            token.ThrowIfCancellationRequested();
            throw;
        }
        token.ThrowIfCancellationRequested();

        var findings = new List<Finding>();
        for (var i = 0; i < total; i++)
            if (results[i] is { } result)
                findings.AddRange(result);
        return new ScanOutcome(findings, failures.OfType<ScanFailure>().ToList());
    }

    public string CategoryOf(Finding finding) =>
        providers.FirstOrDefault(x => x.Id == finding.ProviderId)?.Category ?? finding.ProviderId;

    public bool IsAutoFixable(Finding finding) => ConfidenceRules.IsAutoFixable(finding, CategoryOf(finding));

    public IReadOnlyList<Finding> AutoFixable(IEnumerable<Finding> findings) => findings.Where(IsAutoFixable).ToList();

    public FixResult Fix(Finding finding)
    {
        var provider = providers.FirstOrDefault(x => x.Id == finding.ProviderId)
            ?? throw new InvalidOperationException($"Bilinmeyen sağlayıcı: {finding.ProviderId}");
        return provider.Fix(finding, backup);
    }

    public IReadOnlyList<string> ListBackups() => backup.List();

    public void Restore(string backupPath) => backup.Restore(backupPath);

    public static CleanupService CreateDefault(string backupDirectory)
    {
        var accessor = new WindowsRegistryHiveAccessor();
        var fileSystem = new PhysicalFileSystem();
        var paths = new WindowsEnvironmentPaths();
        var repository = new WindowsUninstallRepository(accessor);
        var classifier = new EntryClassifier(fileSystem, new RegistryMsiCatalog(accessor, fileSystem));
        return new CleanupService(
            [
                new UninstallEntryProvider(repository, classifier),
                new ShortcutProvider(paths, fileSystem),
                new StartupProvider(accessor, paths, fileSystem),
                new ScheduledTaskProvider(paths, fileSystem, new SchtasksRemover()),
                new LeftoverFolderProvider(paths, fileSystem, repository),
                new AppxProvider(new RegistryAppxCatalog(accessor), fileSystem)
            ],
            new FileBackupSink(backupDirectory, repository, accessor));
    }
}
