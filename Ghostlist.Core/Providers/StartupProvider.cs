using Microsoft.Win32;

namespace Ghostlist.Core;

public abstract record StartupIssue;

public sealed record StartupValueIssue(RegistryLocation Location, string ValueName, string TargetPath) : StartupIssue;

public sealed record StartupShortcutIssue(string ShortcutPath, string TargetPath) : StartupIssue;

public sealed class StartupProvider(
    IRegistryHiveAccessor accessor,
    IEnvironmentPaths paths,
    IFileSystem fileSystem) : IIssueProvider
{
    private static readonly string[] RunKeys =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"
    ];

    public string Id => Categories.Startup;
    public string Category => Categories.Startup;

    public IReadOnlyList<Finding> Scan(CancellationToken token = default)
    {
        var findings = new List<Finding>();
        findings.AddRange(ScanRegistry(token));
        findings.AddRange(ScanFolders(token));
        return findings;
    }

    public FixResult Fix(Finding finding, IBackupSink backup)
    {
        if (finding.Status != EntryStatus.Broken) return FixResult.NotEligible();
        switch (finding.Payload)
        {
            case StartupValueIssue issue:
            {
                using var key = accessor.OpenKey(issue.Location.Hive, issue.Location.View, issue.Location.SubKeyPath, writable: true);
                if (key is null) return FixResult.NotEligible();
                var snapshot = new RegistryValueSnapshot(issue.ValueName, key.GetValueKind(issue.ValueName),
                    RegistryValueCodec.Normalize(key.GetValue(issue.ValueName)));
                var path = backup.SaveRegistryValue(new RegistryValueBackup(issue.Location, snapshot, DateTimeOffset.Now), issue.ValueName);
                key.DeleteValue(issue.ValueName);
                return FixResult.Fixed(path);
            }
            case StartupShortcutIssue issue:
                return FixResult.Fixed(backup.MoveFileToBackup(issue.ShortcutPath, Path.GetFileNameWithoutExtension(issue.ShortcutPath)));
            default:
                return FixResult.PayloadMismatch();
        }
    }

    private List<Finding> ScanRegistry(CancellationToken token)
    {
        var findings = new List<Finding>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        foreach (var runKey in RunKeys)
        {
            token.ThrowIfCancellationRequested();
            IRegistryKeyHandle? key;
            try { key = accessor.OpenKey(hive, view, runKey); }
            catch { continue; }
            if (key is null) continue;
            using (key)
            {
                var location = new RegistryLocation(hive, view, runKey);
                foreach (var name in key.GetValueNames())
                {
                    token.ThrowIfCancellationRequested();
                    if (key.GetValue(name) is not string command) continue;
                    var target = UninstallCommandParser.ResolveExecutable(command, view);
                    if (target is null) continue;
                    var evidence = Assess(target, EvidenceKinds.StartupTargetMissing, EvidenceWeights.StartupTargetMissing);
                    if (evidence.Count == 0) continue;
                    var (status, confidence) = ConfidenceRules.Evaluate(evidence);
                    findings.Add(new Finding(
                        $"startup:{hive}:{view}:{runKey}:{name}", name, location.DisplayPath,
                        status, confidence, evidence, Id, new StartupValueIssue(location, name, target)));
                }
            }
        }
        return findings;
    }

    private List<Finding> ScanFolders(CancellationToken token)
    {
        var findings = new List<Finding>();
        foreach (var directory in paths.StartupDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            var files = fileSystem.TryListFiles(directory, "*.lnk", recursive: false);
            if (files is null) continue;
            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                var link = ShellLinkReader.Read(fileSystem.TryReadBytes(file));
                if (link?.LocalPath is null || link.IsNetworkTarget) continue;
                if (!fileSystem.IsFixedVolume(link.LocalPath)) continue;
                var evidence = Assess(link.LocalPath, EvidenceKinds.StartupTargetMissing, EvidenceWeights.StartupTargetMissing);
                if (evidence.Count == 0) continue;
                var (status, confidence) = ConfidenceRules.Evaluate(evidence);
                findings.Add(new Finding(
                    $"startup:{file}", Path.GetFileNameWithoutExtension(file), file,
                    status, confidence, evidence, Id, new StartupShortcutIssue(file, link.LocalPath)));
            }
        }
        return findings;
    }

    private List<Evidence> Assess(string target, string kind, int weight)
    {
        var evidence = new List<Evidence>();
        if (fileSystem.ProbeFile(target) != ProbeResult.Missing) return evidence;
        evidence.Add(new Evidence(kind, target, weight));
        var directory = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(directory) && fileSystem.ProbeDirectory(directory) == ProbeResult.Missing)
            evidence.Add(new Evidence(EvidenceKinds.TargetDirectoryMissing, directory, EvidenceWeights.TargetDirectoryMissing));
        return evidence;
    }
}
