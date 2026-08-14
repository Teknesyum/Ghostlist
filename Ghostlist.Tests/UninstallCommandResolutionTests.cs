using Microsoft.Win32;
using Ghostlist.Core;

namespace Ghostlist.Tests;

public class UninstallCommandResolutionTests
{
    private static string ProgramFiles64 =>
        Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetEnvironmentVariable("ProgramFiles")!;

    private static string ProgramFiles32 =>
        Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? Environment.GetEnvironmentVariable("ProgramFiles")!;

    [Fact]
    public void ProgramFilesFollowsTheRegistryViewOfTheEntry()
    {
        const string command = @"%ProgramFiles%\Ghost\unins000.exe /S";

        var wide = UninstallCommandParser.ResolveExecutable(command, RegistryView.Registry64);
        var narrow = UninstallCommandParser.ResolveExecutable(command, RegistryView.Registry32);

        Assert.Equal(Path.Combine(ProgramFiles64, @"Ghost\unins000.exe"), wide);
        Assert.Equal(Path.Combine(ProgramFiles32, @"Ghost\unins000.exe"), narrow);
        if (Environment.Is64BitOperatingSystem) Assert.NotEqual(wide, narrow);
    }

    [Fact]
    public void CommonProgramFilesFollowsTheViewAndProgramW6432StaysWide()
    {
        var common64 = UninstallCommandParser.ExpandForView(@"%CommonProgramFiles%\Ghost", RegistryView.Registry64);
        var common32 = UninstallCommandParser.ExpandForView(@"%CommonProgramFiles%\Ghost", RegistryView.Registry32);
        var wide32 = UninstallCommandParser.ExpandForView(@"%ProgramW6432%\Ghost", RegistryView.Registry32);

        Assert.StartsWith(ProgramFiles64, common64, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(ProgramFiles32, common32, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(ProgramFiles64, wide32, StringComparison.OrdinalIgnoreCase);
        if (Environment.Is64BitOperatingSystem) Assert.NotEqual(common64, common32);
    }

    [Fact]
    public void RelativeCommandIsResolvedAgainstInstallLocation()
    {
        var resolved = UninstallCommandParser.ResolveExecutable(
            "unins000.exe /S", RegistryView.Registry64, @"C:\Ghost\App");

        Assert.Equal(@"C:\Ghost\App\unins000.exe", resolved);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ghost")]
    public void RelativeCommandWithoutUsableInstallLocationIsNotResolved(string? installLocation)
        => Assert.Null(UninstallCommandParser.ResolveExecutable("unins000.exe /S", RegistryView.Registry64, installLocation));

    [Fact]
    public void RelativeCommandIsNeverResolvedAgainstTheWorkingDirectory()
    {
        var resolved = UninstallCommandParser.ResolveExecutable(@"setup\unins000.exe");

        Assert.Null(resolved);
        Assert.DoesNotContain(Directory.GetCurrentDirectory(), resolved ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RelativeCommandLeavesTheEntrySuspiciousInsteadOfBroken()
    {
        var location = new RegistryLocation(RegistryHive.CurrentUser, RegistryView.Registry64, @"SOFTWARE\Test\Relative");
        var entry = new UninstallEntry("id", "Relative App", null, null, "unins000.exe /S", null, null, false, false, location);

        var classified = new EntryClassifier(new MissingFileSystem()).Classify(entry);

        Assert.Equal(EntryStatus.Suspicious, classified.Status);
        Assert.Null(classified.ResolvedTarget);
    }

    [Theory]
    [InlineData(@"cmd /c ""C:\Ghost\unins000.exe"" /S", @"C:\Ghost\unins000.exe")]
    [InlineData(@"cmd.exe /c start """" ""C:\Ghost\unins000.exe""", @"C:\Ghost\unins000.exe")]
    [InlineData(@"C:\Windows\system32\cmd.exe /c ""C:\Ghost\remove.bat""", @"C:\Ghost\remove.bat")]
    [InlineData(@"rundll32.exe ""C:\Ghost\setup.dll"",UninstallEntry", @"C:\Ghost\setup.dll")]
    [InlineData(@"powershell -c ""C:\Ghost\unins000.exe -quiet""", @"C:\Ghost\unins000.exe")]
    [InlineData(@"powershell.exe -Command ""& 'C:\Ghost\unins000.exe' -quiet""", @"C:\Ghost\unins000.exe")]
    public void WrapperCommandsAreUnwrapped(string command, string expected)
        => Assert.Equal(expected, UninstallCommandParser.ResolveExecutable(command));

    [Theory]
    [InlineData(@"MsiExec.exe /X{A1B2C3D4-1111-2222-3333-444455556666}")]
    [InlineData("cmd /c")]
    [InlineData("powershell -encodedcommand ZQB4AGkAdAA=")]
    [InlineData("rundll32")]
    [InlineData("some unresolvable text")]
    public void UnresolvableCommandsReturnNullInsteadOfAGuessedPath(string command)
        => Assert.Null(UninstallCommandParser.ResolveExecutable(command));

    [Theory]
    [InlineData(@"MsiExec.exe /X{a1b2c3d4-1111-2222-3333-444455556666}", "{A1B2C3D4-1111-2222-3333-444455556666}")]
    [InlineData(@"msiexec /i {A1B2C3D4-1111-2222-3333-444455556666} /qn", "{A1B2C3D4-1111-2222-3333-444455556666}")]
    [InlineData("{A1B2C3D4-1111-2222-3333-444455556666}", "{A1B2C3D4-1111-2222-3333-444455556666}")]
    public void MsiProductCodeIsParsed(string command, string expected)
        => Assert.Equal(expected, UninstallCommandParser.ResolveMsiProductCode(command));

    [Theory]
    [InlineData(null)]
    [InlineData(@"""C:\Ghost\unins000.exe"" /S")]
    [InlineData("msiexec /x GhostApp")]
    public void MsiProductCodeIsNullWhenAbsent(string? command)
        => Assert.Null(UninstallCommandParser.ResolveMsiProductCode(command));

    private sealed class MissingFileSystem : IFileSystem { public bool FileExists(string path) => false; }
}
