using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Aml.Editor.Plugin.Efl.Tests;

/// <summary>
/// Checks the package the editor installs, before anybody installs it.
///
/// Each check stands for a failure that is silent inside the editor: the plugin
/// does not show up, or it shows up with an empty panel, and nothing in any log
/// says why. Finding those by hand costs an install, a restart and a guess each
/// time.
/// </summary>
public class PackageTests
{
    private const string Framework = "lib/net8.0-windows7.0/";

    private static string PluginFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Aml.Editor.Plugin.Efl");
            if (File.Exists(Path.Combine(candidate, "Aml.Editor.Plugin.Efl.csproj"))) return candidate;
        }
        throw new DirectoryNotFoundException("Aml.Editor.Plugin.Efl was not found above " + AppContext.BaseDirectory);
    }

    private static string ProjectVersion() =>
        XDocument.Load(Path.Combine(PluginFolder(), "Aml.Editor.Plugin.Efl.csproj"))
            .Descendants("Version").First().Value;

    /// <summary>The newest package of the current version, from whichever configuration built last.</summary>
    private static List<string> PackageEntries()
    {
        var build = Path.Combine(PluginFolder(), "..", "build", "Aml.Editor.Plugin.Efl");
        var package = Directory.GetFiles(build, $"Aml.Editor.Plugin.Efl.{ProjectVersion()}.nupkg", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault()
            ?? throw new FileNotFoundException("No package for version " + ProjectVersion() + " under " + build);

        using var zip = ZipFile.OpenRead(package);
        return zip.Entries.Select(e => e.FullName).ToList();
    }

    [Fact]
    public void ThePackageDoesNotShipTheContractAssembly()
    {
        // The editor has its own copy. A second one next to the plugin makes the
        // editor skip the plugin without a word.
        Assert.DoesNotContain(PackageEntries(), e => e.EndsWith("Aml.Editor.Plugin.Contract.dll", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ThePackageDoesNotShipWhatTheEditorAlreadyLoads()
    {
        // Aml.Engine in two versions in one process fails with type mismatches
        // that name the same type twice.
        Assert.DoesNotContain(PackageEntries(), e => e.EndsWith("/Aml.Engine.dll", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Aml.Editor.Plugin.Efl.dll")]
    [InlineData("Efl.Conversion.dll")]
    [InlineData("Microsoft.Web.WebView2.Core.dll")]
    [InlineData("Microsoft.Web.WebView2.Wpf.dll")]
    [InlineData("WebView2Loader.dll")]
    [InlineData("efl-assets/index.html")]
    [InlineData("efl-assets/efl.esm.js")]
    [InlineData("efl-assets/efl.css")]
    public void ThePackageShipsEverythingThePluginNeedsAtRuntime(string file)
    {
        // A missing mapper assembly fails on the first click; a missing bundle is
        // an empty panel.
        Assert.Contains(Framework + file, PackageEntries());
    }

    [Fact]
    public void ThePackageCarriesTheThirdPartyNotices()
    {
        // The bundled modeler code and WebView2 are redistributed; their
        // licenses require the notices in the package.
        Assert.Contains("THIRD-PARTY-NOTICES.md", PackageEntries());
    }

    [Fact]
    public void MetadataAndProjectAgreeOnTheVersion()
    {
        var metadata = XDocument.Load(Path.Combine(PluginFolder(), "Metadata.xml"));
        Assert.Equal(ProjectVersion(), metadata.Descendants("Version").Single().Value);
    }

    [Fact]
    public void TheDisplayNameIsUsableAsAnXmlAndWpfName()
    {
        // The editor turns it into an x:Name and an XML element name.
        var metadata = XDocument.Load(Path.Combine(PluginFolder(), "Metadata.xml"));
        var shown = metadata.Descendants("DisplayName").Single().Value;
        Assert.Matches(new Regex("^[A-Za-z_][A-Za-z0-9_]*$"), shown);

        var code = File.ReadAllText(Path.Combine(PluginFolder(), "EflPlugin.xaml.cs"));
        Assert.Contains($"DisplayName = \"{shown}\";", code);
    }
}
