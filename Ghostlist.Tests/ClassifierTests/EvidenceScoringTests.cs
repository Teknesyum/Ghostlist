using Ghostlist.Core;

namespace Ghostlist.Tests.ClassifierTests;

public class EvidenceScoringTests
{
    [Fact]
    public void NoEvidenceMeansHealthy()
    {
        var (status, confidence) = ConfidenceRules.Evaluate([]);

        Assert.Equal(EntryStatus.Healthy, status);
        Assert.Equal(0, confidence);
    }

    [Fact]
    public void SingleEvidenceNeverStampsBrokenNoMatterHowHeavy()
    {
        var (status, confidence) = ConfidenceRules.Evaluate([new Evidence("k", "d", 100)]);

        Assert.Equal(EntryStatus.Suspicious, status);
        Assert.Equal(100, confidence);
    }

    [Fact]
    public void TwoIndependentEvidencesAboveThresholdAreBroken()
    {
        var evidence = new Evidence[]
        {
            new(EvidenceKinds.UninstallerMissing, "a", EvidenceWeights.UninstallerMissing),
            new(EvidenceKinds.TargetDirectoryMissing, "b", EvidenceWeights.TargetDirectoryMissing)
        };

        var (status, confidence) = ConfidenceRules.Evaluate(evidence);

        Assert.Equal(EntryStatus.Broken, status);
        Assert.Equal(95, confidence);
    }

    [Fact]
    public void TwoWeakEvidencesBelowThresholdStaySuspicious()
    {
        var evidence = new Evidence[]
        {
            new(EvidenceKinds.DisplayIconMissing, "a", EvidenceWeights.DisplayIconMissing),
            new(EvidenceKinds.InstallLocationMissing, "b", EvidenceWeights.InstallLocationMissing)
        };

        var (status, confidence) = ConfidenceRules.Evaluate(evidence);

        Assert.Equal(EntryStatus.Suspicious, status);
        Assert.Equal(50, confidence);
    }

    [Fact]
    public void UncertainEvidenceCapsConfidenceAndBlocksBroken()
    {
        var evidence = new Evidence[]
        {
            new(EvidenceKinds.UninstallerMissing, "a", EvidenceWeights.UninstallerMissing),
            new(EvidenceKinds.TargetDirectoryMissing, "b", EvidenceWeights.TargetDirectoryMissing),
            new(EvidenceKinds.MsiCacheUnreadable, "c", EvidenceWeights.Uncertain)
        };

        var (status, confidence) = ConfidenceRules.Evaluate(evidence);

        Assert.Equal(ConfidenceRules.UncertainCeiling, confidence);
        Assert.Equal(EntryStatus.Suspicious, status);
    }

    [Fact]
    public void OnlyUncertainEvidenceIsSuspiciousNotHealthy()
    {
        var (status, _) = ConfidenceRules.Evaluate([new Evidence(EvidenceKinds.UninstallerUnreadable, "a", EvidenceWeights.Uncertain)]);

        Assert.Equal(EntryStatus.Suspicious, status);
    }

    [Fact]
    public void CeilingLimitsCategoriesThatMustNotReachAutoFix()
    {
        var evidence = new Evidence[]
        {
            new(EvidenceKinds.FolderHasNoOwner, "a", EvidenceWeights.FolderHasNoOwner),
            new(EvidenceKinds.FolderHasNoExecutable, "b", EvidenceWeights.FolderHasNoExecutable),
            new(EvidenceKinds.FolderIsStale, "c", EvidenceWeights.FolderIsStale)
        };

        var (status, confidence) = ConfidenceRules.Evaluate(evidence, ConfidenceRules.LeftoverFolderCeiling);

        Assert.Equal(EntryStatus.Broken, status);
        Assert.Equal(ConfidenceRules.LeftoverFolderCeiling, confidence);
        Assert.True(confidence < ConfidenceRules.AutoFixThreshold);
    }

    [Theory]
    [InlineData(Categories.Uninstall, true)]
    [InlineData(Categories.Shortcut, true)]
    [InlineData(Categories.Folder, false)]
    [InlineData(Categories.Msix, false)]
    public void AutoFixIsLimitedToApprovedCategories(string category, bool expected)
    {
        var finding = BrokenFinding(95, 2);

        Assert.Equal(expected, ConfidenceRules.IsAutoFixable(finding, category));
    }

    [Fact]
    public void AutoFixNeedsBothTheThresholdAndTwoIndependentEvidences()
    {
        Assert.False(ConfidenceRules.IsAutoFixable(BrokenFinding(89, 2), Categories.Uninstall));
        Assert.False(ConfidenceRules.IsAutoFixable(BrokenFinding(100, 1), Categories.Uninstall));
        Assert.True(ConfidenceRules.IsAutoFixable(BrokenFinding(ConfidenceRules.AutoFixThreshold, 2), Categories.Uninstall));
    }

    [Fact]
    public void AutoFixNeverPicksSuspiciousFindings()
    {
        var finding = BrokenFinding(100, 3) with { Status = EntryStatus.Suspicious };

        Assert.False(ConfidenceRules.IsAutoFixable(finding, Categories.Uninstall));
    }

    private static Finding BrokenFinding(int confidence, int evidenceCount)
    {
        var evidence = Enumerable.Range(0, evidenceCount).Select(i => new Evidence($"k{i}", $"d{i}", 50)).ToList();
        return new Finding("id", "Title", null, EntryStatus.Broken, confidence, evidence, Categories.Uninstall, new object());
    }
}
