using Microsoft.Win32;
using Ghostlist.Core;

namespace Ghostlist.Tests.ClassifierTests;

public class PackedGuidTests
{
    [Theory]
    [InlineData("{90120000-0030-0000-0000-0000000FF1CE}", "00002109030000000000000000F01FEC")]
    [InlineData("{4A5B6C7D-8E9F-0A1B-2C3D-4E5F6A7B8C9D}", "D7C6B5A4F9E8B1A0C2D3E4F5A6B7C8D9")]
    [InlineData("{00000000-0000-0000-0000-000000000000}", "00000000000000000000000000000000")]
    public void ProductCodeIsPackedIntoThe32DigitInstallerForm(string productCode, string packed)
    {
        Assert.Equal(packed, PackedGuid.Pack(productCode));
        Assert.Equal(32, packed.Length);
    }

    [Theory]
    [InlineData("{90120000-0030-0000-0000-0000000FF1CE}")]
    [InlineData("{4A5B6C7D-8E9F-0A1B-2C3D-4E5F6A7B8C9D}")]
    [InlineData("{A1B2C3D4-1111-2222-3333-444455556666}")]
    public void PackingRoundTrips(string productCode)
        => Assert.Equal(productCode.ToUpperInvariant(), PackedGuid.Unpack(PackedGuid.Pack(productCode)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public void InvalidProductCodeIsNotPacked(string? productCode) => Assert.Null(PackedGuid.Pack(productCode));

    [Theory]
    [InlineData(null)]
    [InlineData("tooshort")]
    [InlineData("ZZ002109030000000000000000F01FEC")]
    public void InvalidPackedFormIsNotUnpacked(string? packed) => Assert.Null(PackedGuid.Unpack(packed));
}

public class MsiOrphanTests
{
    private const string ProductCode = "{A1B2C3D4-1111-2222-3333-444455556666}";
    private const string MachineProducts = @"SOFTWARE\Classes\Installer\Products";
    private const string UserDataRoot = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UserData";
    private const string Sid = "S-1-5-18";
    private static readonly RegistryLocation Location = new(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Test\Msi");

    [Fact]
    public void MsiWithBothRegistrationsMissingIsAnOrphan()
    {
        var result = Classify(HealthyInstallerRegistry(), new FakeFileSystem());

        Assert.Equal(EntryStatus.Broken, result.Status);
        Assert.Contains(result.Evidence, x => x.Kind == EvidenceKinds.MsiProductRegistrationMissing);
        Assert.Contains(result.Evidence, x => x.Kind == EvidenceKinds.MsiUserDataMissing);
        Assert.Equal(90, result.Confidence);
        Assert.Equal(ProductCode, result.MsiProductCode);
    }

    [Fact]
    public void LiveMsiProductRegistrationKeepsTheEntryUnsupported()
    {
        var accessor = HealthyInstallerRegistry();
        accessor.CreateKey(RegistryHive.LocalMachine, RegistryView.Registry64, $@"{MachineProducts}\{PackedGuid.Pack(ProductCode)}");

        var result = Classify(accessor, new FakeFileSystem());

        Assert.Equal(EntryStatus.Unsupported, result.Status);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public void LiveUserDataRegistrationKeepsTheEntryUnsupported()
    {
        var accessor = new InMemoryRegistryHiveAccessor();
        WriteInstallProperties(accessor, @"C:\Windows\Installer\1a2b3c.msi");

        var result = Classify(accessor, new FakeFileSystem().WithFile(@"C:\Windows\Installer\1a2b3c.msi"));

        Assert.Equal(EntryStatus.Unsupported, result.Status);
    }

    [Fact]
    public void UnreadableInstallerCacheNeverCountsAsAMissingPackage()
    {
        var accessor = HealthyInstallerRegistry();
        accessor.CreateKey(RegistryHive.LocalMachine, RegistryView.Registry64, $@"{MachineProducts}\{PackedGuid.Pack(ProductCode)}");
        WriteInstallProperties(accessor, @"C:\Windows\Installer\1a2b3c.msi");
        var fileSystem = new FakeFileSystem().WithFile(@"C:\Windows\Installer\1a2b3c.msi", ProbeResult.Unknown);

        var registration = new RegistryMsiCatalog(accessor, fileSystem).Lookup(ProductCode);

        Assert.Equal(ProbeResult.Unknown, registration.CachePackage);
        Assert.Equal(@"C:\Windows\Installer\1a2b3c.msi", registration.LocalPackagePath);
    }

    [Fact]
    public void CachePackageIsOnlyJudgedWhenTheInstallerRecordedItsPath()
    {
        var registration = new RegistryMsiCatalog(HealthyInstallerRegistry(), new FakeFileSystem()).Lookup(ProductCode);

        Assert.Null(registration.LocalPackagePath);
        Assert.Equal(ProbeResult.Missing, registration.ProductKey);
        Assert.Equal(ProbeResult.Missing, registration.UserData);
    }

    [Fact]
    public void MsiEntryWithoutAParsableProductCodeIsUnsupportedNotBroken()
    {
        var entry = Entry("msiexec /x GhostApp") with { WindowsInstaller = true };

        var result = new EntryClassifier(new FakeFileSystem(), new RegistryMsiCatalog(HealthyInstallerRegistry(), new FakeFileSystem()))
            .Classify(entry);

        Assert.Equal(EntryStatus.Unsupported, result.Status);
        Assert.Equal(EvidenceKinds.MsiProductCodeUnknown, Assert.Single(result.Evidence).Kind);
    }

    [Fact]
    public void AbsentUserDataRootIsUnknownNotMissing()
    {
        var registration = new RegistryMsiCatalog(new InMemoryRegistryHiveAccessor(), new FakeFileSystem()).Lookup(ProductCode);

        Assert.Equal(ProbeResult.Unknown, registration.UserData);
    }

    private static InMemoryRegistryHiveAccessor HealthyInstallerRegistry()
    {
        var accessor = new InMemoryRegistryHiveAccessor();
        accessor.CreateKey(RegistryHive.LocalMachine, RegistryView.Registry64, UserDataRoot).Dispose();
        return accessor;
    }

    private static EntryAssessment Classify(InMemoryRegistryHiveAccessor accessor, FakeFileSystem fileSystem) =>
        new EntryClassifier(fileSystem, new RegistryMsiCatalog(accessor, fileSystem)).Classify(Entry($"MsiExec.exe /X{ProductCode}"));

    private static void WriteInstallProperties(InMemoryRegistryHiveAccessor accessor, string localPackage)
    {
        using var key = accessor.CreateKey(RegistryHive.LocalMachine, RegistryView.Registry64,
            $@"{UserDataRoot}\{Sid}\Products\{PackedGuid.Pack(ProductCode)}\InstallProperties");
        key.SetValue("LocalPackage", localPackage, RegistryValueKind.String);
    }

    private static UninstallEntry Entry(string command) =>
        new("id", "Msi App", "1.0", "Teknesyum", command, null, null, false, false, Location);
}
