using System.Text.Json;
using Microsoft.Win32;

namespace Ghostlist.Core;

public sealed record PathBackupManifest(string Kind, string OriginalPath, string BackupPath, DateTimeOffset CreatedAt);

public sealed class FileBackupSink(string directory, IUninstallRepository repository, IRegistryHiveAccessor accessor) : IBackupSink
{
    public const string FileKind = "file";
    public const string DirectoryKind = "directory";
    private const string ManifestSuffix = ".ghost.json";
    private const string ValueSuffix = ".value.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public FileBackupSink(string directory, IUninstallRepository repository)
        : this(directory, repository, new WindowsRegistryHiveAccessor()) { }

    public string SaveRegistryTree(RegistryTreeBackup backup, string label) =>
        Write(JsonSerializer.Serialize(backup, JsonOptions), label, ".json");

    public string SaveRegistryValue(RegistryValueBackup backup, string label) =>
        Write(JsonSerializer.Serialize(backup, JsonOptions), label, ValueSuffix);

    public string SaveText(string content, string label, string extension) => Write(content, label, extension);

    public string MoveFileToBackup(string sourcePath, string label)
    {
        var payloadPath = ReservePayloadPath(label, Path.GetExtension(sourcePath));
        File.Move(sourcePath, payloadPath);
        return WriteManifest(FileKind, sourcePath, payloadPath, label);
    }

    public string MoveDirectoryToBackup(string sourcePath, string label)
    {
        var payloadPath = ReservePayloadPath(label, string.Empty);
        Directory.Move(sourcePath, payloadPath);
        return WriteManifest(DirectoryKind, sourcePath, payloadPath, label);
    }

    public void Restore(string backupPath)
    {
        EnsureInsideBackupDirectory(backupPath);
        var content = File.ReadAllText(backupPath);

        if (backupPath.EndsWith(ManifestSuffix, StringComparison.OrdinalIgnoreCase))
        {
            var manifest = Deserialize<PathBackupManifest>(content);
            EnsureInsideBackupDirectory(manifest.BackupPath);
            var parent = Path.GetDirectoryName(manifest.OriginalPath);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            if (manifest.Kind == DirectoryKind) Directory.Move(manifest.BackupPath, manifest.OriginalPath);
            else File.Move(manifest.BackupPath, manifest.OriginalPath, overwrite: true);
            File.Delete(backupPath);
            return;
        }

        if (backupPath.EndsWith(ValueSuffix, StringComparison.OrdinalIgnoreCase))
        {
            var backup = Deserialize<RegistryValueBackup>(content);
            using var key = accessor.CreateKey(backup.Location.Hive, backup.Location.View, backup.Location.SubKeyPath);
            key.SetValue(backup.Value.Name, RegistryValueCodec.Denormalize(backup.Value), backup.Value.Kind);
            return;
        }

        repository.Restore(Deserialize<RegistryTreeBackup>(content));
    }

    public IReadOnlyList<string> List()
    {
        if (!Directory.Exists(directory)) return [];
        return Directory.GetFiles(directory, "*.json").OrderByDescending(x => x, StringComparer.Ordinal).ToList();
    }

    private void EnsureInsideBackupDirectory(string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(directory), Path.GetFullPath(path));
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            throw new InvalidOperationException("Yalnızca Ghostlist yedekleri geri yüklenebilir.");
    }

    private static T Deserialize<T>(string content) =>
        JsonSerializer.Deserialize<T>(content, JsonOptions) ?? throw new InvalidDataException("Yedek dosyası geçersiz.");

    private string Write(string content, string label, string extension)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{Stamp()}-{SafeName(label)}-{Guid.NewGuid():N}{extension}");
        File.WriteAllText(path, content);
        return path;
    }

    private string ReservePayloadPath(string label, string extension)
    {
        var payloadDirectory = Path.Combine(directory, "payload");
        Directory.CreateDirectory(payloadDirectory);
        return Path.Combine(payloadDirectory, $"{Stamp()}-{SafeName(label)}-{Guid.NewGuid():N}{extension}");
    }

    private string WriteManifest(string kind, string originalPath, string payloadPath, string label) =>
        Write(JsonSerializer.Serialize(
            new PathBackupManifest(kind, Path.GetFullPath(originalPath), payloadPath, DateTimeOffset.Now), JsonOptions),
            label, ManifestSuffix);

    private static string Stamp() => DateTime.Now.ToString("yyyyMMdd-HHmmss");

    private static string SafeName(string label) =>
        string.Concat(label.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}

public static class RegistryValueCodec
{
    public static object? Normalize(object? value) => value is byte[] bytes ? Convert.ToBase64String(bytes) : value;

    public static object Denormalize(RegistryValueSnapshot item)
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
