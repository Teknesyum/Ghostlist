using Ghostlist.Core;

namespace Ghostlist.Tests;

public class UninstallCommandParserTests
{
    [Theory]
    [InlineData("\"C:\\Games\\Baldur's Gate 3\\unins000.exe\" /VERYSILENT", "C:\\Games\\Baldur's Gate 3\\unins000.exe")]
    [InlineData("C:\\Tools\\uninstall.exe /S", "C:\\Tools\\uninstall.exe")]
    [InlineData("C:\\Türkçe Klasör\\kaldırıcı.exe", "C:\\Türkçe Klasör\\kaldırıcı.exe")]
    public void ResolvesExecutableWithoutRunningIt(string command, string expected)
        => Assert.Equal(expected, UninstallCommandParser.ResolveExecutable(command));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("rundll32 setup.dll,Remove")]
    public void ReturnsNullForUnresolvableCommands(string? command)
        => Assert.Null(UninstallCommandParser.ResolveExecutable(command));
}

