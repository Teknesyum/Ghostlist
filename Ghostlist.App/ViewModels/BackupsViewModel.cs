using Ghostlist.Core;
using System.Collections.ObjectModel;

namespace Ghostlist.App;

public delegate Task<bool> DialogPrompt(string title, string body, IReadOnlyList<string>? lines, bool askConfirmation);

public sealed class BackupsViewModel : ObservableObject
{
    private readonly CleanupService service;
    private readonly BackupCatalog catalog;
    private readonly OperationHistory history;
    private readonly DialogPrompt prompt;
    private readonly Func<Task> afterRestore;
    private readonly List<BackupEntryViewModel> allBackups = [];

    private string search = string.Empty;
    private long totalSize;
    private BackupEntryViewModel? selected;
    private bool isBusy;

    public BackupsViewModel(
        CleanupService service,
        BackupCatalog catalog,
        OperationHistory history,
        DialogPrompt prompt,
        Func<Task> afterRestore)
    {
        this.service = service;
        this.catalog = catalog;
        this.history = history;
        this.prompt = prompt;
        this.afterRestore = afterRestore;

        RefreshCommand = new RelayCommand(Reload, () => !IsBusy);
        RestoreCommand = new RelayCommand(async () => await RestoreAsync(selected), () => !IsBusy && selected?.CanRestore == true);
        PurgeCommand = new RelayCommand(async () => await PurgeAsync(), () => !IsBusy);
        RevealCommand = new RelayCommand(parameter => Reveal(parameter as HistoryEntryViewModel));

        Strings.Current.LanguageChanged += (_, _) => RefreshLanguage();
    }

    public ObservableCollection<BackupEntryViewModel> Backups { get; } = [];

    public ObservableCollection<HistoryEntryViewModel> History { get; } = [];

    public RelayCommand RefreshCommand { get; }
    public RelayCommand RestoreCommand { get; }
    public RelayCommand PurgeCommand { get; }
    public RelayCommand RevealCommand { get; }

    public string Search
    {
        get => search;
        set { if (Set(ref search, value)) Apply(); }
    }

