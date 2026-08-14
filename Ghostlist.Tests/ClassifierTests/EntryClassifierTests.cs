using Microsoft.Win32;
using Ghostlist.Core;

namespace Ghostlist.Tests.ClassifierTests;

public class EntryClassifierTests
{
    private static readonly RegistryLocation Location = new(RegistryHive.CurrentUser, RegistryView.Registry64, @"SOFTWARE\Test");

    [Fact]
    public void SystemComponentIsNeverOfferedForDeletion()
    {
        var result = Classify(Create(@"C:\Gone\unins000.exe") with { SystemComponent = true }, new FakeFileSystem());

        Assert.Equal(EntryStatus.Unsupported, result.Status);
        Assert.Equal(EvidenceKinds.SystemComponent, Assert.Single(result.Evidence).Kind);
    }

    [Fact]
    public void ExistingUninstallerProducesNoEvidenceAndStaysHealthy()
    {
        var fileSystem = new FakeFileSystem().WithFile(@"C:\App\remove.exe").WithDirectory(@"C:\App");

        var result = Classify(Create(@"C:\App\remove.exe /S"), fileSystem);

        Assert.Equal(EntryStatus.Healthy, result.Status);
        Assert.Empty(result.Evidence);
        Assert.Equal(@"C:\App\remove.exe", result.ResolvedTarget);
    }

    [Fact]
    public void MissingUninstallerAloneIsOnlySuspicious()
    {
        var fileSystem = new FakeFileSystem().WithDirectory(@"C:\App");

        var result = Classify(Create(@"C:\App\remove.exe /S"), fileSystem);

        Assert.Equal(EntryStatus.Suspicious, result.Status);
        Assert.Equal(EvidenceKinds.UninstallerMissing, Assert.Single(result.Evidence).Kind);
    }

    [Fact]
    public void MissingUninstallerAndItsDirectoryIsBrokenAndAutoFixable()
    {
        var result = Classify(Create(@"C:\Gone\remove.exe /S"), new FakeFileSystem());

        Assert.Equal(EntryStatus.Broken, result.Status);
        Assert.Contains(result.Evidence, x => x.Kind == EvidenceKinds.UninstallerMissing);
        Assert.Contains(result.Evidence, x => x.Kind == EvidenceKinds.TargetDirectoryMissing);
        Assert.True(result.Confidence >= ConfidenceRules.AutoFixThreshold);
    }

    [Fact]
    public void UnreadableUninstallerLocationIsNeverTreatedAsMissing()
    {
        var fileSystem = new FakeFileSystem()
            .WithFile(@"C:\App\remove.exe", ProbeResult.Unknown)
            .WithDirectory(@"C:\App", ProbeResult.Unknown);

        var result = Classify(Create(@"C:\App\remove.exe /S"), fileSystem);

        Assert.Equal(EntryStatus.Suspicious, result.Status);
        Assert.Equal(EvidenceKinds.UninstallerUnreadable, Assert.Single(result.Evidence).Kind);
    }

    [Fact]
    public void InstallLocationThatEqualsTheTargetDirectoryIsNotCountedTwice()
    {
        var entry = Create(@"C:\Gone\remove.exe /S") with { InstallLocation = @"C:\Gone" };

        var result = Classify(entry, new FakeFileSystem());

        Assert.Equal(1, result.Evidence.Count(x => x.Kind == EvidenceKinds.TargetDirectoryMissing));
        Assert.DoesNotContain(result.Evidence, x => x.Kind == EvidenceKinds.InstallLocationMissing);
    }

    [Fact]
    public void DisplayIconPointingAtTheSameFileIsNotCountedTwice()
    {
        var entry = Create(@"C:\Gone\remove.exe /S") with { DisplayIcon = @"C:\Gone\remove.exe,0" };

        var result = Classify(entry, new FakeFileSystem());

        Assert.DoesNotContain(result.Evidence, x => x.Kind == EvidenceKinds.DisplayIconMissing);
    }

    [Fact]
    public void IndependentInstallLocationAndIconCountAsSeparateEvidence()
    {
        var entry = Create(@"C:\Gone\remove.exe /S") with
        {
            InstallLocation = @"C:\Other",
            DisplayIcon = @"C:\Icons\app.ico,0"
        };

        var result = Classify(entry, new FakeFileSystem());

        Assert.Contains(result.Evidence, x => x.Kind == EvidenceKinds.InstallLocationMissing);
        Assert.Contains(result.Evidence, x => x.Kind == EvidenceKinds.DisplayIconMissing);
        Assert.Equal(100, result.Confidence);
    }

    [Fact]
    public void UnresolvableCommandStaysSuspiciousAndResolvesNoTarget()
    {
        var result = Classify(Create("some unresolvable text"), new FakeFileSystem());

        Assert.Equal(EntryStatus.Suspicious, result.Status);
        Assert.Null(result.ResolvedTarget);
        Assert.Equal(EvidenceKinds.CommandUnresolvable, Assert.Single(result.Evidence).Kind);
    }

    [Fact]
    public void RelativeCommandIsResolvedAgainstInstallLocationBeforeJudging()
    {
        var entry = Create("unins000.exe /S") with { InstallLocation = @"C:\Ghost\App" };
        var fileSystem = new FakeFileSystem().WithFile(@"C:\Ghost\App\unins000.exe").WithDirectory(@"C:\Ghost\App");

        var result = Classify(entry, fileSystem);

        Assert.Equal(EntryStatus.Healthy, result.Status);
        Assert.Equal(@"C:\Ghost\App\unins000.exe", result.ResolvedTarget);
    }

    private static EntryAssessment Classify(UninstallEntry entry, FakeFileSystem? fileSystem = null) =>
        new EntryClassifier(fileSystem ?? new FakeFileSystem()).Classify(entry);

    private static UninstallEntry Create(string? command) =>
        new("id", "Test App", "1.0", "Teknesyum", command, null, null, false, false, Location);
}
