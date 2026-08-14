using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Ghostlist.Core;

public sealed record DiagnosticsInput(
    IReadOnlyList<Finding> Findings,
    IReadOnlyDictionary<string, int> FindingsPerCategory,
    IReadOnlyList<ScanFailure> Failures,
    TimeSpan ScanDuration,
    IReadOnlyList<string> Errors);

public static class DiagnosticsBundle
{
    public const int HistoryLineLimit = 100;
    public const string SummaryEntry = "summary.json";
    public const string ErrorsEntry = "errors.txt";
    public const string HistoryEntry = "history.jsonl";
    public const string ReadmeEntry = "README.txt";

    public static string Write(string path, DiagnosticsInput input, string? historyPath = null)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        if (File.Exists(path)) File.Delete(path);

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(archive, SummaryEntry, Summary(input));
        Add(archive, ErrorsEntry, Errors(input));
        Add(archive, ReadmeEntry, Readme());

        var history = History(historyPath ?? OperationHistory.DefaultPath);
        if (history is not null) Add(archive, HistoryEntry, history);
        return path;
    }

    public static string Summary(DiagnosticsInput input) =>
        JsonSerializer.Serialize(new
        {
            product = "Ghostlist",
            version = Version(),
            windows = PathMasker.Mask(Environment.OSVersion.VersionString),
            architecture = Environment.Is64BitOperatingSystem ? "x64" : "x86",
            elevated = IsElevated(),
            processors = Environment.ProcessorCount,
            scanConcurrency = ScanOptions.DefaultConcurrency,
            scanMilliseconds = (long)input.ScanDuration.TotalMilliseconds,
            findings = input.Findings.Count,
            broken = input.Findings.Count(x => x.Status == EntryStatus.Broken),
            findingsPerCategory = input.FindingsPerCategory,
            failedCategories = input.Failures.Select(x => new
            {
                category = x.Category,
                message = PathMasker.Mask(x.Message)
            })
        }, new JsonSerializerOptions { WriteIndented = true });

    public static string Errors(DiagnosticsInput input)
    {
        var builder = new StringBuilder();
        foreach (var failure in input.Failures)
            builder.AppendLine($"[{failure.Category}] {PathMasker.Mask(failure.Message)}");
        foreach (var error in input.Errors)
            builder.AppendLine(PathMasker.Mask(error));
        return builder.Length == 0 ? "no errors were captured" + System.Environment.NewLine : builder.ToString();
    }

    public static string? History(string historyPath)
    {
        if (!File.Exists(historyPath)) return null;
        try
        {
            var lines = File.ReadAllLines(historyPath);
            var tail = lines.Length <= HistoryLineLimit ? lines : lines[^HistoryLineLimit..];
            return string.Join(System.Environment.NewLine, tail.Select(x => PathMasker.Mask(x)))
                + System.Environment.NewLine;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static bool IsElevated()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    public static string Version() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    private static string Readme() =>
        """
        Ghostlist diagnostics bundle

        This file was written to a location you chose. Ghostlist does not upload it and
        makes no network call while creating it. Nothing leaves this machine unless you
        send this file yourself.

        User names, machine name and SIDs are replaced with <user>, <machine> and <sid>.
        Open the files and read them before you share the bundle.

        summary.json   application and Windows version, elevation, scan timing and counts
        errors.txt     failures captured during the scan
        history.jsonl  last 100 lines of the local fix/restore log, when one exists
        """;

    private static void Add(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }
}
