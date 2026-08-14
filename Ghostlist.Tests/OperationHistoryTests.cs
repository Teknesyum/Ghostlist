using Ghostlist.Core;

namespace Ghostlist.Tests;

public sealed class OperationHistoryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ghostlist-history-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(root, "Ghostlist", "history.jsonl");

    public void Dispose()
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        catch { }
    }

    [Fact]
    public void MissingFileYieldsEmptyHistory() => Assert.Empty(new OperationHistory(FilePath).Read());

    [Fact]
    public void AppendCreatesTheFileAndItsFolder()
    {
        new OperationHistory(FilePath).Append(Record(OperationKinds.Fix, "Hayalet"));
        Assert.True(File.Exists(FilePath));
    }

    [Fact]
    public void EveryAppendAddsExactlyOneLine()
    {
        var history = new OperationHistory(FilePath);
        history.Append(Record(OperationKinds.Fix, "Bir"));
        history.Append(Record(OperationKinds.Fix, "Iki"));
        history.Append(Record(OperationKinds.Restore, "Uc"));

        Assert.Equal(3, File.ReadAllLines(FilePath).Count(x => !string.IsNullOrWhiteSpace(x)));
        Assert.Equal(3, history.Read().Count);
    }

    [Fact]
    public void AppendNeverRewritesEarlierLines()
    {
        var history = new OperationHistory(FilePath);
        history.Append(Record(OperationKinds.Fix, "Ilk"));
        var first = File.ReadAllText(FilePath);

        history.Append(Record(OperationKinds.Restore, "Ikinci"));
        Assert.StartsWith(first, File.ReadAllText(FilePath));
    }

    [Fact]
    public void ReadReturnsNewestFirstAndKeepsEveryField()
    {
        var history = new OperationHistory(FilePath);
        history.Append(new OperationRecord(
            DateTimeOffset.Now.AddMinutes(-5), OperationKinds.Fix, Categories.Shortcut, "C:\\yol\\hayalet.lnk", FixResultKeys.Fixed, "C:\\yedek\\a.json"));
        history.Append(new OperationRecord(
            DateTimeOffset.Now, OperationKinds.Restore, Categories.Uninstall, "HKLM\\Software\\Ghost", FixResultKeys.Fixed, "C:\\yedek\\b.json"));

        var records = history.Read();
        Assert.Equal(OperationKinds.Restore, records[0].Operation);
        Assert.Equal(Categories.Uninstall, records[0].Category);
        Assert.Equal("HKLM\\Software\\Ghost", records[0].Target);
        Assert.Equal("C:\\yedek\\b.json", records[0].BackupPath);
        Assert.Equal(OperationKinds.Fix, records[1].Operation);
        Assert.Equal("C:\\yol\\hayalet.lnk", records[1].Target);
    }

    [Fact]
    public void CorruptLinesAreSkippedWithoutLosingTheRest()
    {
        var history = new OperationHistory(FilePath);
        history.Append(Record(OperationKinds.Fix, "Saglam"));
        File.AppendAllText(FilePath, "{ bu satir bozuk" + Environment.NewLine);
        File.AppendAllText(FilePath, Environment.NewLine);
        history.Append(Record(OperationKinds.Restore, "Digeri"));

        var records = history.Read();
        Assert.Equal(2, records.Count);
        Assert.Contains(records, x => x.Target == "Saglam");
        Assert.Contains(records, x => x.Target == "Digeri");
    }

    [Fact]
    public void HistoryHasNoDeleteOperation()
    {
        var methods = typeof(OperationHistory).GetMethods()
            .Select(x => x.Name)
            .Where(x => x.Contains("Delete", StringComparison.OrdinalIgnoreCase)
                || x.Contains("Clear", StringComparison.OrdinalIgnoreCase)
                || x.Contains("Truncate", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(methods);
    }

    [Fact]
    public void DefaultPathLivesNextToTheBackupFolderNotInsideIt()
    {
        Assert.EndsWith(Path.Combine("Ghostlist", "history.jsonl"), OperationHistory.DefaultPath);
        Assert.DoesNotContain("Backups", OperationHistory.DefaultPath);
    }

    private static OperationRecord Record(string operation, string target) =>
        new(DateTimeOffset.Now, operation, Categories.Uninstall, target, FixResultKeys.Fixed, null);
}
