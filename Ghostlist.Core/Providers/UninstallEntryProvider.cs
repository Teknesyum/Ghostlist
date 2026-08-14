namespace Ghostlist.Core;

public sealed class UninstallEntryProvider(IUninstallRepository repository, EntryClassifier classifier) : IIssueProvider
{
    public string Id => Categories.Uninstall;
    public string Category => Categories.Uninstall;

    public IReadOnlyList<Finding> Scan(CancellationToken token = default)
    {
        var findings = new List<Finding>();
        foreach (var entry in repository.Scan())
        {
            token.ThrowIfCancellationRequested();
            var assessment = classifier.Classify(entry);
            findings.Add(new Finding(
                entry.Id, entry.DisplayName, Subtitle(entry, assessment),
                assessment.Status, assessment.Confidence, assessment.Evidence, Id, entry));
        }
        return findings;
    }

    public FixResult Fix(Finding finding, IBackupSink backup)
    {
        if (finding.Payload is not UninstallEntry entry) return FixResult.PayloadMismatch();
        if (finding.Status != EntryStatus.Broken) return FixResult.NotEligible();

        var path = backup.SaveRegistryTree(repository.Capture(entry), entry.DisplayName);
        repository.Delete(entry.Location);
        return FixResult.Fixed(path);
    }

    private static string? Subtitle(UninstallEntry entry, EntryAssessment assessment)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(entry.Publisher)) parts.Add(entry.Publisher);
        if (!string.IsNullOrWhiteSpace(entry.DisplayVersion)) parts.Add(entry.DisplayVersion);
        if (assessment.ResolvedTarget is not null) parts.Add(assessment.ResolvedTarget);
        else if (assessment.MsiProductCode is not null) parts.Add(assessment.MsiProductCode);
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }
}
