using Microsoft.Win32;

namespace Ghostlist.Core;

public sealed record RegistryKeySnapshot(
    string Name,
    IReadOnlyList<RegistryValueSnapshot> Values,
    IReadOnlyList<RegistryKeySnapshot>? Children = null)
{
    public IReadOnlyList<RegistryKeySnapshot> ChildKeys => Children ?? [];
}

public sealed record RegistryTreeBackup(
    RegistryLocation Location,
    string DisplayName,
    DateTimeOffset CreatedAt,
    IReadOnlyList<RegistryValueSnapshot> Values,
    IReadOnlyList<RegistryKeySnapshot>? Children = null)
{
    public IReadOnlyList<RegistryKeySnapshot> ChildKeys => Children ?? [];
}

public interface IRegistryKeyHandle : IDisposable
{
    IReadOnlyList<string> GetValueNames();
    IReadOnlyList<string> GetSubKeyNames();
    RegistryValueKind GetValueKind(string name);
    object? GetValue(string name);
    void SetValue(string name, object value, RegistryValueKind kind);
    IRegistryKeyHandle? OpenSubKey(string name, bool writable = false);
    IRegistryKeyHandle CreateSubKey(string name);
    void DeleteSubKeyTree(string name);
    void DeleteValue(string name);
}

public interface IRegistryHiveAccessor
{
    IRegistryKeyHandle? OpenKey(RegistryHive hive, RegistryView view, string path, bool writable = false);
    IRegistryKeyHandle CreateKey(RegistryHive hive, RegistryView view, string path);
}

public interface IUninstallRepository
{
    IReadOnlyList<UninstallEntry> Scan();
    RegistryTreeBackup Capture(UninstallEntry entry);
    void Delete(RegistryLocation location);
    void Restore(RegistryTreeBackup backup);
}

public sealed class WindowsRegistryHiveAccessor : IRegistryHiveAccessor
{
    public IRegistryKeyHandle? OpenKey(RegistryHive hive, RegistryView view, string path, bool writable = false)
    {
        var baseKey = RegistryKey.OpenBaseKey(hive, view);
        var key = baseKey.OpenSubKey(path, writable);
        if (key is null)
        {
            baseKey.Dispose();
            return null;
        }
        return new WindowsRegistryKeyHandle(key, baseKey);
    }

    public IRegistryKeyHandle CreateKey(RegistryHive hive, RegistryView view, string path)
    {
        var baseKey = RegistryKey.OpenBaseKey(hive, view);
        return new WindowsRegistryKeyHandle(baseKey.CreateSubKey(path, writable: true), baseKey);
    }
}

internal sealed class WindowsRegistryKeyHandle(RegistryKey key, RegistryKey? owner) : IRegistryKeyHandle
{
    public IReadOnlyList<string> GetValueNames() => key.GetValueNames();
    public IReadOnlyList<string> GetSubKeyNames() => key.GetSubKeyNames();
    public RegistryValueKind GetValueKind(string name) => key.GetValueKind(name);
    public object? GetValue(string name) => key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    public void SetValue(string name, object value, RegistryValueKind kind) => key.SetValue(name, value, kind);
    public void DeleteSubKeyTree(string name) => key.DeleteSubKeyTree(name, throwOnMissingSubKey: true);
    public void DeleteValue(string name) => key.DeleteValue(name, throwOnMissingValue: true);

    public IRegistryKeyHandle? OpenSubKey(string name, bool writable = false)
    {
        var sub = key.OpenSubKey(name, writable);
        return sub is null ? null : new WindowsRegistryKeyHandle(sub, null);
    }

    public IRegistryKeyHandle CreateSubKey(string name) => new WindowsRegistryKeyHandle(key.CreateSubKey(name, writable: true), null);

    public void Dispose()
    {
        key.Dispose();
        owner?.Dispose();
    }
}

public sealed class WindowsUninstallRepository(IRegistryHiveAccessor accessor) : IUninstallRepository
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public WindowsUninstallRepository() : this(new WindowsRegistryHiveAccessor()) { }

    public IReadOnlyList<UninstallEntry> Scan()
    {
        var result = new List<UninstallEntry>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var root = accessor.OpenKey(hive, view, UninstallPath);
            if (root is null) continue;
            foreach (var name in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(name);
                var displayName = key?.GetValue("DisplayName") as string;
                if (key is null || string.IsNullOrWhiteSpace(displayName)) continue;
                var location = new RegistryLocation(hive, view, $@"{UninstallPath}\{name}");
                result.Add(new UninstallEntry(
                    $"{hive}:{view}:{name}", displayName,
                    key.GetValue("DisplayVersion") as string,
                    key.GetValue("Publisher") as string,
                    key.GetValue("QuietUninstallString") as string ?? key.GetValue("UninstallString") as string,
                    key.GetValue("InstallLocation") as string,
                    key.GetValue("DisplayIcon") as string,
                    Convert.ToInt32(key.GetValue("WindowsInstaller") ?? 0) == 1,
                    Convert.ToInt32(key.GetValue("SystemComponent") ?? 0) == 1,
                    location));
            }
        }
        return result.GroupBy(x => x.Id).Select(x => x.First()).OrderBy(x => x.DisplayName).ToList();
    }

    public RegistryTreeBackup Capture(UninstallEntry entry)
    {
        using var key = accessor.OpenKey(entry.Location.Hive, entry.Location.View, entry.Location.SubKeyPath)
            ?? throw new InvalidOperationException("Kayıt artık mevcut değil.");
        var slash = entry.Location.SubKeyPath.LastIndexOf('\\');
        var snapshot = CaptureKey(key, entry.Location.SubKeyPath[(slash + 1)..]);
        return new RegistryTreeBackup(entry.Location, entry.DisplayName, DateTimeOffset.Now, snapshot.Values, snapshot.ChildKeys);
    }

    public void Delete(RegistryLocation location)
    {
        var slash = location.SubKeyPath.LastIndexOf('\\');
        using var parent = accessor.OpenKey(location.Hive, location.View, location.SubKeyPath[..slash], writable: true)
            ?? throw new InvalidOperationException("Üst kayıt anahtarı bulunamadı.");
        parent.DeleteSubKeyTree(location.SubKeyPath[(slash + 1)..]);
    }

    public void Restore(RegistryTreeBackup backup)
    {
        using var key = accessor.CreateKey(backup.Location.Hive, backup.Location.View, backup.Location.SubKeyPath);
        WriteKey(key, backup.Values, backup.ChildKeys);
    }

    private static RegistryKeySnapshot CaptureKey(IRegistryKeyHandle key, string name)
    {
        var values = key.GetValueNames().Select(value => new RegistryValueSnapshot(
            value, key.GetValueKind(value), RegistryValueCodec.Normalize(key.GetValue(value)))).ToList();
        var children = new List<RegistryKeySnapshot>();
        foreach (var child in key.GetSubKeyNames())
        {
            using var sub = key.OpenSubKey(child);
            if (sub is null) continue;
            children.Add(CaptureKey(sub, child));
        }
        return new RegistryKeySnapshot(name, values, children);
    }

    private static void WriteKey(IRegistryKeyHandle key, IReadOnlyList<RegistryValueSnapshot> values, IReadOnlyList<RegistryKeySnapshot> children)
    {
        foreach (var item in values)
            key.SetValue(item.Name, RegistryValueCodec.Denormalize(item), item.Kind);
        foreach (var child in children)
        {
            using var sub = key.CreateSubKey(child.Name);
            WriteKey(sub, child.Values, child.ChildKeys);
        }
    }

}
