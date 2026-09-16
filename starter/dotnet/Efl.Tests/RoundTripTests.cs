using System.Xml.Linq;
using Aml.Engine.CAEX;
using Efl.Conversion;
using Xunit;

namespace Efl.Tests;

/// <summary>
/// The test that decides whether the mapper works: a model that goes into a
/// document and comes back has to be the same model, in both encodings.
///
/// Write these first. Every defect that matters shows up here, and a failure
/// names the property that broke.
/// </summary>
public class RoundTripTests
{
    /// <summary>
    /// Two steps, one store, three flows, one of them with a bend point. Small
    /// enough to reason about, large enough to have a fork and a join.
    /// </summary>
    internal static EflModel Sample()
    {
        var model = new EflModel { Id = "line", Name = "Bottling line" };
        model.Nodes.Add(new EflNode { Id = "fill", Name = "Fill", Kind = EflNodeKind.Step, Value = 12, Bounds = new EflBounds(0, 0, 100, 60) });
        model.Nodes.Add(new EflNode { Id = "buffer", Name = "Buffer", Kind = EflNodeKind.Store, Value = 20, Bounds = new EflBounds(200, 0, 100, 60) });
        model.Nodes.Add(new EflNode { Id = "cap", Name = "Cap", Kind = EflNodeKind.Step, Value = 5, Bounds = new EflBounds(400, 0, 100, 60) });

        model.Flows.Add(new EflFlow { Id = "f1", Name = "Fill_to_Buffer", SourceId = "fill", TargetId = "buffer" });
        model.Flows.Add(new EflFlow { Id = "f2", Name = "Buffer_to_Cap", SourceId = "buffer", TargetId = "cap" });

        var back = new EflFlow { Id = "f3", Name = "Cap_to_Fill", SourceId = "cap", TargetId = "fill" };
        back.Waypoints.Add(new EflPoint(450, 200));
        back.Waypoints.Add(new EflPoint(50, 200));
        model.Flows.Add(back);

        return model;
    }

    private static List<string> Describe(EflModel model) =>
        model.Nodes.Select(n => $"node {n.Id} {n.Kind} '{n.Name}' value={n.Value} bounds={n.Bounds}")
            .Concat(model.Flows.Select(f =>
                $"flow {f.Id} '{f.Name}' {f.SourceId}->{f.TargetId} " +
                string.Join(" ", f.Waypoints.Select(p => $"{p.X},{p.Y}"))))
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToList();

    [Theory]
    [InlineData(EflConnectionStyle.Link)]
    [InlineData(EflConnectionStyle.Element)]
    public void AModelSurvivesTheWayIntoAmlAndBack(EflConnectionStyle style)
    {
        var model = Sample();

        var document = EflToCaex.Convert(model, "Line", style);
        var back = Assert.Single(CaexToEfl.Read(document.CAEXFile.InstanceHierarchy["Line"]!));

        Assert.Empty(back.Warnings);
        Assert.Equal(Describe(model), Describe(back));
    }

    [Theory]
    [InlineData(EflConnectionStyle.Link)]
    [InlineData(EflConnectionStyle.Element)]
    public void WritingTheSameModelBackChangesNoLineOfTheDocument(EflConnectionStyle style)
    {
        // The property a plugin lives on: opening a document and syncing it
        // without editing anything has to leave the file alone. Compared as
        // XML text, not property by property, so nothing can hide.
        var document = EflToCaex.Convert(Sample(), "Line", style);
        var hierarchy = document.CAEXFile.InstanceHierarchy["Line"]!;
        var before = document.CAEXFile.Node.ToString();

        var read = CaexToEfl.Read(hierarchy).Single();
        EflUpdater.UpdateInPlace(document, hierarchy, read);

        Assert.Equal(before, document.CAEXFile.Node.ToString());
    }

