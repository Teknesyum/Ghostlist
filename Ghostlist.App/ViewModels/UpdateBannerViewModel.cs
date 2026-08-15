using System.Diagnostics;
using Ghostlist.Core;

namespace Ghostlist.App;

public sealed class UpdateBannerViewModel : ObservableObject
{
    public const string ReleasesUrl = "https://github.com/Teknesyum/Ghostlist/releases/latest";

    private readonly UpdateChecker checker;
    private readonly IUpdateSettings settings;
    private readonly Action<string>? open;

    private UpdateStatus status = new(UpdateStates.Disabled);
    private bool isChecking;
    private string? manualMessage;

    public UpdateBannerViewModel(UpdateChecker checker, IUpdateSettings settings, Action<string>? open = null)
    {
        this.checker = checker;
        this.settings = settings;
        this.open = open ?? Launch;

        CheckCommand = new RelayCommand(async () => await CheckAsync(manual: true), () => !isChecking);
        OpenReleaseCommand = new RelayCommand(() => this.open(status.Url ?? ReleasesUrl));
        DismissCommand = new RelayCommand(Dismiss);
    }

    public RelayCommand CheckCommand { get; }
    public RelayCommand OpenReleaseCommand { get; }
    public RelayCommand DismissCommand { get; }

    public UpdateStatus Status => status;

    public bool AutomaticCheck
    {
        get => settings.AutomaticUpdateCheck;
        set
        {
            if (settings.AutomaticUpdateCheck == value) return;
            settings.AutomaticUpdateCheck = value;
            settings.Save();
            Raise(nameof(AutomaticCheck));
        }
    }

    public bool IsVisible => status.HasUpdate;

    public string BannerText => status.Latest is null
        ? string.Empty
        : Strings.Current.Format("update.available", ("version", status.Latest.ToString()));

    public string? ManualMessage
    {
        get => manualMessage;
        private set { if (Set(ref manualMessage, value)) Raise(nameof(HasManualMessage)); }
    }

    public bool HasManualMessage => !string.IsNullOrEmpty(manualMessage);

    public async Task CheckAsync(bool manual, CancellationToken token = default)
    {
        if (isChecking) return;
        isChecking = true;
        CheckCommand.RaiseCanExecuteChanged();
        try
        {
            status = await checker.CheckAsync(manual, token);
            ManualMessage = manual ? Strings.Current.Get($"update.state.{status.StateKey}") : null;
        }
        finally
        {
            isChecking = false;
            CheckCommand.RaiseCanExecuteChanged();
            Raise(nameof(Status));
            Raise(nameof(IsVisible));
            Raise(nameof(BannerText));
        }
    }

    public void RefreshLanguage()
    {
        Raise(nameof(BannerText));
        ManualMessage = null;
    }

    private void Dismiss()
    {
        status = new UpdateStatus(UpdateStates.UpToDate, status.Latest, status.Url);
        ManualMessage = null;
        Raise(nameof(IsVisible));
    }

    private static void Launch(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); }
        catch (Exception) { }
    }
}
