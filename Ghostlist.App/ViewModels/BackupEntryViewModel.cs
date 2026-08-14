using Ghostlist.Core;

namespace Ghostlist.App;

public sealed class BackupEntryViewModel(BackupCatalogEntry entry, string? categoryKey, bool wasRestored) : ObservableObject
{
    public BackupCatalogEntry Model => entry;

    public string Path => entry.Path;

    public string DisplayName => entry.DisplayName ?? System.IO.Path.GetFileName(entry.Path);

    public string Target => entry.Target ?? Strings.Current.Get("backup.target.unknown");

    public string KindText => Strings.Current.Get($"backup.kind.{entry.KindKey}");

    public string CategoryText => categoryKey is null
        ? Strings.Current.Get("backup.category.unknown")
        : Strings.Current.Get($"category.{categoryKey}");

    public string DateText => entry.CreatedAt is null
        ? Strings.Current.Get("backup.date.unknown")
        : entry.CreatedAt.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

    public string SizeText => FormatSize(entry.SizeBytes);

    public bool CanRestore => entry.CanRestore;

    public bool IsReadable => entry.IsReadable;

    public bool WasRestored => wasRestored;

    public string StateText => Strings.Current.Get(
        !entry.IsReadable ? "backup.state.unreadable"
        : wasRestored ? "backup.state.restored"
        : entry.CanRestore ? "backup.state.restorable"
        : "backup.state.payloadMissing");

    public bool IsPurgeCandidate =>
        wasRestored || entry.IsOlderThan(TimeSpan.FromDays(BackupCatalog.StaleDays), DateTimeOffset.Now);

    public string SearchText => $"{DisplayName} {Target} {DateText}";

    public void RefreshLanguage()
    {
        Raise(nameof(Target));
        Raise(nameof(KindText));
        Raise(nameof(CategoryText));
        Raise(nameof(DateText));
        Raise(nameof(StateText));
    }

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return Strings.Current.Format("backup.size.bytes", ("count", bytes));
        if (bytes < 1024 * 1024) return Strings.Current.Format("backup.size.kilobytes", ("count", (bytes / 1024.0).ToString("0.#")));
        return Strings.Current.Format("backup.size.megabytes", ("count", (bytes / (1024.0 * 1024.0)).ToString("0.#")));
    }
}

public sealed class HistoryEntryViewModel(OperationRecord record) : ObservableObject
{
    public OperationRecord Model => record;

    public string DateText => record.At.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

    public string OperationText => Strings.Current.Get($"history.operation.{record.Operation}");

    public string CategoryText => Strings.Current.Get($"category.{record.Category}");

    public string Target => record.Target;

    public string ResultText => Strings.Current.Get($"result.{record.ResultKey}");

    public string? BackupPath => record.BackupPath;

    public bool HasBackup => !string.IsNullOrEmpty(record.BackupPath);

    public void RefreshLanguage()
    {
        Raise(nameof(OperationText));
        Raise(nameof(CategoryText));
        Raise(nameof(ResultText));
    }
}
