using System.Net.Http;
using Microsoft.Win32;
using Ghostlist.App;
using Ghostlist.Core;
using Xunit;

namespace Ghostlist.Tests.ViewModelTests;

[Collection(nameof(LanguageCollection))]
public class LanguageTests : IDisposable
{
    public void Dispose()
    {
        Strings.Current.Use(Strings.Turkish);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void SwitchingLanguageResolvesTheSameKeyInBothTables()
    {
        Strings.Current.Use(Strings.Turkish);
        var turkish = Strings.Current.Get("btn.rescan");
        Strings.Current.Use(Strings.English);
        var english = Strings.Current.Get("btn.rescan");

        Assert.Equal("Yeniden tara", turkish);
        Assert.Equal("Rescan", english);
    }

    [Fact]
    public void EveryKeyResolvesInBothLanguagesWithoutFallingBackToTheKeyItself()
    {
        foreach (var language in new[] { Strings.Turkish, Strings.English })
        {
            Strings.Current.Use(language);
            foreach (var key in new[]
            {
                "btn.rescan", "btn.stopScan", "btn.export", "btn.diagnostics",
                "update.check", "update.auto", "update.state.up_to_date",
                "scan.state.running", "tab.findings", "tab.backups"
            })
                Assert.NotEqual(key, Strings.Current.Get(key));
        }
    }

    [Fact]
    public void PlaceholdersAreFilledInBothLanguages()
    {
        foreach (var language in new[] { Strings.Turkish, Strings.English })
        {
            Strings.Current.Use(language);
            var text = Strings.Current.Format("update.available", ("version", "2.1.0"));
            Assert.Contains("2.1.0", text);
            Assert.DoesNotContain("{version}", text);
        }
    }

    [Fact]
    public void LanguageChangeReachesTheViewModelsThatShowText()
    {
        Strings.Current.Use(Strings.Turkish);
        var group = new CategoryGroupViewModel(Categories.Shortcut);
        var turkish = group.Header;

        Strings.Current.Use(Strings.English);
        group.RefreshLanguage();

        Assert.NotEqual(turkish, group.Header);
        Assert.Equal("Shortcuts", group.Header);
    }
}

[Collection(nameof(LanguageCollection))]
public class CategoryGroupTests
{
    [Fact]
    public void GroupCountsOnlyTheItemsItWasGiven()
    {
        var group = new CategoryGroupViewModel(Categories.Shortcut);
        Assert.Equal(0, group.Count);

        group.Reset([Item(group, "a"), Item(group, "b"), Item(group, "c")]);
        Assert.Equal(3, group.Count);

        group.Reset([Item(group, "a")]);
        Assert.Equal(1, group.Count);
    }

    [Fact]
    public void SelectingAGroupOnlyTouchesItsOwnItems()
    {
        var shortcuts = new CategoryGroupViewModel(Categories.Shortcut);
        var startup = new CategoryGroupViewModel(Categories.Startup);
        var mine = Item(shortcuts, "a");
        var other = Item(startup, "b");
        shortcuts.Reset([mine]);
        startup.Reset([other]);

        shortcuts.SelectCommand.Execute(null);

        Assert.True(mine.IsSelected);
        Assert.False(other.IsSelected);
    }

    private static FindingViewModel Item(CategoryGroupViewModel group, string id) =>
        new(TestFindings.Broken(id, group.Category), group);
}

[Collection(nameof(LanguageCollection))]
public class BulkFixSelectionTests
{
    [Fact]
    public void LeftoverFoldersAndMsixNeverEnterTheAutomaticFix()
    {
        var service = TestFindings.Service();
        var folder = TestFindings.Broken("folder-1", Categories.Folder, confidence: 100);
        var msix = TestFindings.Broken("msix-1", Categories.Msix, confidence: 100);

        Assert.False(ConfidenceRules.IsAutoFixable(folder, Categories.Folder));
        Assert.False(ConfidenceRules.IsAutoFixable(msix, Categories.Msix));
        Assert.Empty(service.AutoFixable([folder, msix]));
    }

