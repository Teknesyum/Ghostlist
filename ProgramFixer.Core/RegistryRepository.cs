using Microsoft.Win32;
using System.Text.Json;

namespace ProgramFixer.Core;

public interface IUninstallRepository
{
    IReadOnlyList<UninstallEntry> Scan();
    RegistryBackup Capture(UninstallEntry entry);
    void Delete(RegistryLocation location);
    void Restore(RegistryBackup backup);
}

public sealed class WindowsUninstallRepository : IUninstallRepository
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public IReadOnlyList<UninstallEntry> Scan()
    {
        var result = new List<UninstallEntry>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var root = baseKey.OpenSubKey(UninstallPath, writable: false);
            if (root is null) continue;
            foreach (var name in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(name, writable: false);
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
                    Convert.ToInt32(key.GetValue("WindowsInstaller", 0)) == 1,
                    Convert.ToInt32(key.GetValue("SystemComponent", 0)) == 1,
                    location));
            }
        }
        return result.GroupBy(x => x.Id).Select(x => x.First()).OrderBy(x => x.DisplayName).ToList();
    }

    public RegistryBackup Capture(UninstallEntry entry)
    {
        using var baseKey = RegistryKey.OpenBaseKey(entry.Location.Hive, entry.Location.View);
        using var key = baseKey.OpenSubKey(entry.Location.SubKeyPath, writable: false)
            ?? throw new InvalidOperationException("Kayıt artık mevcut değil.");
        var values = key.GetValueNames().Select(name => new RegistryValueSnapshot(
            name, key.GetValueKind(name), NormalizeValue(key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)))).ToList();
        return new RegistryBackup(entry.Location, entry.DisplayName, DateTimeOffset.Now, values);
    }

    public void Delete(RegistryLocation location)
    {
        using var baseKey = RegistryKey.OpenBaseKey(location.Hive, location.View);
        var slash = location.SubKeyPath.LastIndexOf('\\');
        using var parent = baseKey.OpenSubKey(location.SubKeyPath[..slash], writable: true)
            ?? throw new InvalidOperationException("Üst kayıt anahtarı bulunamadı.");
        parent.DeleteSubKeyTree(location.SubKeyPath[(slash + 1)..], throwOnMissingSubKey: true);
    }

    public void Restore(RegistryBackup backup)
    {
        using var baseKey = RegistryKey.OpenBaseKey(backup.Location.Hive, backup.Location.View);
        using var key = baseKey.CreateSubKey(backup.Location.SubKeyPath, writable: true);
        foreach (var item in backup.Values)
            key.SetValue(item.Name, DenormalizeValue(item), item.Kind);
    }

    private static object? NormalizeValue(object? value) => value is byte[] bytes ? Convert.ToBase64String(bytes) : value;
    private static object DenormalizeValue(RegistryValueSnapshot item)
    {
        if (item.Value is not JsonElement json)
            return item.Kind == RegistryValueKind.Binary && item.Value is string text ? Convert.FromBase64String(text) : item.Value ?? string.Empty;
        return item.Kind switch
        {
            RegistryValueKind.DWord => json.GetInt32(),
            RegistryValueKind.QWord => json.GetInt64(),
            RegistryValueKind.MultiString => json.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray(),
            RegistryValueKind.Binary => Convert.FromBase64String(json.GetString() ?? string.Empty),
            _ => json.GetString() ?? string.Empty
        };
    }
}

public sealed class CleanupService(IUninstallRepository repository, EntryClassifier classifier, string backupDirectory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public IReadOnlyList<UninstallEntry> Scan() => repository.Scan().Select(classifier.Classify).ToList();

    public string RemoveBrokenEntry(UninstallEntry entry)
    {
        if (entry.Status != EntryStatus.Broken) throw new InvalidOperationException("Yalnızca doğrulanmış bozuk kayıtlar kaldırılabilir.");
        Directory.CreateDirectory(backupDirectory);
        var backup = repository.Capture(entry);
        var safeName = string.Concat(entry.DisplayName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var path = Path.Combine(backupDirectory, $"{DateTime.Now:yyyyMMdd-HHmmss}-{safeName}-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(backup, JsonOptions));
        repository.Delete(entry.Location);
        return path;
    }

    public void Restore(string path)
    {
        var relativePath = Path.GetRelativePath(Path.GetFullPath(backupDirectory), Path.GetFullPath(path));
        if (Path.IsPathRooted(relativePath) || relativePath == ".." || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            throw new InvalidOperationException("Yalnızca ProgramFixer yedekleri geri yüklenebilir.");
        var backup = JsonSerializer.Deserialize<RegistryBackup>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Yedek dosyası geçersiz.");
        repository.Restore(backup);
    }
}
