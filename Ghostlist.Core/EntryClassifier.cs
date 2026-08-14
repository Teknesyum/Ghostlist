namespace Ghostlist.Core;

public interface IFileSystem
{
    ProbeResult ProbeFile(string path);
    ProbeResult ProbeDirectory(string path);
    IReadOnlyList<string>? TryListEntries(string path);
    DateTimeOffset? TryGetLastWrite(string path);
    IReadOnlyList<string>? TryListFiles(string path, string pattern, bool recursive);
    IReadOnlyList<string>? TryListDirectories(string path);
    string? TryReadText(string path);
    byte[]? TryReadBytes(string path);
    bool IsFixedVolume(string path);
}

public sealed class PhysicalFileSystem : IFileSystem
{
    public ProbeResult ProbeFile(string path)
    {
        try
        {
            if (File.Exists(path)) return ProbeResult.Present;
            var parent = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(parent)) return ProbeResult.Missing;
            return ProbeDirectory(parent) switch
            {
                ProbeResult.Present => ProbeResult.Missing,
                ProbeResult.Missing => ProbeResult.Missing,
                _ => ProbeResult.Unknown
            };
        }
        catch (UnauthorizedAccessException) { return ProbeResult.Unknown; }
        catch (IOException) { return ProbeResult.Unknown; }
    }

    public ProbeResult ProbeDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) return ProbeResult.Present;
            var root = Path.GetPathRoot(path);
            if (!string.IsNullOrEmpty(root) && !Directory.Exists(root)) return ProbeResult.Unknown;
            return ProbeResult.Missing;
        }
        catch (UnauthorizedAccessException) { return ProbeResult.Unknown; }
        catch (IOException) { return ProbeResult.Unknown; }
    }

    public IReadOnlyList<string>? TryListEntries(string path)
    {
        try { return Directory.GetFileSystemEntries(path); }
        catch { return null; }
    }

    public DateTimeOffset? TryGetLastWrite(string path)
    {
        try
        {
            if (Directory.Exists(path)) return new DateTimeOffset(Directory.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            if (File.Exists(path)) return new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            return null;
        }
        catch { return null; }
    }

    public IReadOnlyList<string>? TryListFiles(string path, string pattern, bool recursive)
    {
        try { return Directory.GetFiles(path, pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly); }
        catch { return null; }
    }

    public IReadOnlyList<string>? TryListDirectories(string path)
    {
        try { return Directory.GetDirectories(path); }
        catch { return null; }
    }

    public string? TryReadText(string path)
    {
        try { return File.ReadAllText(path); }
        catch { return null; }
    }

    public byte[]? TryReadBytes(string path)
    {
        try { return File.ReadAllBytes(path); }
        catch { return null; }
    }

    public bool IsFixedVolume(string path)
    {
        try
        {
            if (path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root)) return false;
            return new DriveInfo(root).DriveType == DriveType.Fixed;
        }
        catch { return false; }
    }
}

public sealed record EntryAssessment(
    EntryStatus Status,
    int Confidence,
    IReadOnlyList<Evidence> Evidence,
    string? ResolvedTarget,
    string? MsiProductCode);

public sealed class EntryClassifier(IFileSystem fileSystem, IMsiCatalog msiCatalog)
{
    public EntryClassifier(IFileSystem fileSystem) : this(fileSystem, new EmptyMsiCatalog()) { }

    public EntryAssessment Classify(UninstallEntry entry)
    {
        if (entry.SystemComponent)
            return new EntryAssessment(EntryStatus.Unsupported, 0,
                [new Evidence(EvidenceKinds.SystemComponent, entry.Location.DisplayPath, EvidenceWeights.Uncertain)], null, null);

        var productCode = UninstallCommandParser.ResolveMsiProductCode(entry.UninstallString);
        if (entry.WindowsInstaller || productCode is not null)
            return ClassifyMsi(entry, productCode);

        return ClassifyExecutable(entry);
    }

