using Ghostlist.Core;

namespace Ghostlist.Tests.ClassifierTests;

public sealed class FakeFileSystem : IFileSystem
{
    private readonly Dictionary<string, ProbeResult> files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ProbeResult> directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<string>?> listings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> lastWrites = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> texts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, byte[]> blobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> removableRoots = new(StringComparer.OrdinalIgnoreCase);

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

    public FakeFileSystem WithText(string path, string content)
    {
        texts[Normalize(path)] = content;
        files[Normalize(path)] = ProbeResult.Present;
        return this;
    }

    public FakeFileSystem WithBytes(string path, byte[] content)
    {
        blobs[Normalize(path)] = content;
        files[Normalize(path)] = ProbeResult.Present;
        return this;
    }

    public FakeFileSystem WithRemovableVolume(string root)
    {
        removableRoots.Add(Normalize(root));
        return this;
    }

    public ProbeResult ProbeFile(string path) => files.TryGetValue(Normalize(path), out var result) ? result : DefaultFile;
    public ProbeResult ProbeDirectory(string path) => directories.TryGetValue(Normalize(path), out var result) ? result : DefaultDirectory;
    public IReadOnlyList<string>? TryListEntries(string path) => listings.TryGetValue(Normalize(path), out var entries) ? entries : [];
    public DateTimeOffset? TryGetLastWrite(string path) => lastWrites.TryGetValue(Normalize(path), out var moment) ? moment : null;
    public string? TryReadText(string path) => texts.TryGetValue(Normalize(path), out var content) ? content : null;
    public byte[]? TryReadBytes(string path) => blobs.TryGetValue(Normalize(path), out var content) ? content : null;

    public IReadOnlyList<string>? TryListDirectories(string path) =>
        listings.TryGetValue(Normalize(path), out var entries) ? entries?.Where(x => directories.ContainsKey(Normalize(x))).ToList() : null;

    public IReadOnlyList<string>? TryListFiles(string path, string pattern, bool recursive)
    {
        if (!listings.TryGetValue(Normalize(path), out var entries)) return null;
        if (entries is null) return null;
        var extension = pattern.StartsWith("*.", StringComparison.Ordinal) ? pattern[1..] : null;
        var matches = entries.Where(x => !directories.ContainsKey(Normalize(x)));
        if (extension is not null) matches = matches.Where(x => x.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
        return matches.ToList();
    }

    public bool IsFixedVolume(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        var root = Normalize(Path.GetPathRoot(path) ?? string.Empty);
        return !removableRoots.Contains(root);
    }

    private static string Normalize(string path) => path.TrimEnd('\\');
}
