using Microsoft.Win32;
using ProgramFixer.Core;

namespace ProgramFixer.Tests;

public class EntryClassifierTests
{
    private static readonly RegistryLocation Location = new(RegistryHive.CurrentUser, RegistryView.Registry64, @"SOFTWARE\Test");

    [Fact]
    public void MissingUninstallerIsBroken()
    {
        var entry = Create("\"C:\\Games\\Missing\\unins000.exe\"");
        var result = new EntryClassifier(new FakeFileSystem(false)).Classify(entry);
        Assert.Equal(EntryStatus.Broken, result.Status);
        Assert.Contains("bulunamadı", result.Reason);
    }

    [Fact]
    public void ExistingUninstallerIsHealthy()
    {
        var result = new EntryClassifier(new FakeFileSystem(true)).Classify(Create("C:\\App\\remove.exe /S"));
        Assert.Equal(EntryStatus.Healthy, result.Status);
    }

    [Fact]
    public void MsiIsNeverOfferedForRegistryDeletion()
    {
        var result = new EntryClassifier(new FakeFileSystem(false)).Classify(Create("MsiExec.exe /X{00000000-0000-0000-0000-000000000000}"));
        Assert.Equal(EntryStatus.Unsupported, result.Status);
    }

    [Fact]
    public void SystemComponentIsNeverOfferedForDeletion()
    {
        var result = new EntryClassifier(new FakeFileSystem(false)).Classify(Create("C:\\Missing.exe") with { SystemComponent = true });
        Assert.Equal(EntryStatus.Unsupported, result.Status);
    }

    private static UninstallEntry Create(string? command) => new("id", "Test App", null, null, command, null, null, false, false, Location);
    private sealed class FakeFileSystem(bool exists) : IFileSystem { public bool FileExists(string path) => exists; }
}