    private EntryAssessment ClassifyMsi(UninstallEntry entry, string? productCode)
    {
        if (productCode is null)
            return new EntryAssessment(EntryStatus.Unsupported, 0,
                [new Evidence(EvidenceKinds.MsiProductCodeUnknown, entry.UninstallString ?? string.Empty, EvidenceWeights.Uncertain)], null, null);

        var registration = msiCatalog.Lookup(productCode);
        if (registration.ProductKey == ProbeResult.Present || registration.UserData == ProbeResult.Present)
            return new EntryAssessment(EntryStatus.Unsupported, 0, [], null, productCode);

        var evidence = new List<Evidence>();
        Add(evidence, registration.ProductKey, EvidenceKinds.MsiProductRegistrationMissing,
            EvidenceKinds.MsiRegistryUnreadable, productCode, EvidenceWeights.MsiProductRegistrationMissing);
        Add(evidence, registration.UserData, EvidenceKinds.MsiUserDataMissing,
            EvidenceKinds.MsiRegistryUnreadable, productCode, EvidenceWeights.MsiUserDataMissing);
        if (registration.LocalPackagePath is not null)
            Add(evidence, registration.CachePackage, EvidenceKinds.MsiCachePackageMissing,
                EvidenceKinds.MsiCacheUnreadable, registration.LocalPackagePath, EvidenceWeights.MsiCachePackageMissing);

        var (status, confidence) = ConfidenceRules.Evaluate(evidence);
        return new EntryAssessment(status, confidence, evidence, null, productCode);
    }

    private EntryAssessment ClassifyExecutable(UninstallEntry entry)
    {
        var view = entry.Location.View;
        var target = UninstallCommandParser.ResolveExecutable(entry.UninstallString, view, entry.InstallLocation);
        var installLocation = Normalize(UninstallCommandParser.ExpandForView(entry.InstallLocation, view));

        if (target is null)
        {
            var unresolvable = new List<Evidence>
            {
                new(EvidenceKinds.CommandUnresolvable, entry.UninstallString ?? string.Empty, EvidenceWeights.CommandUnresolvable)
            };
            var (unresolvedStatus, unresolvedConfidence) = ConfidenceRules.Evaluate(unresolvable);
            return new EntryAssessment(unresolvedStatus, unresolvedConfidence, unresolvable, null, null);
        }

        var evidence = new List<Evidence>();
        var targetProbe = fileSystem.ProbeFile(target);
        Add(evidence, targetProbe, EvidenceKinds.UninstallerMissing, EvidenceKinds.UninstallerUnreadable, target, EvidenceWeights.UninstallerMissing);

        var targetDirectory = Normalize(Path.GetDirectoryName(target));
        if (targetProbe == ProbeResult.Missing && targetDirectory is not null
            && fileSystem.ProbeDirectory(targetDirectory) == ProbeResult.Missing)
            evidence.Add(new Evidence(EvidenceKinds.TargetDirectoryMissing, targetDirectory, EvidenceWeights.TargetDirectoryMissing));

        if (installLocation is not null && !SamePath(installLocation, targetDirectory))
            Add(evidence, fileSystem.ProbeDirectory(installLocation), EvidenceKinds.InstallLocationMissing,
                EvidenceKinds.InstallLocationUnreadable, installLocation, EvidenceWeights.InstallLocationMissing);

        var icon = ResolveIcon(entry, view);
        if (icon is not null && !SamePath(icon, target) && fileSystem.ProbeFile(icon) == ProbeResult.Missing)
            evidence.Add(new Evidence(EvidenceKinds.DisplayIconMissing, icon, EvidenceWeights.DisplayIconMissing));

        var (status, confidence) = ConfidenceRules.Evaluate(evidence);
        return new EntryAssessment(status, confidence, evidence, target, null);
    }

    private static void Add(List<Evidence> evidence, ProbeResult probe, string missingKind, string unknownKind, string detail, int weight)
    {
        if (probe == ProbeResult.Missing) evidence.Add(new Evidence(missingKind, detail, weight));
        else if (probe == ProbeResult.Unknown) evidence.Add(new Evidence(unknownKind, detail, EvidenceWeights.Uncertain));
    }

    private static string? ResolveIcon(UninstallEntry entry, Microsoft.Win32.RegistryView view)
    {
        if (string.IsNullOrWhiteSpace(entry.DisplayIcon)) return null;
        var value = UninstallCommandParser.ExpandForView(entry.DisplayIcon, view).Trim().Trim('"');
        var comma = value.LastIndexOf(',');
        if (comma > 2) value = value[..comma];
        value = value.Trim().Trim('"');
        try { return Path.IsPathFullyQualified(value) ? Path.GetFullPath(value) : null; }
        catch { return null; }
    }

    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var value = path.Trim().Trim('"').TrimEnd('\\');
        try { return Path.IsPathFullyQualified(value) ? Path.GetFullPath(value) : null; }
        catch { return null; }
    }

    private static bool SamePath(string? left, string? right) =>
        left is not null && right is not null
        && string.Equals(left.TrimEnd('\\'), right.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}

public sealed class EmptyMsiCatalog : IMsiCatalog
{
    public MsiRegistration Lookup(string productCode) =>
        new(ProbeResult.Unknown, ProbeResult.Unknown, ProbeResult.Unknown, null);
}