    [Fact]
    public void TheOtherFourCategoriesDoEnterTheAutomaticFix()
    {
        foreach (var category in new[] { Categories.Uninstall, Categories.Shortcut, Categories.Startup, Categories.Task })
            Assert.True(ConfidenceRules.IsAutoFixable(TestFindings.Broken("id", category, confidence: 95), category),
                $"{category} should be auto fixable");
    }

    [Fact]
    public void AFindingBelowTheThresholdIsNotOfferedForAutomaticFixing()
    {
        var category = Categories.Shortcut;

        Assert.False(ConfidenceRules.IsAutoFixable(TestFindings.Broken("a", category, confidence: 89), category));
        Assert.True(ConfidenceRules.IsAutoFixable(TestFindings.Broken("b", category, confidence: 90), category));
        Assert.Equal(90, ConfidenceRules.AutoFixThreshold);
    }

    [Fact]
    public void ASingleEvidenceFindingIsNotOfferedForAutomaticFixingEvenAtFullConfidence()
    {
        var finding = new Finding("id", "One clue", null, EntryStatus.Broken, 100,
            [new Evidence(EvidenceKinds.ShortcutTargetMissing, "x", 100)], Categories.Shortcut, "payload");

        Assert.False(ConfidenceRules.IsAutoFixable(finding, Categories.Shortcut));
    }

    [Fact]
    public void SuspiciousFindingsAreNeverAutomaticallyFixed()
    {
        var finding = TestFindings.Broken("id", Categories.Shortcut, confidence: 95) with { Status = EntryStatus.Suspicious };

        Assert.False(ConfidenceRules.IsAutoFixable(finding, Categories.Shortcut));
    }

    [Fact]
    public void LockedFindingsAreTheOnesUnderTheMachineHive()
    {
        var machine = new RegistryLocation(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\A");
        var user = new RegistryLocation(RegistryHive.CurrentUser, RegistryView.Registry64, @"SOFTWARE\A");
        var group = new CategoryGroupViewModel(Categories.Uninstall);

        Assert.True(new FindingViewModel(TestFindings.WithLocation(machine), group).RequiresElevation);
        Assert.False(new FindingViewModel(TestFindings.WithLocation(user), group).RequiresElevation);
    }
}

[Collection(nameof(LanguageCollection))]
public class UpdateBannerTests
{
    [Fact]
    public async Task TheBannerStaysHiddenWhileAutomaticCheckingIsOff()
    {
        var source = new NeverCalledSource();
        var settings = new FakeSettings { AutomaticUpdateCheck = false };
        var banner = new UpdateBannerViewModel(
            new UpdateChecker(source, settings, new SemanticVersion(2, 0, 0)), settings, _ => { });

        await banner.CheckAsync(manual: false);

        Assert.False(banner.IsVisible);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task ANewerReleaseShowsTheBannerWithTheVersionInIt()
    {
        var settings = new FakeSettings { AutomaticUpdateCheck = true };
        var banner = new UpdateBannerViewModel(
            new UpdateChecker(new StubSource("v2.1.0"), settings, new SemanticVersion(2, 0, 0)), settings, _ => { });

        await banner.CheckAsync(manual: true);

        Assert.True(banner.IsVisible);
        Assert.Contains("2.1.0", banner.BannerText);
    }

    [Fact]
    public async Task DismissingHidesTheBannerWithoutTouchingTheSettings()
    {
        var settings = new FakeSettings { AutomaticUpdateCheck = true };
        var banner = new UpdateBannerViewModel(
            new UpdateChecker(new StubSource("v2.1.0"), settings, new SemanticVersion(2, 0, 0)), settings, _ => { });
        await banner.CheckAsync(manual: true);

        banner.DismissCommand.Execute(null);

        Assert.False(banner.IsVisible);
        Assert.True(settings.AutomaticUpdateCheck);
    }

    [Fact]
    public async Task AFailedManualCheckReportsQuietlyInsteadOfThrowing()
    {
        var settings = new FakeSettings { AutomaticUpdateCheck = true };
        var banner = new UpdateBannerViewModel(
            new UpdateChecker(new FailingSource(), settings, new SemanticVersion(2, 0, 0)), settings, _ => { });

        await banner.CheckAsync(manual: true);

        Assert.False(banner.IsVisible);
        Assert.True(banner.HasManualMessage);
        Assert.Equal(UpdateStates.Unreachable, banner.Status.StateKey);
    }

    [Fact]
    public void TurningAutomaticCheckingOnPersistsIt()
    {
        var settings = new FakeSettings { AutomaticUpdateCheck = false };
        var banner = new UpdateBannerViewModel(
            new UpdateChecker(new StubSource("v2.0.0"), settings, new SemanticVersion(2, 0, 0)), settings, _ => { });

        banner.AutomaticCheck = true;

        Assert.True(settings.AutomaticUpdateCheck);
        Assert.Equal(1, settings.Saves);
    }

    [Fact]
    public void OpeningTheReleaseNotesNeverDownloadsAnything()
    {
        var opened = new List<string>();
        var settings = new FakeSettings();
        var banner = new UpdateBannerViewModel(
            new UpdateChecker(new StubSource("v2.1.0"), settings, new SemanticVersion(2, 0, 0)), settings, opened.Add);

        banner.OpenReleaseCommand.Execute(null);

        Assert.Equal(UpdateBannerViewModel.ReleasesUrl, Assert.Single(opened));
    }

    private sealed class StubSource(string tag) : IReleaseSource
    {
        public Task<ReleaseInfo?> LatestAsync(CancellationToken token) =>
            Task.FromResult<ReleaseInfo?>(new ReleaseInfo(tag, null));
    }

    private sealed class NeverCalledSource : IReleaseSource
    {
        public int Calls { get; private set; }
        public Task<ReleaseInfo?> LatestAsync(CancellationToken token)
        {
            Calls++;
            return Task.FromResult<ReleaseInfo?>(new ReleaseInfo("v9.9.9", null));
        }
    }

    private sealed class FailingSource : IReleaseSource
    {
        public Task<ReleaseInfo?> LatestAsync(CancellationToken token) => throw new HttpRequestException("offline");
    }

    private sealed class FakeSettings : IUpdateSettings
    {
        public bool AutomaticUpdateCheck { get; set; }
        public DateTimeOffset? LastUpdateCheck { get; set; }
        public int Saves { get; private set; }
        public void Save() => Saves++;
    }
}

[Collection(nameof(LanguageCollection))]
public class SettingsTests
{
    [Fact]
    public void AutomaticUpdateCheckingIsOffOnAFreshInstall() =>
        Assert.False(new AppSettings().AutomaticUpdateCheck);

