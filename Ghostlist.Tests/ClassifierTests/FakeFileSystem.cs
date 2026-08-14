using Ghostlist.Core;

namespace Ghostlist.Tests.ClassifierTests;

public sealed class FakeFileSystem : IFileSystem
{
    private readonly Dictionary<string, ProbeResult> files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ProbeResult> directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<string>?> listings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> lastWrites = new(StringComparer.OrdinalIgnoreCase);

    public ProbeResult DefaultFile { get; init; } = ProbeResult.Missing;
    public ProbeResult DefaultDirectory { get; init; } = ProbeResult.Missing;

    public FakeFileSystem WithFile(string path, ProbeResult result = ProbeResult.Present)
    {
        files[Normalize(path)] = result;
        return this;
    }

    public FakeFileSystem WithDirectory(string path, ProbeResult result = ProbeResult.Present)
    {
        directories[Normalize(path)] = result;
        return this;
    }

    public FakeFileSystem WithListing(string path, IReadOnlyList<string>? entries)
    {
        listings[Normalize(path)] = entries;
        return this;
    }

    public FakeFileSystem WithLastWrite(string path, DateTimeOffset moment)
    {
        lastWrites[Normalize(path)] = moment;
        return this;
    }

    public ProbeResult ProbeFile(string path) => files.TryGetValue(Normalize(path), out var result) ? result : DefaultFile;
    public ProbeResult ProbeDirectory(string path) => directories.TryGetValue(Normalize(path), out var result) ? result : DefaultDirectory;
    public IReadOnlyList<string>? TryListEntries(string path) => listings.TryGetValue(Normalize(path), out var entries) ? entries : [];
    public DateTimeOffset? TryGetLastWrite(string path) => lastWrites.TryGetValue(Normalize(path), out var moment) ? moment : null;

    private static string Normalize(string path) => path.TrimEnd('\\');
}
