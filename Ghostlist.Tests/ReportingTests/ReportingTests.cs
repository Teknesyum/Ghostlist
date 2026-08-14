using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using Ghostlist.Core;
using Xunit;

namespace Ghostlist.Tests.ReportingTests;

public class PathMaskerTests
{
    [Theory]
    [InlineData(@"C:\Users\ahmet\AppData\Local\Ghostlist", @"C:\Users\<user>\AppData\Local\Ghostlist")]
    [InlineData(@"c:\users\Ahmet\Desktop\x.lnk", @"c:\users\<user>\Desktop\x.lnk")]
    [InlineData(@"\\?\C:\Users\ahmet\", @"\\?\C:\Users\<user>\")]
    [InlineData(@"C:\Users\ahmet", @"C:\Users\<user>")]
    public void UserFolderIsMaskedWhoeverTheUserIs(string input, string expected) =>
        Assert.Equal(expected, PathMasker.Mask(input, "someone-else", "SOME-PC"));

    [Fact]
    public void SidIsMasked() =>
        Assert.Equal(
            @"HKEY_USERS\<sid>\SOFTWARE",
            PathMasker.Mask(@"HKEY_USERS\S-1-5-21-1004336348-1177238915-682003330-512\SOFTWARE", "u", "m"));

    [Fact]
    public void MachineNameIsMasked() =>
        Assert.Equal(@"\\<machine>\share", PathMasker.Mask(@"\\GHOST-PC\share", "ahmet", "GHOST-PC"));

    [Fact]
    public void UserNameIsMaskedOutsideOfPathsToo() =>
        Assert.Equal("owner <user> ran it", PathMasker.Mask("owner ahmet ran it", "ahmet", "GHOST-PC"));

    [Fact]
    public void SimilarWordsAreNotMangled() =>
        Assert.Equal("ahmetoglu", PathMasker.Mask("ahmetoglu", "ahmet", "GHOST-PC"));

    [Fact]
    public void StackTracesAreMaskedIncludingTheSourceFilePath()
    {
        var trace = string.Join(System.Environment.NewLine,
            "System.IO.IOException: access to GHOST-PC denied",
            @"   at Ghostlist.Core.FileBackupSink.Move(String path) in C:\Users\ahmet\src\Ghostlist\Ghostlist.Core\Backup\FileBackupSink.cs:line 42",
            @"   at Ghostlist.App.MainViewModel.FixAsync() in C:\Users\ahmet\src\Ghostlist\Ghostlist.App\ViewModels\MainViewModel.cs:line 300");

        var masked = PathMasker.Mask(trace, "ahmet", "GHOST-PC");

        Assert.DoesNotContain("ahmet", masked, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GHOST-PC", masked, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"C:\Users\<user>\src\Ghostlist", masked);
        Assert.Contains("line 42", masked);
    }

    [Fact]
    public void EmptyInputStaysEmpty() => Assert.Equal(string.Empty, PathMasker.Mask(null, "a", "b"));
}

public class ScanReportTests
{
    [Theory]
    [InlineData("=cmd|'/c calc'!A1", "'=cmd|'/c calc'!A1")]
    [InlineData("+1234", "'+1234")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    public void FormulaLeadingCharactersAreNeutralised(string input, string expected) =>
        Assert.Equal(expected, ScanReport.Cell(input));

    [Fact]
    public void CommasAndQuotesAreEscaped()
    {
        Assert.Equal("\"a,b\"", ScanReport.Cell("a,b"));
        Assert.Equal("\"say \"\"hi\"\"\"", ScanReport.Cell("say \"hi\""));
        Assert.Equal("\"two\r\nlines\"", ScanReport.Cell("two\r\nlines"));
    }

    [Fact]
    public void PlainValuesAreNotQuoted() => Assert.Equal("shortcut", ScanReport.Cell("shortcut"));

    [Fact]
    public void HeaderIsEnglishAndFixed() =>
        Assert.Equal("Category,Name,Status,Confidence,Target,Location,Evidence",
            ScanReport.Csv([]).TrimEnd('\r', '\n'));

    [Fact]
    public void RowCarriesTargetAndLocationForEachPayloadKind()
    {
        var location = new RegistryLocation(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Ghost");
        var entry = new UninstallEntry("id", "Ghost App", null, null, @"C:\Gone\unins.exe", null, null, false, false, location);

        var uninstall = ScanReport.Row(Categories.Uninstall, Finding(entry, "Ghost App"));
        Assert.Equal(@"C:\Gone\unins.exe", uninstall.Target);
        Assert.Equal(location.DisplayPath, uninstall.Location);

        var shortcut = ScanReport.Row(Categories.Shortcut, Finding(new ShortcutIssue(@"C:\Menu\a.lnk", @"C:\Gone\a.exe"), "a"));
        Assert.Equal(@"C:\Gone\a.exe", shortcut.Target);
        Assert.Equal(@"C:\Menu\a.lnk", shortcut.Location);

        var task = ScanReport.Row(Categories.Task, Finding(new ScheduledTaskIssue(@"\Ghost", @"C:\Tasks\Ghost", @"C:\Gone\t.exe"), "Ghost"));
        Assert.Equal(@"C:\Gone\t.exe", task.Target);
        Assert.Equal(@"C:\Tasks\Ghost", task.Location);
    }

    [Fact]
    public void EvidenceIsFlattenedIntoOneCell()
    {
        var row = ScanReport.Row(Categories.Shortcut, Finding(new ShortcutIssue("a", "b"), "a"));

        Assert.Equal("shortcut_target_missing=60; target_directory_missing=35", row.Evidence);
    }

    [Fact]
    public void CsvFileStartsWithAUtf8ByteOrderMarkSoExcelReadsIt()
    {
        var path = Path.Combine(TempDirectory(), "report.csv");

        ScanReport.Write(path, [ScanReport.Row(Categories.Shortcut, Finding(new ShortcutIssue("a", "b"), "Kısayol"))]);

        var bytes = File.ReadAllBytes(path);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Contains("Kısayol", File.ReadAllText(path, Encoding.UTF8));
    }

    [Fact]
    public void JsonExportRoundTripsEveryColumn()
    {
        var path = Path.Combine(TempDirectory(), "report.json");
        var row = ScanReport.Row(Categories.Shortcut, Finding(new ShortcutIssue(@"C:\Menu\a.lnk", @"C:\Gone\a.exe"), "a"));

        ScanReport.Write(path, [row]);

        var parsed = JsonSerializer.Deserialize<List<ReportRow>>(File.ReadAllText(path))!;
        Assert.Equal(row, Assert.Single(parsed));
    }

    [Fact]
    public void FormatIsChosenFromTheExtension()
    {
        Assert.Equal(ReportFormats.Json, ReportFormats.For(@"C:\x\report.JSON"));
        Assert.Equal(ReportFormats.Csv, ReportFormats.For(@"C:\x\report.csv"));
        Assert.Equal(ReportFormats.Csv, ReportFormats.For(@"C:\x\report.txt"));
    }

    private static Finding Finding(object payload, string title) =>
        new("id", title, "subtitle", EntryStatus.Broken, 95,
            [
                new Evidence(EvidenceKinds.ShortcutTargetMissing, "x", EvidenceWeights.ShortcutTargetMissing),
                new Evidence(EvidenceKinds.TargetDirectoryMissing, "y", EvidenceWeights.TargetDirectoryMissing)
            ],
            Categories.Shortcut, payload);

    internal static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ghostlist-report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

public class DiagnosticsBundleTests
{
    [Fact]
    public void BundleContainsSummaryErrorsAndReadme()
    {
        var path = Path.Combine(ScanReportTests.TempDirectory(), "bundle.zip");

        DiagnosticsBundle.Write(path, Input(), historyPath: Path.Combine(Path.GetTempPath(), "no-history-here.jsonl"));

        using var archive = ZipFile.OpenRead(path);
        Assert.Contains(archive.Entries, x => x.Name == DiagnosticsBundle.SummaryEntry);
        Assert.Contains(archive.Entries, x => x.Name == DiagnosticsBundle.ErrorsEntry);
        Assert.Contains(archive.Entries, x => x.Name == DiagnosticsBundle.ReadmeEntry);
        Assert.DoesNotContain(archive.Entries, x => x.Name == DiagnosticsBundle.HistoryEntry);
    }

    [Fact]
    public void OnlyTheLastHundredHistoryLinesAreIncluded()
    {
        var directory = ScanReportTests.TempDirectory();
        var historyPath = Path.Combine(directory, "history.jsonl");
        File.WriteAllLines(historyPath, Enumerable.Range(1, 250).Select(i => $"{{\"line\":{i}}}"));
        var path = Path.Combine(directory, "bundle.zip");

        DiagnosticsBundle.Write(path, Input(), historyPath);

        using var archive = ZipFile.OpenRead(path);
        var entry = Assert.Single(archive.Entries, x => x.Name == DiagnosticsBundle.HistoryEntry);
        using var reader = new StreamReader(entry.Open());
        var lines = reader.ReadToEnd().Split(System.Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(DiagnosticsBundle.HistoryLineLimit, lines.Length);
        Assert.Contains("\"line\":151", lines[0]);
        Assert.Contains("\"line\":250", lines[^1]);
    }

    [Fact]
    public void HistoryLinesAreMaskedBeforeTheyEnterTheBundle()
    {
        var directory = ScanReportTests.TempDirectory();
        var historyPath = Path.Combine(directory, "history.jsonl");
        File.WriteAllLines(historyPath, [$@"{{""target"":""C:\\Users\\{System.Environment.UserName}\\app.lnk""}}"]);

        var history = DiagnosticsBundle.History(historyPath)!;

        Assert.DoesNotContain(System.Environment.UserName, history, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(PathMasker.UserPlaceholder, history);
    }

    [Fact]
    public void CapturedErrorsAreMasked()
    {
        var trace = $@"System.IO.IOException at C:\Users\{System.Environment.UserName}\app\x.cs:line 3 on {System.Environment.MachineName}";

        var text = DiagnosticsBundle.Errors(Input(errors: [trace]));

        Assert.DoesNotContain(System.Environment.UserName, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(System.Environment.MachineName, text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("line 3", text);
    }

    [Fact]
    public void SummaryNeverCarriesTheMachineNameOrUserName()
    {
        var summary = DiagnosticsBundle.Summary(Input(
            failures: [new ScanFailure("shortcut", "shortcut", $@"denied at C:\Users\{System.Environment.UserName}\x on {System.Environment.MachineName}")]));

        Assert.DoesNotContain(System.Environment.UserName, summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(System.Environment.MachineName, summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SummaryCarriesTimingAndPerCategoryCounts()
    {
        var summary = DiagnosticsBundle.Summary(Input());

        using var document = JsonDocument.Parse(summary);
        var root = document.RootElement;
        Assert.Equal("Ghostlist", root.GetProperty("product").GetString());
        Assert.Equal(1234, root.GetProperty("scanMilliseconds").GetInt64());
        Assert.Equal(2, root.GetProperty("findingsPerCategory").GetProperty(Categories.Shortcut).GetInt32());
        Assert.True(root.GetProperty("scanConcurrency").GetInt32() <= ScanOptions.ConcurrencyCeiling);
    }

    [Fact]
    public void MissingHistoryFileIsNotAnError() =>
        Assert.Null(DiagnosticsBundle.History(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));

    private static DiagnosticsInput Input(
        IReadOnlyList<ScanFailure>? failures = null,
        IReadOnlyList<string>? errors = null) =>
        new([], new Dictionary<string, int> { [Categories.Shortcut] = 2 },
            failures ?? [], TimeSpan.FromMilliseconds(1234), errors ?? []);
}