    [Theory]
    [InlineData(EflConnectionStyle.Link)]
    [InlineData(EflConnectionStyle.Element)]
    public void TheJsonFormatCarriesTheSameModel(EflConnectionStyle style)
    {
        var model = Sample();

        var json = EflJson.Write(model);
        var fromJson = EflJson.Read(json);
        var document = EflToCaex.Convert(fromJson, "Line", style);
        var back = CaexToEfl.Read(document.CAEXFile.InstanceHierarchy["Line"]!).Single();

        Assert.Equal(Describe(model), Describe(back));
    }

    [Fact]
    public void AnUpdateKeepsWhatSomebodyElseAddedToTheDocument()
    {
        var document = EflToCaex.Convert(Sample(), "Line");
        var hierarchy = document.CAEXFile.InstanceHierarchy["Line"]!;
        var diagram = hierarchy.InternalElement.Single();

        // An attribute of another tool on one of our elements, and an element
        // of another tool next to ours.
        var step = diagram.InternalElement.First(e => e.Name == "Fill");
        step.Attribute.Append("Owner").Value = "Maintenance";
        var foreign = hierarchy.InternalElement.Append("PlantData");

        var model = Sample();
        model.Nodes.First(n => n.Id == "fill").Name = "Filling";
        EflUpdater.UpdateInPlace(document, hierarchy, model);

        Assert.Equal("Maintenance", step.Attribute["Owner"]?.Value);
        Assert.NotNull(hierarchy.InternalElement[foreign.Name]);
    }

    [Fact]
    public void RemovingANodeRemovesItsFlowsAndNothingElse()
    {
        var document = EflToCaex.Convert(Sample(), "Line");
        var hierarchy = document.CAEXFile.InstanceHierarchy["Line"]!;

        var reduced = Sample();
        reduced.Nodes.RemoveAll(n => n.Id == "cap");
        reduced.Flows.RemoveAll(f => f.SourceId == "cap" || f.TargetId == "cap");

        var summary = EflUpdater.UpdateInPlace(document, hierarchy, reduced);
        var back = CaexToEfl.Read(hierarchy).Single();

        Assert.Equal(1, summary.Removed);
        Assert.Equal(2, back.Nodes.Count);
        Assert.Single(back.Flows);
        Assert.Empty(back.Warnings);
    }

    [Theory]
    [InlineData(EflConnectionStyle.Link)]
    [InlineData(EflConnectionStyle.Element)]
    public void LinksWrittenAsElementAndInterfaceIdAreReadToo(EflConnectionStyle style)
    {
        // Another tool, or an older CAEX version, writes "ElementId:InterfaceId".
        var document = EflToCaex.Convert(Sample(), "Line", style);
        var hierarchy = document.CAEXFile.InstanceHierarchy["Line"]!;
        var diagram = hierarchy.InternalElement.Single();
        // Both encodings keep every link on the diagram element.
        var ownerOf = diagram.InternalElement
            .SelectMany(e => e.ExternalInterface.Select(i => (Interface: i.ID, Element: e.ID)))
            .ToDictionary(x => x.Interface!, x => x.Element);
        foreach (var link in diagram.InternalLink)
        {
            link.RefPartnerSideA = ownerOf[link.RefPartnerSideA!] + ":" + link.RefPartnerSideA;
            link.RefPartnerSideB = ownerOf[link.RefPartnerSideB!] + ":" + link.RefPartnerSideB;
        }

        var back = CaexToEfl.Read(hierarchy).Single();
        Assert.Empty(back.Warnings);
        Assert.Equal(Describe(Sample()), Describe(back));

        // And an update neither duplicates nor drops those links.
        var linksBefore = diagram.InternalLink.Count();
        var summary = EflUpdater.UpdateInPlace(document, hierarchy, Sample());
        Assert.Equal(0, summary.Removed);
        Assert.Equal(linksBefore, diagram.InternalLink.Count());
    }

