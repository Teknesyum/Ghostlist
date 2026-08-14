using Microsoft.Win32;

namespace Ghostlist.Core;

public enum EntryStatus { Healthy, Broken, Suspicious, Unsupported }

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
    RegistryLocation Location,
    EntryStatus Status = EntryStatus.Suspicious,
    string Reason = "Henüz değerlendirilmedi.",
    string? ResolvedTarget = null)
{
    public bool IsSelected { get; set; }

    public string StatusText => Status switch
    {
        EntryStatus.Healthy => "Sağlam",
        EntryStatus.Broken => "Bozuk",
        EntryStatus.Unsupported => "Desteklenmiyor",
        _ => "Şüpheli"
    };
}

public sealed record RegistryValueSnapshot(string Name, RegistryValueKind Kind, object? Value);
public sealed record RegistryBackup(RegistryLocation Location, string DisplayName, DateTimeOffset CreatedAt, IReadOnlyList<RegistryValueSnapshot> Values);
