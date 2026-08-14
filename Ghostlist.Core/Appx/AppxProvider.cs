namespace Ghostlist.Core;

public sealed class AppxProvider(IAppxCatalog catalog, IFileSystem fileSystem) : IIssueProvider
{
    public string Id => Categories.Msix;
    public string Category => Categories.Msix;

    public IReadOnlyList<Finding> Scan(CancellationToken token = default)
    {
        var findings = new List<Finding>();
        foreach (var package in catalog.GetStagedPackages())
        {
            token.ThrowIfCancellationRequested();
            if (package.InstallLocation is null) continue;
            var evidence = Assess(package.InstallLocation);
            if (evidence.Count == 0) continue;
            var (status, confidence) = ConfidenceRules.Evaluate(evidence);
            if (status == EntryStatus.Healthy) continue;
            findings.Add(new Finding(
                $"msix:{package.FullName}", PackageName(package.FullName), package.InstallLocation,
                status, confidence, evidence, Id, package));
        }
        return findings;
    }

    public FixResult Fix(Finding finding, IBackupSink backup)
    {
        if (finding.Payload is not AppxPackage package) return FixResult.PayloadMismatch();
        return FixResult.Manual($"Remove-AppxPackage -Package {package.FullName}");
    }

    private List<Evidence> Assess(string installLocation)
    {
        var evidence = new List<Evidence>();
        var folder = fileSystem.ProbeDirectory(installLocation);
        if (folder == ProbeResult.Unknown) return evidence;

        if (folder == ProbeResult.Missing)
        {
            evidence.Add(new Evidence(EvidenceKinds.AppxInstallFolderMissing, installLocation, EvidenceWeights.AppxInstallFolderMissing));
            var storeRoot = Path.GetDirectoryName(installLocation);
            if (string.IsNullOrEmpty(storeRoot)) return evidence;
            evidence.Add(fileSystem.ProbeDirectory(storeRoot) switch
            {
                ProbeResult.Present => new Evidence(EvidenceKinds.AppxStoreRootPresent, storeRoot, EvidenceWeights.AppxStoreRootPresent),
                _ => new Evidence(EvidenceKinds.AppxStoreRootUnreadable, storeRoot, EvidenceWeights.Uncertain)
            });
            return evidence;
        }

        var manifest = Path.Combine(installLocation, "AppxManifest.xml");
        if (fileSystem.ProbeFile(manifest) != ProbeResult.Missing) return evidence;

        evidence.Add(new Evidence(EvidenceKinds.AppxManifestMissing, manifest, EvidenceWeights.AppxManifestMissing));
        var entries = fileSystem.TryListEntries(installLocation);
        if (entries is null)
            evidence.Add(new Evidence(EvidenceKinds.AppxStoreRootUnreadable, installLocation, EvidenceWeights.Uncertain));
        else if (entries.Count == 0)
            evidence.Add(new Evidence(EvidenceKinds.AppxPackageFolderEmpty, installLocation, EvidenceWeights.AppxPackageFolderEmpty));
        return evidence;
    }

    private static string PackageName(string fullName)
    {
        var separator = fullName.IndexOf('_');
        return separator > 0 ? fullName[..separator] : fullName;
    }
}
