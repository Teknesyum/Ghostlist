namespace Ghostlist.Core;

public static class UpdateStates
{
    public const string Disabled = "disabled";
    public const string TooSoon = "too_soon";
    public const string UpToDate = "up_to_date";
    public const string Available = "available";
    public const string Unreachable = "unreachable";
}

public sealed record ReleaseInfo(string Tag, string? Url);

public sealed record UpdateStatus(string StateKey, SemanticVersion? Latest = null, string? Url = null)
{
    public bool HasUpdate => StateKey == UpdateStates.Available;
}

public interface IReleaseSource
{
    Task<ReleaseInfo?> LatestAsync(CancellationToken token);
}

public interface IUpdateSettings
{
    bool AutomaticUpdateCheck { get; set; }
    DateTimeOffset? LastUpdateCheck { get; set; }
    void Save();
}

public sealed class UpdateChecker(
    IReleaseSource source,
    IUpdateSettings settings,
    SemanticVersion current,
    TimeProvider? time = null)
{
    public static readonly TimeSpan AutomaticInterval = TimeSpan.FromDays(1);

    private readonly TimeProvider time = time ?? TimeProvider.System;

    public async Task<UpdateStatus> CheckAsync(bool manual, CancellationToken token = default)
    {
        if (!manual)
        {
            if (!settings.AutomaticUpdateCheck) return new UpdateStatus(UpdateStates.Disabled);
            if (settings.LastUpdateCheck is { } last && time.GetUtcNow() - last < AutomaticInterval)
                return new UpdateStatus(UpdateStates.TooSoon);
        }

        ReleaseInfo? release;
        try
        {
            release = await source.LatestAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new UpdateStatus(UpdateStates.Unreachable);
        }

        settings.LastUpdateCheck = time.GetUtcNow();
        settings.Save();

        var latest = SemanticVersion.TryParse(release?.Tag);
        if (latest is null) return new UpdateStatus(UpdateStates.Unreachable);
        return latest > current
            ? new UpdateStatus(UpdateStates.Available, latest, release?.Url)
            : new UpdateStatus(UpdateStates.UpToDate, latest, release?.Url);
    }
}
