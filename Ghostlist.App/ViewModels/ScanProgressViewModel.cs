using System.Collections.ObjectModel;
using Ghostlist.Core;

namespace Ghostlist.App;

public sealed class ScanCategoryViewModel(string category) : ObservableObject
{
    public const string Pending = "pending";

    private string stateKey = Pending;
    private string? error;

    public string Category { get; } = category;

    public string Header => Strings.Current.Get($"category.{Category}");

    public string StateKey
    {
        get => stateKey;
        set
        {
            if (!Set(ref stateKey, value)) return;
            Raise(nameof(StateText));
            Raise(nameof(IsRunning));
            Raise(nameof(IsDone));
            Raise(nameof(IsFailed));
        }
    }

    public string? Error
    {
        get => error;
        set { if (Set(ref error, value)) Raise(nameof(HasError)); }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(error);

    public string StateText => Strings.Current.Get($"scan.state.{stateKey}");

    public bool IsRunning => stateKey == ScanStates.Running;

    public bool IsDone => stateKey == ScanStates.Completed;

    public bool IsFailed => stateKey == ScanStates.Failed;

    public void Reset()
    {
        StateKey = Pending;
        Error = null;
    }

    public void RefreshLanguage()
    {
        Raise(nameof(Header));
        Raise(nameof(StateText));
    }
}

public sealed class ScanProgressViewModel : ObservableObject
{
    private readonly Dictionary<string, ScanCategoryViewModel> byCategory = [];

    private bool isRunning;
    private int completed;
    private int total;

    public ObservableCollection<ScanCategoryViewModel> Categories { get; } = [];

    public bool IsRunning
    {
        get => isRunning;
        private set => Set(ref isRunning, value);
    }

    public int Completed
    {
        get => completed;
        private set { if (Set(ref completed, value)) Raise(nameof(CountText)); }
    }

    public int Total
    {
        get => total;
        private set { if (Set(ref total, value)) Raise(nameof(CountText)); }
    }

    public string CountText => $"{completed} / {total}";

    public bool HasFailures => Categories.Any(x => x.IsFailed);

    public void Begin(IEnumerable<string> categories)
    {
        if (Categories.Count == 0)
            foreach (var category in categories)
            {
                var item = new ScanCategoryViewModel(category);
                byCategory[category] = item;
                Categories.Add(item);
            }
        foreach (var item in Categories) item.Reset();
        Completed = 0;
        Total = Categories.Count;
        IsRunning = true;
        Raise(nameof(HasFailures));
    }

    public void Report(ScanProgress progress)
    {
        if (byCategory.TryGetValue(progress.Category, out var item))
        {
            item.StateKey = progress.StateKey;
            item.Error = progress.Error;
        }
        Completed = progress.Completed;
        Total = progress.Total;
        Raise(nameof(HasFailures));
    }

    public void End()
    {
        IsRunning = false;
        foreach (var item in Categories)
            if (item.StateKey == ScanCategoryViewModel.Pending)
                item.Reset();
        Raise(nameof(HasFailures));
    }

    public void RefreshLanguage()
    {
        foreach (var item in Categories) item.RefreshLanguage();
    }
}
