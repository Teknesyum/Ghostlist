using System.Text.Json;

namespace Ghostlist.Core;

public static class BackupKinds
{
    public const string RegistryTree = "registry_tree";
    public const string RegistryValue = "registry_value";
    public const string File = "file";
    public const string Directory = "directory";
    public const string Unreadable = "unreadable";
}

public sealed record BackupCatalogEntry(
    string Path,
    string KindKey,
    DateTimeOffset? CreatedAt,
    string? DisplayName,
    string? Target,
    bool CanRestore,
    long SizeBytes)
{
    public bool IsReadable => KindKey != BackupKinds.Unreadable;

    public bool IsOlderThan(TimeSpan age, DateTimeOffset now) =>
        CreatedAt is null || now - CreatedAt.Value > age;
}

public sealed class BackupCatalog(string directory)
{
    public const int StaleDays = 90;
    private const int HeadBytes = 64 * 1024;

    public string Directory => directory;

    public IReadOnlyList<BackupCatalogEntry> List()
    {
        if (!System.IO.Directory.Exists(directory)) return [];

        var entries = new List<BackupCatalogEntry>();
        foreach (var path in System.IO.Directory.GetFiles(directory, "*.json"))
            entries.Add(Describe(path));

        return entries
            .OrderByDescending(x => x.CreatedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(x => x.Path, StringComparer.Ordinal)
            .ToList();
    }

    public long TotalSize() => List().Sum(x => x.SizeBytes);

    public void Delete(BackupCatalogEntry entry)
    {
        EnsureInside(entry.Path);
        var payload = PayloadOf(entry.Path);
        if (payload is not null)
        {
            EnsureInside(payload);
            if (System.IO.Directory.Exists(payload)) System.IO.Directory.Delete(payload, recursive: true);
            else if (System.IO.File.Exists(payload)) System.IO.File.Delete(payload);
        }
        if (System.IO.File.Exists(entry.Path)) System.IO.File.Delete(entry.Path);
    }

    private BackupCatalogEntry Describe(string path)
    {
        long size;
        try { size = new FileInfo(path).Length; }
        catch { size = 0; }

        Dictionary<string, string> fields;
        try { fields = ReadHead(path); }
        catch { return Unreadable(path, size); }

        if (fields.TryGetValue("Kind", out var kind) && fields.TryGetValue("OriginalPath", out var original))
        {
            if (kind != BackupKinds.File && kind != BackupKinds.Directory) return Unreadable(path, size);
            var payload = fields.GetValueOrDefault("BackupPath");
            var restorable = payload is not null && (System.IO.File.Exists(payload) || System.IO.Directory.Exists(payload));
            return new BackupCatalogEntry(
                path, kind, ParseDate(fields),
                System.IO.Path.GetFileName(original.TrimEnd(System.IO.Path.DirectorySeparatorChar)),
                original, restorable, size + PayloadSize(payload));
        }

        var subKey = fields.GetValueOrDefault("Location.SubKeyPath");
        if (subKey is null) return Unreadable(path, size);
        var hive = fields.GetValueOrDefault("Location.Hive");
        var root = hive is null ? subKey : $"{hive}\\{subKey}";

        if (fields.TryGetValue("Value.Name", out var valueName))
            return new BackupCatalogEntry(
                path, BackupKinds.RegistryValue, ParseDate(fields),
                valueName.Length == 0 ? subKey : valueName, $"{root}\\{valueName}", true, size);

        return new BackupCatalogEntry(
            path, BackupKinds.RegistryTree, ParseDate(fields),
            fields.GetValueOrDefault("DisplayName") ?? subKey, root, true, size);
    }

    private static BackupCatalogEntry Unreadable(string path, long size) =>
        new(path, BackupKinds.Unreadable, null, System.IO.Path.GetFileName(path), null, false, size);

    private static DateTimeOffset? ParseDate(IReadOnlyDictionary<string, string> fields) =>
        fields.TryGetValue("CreatedAt", out var text)
        && DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var value)
            ? value
            : null;

    private string? PayloadOf(string path)
    {
        try
        {
            var fields = ReadHead(path);
            if (!fields.ContainsKey("OriginalPath")) return null;
            return fields.GetValueOrDefault("BackupPath");
        }
        catch { return null; }
    }

    private static long PayloadSize(string? payload)
    {
        try
        {
            if (payload is null) return 0;
            if (System.IO.File.Exists(payload)) return new FileInfo(payload).Length;
            if (!System.IO.Directory.Exists(payload)) return 0;
            return System.IO.Directory.EnumerateFiles(payload, "*", SearchOption.AllDirectories)
                .Sum(x => { try { return new FileInfo(x).Length; } catch { return 0L; } });
        }
        catch { return 0; }
    }

    private void EnsureInside(string path)
    {
        var relative = System.IO.Path.GetRelativePath(System.IO.Path.GetFullPath(directory), System.IO.Path.GetFullPath(path));
        if (System.IO.Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith($"..{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            throw new InvalidOperationException("Yalnızca Ghostlist yedekleri silinebilir.");
    }

    private static Dictionary<string, string> ReadHead(string path)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var buffer = ReadPrefix(path);
        var reader = new Utf8JsonReader(buffer, isFinalBlock: false, state: default);
        var scopes = new Stack<string>();
        string? property = null;

        try
        {
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        property = reader.GetString();
                        break;
                    case JsonTokenType.StartObject:
                        if (property is not null) scopes.Push(property);
                        property = null;
                        break;
                    case JsonTokenType.EndObject:
                        if (scopes.Count > 0) scopes.Pop();
                        property = null;
                        break;
                    case JsonTokenType.StartArray:
                        if (!SkipArray(ref reader)) return fields;
                        property = null;
                        break;
                    case JsonTokenType.String:
                    case JsonTokenType.Number:
                    case JsonTokenType.True:
                    case JsonTokenType.False:
                    case JsonTokenType.Null:
                        if (property is not null && scopes.Count <= 1)
                            fields[Key(scopes, property)] = Scalar(ref reader);
                        property = null;
                        break;
                }
            }
        }
        catch (JsonException) { }

        return fields;
    }

    private static bool SkipArray(ref Utf8JsonReader reader)
    {
        var depth = 1;
        while (depth > 0)
        {
            if (!reader.Read()) return false;
            if (reader.TokenType == JsonTokenType.StartArray) depth++;
            else if (reader.TokenType == JsonTokenType.EndArray) depth--;
        }
        return true;
    }

    private static string Key(Stack<string> scopes, string property) =>
        scopes.Count == 0 ? property : $"{scopes.Peek()}.{property}";

    private static string Scalar(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.String => reader.GetString() ?? string.Empty,
        JsonTokenType.True => "true",
        JsonTokenType.False => "false",
        JsonTokenType.Null => string.Empty,
        _ => System.Text.Encoding.UTF8.GetString(reader.ValueSpan)
    };

    private static byte[] ReadPrefix(string path)
    {
        using var stream = System.IO.File.OpenRead(path);
        var length = (int)Math.Min(stream.Length, HeadBytes);
        var buffer = new byte[length];
        var read = 0;
        while (read < length)
        {
            var step = stream.Read(buffer, read, length - read);
            if (step <= 0) break;
            read += step;
        }
        return read == length ? buffer : buffer[..read];
    }
}
