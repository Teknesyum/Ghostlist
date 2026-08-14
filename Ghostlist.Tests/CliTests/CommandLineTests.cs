using Ghostlist.Cli;
using Ghostlist.Core;

namespace Ghostlist.Tests.CliTests;

public class CommandLineTests
{
    [Fact]
    public void NoArgumentsAsksForHelp() =>
        Assert.Equal(CommandKind.Help, CommandLine.Parse([]).Kind);

    [Theory]
    [InlineData("help")]
    [InlineData("--help")]
    [InlineData("-h")]
    public void HelpAliasesAreAccepted(string argument) =>
        Assert.Equal(CommandKind.Help, CommandLine.Parse([argument]).Kind);

    [Fact]
    public void UnknownCommandIsInvalid()
    {
        var plan = CommandLine.Parse(["clean"]);
        Assert.Equal(CommandKind.Invalid, plan.Kind);
        Assert.Contains("clean", plan.Error);
    }

    [Fact]
    public void ScanDefaultsToEverythingAtAnyConfidence()
    {
        var plan = CommandLine.Parse(["scan"]);
        Assert.Equal(CommandKind.Scan, plan.Kind);
        Assert.Null(plan.Category);
        Assert.False(plan.Json);
        Assert.Equal(0, plan.MinConfidence);
    }

    [Fact]
    public void ScanReadsCategoryJsonAndConfidence()
    {
        var plan = CommandLine.Parse(["scan", "--category", "shortcut", "--json", "--min-confidence", "70"]);
        Assert.Equal(Categories.Shortcut, plan.Category);
        Assert.True(plan.Json);
        Assert.Equal(70, plan.MinConfidence);
    }

    [Fact]
    public void ScanRejectsUnknownCategory() =>
        Assert.Contains("driver", CommandLine.Parse(["scan", "--category", "driver"]).Error);

    [Theory]
    [InlineData("-1")]
    [InlineData("101")]
    [InlineData("abc")]
    public void ConfidenceOutsideRangeIsInvalid(string value)
    {
        var plan = CommandLine.Parse(["scan", "--min-confidence", value]);
        Assert.Equal(CommandKind.Invalid, plan.Kind);
    }

    [Fact]
    public void MissingOptionValueIsInvalid() =>
        Assert.Equal(CommandKind.Invalid, CommandLine.Parse(["scan", "--category", "--json"]).Kind);

    [Fact]
    public void UnknownOptionIsInvalid() =>
        Assert.Contains("--verbose", CommandLine.Parse(["scan", "--verbose"]).Error);

    [Fact]
    public void FixByIdCarriesTheIdAndNoAll()
    {
        var plan = CommandLine.Parse(["fix", "--id", "uninstall:Foo", "--dry-run"]);
        Assert.Equal(CommandKind.Fix, plan.Kind);
        Assert.Equal("uninstall:Foo", plan.Id);
        Assert.False(plan.All);
        Assert.True(plan.DryRun);
    }

    [Fact]
    public void FixAllDefaultsToTheAutomaticThreshold()
    {
        var plan = CommandLine.Parse(["fix", "--all"]);
        Assert.True(plan.All);
        Assert.Equal(ConfidenceRules.AutoFixThreshold, plan.MinConfidence);
        Assert.False(plan.Yes);
    }

    [Fact]
    public void FixAllAcceptsYesAndItsOwnThreshold()
    {
        var plan = CommandLine.Parse(["fix", "--all", "--yes", "--min-confidence", "95"]);
        Assert.True(plan.Yes);
        Assert.Equal(95, plan.MinConfidence);
    }

    [Fact]
    public void FixWithoutTargetIsInvalid() =>
        Assert.Contains("exactly one", CommandLine.Parse(["fix"]).Error);

    [Fact]
    public void FixWithBothTargetsIsInvalid() =>
        Assert.Contains("exactly one", CommandLine.Parse(["fix", "--all", "--id", "x"]).Error);

    [Fact]
    public void ConfidenceOnSingleFixIsInvalid() =>
        Assert.Contains("--min-confidence", CommandLine.Parse(["fix", "--id", "x", "--min-confidence", "50"]).Error);

    [Fact]
    public void RestoreListAndBackupAreExclusive()
    {
        Assert.True(CommandLine.Parse(["restore", "--list"]).ListBackups);
        Assert.Equal(@"C:\backup.json", CommandLine.Parse(["restore", "--backup", @"C:\backup.json"]).BackupPath);
        Assert.Equal(CommandKind.Invalid, CommandLine.Parse(["restore"]).Kind);
        Assert.Equal(CommandKind.Invalid, CommandLine.Parse(["restore", "--list", "--backup", "x"]).Kind);
    }

    [Fact]
    public void ExitCodesFollowTheContract()
    {
        Assert.Equal(0, Program.ExitClean);
        Assert.Equal(1, Program.ExitFindings);
        Assert.Equal(2, Program.ExitError);
    }
}
