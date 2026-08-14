using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Ghostlist.Core;

public sealed record ReportRow(
    string Category,
    string Name,
    string Status,
    int Confidence,
    string Target,
    string Location,
    string Evidence);

public static class ReportFormats
{
    public const string Csv = "csv";
    public const string Json = "json";

    public static string For(string path) =>
        Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase) ? Json : Csv;
}

public static class ScanReport
{
    public static readonly IReadOnlyList<string> Columns =
        ["Category", "Name", "Status", "Confidence", "Target", "Location", "Evidence"];

    public static IReadOnlyList<ReportRow> Rows(CleanupService service, IEnumerable<Finding> findings) =>
        findings.Select(x => Row(service.CategoryOf(x), x)).ToList();

    public static ReportRow Row(string category, Finding finding) =>
        new(category,
            finding.Title,
            finding.Status.ToString(),
            finding.Confidence,
            Target(finding),
            Location(finding),
            string.Join("; ", finding.Evidence.Select(x => $"{x.Kind}={x.Weight}")));

    public static void Write(string path, IReadOnlyList<ReportRow> rows)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        if (ReportFormats.For(path) == ReportFormats.Json)
            File.WriteAllText(path, Json(rows), new UTF8Encoding(false));
        else
            File.WriteAllText(path, Csv(rows), new UTF8Encoding(true));
    }

    public static string Csv(IReadOnlyList<ReportRow> rows)
    {
        var builder = new StringBuilder();
        builder.Append(string.Join(",", Columns)).Append("\r\n");
        foreach (var row in rows)
            builder
                .Append(string.Join(",", Fields(row).Select(Cell)))
                .Append("\r\n");
        return builder.ToString();
    }

    public static string Json(IReadOnlyList<ReportRow> rows) =>
        JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true });

    public static string Cell(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && "=+-@\t\r".Contains(text[0])) text = "'" + text;
        return text.Contains('"') || text.Contains(',') || text.Contains('\n') || text.Contains('\r')
            ? $"\"{text.Replace("\"", "\"\"")}\""
            : text;
    }

    private static IEnumerable<string> Fields(ReportRow row) =>
        [row.Category, row.Name, row.Status, row.Confidence.ToString(CultureInfo.InvariantCulture),
         row.Target, row.Location, row.Evidence];

    private static string Target(Finding finding) => finding.Payload switch
    {
        UninstallEntry entry => entry.UninstallString ?? entry.InstallLocation ?? string.Empty,
        ShortcutIssue issue => issue.TargetPath,
        StartupValueIssue issue => issue.TargetPath,
        StartupShortcutIssue issue => issue.TargetPath,
        ScheduledTaskIssue issue => issue.TargetPath,
        LeftoverFolderIssue issue => issue.FolderPath,
        AppxPackage package => package.InstallLocation ?? string.Empty,
        _ => finding.Subtitle ?? string.Empty
    };

    private static string Location(Finding finding) => finding.Payload switch
    {
        UninstallEntry entry => entry.Location.DisplayPath,
        ShortcutIssue issue => issue.ShortcutPath,
        StartupValueIssue issue => $"{issue.Location.DisplayPath}\\{issue.ValueName}",
        StartupShortcutIssue issue => issue.ShortcutPath,
        ScheduledTaskIssue issue => issue.XmlPath,
        LeftoverFolderIssue issue => issue.FolderPath,
        AppxPackage package => package.FullName,
        _ => finding.Subtitle ?? string.Empty
    };
}
