using System.Xml.Linq;
using Aml.Engine.CAEX;
using Efl.Conversion;
using Xunit;

namespace Efl.Tests;

/// <summary>
/// The shared layout library (OMG_DD_AttributeTypeLib) in the documents the
/// mapper writes.
///
/// What these guard is invisible in every round trip test: a type path that
/// resolves to nothing still reads back fine, because the reader goes by
/// attribute names. It only shows in the AutomationML Editor, as unresolved
/// attributes, which is exactly what embedding the library is meant to prevent.
/// </summary>
public class DiagramInterchangeTests
{
    /// <summary>Every RefAttributeType that points into OMG_DD, and whether the document can resolve it.</summary>
    private static List<string> UnresolvedLayoutTypes(CAEXDocument document)
    {
        var caex = document.CAEXFile;
        var inline = caex.AttributeTypeLib[EflDiagramInterchange.LibName];
        var aliases = caex.ExternalReference.Select(r => r.Alias + "@").ToList();

        return caex.Node.Descendants()
            .Attributes("RefAttributeType")
            .Select(a => a.Value)
            .Where(v => v.Contains(EflDiagramInterchange.LibName + "/", StringComparison.Ordinal))
            .Where(v =>
            {
                var aliased = aliases.FirstOrDefault(a => v.StartsWith(a, StringComparison.Ordinal));
                if (aliased != null) return false; // resolved through a reference the document declares
                var type = v[(v.LastIndexOf('/') + 1)..];
                return inline?.AttributeType[type] == null;
            })
            .Distinct()
            .ToList();
    }

    [Theory]
    [InlineData(EflConnectionStyle.Link)]
    [InlineData(EflConnectionStyle.Element)]
    public void ADocumentTheMapperCreatesCarriesTheLayoutLibraryAndResolvesEveryLayoutType(EflConnectionStyle style)
    {
        var document = EflToCaex.Convert(RoundTripTests.Sample(), "Line", style);

        Assert.NotNull(document.CAEXFile.AttributeTypeLib[EflDiagramInterchange.LibName]);
        Assert.DoesNotContain(document.CAEXFile.ExternalReference, r => r.Alias == EflDiagramInterchange.Alias);
        Assert.Empty(UnresolvedLayoutTypes(document));

        // Positions, docking points and bend points all use the shared types.
        var xml = document.CAEXFile.Node.ToString();
        Assert.Contains("RefAttributeType=\"OMG_DD_AttributeTypeLib/DD_Bounds\"", xml);
        Assert.Contains("RefAttributeType=\"OMG_DD_AttributeTypeLib/DD_Point\"", xml);
        Assert.Contains("RefAttributeType=\"OMG_DD_AttributeTypeLib/DD_Waypoint\"", xml);
    }

    [Theory]
    [InlineData(EflConnectionStyle.Link)]
    [InlineData(EflConnectionStyle.Element)]
    public void ADocumentTheMapperCreatesIsValidAgainstTheCaexSchema(EflConnectionStyle style)
    {
        // The library is added as XML; its place among the other libraries has
        // to be one the schema allows.
        var document = EflDocuments.Load(EflDocuments.ToXml(EflToCaex.Convert(RoundTripTests.Sample(), "Line", style)));

        var valid = document.Validate(out var errors);
        Assert.True(valid, string.Join(Environment.NewLine, errors ?? Array.Empty<string>()));
    }

    [Fact]
    public void TheSchemaCheckRejectsALibraryInAPlaceTheSchemaDoesNotAllow()
    {
        // Negative control for the test above: without it, a Validate that
        // accepted everything would look the same.
        var document = EflToCaex.Convert(RoundTripTests.Sample(), "Line");
        var library = document.CAEXFile.Node.Elements().Single(e => (string?)e.Attribute("Name") == EflDiagramInterchange.LibName);
        library.Remove();
        document.CAEXFile.Node.Elements().First(e => e.Name.LocalName == "InstanceHierarchy").AddBeforeSelf(library);

        var reloaded = EflDocuments.Load(document.CAEXFile.Node.Document!.ToString());
        Assert.False(reloaded.Validate(out _));
    }

    [Fact]
    public void TheEmbeddedLibraryIsThePublishedFile()
    {
        // One copy of the file, in starter/libraries; the assembly carries it.
        var published = Path.Combine(
            Path.GetDirectoryName(ExampleAndGeometryTests.ExampleFile("bottling-line.json"))!,
            "..", "libraries", EflDiagramInterchange.FileName);

        Assert.Equal(File.ReadAllText(published).ReplaceLineEndings(), EflDiagramInterchange.Xml.ReplaceLineEndings());
    }

    [Fact]
    public void TheLibraryArtefactReferencesTheLayoutLibraryInsteadOfCarryingIt()
    {
        var artefact = EflLibraries.CreateArtefact();

        Assert.Null(artefact.CAEXFile.AttributeTypeLib[EflDiagramInterchange.LibName]);
        var reference = Assert.Single(artefact.CAEXFile.ExternalReference, r => r.Alias == EflDiagramInterchange.Alias);
        Assert.Equal(EflDiagramInterchange.FileName, reference.Path);
        Assert.Contains("RefAttributeType=\"OMG_DD@OMG_DD_AttributeTypeLib/DD_Bounds\"", artefact.CAEXFile.Node.ToString());
        Assert.Empty(UnresolvedLayoutTypes(artefact));
    }

    [Fact]
    public void AnUpdateFollowsADocumentThatReferencesTheLayoutLibraryUnderItsOwnAlias()
    {
        // Somebody else's document: the layout library referenced as "DD", not
        // carried. An update must write "DD@..." and must not embed a second copy.
        var document = CAEXDocument.New_CAEXDocument();
        var reference = document.CAEXFile.ExternalReference.Append();
        reference.Alias = "DD";
        reference.Path = "libs/" + EflDiagramInterchange.FileName;
        var hierarchy = document.CAEXFile.InstanceHierarchy.Append("Line");
        EflLibraries.EnsureLibraries(document.CAEXFile);

        EflUpdater.UpdateInPlace(document, hierarchy, RoundTripTests.Sample());

        Assert.Null(document.CAEXFile.AttributeTypeLib[EflDiagramInterchange.LibName]);
        var xml = document.CAEXFile.Node.ToString();
        Assert.Contains("RefAttributeType=\"DD@OMG_DD_AttributeTypeLib/DD_Bounds\"", xml);
        Assert.DoesNotContain("RefAttributeType=\"OMG_DD_AttributeTypeLib/", xml);
        Assert.Empty(UnresolvedLayoutTypes(document));
    }
}