    public BackupEntryViewModel? Selected
    {
        get => selected;
        set
        {
            Set(ref selected, value);
            Raise(nameof(HasSelection));
            RestoreCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasSelection => selected is not null;

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (!Set(ref isBusy, value)) return;
            RefreshCommand.RaiseCanExecuteChanged();
            RestoreCommand.RaiseCanExecuteChanged();
            PurgeCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasBackups => allBackups.Count > 0;

    public bool HasHistory => History.Count > 0;

    public string TotalSizeText =>
        Strings.Current.Format("backup.totalSize", ("size", BackupEntryViewModel.FormatSize(totalSize)), ("count", allBackups.Count));

    public string PurgeSummaryText =>
        Strings.Current.Format("backup.purgeSummary",
            ("count", allBackups.Count(x => x.IsPurgeCandidate)), ("days", BackupCatalog.StaleDays));

    public void Reload()
    {
        var previous = selected?.Path;
        var records = history.Read();
        var restored = records
            .Where(x => x.Operation == OperationKinds.Restore && x.BackupPath is not null)
            .Select(x => x.BackupPath!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var categories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
            if (record.BackupPath is not null) categories[record.BackupPath] = record.Category;

        allBackups.Clear();
        foreach (var entry in catalog.List())
            allBackups.Add(new BackupEntryViewModel(
                entry, categories.GetValueOrDefault(entry.Path), restored.Contains(entry.Path)));

        totalSize = allBackups.Sum(x => x.Model.SizeBytes);

        History.Clear();
        foreach (var record in records) History.Add(new HistoryEntryViewModel(record));

        Apply();
        Selected = allBackups.FirstOrDefault(x => x.Path == previous);
        Raise(nameof(HasBackups));
        Raise(nameof(HasHistory));
        Raise(nameof(TotalSizeText));
        Raise(nameof(PurgeSummaryText));
    }

    public void RecordFix(string category, string target, string resultKey, string? backupPath) =>
        history.Append(new OperationRecord(DateTimeOffset.Now, OperationKinds.Fix, category, target, resultKey, backupPath));

    public void RecordRestore(string backupPath)
    {
        var entry = allBackups.FirstOrDefault(x => string.Equals(x.Path, backupPath, StringComparison.OrdinalIgnoreCase));
        history.Append(new OperationRecord(
            DateTimeOffset.Now, OperationKinds.Restore,
            entry is null ? Categories.Uninstall : CategoryOf(entry),
            entry?.Target ?? backupPath, FixResultKeys.Fixed, backupPath));
    }

    public async Task RestoreAsync(BackupEntryViewModel? entry)
    {
        if (entry is null || !entry.CanRestore) return;

        var accepted = await prompt(
            Strings.Current.Get("backup.confirmRestore.title"),
            Strings.Current.Format("backup.confirmRestore.body", ("name", entry.DisplayName)),
            [
                Strings.Current.Format("backup.confirmRestore.target", ("target", entry.Target)),
                Strings.Current.Format("backup.confirmRestore.date", ("date", entry.DateText))
            ],
            true);
        if (!accepted) return;

        IsBusy = true;
        try
        {
            await Task.Run(() => service.Restore(entry.Path));
            history.Append(new OperationRecord(
                DateTimeOffset.Now, OperationKinds.Restore, CategoryOf(entry), entry.Target, FixResultKeys.Fixed, entry.Path));
        }
        catch (Exception ex)
        {
            IsBusy = false;
            await prompt(Strings.Current.Get("dialog.error.title"), ex.Message, null, false);
            Reload();
            return;
        }
        finally { IsBusy = false; }

        Reload();
        await afterRestore();
    }

    private async Task PurgeAsync()
    {
        var candidates = allBackups.Where(x => x.IsPurgeCandidate).ToList();
        if (candidates.Count == 0)
        {
            await prompt(
                Strings.Current.Get("backup.purge.noneTitle"),
                Strings.Current.Format("backup.purge.noneBody", ("days", BackupCatalog.StaleDays)),
                null, false);
            return;
        }

        IsBusy = true;
        var removed = 0;
        var failures = new List<string>();
        try
        {
            foreach (var candidate in candidates)
            {
                var accepted = await prompt(
                    Strings.Current.Get("backup.purge.confirmTitle"),
                    Strings.Current.Format("backup.purge.confirmBody", ("name", candidate.DisplayName)),
                    [
                        Strings.Current.Format("backup.confirmRestore.target", ("target", candidate.Target)),
                        Strings.Current.Format("backup.confirmRestore.date", ("date", candidate.DateText)),
                        Strings.Current.Get(candidate.WasRestored ? "backup.purge.reasonRestored" : "backup.purge.reasonOld")
                    ],
                    true);
                if (!accepted) continue;

                try
                {
                    catalog.Delete(candidate.Model);
                    removed++;
                }
                catch (Exception ex) { failures.Add($"{candidate.DisplayName}: {ex.Message}"); }
            }
        }
        finally { IsBusy = false; }

        Reload();
        await prompt(
            Strings.Current.Get("backup.purge.doneTitle"),
            Strings.Current.Format("backup.purge.doneBody", ("count", removed)),
            failures.Count == 0 ? null : failures.Take(6).ToList(),
            false);
    }

    private void Reveal(HistoryEntryViewModel? record)
    {
        if (record?.BackupPath is null) return;
        var match = allBackups.FirstOrDefault(x => string.Equals(x.Path, record.BackupPath, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            _ = prompt(
                Strings.Current.Get("history.missing.title"),
                Strings.Current.Format("history.missing.body", ("path", record.BackupPath)),
                null, false);
            return;
        }
        Search = string.Empty;
        Selected = match;
    }

    private string CategoryOf(BackupEntryViewModel entry) =>
        History.FirstOrDefault(x => string.Equals(x.BackupPath, entry.Path, StringComparison.OrdinalIgnoreCase))
            ?.Model.Category ?? Categories.Uninstall;

    private void Apply()
    {
        var query = search.Trim();
        var visible = query.Length == 0
            ? allBackups
            : allBackups.Where(x => x.SearchText.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();

        Backups.Clear();
        foreach (var item in visible) Backups.Add(item);
    }

    private void RefreshLanguage()
    {
        foreach (var item in allBackups) item.RefreshLanguage();
        foreach (var item in History) item.RefreshLanguage();
        Raise(nameof(TotalSizeText));
        Raise(nameof(PurgeSummaryText));
    }
}
