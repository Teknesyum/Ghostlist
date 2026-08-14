namespace Ghostlist.Core;

public sealed record ShortcutIssue(string ShortcutPath, string TargetPath);

public sealed class ShortcutProvider(IEnvironmentPaths paths, IFileSystem fileSystem) : IIssueProvider
{
    public string Id => Categories.Shortcut;
    public string Category => Categories.Shortcut;

    public IReadOnlyList<Finding> Scan(CancellationToken token = default)
    {
        var findings = new List<Finding>();
        foreach (var directory in paths.ShortcutDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            var files = fileSystem.TryListFiles(directory, "*.lnk", recursive: true);
            if (files is null) continue;
            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                var finding = Inspect(file);
                if (finding is not null) findings.Add(finding);
            }
        }
        return findings;
    }

    public FixResult Fix(Finding finding, IBackupSink backup)
    {
        if (finding.Payload is not ShortcutIssue issue) return FixResult.PayloadMismatch();
        if (finding.Status != EntryStatus.Broken) return FixResult.NotEligible();
        return FixResult.Fixed(backup.MoveFileToBackup(issue.ShortcutPath, Path.GetFileNameWithoutExtension(issue.ShortcutPath)));
    }

    private Finding? Inspect(string shortcutPath)
    {
        var link = ShellLinkReader.Read(fileSystem.TryReadBytes(shortcutPath));
        if (link?.LocalPath is null || link.IsNetworkTarget) return null;
        if (!fileSystem.IsFixedVolume(link.LocalPath)) return null;
        if (fileSystem.ProbeFile(link.LocalPath) != ProbeResult.Missing) return null;

        var directory = Path.GetDirectoryName(link.LocalPath);
        var evidence = new List<Evidence>
        {
            new(EvidenceKinds.ShortcutTargetMissing, link.LocalPath, EvidenceWeights.ShortcutTargetMissing)
        };
        if (!string.IsNullOrEmpty(directory) && fileSystem.ProbeDirectory(directory) == ProbeResult.Missing)
            evidence.Add(new Evidence(EvidenceKinds.TargetDirectoryMissing, directory, EvidenceWeights.TargetDirectoryMissing));

        var (status, confidence) = ConfidenceRules.Evaluate(evidence);
        return new Finding(
            $"shortcut:{shortcutPath}", Path.GetFileNameWithoutExtension(shortcutPath), shortcutPath,
            status, confidence, evidence, Id, new ShortcutIssue(shortcutPath, link.LocalPath));
    }
}
