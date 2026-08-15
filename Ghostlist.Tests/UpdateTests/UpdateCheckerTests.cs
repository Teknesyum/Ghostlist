using System.Net.Http;
using Ghostlist.Core;
using Xunit;

namespace Ghostlist.Tests.UpdateTests;

public class SemanticVersionTests
{
    [Theory]
    [InlineData("v2.0.0", 2, 0, 0)]
    [InlineData("2.0.0", 2, 0, 0)]
    [InlineData("V10.20.30", 10, 20, 30)]
    [InlineData("2.0.0.0", 2, 0, 0)]
    [InlineData("2.1", 2, 1, 0)]
    public void TagsAreParsedWithOrWithoutThePrefix(string text, int major, int minor, int patch) =>
        Assert.Equal(new SemanticVersion(major, minor, patch), SemanticVersion.TryParse(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("latest")]
    [InlineData("v2.x.0")]
    [InlineData("v-1.0.0")]
    [InlineData("2.0.0-")]
    public void GarbageIsRejectedInsteadOfGuessed(string? text) => Assert.Null(SemanticVersion.TryParse(text));

    [Fact]
    public void ComparisonIsNumericNotLexicographic()
    {
        var ten = SemanticVersion.TryParse("v2.0.10")!;
        var nine = SemanticVersion.TryParse("v2.0.9")!;

        Assert.True(ten > nine);
        Assert.False(nine > ten);
        Assert.True(string.CompareOrdinal("v2.0.10", "v2.0.9") < 0);
    }

    [Fact]
    public void MajorBeatsMinorBeatsPatch()
    {
        Assert.True(SemanticVersion.TryParse("3.0.0")! > SemanticVersion.TryParse("2.99.99")!);
        Assert.True(SemanticVersion.TryParse("2.10.0")! > SemanticVersion.TryParse("2.9.99")!);
        Assert.True(SemanticVersion.TryParse("2.0.100")! > SemanticVersion.TryParse("2.0.99")!);
    }

    [Fact]
    public void PreReleaseSortsBelowTheSameFinalVersion()
    {
        var preview = SemanticVersion.TryParse("v2.1.0-beta.1")!;
        var final = SemanticVersion.TryParse("v2.1.0")!;

        Assert.True(preview.IsPreRelease);
        Assert.True(final > preview);
        Assert.Equal("2.1.0-beta.1", preview.ToString());
    }

    [Fact]
    public void BuildMetadataIsIgnored() =>
        Assert.Equal(SemanticVersion.TryParse("2.0.0"), SemanticVersion.TryParse("2.0.0+abc123"));
}

public class UpdateCheckerTests
{
    private static readonly SemanticVersion Current = new(2, 0, 0);

    [Fact]
    public async Task NoNetworkCallIsMadeWhileAutomaticCheckingIsOff()
    {
        var source = new CountingSource("v9.9.9");
        var settings = new FakeSettings { AutomaticUpdateCheck = false };
        var checker = new UpdateChecker(source, settings, Current);

        var status = await checker.CheckAsync(manual: false);

        Assert.Equal(0, source.Calls);
        Assert.Equal(UpdateStates.Disabled, status.StateKey);
        Assert.Null(settings.LastUpdateCheck);
        Assert.Equal(0, settings.Saves);
    }

    [Fact]
    public async Task ManualCheckWorksEvenWhileAutomaticCheckingIsOff()
    {
        var source = new CountingSource("v9.9.9");
        var settings = new FakeSettings { AutomaticUpdateCheck = false };
        var checker = new UpdateChecker(source, settings, Current);

        var status = await checker.CheckAsync(manual: true);

        Assert.Equal(1, source.Calls);
        Assert.True(status.HasUpdate);
        Assert.Equal("9.9.9", status.Latest!.ToString());
    }

