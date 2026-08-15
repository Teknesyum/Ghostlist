using System.Net.Http;
using System.Text.Json;

namespace Ghostlist.Core;

public sealed class GitHubReleaseSource(
    string owner = GitHubReleaseSource.DefaultOwner,
    string repository = GitHubReleaseSource.DefaultRepository) : IReleaseSource
{
    public const string DefaultOwner = "Teknesyum";
    public const string DefaultRepository = "Ghostlist";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public string Endpoint => $"https://api.github.com/repos/{owner}/{repository}/releases/latest";

    public async Task<ReleaseInfo?> LatestAsync(CancellationToken token)
    {
        using var client = new HttpClient { Timeout = Timeout };
        client.DefaultRequestHeaders.Add("User-Agent", "Ghostlist");
        client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");

        using var response = await client.GetAsync(Endpoint, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;

        var payload = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        return Parse(payload);
    }

    public static ReleaseInfo? Parse(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!document.RootElement.TryGetProperty("tag_name", out var tag)) return null;
            var name = tag.GetString();
            if (string.IsNullOrWhiteSpace(name)) return null;
            var url = document.RootElement.TryGetProperty("html_url", out var link) ? link.GetString() : null;
            return new ReleaseInfo(name, url);
        }
        catch (JsonException) { return null; }
    }
}
