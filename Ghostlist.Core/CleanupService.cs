namespace Ghostlist.Core;

public sealed class CleanupService(IReadOnlyList<IIssueProvider> providers, IBackupSink backup)
{
    public IReadOnlyList<IIssueProvider> Providers => providers;

    public IReadOnlyList<Finding> Scan()
    {
        var findings = new List<Finding>();
        foreach (var provider in providers) findings.AddRange(provider.Scan());
        return findings;
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
