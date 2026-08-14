using Microsoft.Win32;

namespace Ghostlist.Core;

public sealed record MsiRegistration(
    ProbeResult ProductKey,
    ProbeResult UserData,
    ProbeResult CachePackage,
    string? LocalPackagePath);

public interface IMsiCatalog
{
    MsiRegistration Lookup(string productCode);
}

public sealed class RegistryMsiCatalog(IRegistryHiveAccessor accessor, IFileSystem fileSystem) : IMsiCatalog
{
    private const string MachineProducts = @"SOFTWARE\Classes\Installer\Products";
    private const string UserProducts = @"SOFTWARE\Microsoft\Installer\Products";
    private const string UserDataRoot = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UserData";

    public MsiRegistration Lookup(string productCode)
    {
        var packed = PackedGuid.Pack(productCode);
        if (packed is null) return new MsiRegistration(ProbeResult.Unknown, ProbeResult.Unknown, ProbeResult.Unknown, null);

        var productKey = ProbeKey(RegistryHive.LocalMachine, RegistryView.Registry64, $@"{MachineProducts}\{packed}");
        if (productKey == ProbeResult.Missing)
            productKey = ProbeKey(RegistryHive.CurrentUser, RegistryView.Registry64, $@"{UserProducts}\{packed}");

        var localPackage = FindLocalPackage(packed, out var userData);
        var cache = localPackage is null ? ProbeResult.Unknown : fileSystem.ProbeFile(localPackage);
        return new MsiRegistration(productKey, userData, cache, localPackage);
    }

    private ProbeResult ProbeKey(RegistryHive hive, RegistryView view, string path)
    {
        try
        {
            using var key = accessor.OpenKey(hive, view, path);
            return key is null ? ProbeResult.Missing : ProbeResult.Present;
        }
        catch { return ProbeResult.Unknown; }
    }

    private string? FindLocalPackage(string packed, out ProbeResult userData)
    {
        userData = ProbeResult.Missing;
        try
        {
            using var root = accessor.OpenKey(RegistryHive.LocalMachine, RegistryView.Registry64, UserDataRoot);
            if (root is null)
            {
                userData = ProbeResult.Unknown;
                return null;
            }
            foreach (var sid in root.GetSubKeyNames())
            {
                using var properties = accessor.OpenKey(RegistryHive.LocalMachine, RegistryView.Registry64,
                    $@"{UserDataRoot}\{sid}\Products\{packed}\InstallProperties");
                if (properties is null) continue;
                userData = ProbeResult.Present;
                return properties.GetValue("LocalPackage") as string;
            }
            return null;
        }
        catch
        {
            userData = ProbeResult.Unknown;
            return null;
        }
    }
}