    [Fact]
    public void AnUpdateKeepsAFlowTheCanvasCouldNotShow()
    {
        // A flow element whose link was deleted by hand: the reader leaves it off
        // the canvas with a warning. The canvas then sends a model without it,
        // which must not count as "the user deleted this flow".
        var document = EflToCaex.Convert(Sample(), "Line", EflConnectionStyle.Element);
        var hierarchy = document.CAEXFile.InstanceHierarchy["Line"]!;
        var diagram = hierarchy.InternalElement.Single();
        diagram.InternalLink.Single(l => l.Name == "Cap_to_Fill_target").Remove();

        var shown = CaexToEfl.Read(hierarchy).Single();
        Assert.Contains("f3", shown.Unresolved);
        Assert.DoesNotContain(shown.Flows, f => f.Id == "f3");

        var summary = EflUpdater.UpdateInPlace(document, hierarchy, EflJson.Read(EflJson.Write(shown)));

        Assert.Equal(0, summary.Removed);
        Assert.Contains(diagram.InternalElement, e => e.Name == "Cap_to_Fill");
        Assert.Contains(summary.Notes, n => n.Contains("Cap_to_Fill"));

        // The half that still exists keeps its port on the node.
        var fill = diagram.InternalElement.Single(e => e.Name == "Fill");
        Assert.Contains(fill.ExternalInterface, i => i.Name == "In_f3");
    }

    [Theory]
    [InlineData(EflConnectionStyle.Link)]
    [InlineData(EflConnectionStyle.Element)]
    public void AFlowFromANodeToItselfGetsTwoPortsAndSurvivesTheRoundTrip(EflConnectionStyle style)
    {
        // The validator forbids this in EFL (EFL04), but many languages allow
        // it, and the mapper must not be the part that breaks it.
        var model = Sample();
        var loop = new EflFlow { Id = "f4", Name = "Rework", SourceId = "cap", TargetId = "cap" };
        loop.Waypoints.Add(new EflPoint(530, 10));
        loop.Waypoints.Add(new EflPoint(530, 50));
        model.Flows.Add(loop);

        var document = EflToCaex.Convert(model, "Line", style);
        var hierarchy = document.CAEXFile.InstanceHierarchy["Line"]!;
        var cap = hierarchy.InternalElement.Single().InternalElement.Single(e => e.Name == "Cap");

        Assert.Contains(cap.ExternalInterface, i => i.Name == "Out_f4");
        Assert.Contains(cap.ExternalInterface, i => i.Name == "In_f4");
        Assert.Equal(Describe(model), Describe(CaexToEfl.Read(hierarchy).Single()));

        var before = document.CAEXFile.Node.ToString();
        EflUpdater.UpdateInPlace(document, hierarchy, model);
        Assert.Equal(before, document.CAEXFile.Node.ToString());
    }

    [Fact]
    public void TheSameModelAlwaysProducesTheSameIds()
    {
        // Without this an update replaces every element instead of touching
        // the ones that changed.
        var first = EflToCaex.Convert(Sample(), "Line");
        var second = EflToCaex.Convert(Sample(), "Line");

        static IEnumerable<string> Ids(CAEXDocument document) =>
            document.CAEXFile.Node.DescendantsAndSelf().Attributes("ID").Select(a => a.Value).Order();

        Assert.Equal(Ids(first), Ids(second));
    }

    [Fact]
    public void AFlowToANodeThatIsNotThereIsRefusedBeforeAnythingIsWritten()
    {
        var document = EflToCaex.Convert(Sample(), "Line");
        var hierarchy = document.CAEXFile.InstanceHierarchy["Line"]!;
        var before = document.CAEXFile.Node.ToString();

        var broken = Sample();
        broken.Flows.Add(new EflFlow { Id = "f4", SourceId = "fill", TargetId = "ghost" });

        Assert.Throws<ArgumentException>(() => EflUpdater.UpdateInPlace(document, hierarchy, broken));
        Assert.Equal(before, document.CAEXFile.Node.ToString());
    }
}
