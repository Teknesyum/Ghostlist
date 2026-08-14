using System.Text;
using System.Text.Json;

namespace Ghostlist.Core;

public static class OperationKinds
{
    public const string Fix = "fix";
    public const string Restore = "restore";
}

public sealed record OperationRecord(
    DateTimeOffset At,
    string Operation,
    string Category,
    string Target,
    string ResultKey,
    string? BackupPath);

public sealed class OperationHistory(string filePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public OperationHistory() : this(DefaultPath) { }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ghostlist", "history.jsonl");

    public string FilePath => filePath;

    public void Append(OperationRecord record)
    {
        try
        {
            var parent = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            File.AppendAllText(filePath, JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
            // Günlük yazılamazsa işlem durmaz.
        }
    }

    public IReadOnlyList<OperationRecord> Read()
    {
        if (!File.Exists(filePath)) return [];

        var records = new List<OperationRecord>();
        try
        {
            foreach (var line in File.ReadLines(filePath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var record = JsonSerializer.Deserialize<OperationRecord>(line, JsonOptions);
                    if (record is not null) records.Add(record);
                }
                catch (JsonException)
                {
                }
            }
        }
        catch (IOException)
        {
            return records;
        }

        records.Reverse();
        return records;
    }
}
