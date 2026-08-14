namespace Ghostlist.App;

public sealed class CategoryGroupViewModel : ObservableObject
{
    private readonly List<FindingViewModel> items = [];

    public CategoryGroupViewModel(string category)
    {
        Category = category;
        SelectCommand = new RelayCommand(() => SetSelection(true));
        ClearCommand = new RelayCommand(() => SetSelection(false));
    }

    public string Category { get; }

    public string Header => Strings.Current.Get($"category.{Category}");

    public int Count => items.Count;

    public RelayCommand SelectCommand { get; }

    public RelayCommand ClearCommand { get; }

    public void Reset(IEnumerable<FindingViewModel> visible)
    {
        items.Clear();
        items.AddRange(visible);
        Raise(nameof(Count));
    }

    public void RefreshLanguage() => Raise(nameof(Header));

    private void SetSelection(bool selected)
    {
        foreach (var item in items) item.IsSelected = selected;
    }
}
