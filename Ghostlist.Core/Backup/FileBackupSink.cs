using System.Text.Json;

namespace Ghostlist.Core;

public sealed record FileBackupManifest(string Kind, string OriginalPath, string BackupPath, DateTimeOffset CreatedAt);

public sealed class FileBackupSink(string directory, IUninstallRepository repository) : IBackupSink
{
    public const string FileManifestKind = "file";
    private const string ManifestSuffix = ".ghost.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string SaveRegistryTree(RegistryTreeBackup backup, string label)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{Stamp()}-{SafeName(label)}-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(backup, JsonOptions));
        return path;
    }

    public string MoveFileToBackup(string sourcePath, string label)
    {
        var payloadDirectory = Path.Combine(directory, "files");
        Directory.CreateDirectory(payloadDirectory);
        var payloadPath = Path.Combine(payloadDirectory, $"{Stamp()}-{SafeName(label)}-{Guid.NewGuid():N}{Path.GetExtension(sourcePath)}");
        File.Move(sourcePath, payloadPath);
        var manifestPath = Path.Combine(directory, $"{Stamp()}-{SafeName(label)}-{Guid.NewGuid():N}{ManifestSuffix}");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(
            new FileBackupManifest(FileManifestKind, Path.GetFullPath(sourcePath), payloadPath, DateTimeOffset.Now), JsonOptions));
        return manifestPath;
    }

    public string SaveText(string content, string label, string extension)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{Stamp()}-{SafeName(label)}-{Guid.NewGuid():N}{extension}");
        File.WriteAllText(path, content);
        return path;
    }

    public void Restore(string backupPath)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(directory), Path.GetFullPath(backupPath));
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            throw new InvalidOperationException("Yalnızca Ghostlist yedekleri geri yüklenebilir.");

        var content = File.ReadAllText(backupPath);
        if (backupPath.EndsWith(ManifestSuffix, StringComparison.OrdinalIgnoreCase))
        {
            var manifest = JsonSerializer.Deserialize<FileBackupManifest>(content, JsonOptions)
                ?? throw new InvalidDataException("Yedek dosyası geçersiz.");
            Directory.CreateDirectory(Path.GetDirectoryName(manifest.OriginalPath)!);
            File.Move(manifest.BackupPath, manifest.OriginalPath, overwrite: true);
            File.Delete(backupPath);
            return;
        }

        var tree = JsonSerializer.Deserialize<RegistryTreeBackup>(content, JsonOptions)
            ?? throw new InvalidDataException("Yedek dosyası geçersiz.");
        repository.Restore(tree);
    }

    public IReadOnlyList<string> List()
    {
        if (!Directory.Exists(directory)) return [];
        return Directory.GetFiles(directory, "*.json").OrderByDescending(x => x).ToList();
    }

    private static string Stamp() => DateTime.Now.ToString("yyyyMMdd-HHmmss");

    private static string SafeName(string label) =>
        string.Concat(label.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
