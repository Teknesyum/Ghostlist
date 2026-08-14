using Microsoft.Win32;
using Ghostlist.Core;

namespace Ghostlist.App;

public sealed class FindingViewModel(Finding finding, CategoryGroupViewModel group) : ObservableObject
{
    private bool isSelected;

    public Finding Model => finding;

    public CategoryGroupViewModel Group => group;

    public string Category => group.Category;

    public string Title => finding.Title;

    public string Subtitle => finding.Subtitle ?? string.Empty;

    public int Confidence => finding.Confidence;

    public string StatusText => Strings.Current.Get($"status.{StatusKey(finding.Status)}");

    public string CategoryText => group.Header;

    public string ConfidenceText => Strings.Current.Format("detail.confidence", ("confidence", finding.Confidence));

    public IReadOnlyList<string> EvidenceLines => finding.Evidence.Count == 0
        ? [Strings.Current.Get("evidence.none")]
        : [.. finding.Evidence.Select(Describe)];

    public bool RequiresElevation => finding.Payload switch
    {
        UninstallEntry entry => entry.Location.Hive == RegistryHive.LocalMachine,
        StartupValueIssue issue => issue.Location.Hive == RegistryHive.LocalMachine,
        _ => false
    };

    public bool IsLocked => RequiresElevation && !Elevation.IsElevated;

    public string LockedText => Strings.Current.Get("admin.lockedRow");

    public bool IsSelected
    {
        get => isSelected;
        set { if (!IsLocked) Set(ref isSelected, value); }
    }

    public void RefreshLanguage()
    {
        Raise(nameof(StatusText));
        Raise(nameof(CategoryText));
        Raise(nameof(ConfidenceText));
        Raise(nameof(EvidenceLines));
        Raise(nameof(LockedText));
    }

    public static string StatusKey(EntryStatus status) => status switch
    {
        EntryStatus.Healthy => "healthy",
        EntryStatus.Broken => "broken",
        EntryStatus.Unsupported => "unsupported",
        _ => "suspicious"
    };

    private static string Describe(Evidence evidence)
    {
        var text = Strings.Current.Get($"evidence.{evidence.Kind}");
        var note = evidence.IsConclusive
            ? Strings.Current.Format("evidence.weight", ("weight", evidence.Weight))
            : Strings.Current.Get("evidence.uncertain");
        return string.IsNullOrWhiteSpace(evidence.Detail)
            ? $"{text}  ({note})"
            : $"{text}  —  {evidence.Detail}  ({note})";
    }
}
