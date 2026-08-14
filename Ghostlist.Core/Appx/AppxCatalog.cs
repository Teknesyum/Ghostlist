using Microsoft.Win32;

namespace Ghostlist.Core;

public sealed record AppxPackage(string FullName, string? InstallLocation);

public interface IAppxCatalog
{
    IReadOnlyList<AppxPackage> GetStagedPackages();
}

public sealed class RegistryAppxCatalog(IRegistryHiveAccessor accessor) : IAppxCatalog
{
    private const string PackageRepository =
        @"SOFTWARE\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\PackageRepository\Packages";

    public IReadOnlyList<AppxPackage> GetStagedPackages()
    {
        try
        {
            using var root = accessor.OpenKey(RegistryHive.LocalMachine, RegistryView.Registry64, PackageRepository);
            if (root is null) return [];
            var packages = new List<AppxPackage>();
            foreach (var name in root.GetSubKeyNames())
            {
                using var key = accessor.OpenKey(RegistryHive.LocalMachine, RegistryView.Registry64, $@"{PackageRepository}\{name}");
                if (key is null) continue;
                var path = key.GetValue("Path") as string ?? key.GetValue("PackageRootFolder") as string;
                packages.Add(new AppxPackage(name, string.IsNullOrWhiteSpace(path) ? null : path));
            }
            return packages;
        }
        catch { return []; }
    }
}