    [Fact]
    public async Task AutomaticCheckRunsAtMostOncePerDay()
    {
        var source = new CountingSource("v2.0.0");
        var now = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        var time = new FixedTime(now);
        var settings = new FakeSettings { AutomaticUpdateCheck = true };
        var checker = new UpdateChecker(source, settings, Current, time);

        await checker.CheckAsync(manual: false);
        Assert.Equal(1, source.Calls);
        Assert.Equal(now, settings.LastUpdateCheck);

        time.Now = now.AddHours(6);
        var second = await checker.CheckAsync(manual: false);
        Assert.Equal(1, source.Calls);
        Assert.Equal(UpdateStates.TooSoon, second.StateKey);

        time.Now = now.AddHours(25);
        await checker.CheckAsync(manual: false);
        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task LastCheckTimeIsPersisted()
    {
        var now = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        var settings = new FakeSettings { AutomaticUpdateCheck = true };

        await new UpdateChecker(new CountingSource("v2.0.0"), settings, Current, new FixedTime(now))
            .CheckAsync(manual: false);

        Assert.Equal(now, settings.LastUpdateCheck);
        Assert.Equal(1, settings.Saves);
    }

    [Theory]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(InvalidOperationException))]
    public async Task NetworkFailuresAreSwallowedIntoAQuietState(Type exceptionType)
    {
        var checker = new UpdateChecker(
            new ThrowingSource((Exception)Activator.CreateInstance(exceptionType)!),
            new FakeSettings { AutomaticUpdateCheck = true }, Current);

        var status = await checker.CheckAsync(manual: true);

        Assert.Equal(UpdateStates.Unreachable, status.StateKey);
        Assert.False(status.HasUpdate);
    }

    [Fact]
    public async Task AnUnparsableTagIsTreatedAsUnreachableRatherThanAnUpdate()
    {
        var checker = new UpdateChecker(new CountingSource("nightly"),
            new FakeSettings { AutomaticUpdateCheck = true }, Current);

        Assert.Equal(UpdateStates.Unreachable, (await checker.CheckAsync(manual: true)).StateKey);
    }

    [Fact]
    public async Task AnOlderOrEqualReleaseIsNotAnUpdate()
    {
        var settings = new FakeSettings { AutomaticUpdateCheck = true };

        Assert.Equal(UpdateStates.UpToDate,
            (await new UpdateChecker(new CountingSource("v2.0.0"), settings, Current).CheckAsync(true)).StateKey);
        Assert.Equal(UpdateStates.UpToDate,
            (await new UpdateChecker(new CountingSource("v1.9.9"), settings, Current).CheckAsync(true)).StateKey);
    }

    [Fact]
    public async Task CancellationIsNotSwallowed()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        var checker = new UpdateChecker(new ThrowingSource(new OperationCanceledException()),
            new FakeSettings { AutomaticUpdateCheck = true }, Current);

        await Assert.ThrowsAsync<OperationCanceledException>(() => checker.CheckAsync(true, source.Token));
    }

    [Fact]
    public void NothingIsDownloadedOrInstalled()
    {
        var members = typeof(UpdateChecker).GetMethods().Select(x => x.Name)
            .Concat(typeof(IReleaseSource).GetMethods().Select(x => x.Name))
            .ToList();

        Assert.DoesNotContain(members, x => x.Contains("Download", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(members, x => x.Contains("Install", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheGithubEndpointIsTheReleasesApiAndTheTimeoutIsFiveSeconds()
    {
        var source = new GitHubReleaseSource();

        Assert.Equal("https://api.github.com/repos/Teknesyum/Ghostlist/releases/latest", source.Endpoint);
        Assert.Equal(TimeSpan.FromSeconds(5), GitHubReleaseSource.Timeout);
    }

    [Fact]
    public void ReleasePayloadsAreParsedAndBadOnesRejected()
    {
        var release = GitHubReleaseSource.Parse("""{"tag_name":"v2.1.0","html_url":"https://example/r"}""");

        Assert.Equal("v2.1.0", release!.Tag);
        Assert.Equal("https://example/r", release.Url);
        Assert.Null(GitHubReleaseSource.Parse("not json"));
        Assert.Null(GitHubReleaseSource.Parse("""{"message":"API rate limit exceeded"}"""));
        Assert.Null(GitHubReleaseSource.Parse("""{"tag_name":""}"""));
    }

    private sealed class CountingSource(string tag) : IReleaseSource
    {
        public int Calls { get; private set; }
        public Task<ReleaseInfo?> LatestAsync(CancellationToken token)
        {
            Calls++;
            return Task.FromResult<ReleaseInfo?>(new ReleaseInfo(tag, "https://example/release"));
        }
    }

    private sealed class ThrowingSource(Exception error) : IReleaseSource
    {
        public Task<ReleaseInfo?> LatestAsync(CancellationToken token) => throw error;
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeSettings : IUpdateSettings
    {
        public bool AutomaticUpdateCheck { get; set; }
        public DateTimeOffset? LastUpdateCheck { get; set; }
        public int Saves { get; private set; }
        public void Save() => Saves++;
    }
}
