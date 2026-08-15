using Microsoft.Win32;
using Ghostlist.Core;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Data;

namespace Ghostlist.App;

public sealed class MainViewModel : ObservableObject
{
    private static readonly string[] CategoryOrder =
    [
        Categories.Uninstall, Categories.Shortcut, Categories.Startup,
        Categories.Task, Categories.Folder, Categories.Msix
    ];

    private readonly CleanupService service;
    private readonly AppSettings settings;
    private readonly List<FindingViewModel> all = [];
    private readonly Dictionary<string, CategoryGroupViewModel> groups = [];

    private readonly List<string> errors = [];

    private CancellationTokenSource? scan;
    private TimeSpan scanDuration;
    private IReadOnlyList<ScanFailure> scanFailures = [];
    private bool isBusy;
    private bool brokenOnly;
    private bool showBackups;
    private string statusMessage;
    private FindingViewModel? selected;
    private DialogRequest? dialog;

    public MainViewModel(CleanupService service, AppSettings settings)
        : this(service, settings, new BackupCatalog(BackupPaths.BackupDirectory), new OperationHistory()) { }

    public MainViewModel(CleanupService service, AppSettings settings, BackupCatalog catalog, OperationHistory history)
    {
        this.service = service;
        this.settings = settings;
        statusMessage = Strings.Current.Get("status.ready");

        Backups = new BackupsViewModel(service, catalog, history, ShowDialogAsync, ScanAsync);
        ShowFindingsCommand = new RelayCommand(() => ShowBackups = false);
        ShowBackupsCommand = new RelayCommand(() => ShowBackups = true);

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionNumber = $"{version?.Major ?? 1}.{version?.Minor ?? 0}.{version?.Build ?? 0}";

        Export = new ExportCommands(service, () => all.Select(x => x.Model).ToList(), Diagnostics, ShowDialogAsync,
            message => StatusMessage = message);

        Updates = new UpdateBannerViewModel(
            new UpdateChecker(new GitHubReleaseSource(), settings, SemanticVersion.TryParse(VersionNumber) ?? new SemanticVersion(0, 0, 0)),
            settings);

        ScanCommand = new RelayCommand(async () => await ScanAsync(), () => !IsBusy);
        StopScanCommand = new RelayCommand(StopScan, () => Progress.IsRunning);
        ExportReportCommand = new RelayCommand(async () => await Guarded(Export.ExportReportAsync), () => !IsBusy);
        ExportDiagnosticsCommand = new RelayCommand(async () => await Guarded(Export.ExportDiagnosticsAsync), () => !IsBusy);
        SelectAllCommand = new RelayCommand(SelectAll, () => !IsBusy);
        ClearSelectionCommand = new RelayCommand(ClearSelection, () => !IsBusy);
        FixSelectedCommand = new RelayCommand(async () => await FixSelectedAsync(), () => !IsBusy);
        FixAllCommand = new RelayCommand(async () => await FixAllAsync(), () => !IsBusy);
        RestoreCommand = new RelayCommand(async () => await RestoreAsync(), () => !IsBusy);
        InfoCommand = new RelayCommand(async () => await ShowInfoAsync(), () => !IsBusy);
        ElevateCommand = new RelayCommand(Elevate, () => !Elevation.IsElevated);
        LanguageCommand = new RelayCommand(parameter => UseLanguage(parameter as string ?? Strings.Turkish));

        foreach (var category in CategoryOrder) groups[category] = new CategoryGroupViewModel(category);

        FindingsView = CollectionViewSource.GetDefaultView(Findings);
        FindingsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FindingViewModel.Group)));

        Strings.Current.LanguageChanged += (_, _) => RefreshLanguage();
    }

    public ObservableCollection<FindingViewModel> Findings { get; } = [];

    public ICollectionView FindingsView { get; }

    public RelayCommand ScanCommand { get; }
    public RelayCommand StopScanCommand { get; }
    public RelayCommand ExportReportCommand { get; }
    public RelayCommand ExportDiagnosticsCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }
    public RelayCommand FixSelectedCommand { get; }
    public RelayCommand FixAllCommand { get; }
    public RelayCommand RestoreCommand { get; }
    public RelayCommand InfoCommand { get; }
    public RelayCommand ElevateCommand { get; }
    public RelayCommand LanguageCommand { get; }
    public RelayCommand ShowFindingsCommand { get; }
    public RelayCommand ShowBackupsCommand { get; }

    public BackupsViewModel Backups { get; }

    public ScanProgressViewModel Progress { get; } = new();

    public ExportCommands Export { get; }

    public UpdateBannerViewModel Updates { get; }

    public bool ShowBackups
    {
        get => showBackups;
        set
        {
            if (!Set(ref showBackups, value)) return;
            Raise(nameof(ShowFindings));
            if (value) Backups.Reload();
        }
    }

    public bool ShowFindings => !showBackups;

    public string VersionNumber { get; }

    public string VersionText => Strings.Current.Format("footer.version", ("version", VersionNumber));

    public bool IsElevated => Elevation.IsElevated;

    public bool IsLimited => !Elevation.IsElevated;

    public string AdminText => Strings.Current.Get(Elevation.IsElevated ? "admin.elevated" : "admin.limited");

    public string Language => Strings.Current.Language;

    public bool IsTurkish => Strings.Current.Language == Strings.Turkish;

    public bool IsEnglish => Strings.Current.Language == Strings.English;

    public DialogRequest? Dialog
    {
        get => dialog;
        private set { Set(ref dialog, value); Raise(nameof(HasDialog)); }
    }

    public bool HasDialog => dialog is not null;

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (!Set(ref isBusy, value)) return;
            Raise(nameof(IsIdle));
            foreach (var command in new[] { ScanCommand, SelectAllCommand, ClearSelectionCommand, FixSelectedCommand, FixAllCommand, RestoreCommand, InfoCommand, ExportReportCommand, ExportDiagnosticsCommand })
                command.RaiseCanExecuteChanged();
        }
    }

    public bool IsIdle => !isBusy;

    public bool BrokenOnly
    {
        get => brokenOnly;
        set { if (Set(ref brokenOnly, value)) Regroup(); }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => Set(ref statusMessage, value);
    }

    public FindingViewModel? Selected
    {
        get => selected;
        set { Set(ref selected, value); Raise(nameof(HasSelection)); }
    }

    public bool HasSelection => selected is not null;

    public async Task StartAsync()
    {
        UseLanguage(settings.Language);
        await ScanAsync();
        await Updates.CheckAsync(manual: false);
    }

    private void UseLanguage(string language)
    {
        Strings.Current.Use(language);
        if (settings.Language == Strings.Current.Language) return;
        settings.Language = Strings.Current.Language;
        settings.Save();
    }

    private void RefreshLanguage()
    {
        StatusMessage = Strings.Current.Get("status.ready");
        foreach (var group in groups.Values) group.RefreshLanguage();
        Progress.RefreshLanguage();
        Updates.RefreshLanguage();
        foreach (var item in all) item.RefreshLanguage();
        Raise(nameof(VersionText));
        Raise(nameof(AdminText));
        Raise(nameof(Language));
        Raise(nameof(IsTurkish));
        Raise(nameof(IsEnglish));
    }

    private void StopScan() => scan?.Cancel();

    private async Task ScanAsync()
    {
        IsBusy = true;
        scan = new CancellationTokenSource();
        Progress.Begin(service.Providers.Select(x => x.Category));
        StopScanCommand.RaiseCanExecuteChanged();
        var reporter = new Progress<ScanProgress>(report =>
        {
            Progress.Report(report);
            StatusMessage = report.StateKey == ScanStates.Failed
                ? Strings.Current.Format("status.scanCategoryFailed",
                    ("category", Strings.Current.Get($"category.{report.Category}")))
                : Strings.Current.Format("status.scanning",
                    ("category", Strings.Current.Get($"category.{report.Category}")));
        });

        try
        {
            all.Clear();
            Selected = null;
            Regroup();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var outcome = await service.ScanAsync(reporter, null, scan.Token);
            clock.Stop();
            scanDuration = clock.Elapsed;
            scanFailures = outcome.Failures;
            foreach (var item in outcome.Findings)
                all.Add(new FindingViewModel(item, groups[service.CategoryOf(item)]));
            Regroup();
            StatusMessage = outcome.HasFailures
                ? Strings.Current.Format("status.scanDoneWithErrors",
                    ("total", all.Count),
                    ("broken", all.Count(x => x.Model.Status == EntryStatus.Broken)),
                    ("failed", outcome.Failures.Count))
                : Strings.Current.Format("status.scanDone",
                    ("total", all.Count),
                    ("broken", all.Count(x => x.Model.Status == EntryStatus.Broken)));
        }
        catch (OperationCanceledException)
        {
            all.Clear();
            Selected = null;
            Regroup();
            StatusMessage = Strings.Current.Get("status.scanCancelled");
        }
        catch (Exception ex) { await ShowErrorAsync(ex); }
        finally
        {
            Progress.End();
            scan.Dispose();
            scan = null;
            StopScanCommand.RaiseCanExecuteChanged();
            IsBusy = false;
        }
    }

    private void Regroup()
    {
        var visible = (brokenOnly ? all.Where(x => x.Model.Status == EntryStatus.Broken) : all)
            .OrderBy(x => Array.IndexOf(CategoryOrder, x.Category))
            .ThenByDescending(x => x.Confidence)
            .ThenBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        Findings.Clear();
        foreach (var item in visible) Findings.Add(item);
        foreach (var category in CategoryOrder)
            groups[category].Reset(visible.Where(x => x.Category == category));
    }

    private void SelectAll()
    {
        var count = 0;
        foreach (var item in Findings.Where(x => !x.IsLocked))
        {
            item.IsSelected = true;
            count++;
        }
        StatusMessage = Strings.Current.Format("status.selected", ("count", count));
    }

    private void ClearSelection()
    {
        foreach (var item in all) item.IsSelected = false;
        StatusMessage = Strings.Current.Get("status.selectionCleared");
    }

    private async Task ShowInfoAsync() =>
        await ShowDialogAsync(Strings.Current.Get("dialog.info.title"), Strings.Current.Get("dialog.info.body"), null, false);

    private async Task FixSelectedAsync()
    {
        var picked = all.Where(x => x.IsSelected).ToList();
        if (picked.Count == 0)
        {
            await ShowDialogAsync(Strings.Current.Get("dialog.noSelection.title"), Strings.Current.Get("dialog.noSelection.body"), null, false);
            return;
        }
        var eligible = picked.Where(x => x.Model.Status == EntryStatus.Broken && !x.IsLocked).ToList();
        if (eligible.Count == 0)
        {
            await ShowDialogAsync(Strings.Current.Get("dialog.nothingEligible.title"), Strings.Current.Get("dialog.nothingEligible.body"), null, false);
            return;
        }
        await FixAsync(eligible, picked.Count - eligible.Count);
    }

    private async Task FixAllAsync()
    {
        var automatic = all.Where(x => !x.IsLocked && service.IsAutoFixable(x.Model)).ToList();
        if (automatic.Count == 0)
        {
            await ShowDialogAsync(
                Strings.Current.Get("dialog.noAuto.title"),
                Strings.Current.Format("dialog.noAuto.body",
                    ("threshold", ConfidenceRules.AutoFixThreshold),
                    ("evidence", ConfidenceRules.MinimumIndependentEvidence)),
                null, false);
            return;
        }
        await FixAsync(automatic, 0);
    }

    private async Task FixAsync(IReadOnlyList<FindingViewModel> targets, int skipped)
    {
        var lines = targets
            .GroupBy(x => x.Category)
            .OrderBy(x => Array.IndexOf(CategoryOrder, x.Key))
            .Select(x => Strings.Current.Format("dialog.confirmFix.line",
                ("category", Strings.Current.Get($"category.{x.Key}")), ("count", x.Count())))
            .ToList();
        if (skipped > 0) lines.Add(Strings.Current.Format("dialog.confirmFix.skipped", ("count", skipped)));

        var accepted = await ShowDialogAsync(
            Strings.Current.Get("dialog.confirmFix.title"),
            Strings.Current.Format("dialog.confirmFix.body", ("count", targets.Count)),
            lines, true);
        if (!accepted) return;

        IsBusy = true;
        await Task.Run(() => SystemRestore.TryCreate("Ghostlist bulk fix"));
        var completed = 0;
        var manual = new List<string>();
        var failures = new List<string>();
        try
        {
            foreach (var item in targets)
            {
                StatusMessage = Strings.Current.Format("status.fixing", ("done", completed), ("total", targets.Count));
                try
                {
                    var result = await Task.Run(() => service.Fix(item.Model));
                    Backups.RecordFix(item.Category, item.Title, result.ResultKey, result.BackupPath);
                    if (result.Success) completed++;
                    else if (result.ManualCommand is not null) manual.Add($"{item.Title}: {result.ManualCommand}");
                    else failures.Add($"{item.Title}: {Strings.Current.Get($"result.{result.ResultKey}")}");
                }
                catch (Exception ex)
                {
                    Backups.RecordFix(item.Category, item.Title, FixResultKeys.Failed, null);
                    failures.Add($"{item.Title}: {ex.Message}");
                }
            }
        }
        finally { IsBusy = false; }

        await ScanAsync();

        var report = new List<string>();
        if (manual.Count > 0)
        {
            report.Add(Strings.Current.Get("dialog.result.manual"));
            report.AddRange(manual.Take(6));
        }
        if (failures.Count > 0)
        {
            report.Add(Strings.Current.Get("dialog.result.failed"));
            report.AddRange(failures.Take(6));
        }
        await ShowDialogAsync(
            Strings.Current.Get(failures.Count == 0 ? "dialog.result.title" : "dialog.result.partialTitle"),
            Strings.Current.Format("dialog.result.body", ("count", completed)),
            report, false);
    }

    private async Task RestoreAsync()
    {
        var backupDirectory = BackupPaths.BackupDirectory;
        System.IO.Directory.CreateDirectory(backupDirectory);
        var picker = new OpenFileDialog
        {
            Title = Strings.Current.Get("dialog.restore.title"),
            Filter = $"{Strings.Current.Get("dialog.restore.filter")} (*.json)|*.json",
            InitialDirectory = backupDirectory
        };
        if (picker.ShowDialog() != true) return;
        IsBusy = true;
        try
        {
            await Task.Run(() => service.Restore(picker.FileName));
            Backups.RecordRestore(picker.FileName);
            StatusMessage = Strings.Current.Get("status.restored");
        }
        catch (Exception ex)
        {
            IsBusy = false;
            await ShowErrorAsync(ex);
            return;
        }
        finally { IsBusy = false; }
        await ScanAsync();
    }

    private void Elevate()
    {
        if (Elevation.Restart()) return;
        _ = ShowDialogAsync(Strings.Current.Get("dialog.error.title"), Strings.Current.Get("admin.restartFailed"), null, false);
    }

    private Task ShowErrorAsync(Exception ex)
    {
        errors.Add(ex.ToString());
        if (errors.Count > DiagnosticsBundle.HistoryLineLimit) errors.RemoveAt(0);
        return ShowDialogAsync(Strings.Current.Get("dialog.error.title"), ex.Message, null, false);
    }

    private DiagnosticsInput Diagnostics() =>
        new(all.Select(x => x.Model).ToList(),
            all.GroupBy(x => x.Category).ToDictionary(x => x.Key, x => x.Count()),
            scanFailures,
            scanDuration,
            errors.ToList());

    private async Task Guarded(Func<Task> action)
    {
        IsBusy = true;
        try { await action(); }
        catch (Exception ex)
        {
            IsBusy = false;
            await ShowErrorAsync(ex);
            return;
        }
        finally { IsBusy = false; }
    }

    private async Task<bool> ShowDialogAsync(string title, string body, IReadOnlyList<string>? lines, bool askConfirmation)
    {
        var request = new DialogRequest(title, body, lines, askConfirmation);
        Dialog = request;
        var answer = await request.Completion;
        Dialog = null;
        return answer;
    }
}