    [Fact]
    public void AFreshInstallHasNeverChecked() =>
        Assert.Null(new AppSettings().LastUpdateCheck);

    [Fact]
    public void SettingsImplementTheUpdateContract() =>
        Assert.IsAssignableFrom<IUpdateSettings>(new AppSettings());
}

[CollectionDefinition(nameof(LanguageCollection), DisableParallelization = true)]
public class LanguageCollection;

internal static class TestFindings
{
    public static Finding Broken(string id, string category, int confidence = 95) =>
        new(id, id, "subtitle", EntryStatus.Broken, confidence,
            [
                new Evidence(EvidenceKinds.ShortcutTargetMissing, "x", EvidenceWeights.ShortcutTargetMissing),
                new Evidence(EvidenceKinds.TargetDirectoryMissing, "y", EvidenceWeights.TargetDirectoryMissing)
            ],
            category, id);

    public static Finding WithLocation(RegistryLocation location) =>
        Broken("id", Categories.Uninstall) with
        {
            Payload = new UninstallEntry("id", "App", null, null, null, null, null, false, false, location)
        };

    public static CleanupService Service() => new([], new NullSink());

    private sealed class NullSink : IBackupSink
    {
        public string SaveRegistryTree(RegistryTreeBackup backup, string label) => "none";
        public string SaveRegistryValue(RegistryValueBackup backup, string label) => "none";
        public string MoveFileToBackup(string sourcePath, string label) => "none";
        public string MoveDirectoryToBackup(string sourcePath, string label) => "none";
        public string SaveText(string content, string label, string extension) => "none";
        public void Restore(string backupPath) { }
        public IReadOnlyList<string> List() => [];
    }
}
