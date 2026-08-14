using Microsoft.Win32;

namespace Ghostlist.Core;

public enum EntryStatus { Healthy, Broken, Suspicious, Unsupported }

public enum ProbeResult { Present, Missing, Unknown }

public static class Categories
{
    public const string Uninstall = "uninstall";
    public const string Shortcut = "shortcut";
    public const string Startup = "startup";
    public const string Task = "task";
    public const string Folder = "folder";
    public const string Msix = "msix";
}

public static class EvidenceKinds
{
    public const string SystemComponent = "system_component";
    public const string CommandUnresolvable = "command_unresolvable";
    public const string UninstallerMissing = "uninstaller_missing";
    public const string UninstallerUnreadable = "uninstaller_unreadable";
    public const string TargetDirectoryMissing = "target_directory_missing";
    public const string InstallLocationMissing = "install_location_missing";
    public const string InstallLocationUnreadable = "install_location_unreadable";
    public const string DisplayIconMissing = "display_icon_missing";
    public const string MsiProductRegistrationMissing = "msi_product_registration_missing";
    public const string MsiUserDataMissing = "msi_user_data_missing";
    public const string MsiCachePackageMissing = "msi_cache_package_missing";
    public const string MsiRegistryUnreadable = "msi_registry_unreadable";
    public const string MsiCacheUnreadable = "msi_cache_unreadable";
    public const string MsiProductCodeUnknown = "msi_product_code_unknown";
    public const string AppxInstallFolderMissing = "appx_install_folder_missing";
    public const string AppxStoreRootPresent = "appx_store_root_present";
    public const string AppxStoreRootUnreadable = "appx_store_root_unreadable";
    public const string AppxManifestMissing = "appx_manifest_missing";
    public const string AppxPackageFolderEmpty = "appx_package_folder_empty";
    public const string ShortcutTargetMissing = "shortcut_target_missing";
    public const string StartupTargetMissing = "startup_target_missing";
    public const string TaskTargetMissing = "task_target_missing";
    public const string FolderHasNoOwner = "folder_has_no_owner";
    public const string FolderHasNoExecutable = "folder_has_no_executable";
    public const string FolderIsStale = "folder_is_stale";
}

public static class EvidenceWeights
{
    public const int Uncertain = 0;
    public const int CommandUnresolvable = 25;
    public const int UninstallerMissing = 60;
    public const int TargetDirectoryMissing = 35;
    public const int InstallLocationMissing = 30;
    public const int DisplayIconMissing = 20;
    public const int MsiProductRegistrationMissing = 45;
    public const int MsiUserDataMissing = 45;
    public const int MsiCachePackageMissing = 45;
    public const int AppxInstallFolderMissing = 60;
    public const int AppxStoreRootPresent = 35;
    public const int AppxManifestMissing = 55;
    public const int AppxPackageFolderEmpty = 35;
    public const int ShortcutTargetMissing = 60;
    public const int StartupTargetMissing = 60;
    public const int TaskTargetMissing = 60;
    public const int FolderHasNoOwner = 40;
    public const int FolderHasNoExecutable = 30;
    public const int FolderIsStale = 20;
}

public static class FixResultKeys
{
    public const string Fixed = "fixed";
    public const string ManualCommandRequired = "manual_command_required";
    public const string NotEligible = "not_eligible";
    public const string PayloadMismatch = "payload_mismatch";
    public const string Failed = "failed";
}

public sealed record Evidence(string Kind, string Detail, int Weight)
{
    public bool IsConclusive => Weight > 0;
}

public sealed record Finding(
    string Id,
    string Title,
    string? Subtitle,
    EntryStatus Status,
    int Confidence,
    IReadOnlyList<Evidence> Evidence,
    string ProviderId,
    object Payload)
{
    public bool IsSelected { get; set; }
}

public sealed record FixResult(bool Success, string ResultKey, string? BackupPath = null, string? ManualCommand = null)
{
    public static FixResult Fixed(string backupPath) => new(true, FixResultKeys.Fixed, backupPath);
    public static FixResult Manual(string command) => new(false, FixResultKeys.ManualCommandRequired, null, command);
    public static FixResult NotEligible() => new(false, FixResultKeys.NotEligible);
    public static FixResult PayloadMismatch() => new(false, FixResultKeys.PayloadMismatch);
}

public static class ConfidenceRules
{
    public const int BrokenThreshold = 70;
    public const int SuspiciousThreshold = 20;
    public const int AutoFixThreshold = 90;
    public const int MinimumIndependentEvidence = 2;
    public const int UncertainCeiling = 60;
    public const int LeftoverFolderCeiling = 80;

    public static readonly IReadOnlyList<string> AutoFixableCategories =
        [Categories.Uninstall, Categories.Shortcut, Categories.Startup, Categories.Task];

    public static (EntryStatus Status, int Confidence) Evaluate(IReadOnlyList<Evidence> evidence, int ceiling = 100)
    {
        var conclusive = evidence.Count(x => x.IsConclusive);
        var uncertain = evidence.Count != conclusive;
        var confidence = Math.Clamp(evidence.Where(x => x.IsConclusive).Sum(x => x.Weight), 0, 100);
        if (uncertain) confidence = Math.Min(confidence, UncertainCeiling);
        confidence = Math.Min(confidence, ceiling);

        if (conclusive == 0) return (uncertain ? EntryStatus.Suspicious : EntryStatus.Healthy, confidence);
        if (conclusive >= MinimumIndependentEvidence && confidence >= BrokenThreshold) return (EntryStatus.Broken, confidence);
        return (EntryStatus.Suspicious, Math.Max(confidence, SuspiciousThreshold));
    }

    public static bool IsAutoFixable(Finding finding, string category) =>
        finding.Status == EntryStatus.Broken
        && finding.Confidence >= AutoFixThreshold
        && finding.Evidence.Count(x => x.IsConclusive) >= MinimumIndependentEvidence
        && AutoFixableCategories.Contains(category);
}

public sealed record RegistryLocation(
    RegistryHive Hive,
    RegistryView View,
    string SubKeyPath)
{
    public string DisplayPath => $"{Hive} ({(View == RegistryView.Registry64 ? "64-bit" : "32-bit")})\\{SubKeyPath}";
}

public sealed record UninstallEntry(
    string Id,
    string DisplayName,
    string? DisplayVersion,
    string? Publisher,
    string? UninstallString,
    string? InstallLocation,
    string? DisplayIcon,
    bool WindowsInstaller,
    bool SystemComponent,
    RegistryLocation Location);

public sealed record RegistryValueSnapshot(string Name, RegistryValueKind Kind, object? Value);
