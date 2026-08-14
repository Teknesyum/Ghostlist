using System.Reflection;
using System.Xml.Linq;
using Ghostlist.Cli;

namespace Ghostlist.Tests;

public class VersionTests
{
    private static XElement LoadProps()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Directory.Build.props");
            if (File.Exists(candidate))
            {
                return XDocument.Load(candidate).Root!.Element("PropertyGroup")!;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Directory.Build.props was not found above the test output directory.");
    }

    private static string PropsValue(string name) => LoadProps().Element(name)!.Value;

    [Fact]
    public void DirectoryBuildPropsDeclaresEveryVersionField()
    {
        var props = LoadProps();
        foreach (var name in new[] { "Version", "AssemblyVersion", "FileVersion", "Company", "Product", "Copyright" })
        {
            Assert.False(string.IsNullOrWhiteSpace(props.Element(name)?.Value), $"Directory.Build.props is missing <{name}>.");
        }
    }

    [Fact]
    public void CommandLineAssemblyCarriesThePropsVersion()
    {
        var assembly = typeof(Program).Assembly;
        Assert.Equal(PropsValue("AssemblyVersion"), assembly.GetName().Version!.ToString());
    }

    [Fact]
    public void CommandLineAssemblyCarriesThePropsFileVersion()
    {
        var assembly = typeof(Program).Assembly;
        var fileVersion = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()!.Version;
        Assert.Equal(PropsValue("FileVersion"), fileVersion);
    }

    [Fact]
    public void CommandLineAssemblyCarriesThePropsInformationalVersion()
    {
        var assembly = typeof(Program).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Assert.StartsWith(PropsValue("Version"), informational);
    }

    [Fact]
    public void CommandLineAssemblyCarriesThePropsCompanyAndProduct()
    {
        var assembly = typeof(Program).Assembly;
        Assert.Equal(PropsValue("Company"), assembly.GetCustomAttribute<AssemblyCompanyAttribute>()!.Company);
        Assert.Equal(PropsValue("Product"), assembly.GetCustomAttribute<AssemblyProductAttribute>()!.Product);
        Assert.Equal(PropsValue("Copyright"), assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()!.Copyright);
    }

    [Fact]
    public void CoreAssemblyCarriesTheSameVersion()
    {
        var core = typeof(Ghostlist.Core.CleanupService).Assembly;
        Assert.Equal(PropsValue("AssemblyVersion"), core.GetName().Version!.ToString());
    }
}
