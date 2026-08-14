namespace Ghostlist.Core;

public sealed record LeftoverFolderIssue(string FolderPath);

public sealed class LeftoverFolderProvider(
    IEnvironmentPaths paths,
    IFileSystem fileSystem,
    IUninstallRepository repository,
    TimeProvider? time = null) : IIssueProvider
{
    public const int StaleAfterDays = 90;
    private static readonly string[] ExecutableExtensions = [".exe", ".dll", ".com", ".bat", ".cmd", ".msi", ".sys"];

    private readonly TimeProvider time = time ?? TimeProvider.System;

    public string Id => Categories.Folder;
    public string Category => Categories.Folder;

    public IReadOnlyList<Finding> Scan()
    {
        var owned = OwnedDirectories();
        var findings = new List<Finding>();
        foreach (var root in paths.ProgramDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var directories = fileSystem.TryListDirectories(root);
            if (directories is null) continue;
            foreach (var directory in directories)
            {
                var finding = Inspect(directory, owned);
                if (finding is not null) findings.Add(finding);
            }
        }
        return findings;
    }

    public FixResult Fix(Finding finding, IBackupSink backup)
    {
        if (finding.Payload is not LeftoverFolderIssue issue) return FixResult.PayloadMismatch();
        if (finding.Status != EntryStatus.Broken) return FixResult.NotEligible();
        return FixResult.Fixed(backup.MoveDirectoryToBackup(issue.FolderPath, Path.GetFileName(issue.FolderPath)));
    }

    private Finding? Inspect(string directory, IReadOnlyCollection<string> owned)
    {
        if (owned.Any(x => IsSameOrInside(directory, x))) return null;

        var files = fileSystem.TryListFiles(directory, "*", recursive: true);
        if (files is null) return null;
        if (files.Any(x => ExecutableExtensions.Contains(Path.GetExtension(x), StringComparer.OrdinalIgnoreCase))) return null;

        var lastWrite = LastWrite(directory, files);
        if (lastWrite is null) return null;
        if (time.GetUtcNow() - lastWrite.Value < TimeSpan.FromDays(StaleAfterDays)) return null;

        var evidence = new List<Evidence>
        {
            new(EvidenceKinds.FolderHasNoOwner, directory, EvidenceWeights.FolderHasNoOwner),
            new(EvidenceKinds.FolderHasNoExecutable, directory, EvidenceWeights.FolderHasNoExecutable),
            new(EvidenceKinds.FolderIsStale, lastWrite.Value.ToString("O"), EvidenceWeights.FolderIsStale)
        };
        var (status, confidence) = ConfidenceRules.Evaluate(evidence, ConfidenceRules.LeftoverFolderCeiling);
        return new Finding(
            $"folder:{directory}", Path.GetFileName(directory), directory,
            status, confidence, evidence, Id, new LeftoverFolderIssue(directory));
    }

    private DateTimeOffset? LastWrite(string directory, IReadOnlyList<string> files)
    {
        var moments = new List<DateTimeOffset>();
        var folder = fileSystem.TryGetLastWrite(directory);
        if (folder is not null) moments.Add(folder.Value);
        foreach (var file in files)
        {
            var moment = fileSystem.TryGetLastWrite(file);
            if (moment is not null) moments.Add(moment.Value);
        }
        return moments.Count == 0 ? null : moments.Max();
    }

    private IReadOnlyCollection<string> OwnedDirectories()
    {
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Entries())
        {
            Collect(owned, UninstallCommandParser.ExpandForView(entry.InstallLocation, entry.Location.View));
            var target = UninstallCommandParser.ResolveExecutable(entry.UninstallString, entry.Location.View, entry.InstallLocation);
            if (target is not null) Collect(owned, Path.GetDirectoryName(target));
            Collect(owned, Path.GetDirectoryName(UninstallCommandParser.ExpandForView(entry.DisplayIcon, entry.Location.View).Trim('"')));
        }
        return owned;
    }

    private IReadOnlyList<UninstallEntry> Entries()
    {
        try { return repository.Scan(); }
        catch { return []; }
    }

    private static void Collect(HashSet<string> owned, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            var value = path.Trim().Trim('"').TrimEnd('\\');
            if (Path.IsPathFullyQualified(value)) owned.Add(Path.GetFullPath(value));
        }
        catch { }
    }

    private static bool IsSameOrInside(string directory, string owned)
    {
        var left = directory.TrimEnd('\\');
        var right = owned.TrimEnd('\\');
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
            || right.StartsWith(left + "\\", StringComparison.OrdinalIgnoreCase);
    }
}
