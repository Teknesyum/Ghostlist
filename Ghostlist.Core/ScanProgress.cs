namespace Ghostlist.Core;

public static class ScanStates
{
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public sealed record ScanProgress(
    string ProviderId,
    string Category,
    string StateKey,
    int Completed,
    int Total,
    string? Error)
{
    public bool IsFailure => StateKey == ScanStates.Failed;
}

public sealed record ScanFailure(string ProviderId, string Category, string Message);

public sealed record ScanOutcome(IReadOnlyList<Finding> Findings, IReadOnlyList<ScanFailure> Failures)
{
    public static ScanOutcome Empty { get; } = new([], []);

    public bool HasFailures => Failures.Count > 0;
}

public sealed record ScanOptions(int MaxConcurrency)
{
    public const int ConcurrencyCeiling = 4;

    public static int DefaultConcurrency => Math.Max(1, Math.Min(Environment.ProcessorCount, ConcurrencyCeiling));

    public static ScanOptions Default => new(DefaultConcurrency);

    public int MaxConcurrency { get; } = MaxConcurrency >= 1
        ? MaxConcurrency
        : throw new ArgumentOutOfRangeException(nameof(MaxConcurrency));
}
