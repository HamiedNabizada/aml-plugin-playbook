using Aml.Engine.CAEX;

namespace Efl.Conversion;

/// <summary>
/// Writes a model into a CAEX document as a new InstanceHierarchy.
///
/// Shape of the result, with <see cref="EflConnectionStyle.Link"/>:
/// <code>
/// InstanceHierarchy
///   EFL_Diagram
///     EFL_Step / EFL_Store      one per node, with one EFL_FlowOut or EFL_FlowIn per incident flow
///     InternalLink              one per flow, joining the two interfaces
/// </code>
///
/// and with <see cref="EflConnectionStyle.Element"/>:
/// <code>
/// InstanceHierarchy
///   EFL_Diagram
///     EFL_Step / EFL_Store      one per node, with one EFL_FlowEnd per incident flow
///     EFL_Flow                  one per flow, with EFL_FlowSource and EFL_FlowTarget
///     InternalLink              two per flow, from the flow's ends to the nodes' ports
/// </code>
///
/// Use <see cref="EflUpdater"/> to refresh a hierarchy that already exists.
/// This class always builds from scratch.
/// </summary>
public static class EflToCaex
{
    public const string DefaultHierarchyName = "EFL_Diagrams";

    /// <param name="writtenAt">
    /// The time written into SourceDocumentInformation. Defaults to now. Pass a
    /// fixed value for files kept in a repository (examples, test fixtures), so
    /// writing them again with an unchanged mapper changes no byte and CI can
    /// check that they are current.
    /// </param>
    public static CAEXDocument Convert(
        EflModel model, string? hierarchyName = null, EflConnectionStyle style = EflConnectionStyle.Link,
        DateTime? writtenAt = null)
    {
        var document = CAEXDocument.New_CAEXDocument();
        document.CAEXFile.FileName = "efl-export.aml";

        EflDocuments.StampSource(document, "efl-aml-mapper", EflNames.LibraryVersion, writtenAt);

        AppendInto(document, model, hierarchyName, style);
        return document;
    }

    public static InstanceHierarchyType AppendInto(
        CAEXDocument document, EflModel model, string? hierarchyName = null,
        EflConnectionStyle style = EflConnectionStyle.Link)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);

        EflLibraries.EnsureLibraries(document.CAEXFile);

        var name = string.IsNullOrWhiteSpace(hierarchyName) ? DefaultHierarchyName : hierarchyName!;
        var hierarchy = document.CAEXFile.InstanceHierarchy.Append(name);
        hierarchy.ID = EflIds.For("hierarchy", name);

        var classes = EflWrite.Classes(document);
        var ids = new IdSpace(document);

        var diagram = EflWrite.CreateInstance(classes, EflNames.Diagram);
        diagram.ID = EflIds.DistinctElement(model.Id, ids);
        EflWrite.SetDisplayName(diagram, model.Name, model.Id, created: true);
        EflWrite.Append(hierarchy, diagram);
        EflWrite.SetIdentification(diagram, model.Id, model.Name);

        // The port on a node that belongs to one flow, by node and flow.
        var ports = new Dictionary<string, ExternalInterfaceType>(StringComparer.Ordinal);

        foreach (var node in model.Nodes)
        {
            var element = EflWrite.CreateInstance(
                classes, node.Kind == EflNodeKind.Step ? EflNames.Step : EflNames.Store);
            element.ID = EflIds.DistinctElement(node.Id, ids);
            EflWrite.Append(diagram, element);
            EflWrite.WriteNode(element, node, created: true);

            foreach (var (flow, outgoing) in EndsAt(model, node.Id))
            {
                var key = PortKey(node.Id, flow.Id, outgoing);
                if (ports.ContainsKey(key)) continue;

                var portName = PortName(flow, outgoing);
                var portClass = style == EflConnectionStyle.Link
                    ? EflNames.InterfaceClass(outgoing ? EflNames.FlowOut : EflNames.FlowIn)
                    : EflNames.InterfaceClass(EflNames.FlowEnd);

                ports[key] = EflWrite.EnsureInterface(
                    element, portName, EflIds.Interface(element.ID!, portName), portClass);
            }
        }

        foreach (var flow in model.Flows)
        {
            var polyline = EflGeometry.Polyline(model, flow);

            if (style == EflConnectionStyle.Link)
            {
                WriteFlowAsLink(diagram, flow, ports, polyline);
            }
            else
            {
                WriteFlowAsElement(diagram, classes, ids, flow, ports, polyline);
            }
        }

        return hierarchy;
    }

    /// <summary>
    /// The flow as a single InternalLink between the two nodes. The link has a
    /// name and an id, and nothing else: an encoding for connections that carry
    /// no attributes of their own.
    /// </summary>
    private static void WriteFlowAsLink(
        InternalElementType diagram, EflFlow flow,
        Dictionary<string, ExternalInterfaceType> ports, IReadOnlyList<EflPoint> polyline)
    {
        if (!ports.TryGetValue(PortKey(flow.SourceId, flow.Id, outgoing: true), out var source)) return;
        if (!ports.TryGetValue(PortKey(flow.TargetId, flow.Id, outgoing: false), out var target)) return;

        if (polyline.Count > 0) EflWrite.SetPortCoordinate(source, polyline[0]);
        if (polyline.Count > 1) EflWrite.SetPortCoordinate(target, polyline[^1]);

        // The bend points go onto the source side interface as Waypoint_1..n,
        // which is where the FPD library puts them: an InternalLink has no
        // attributes of its own, and the two docking points are already on the
        // two PortCoordinate attributes.
        EflWrite.ReplaceWaypoints(source, flow.Waypoints);

        // The flow's own id becomes the link's id, the way the FPD library
        // does it: a link has no Identification attribute, and its id is the
        // only place an identity can live. Its name is the label, or the id
        // again when the model gives no name.
        EflWrite.EnsureLink(diagram, flow.Name ?? flow.Id, flow.Id, source, target);
    }

    /// <summary>
    /// The flow as an element of its own with two interfaces, joined to the
    /// nodes by two links. Everything a connection can carry has a place here:
    /// an id, a name, attributes and a route.
    /// </summary>
    private static void WriteFlowAsElement(
        InternalElementType diagram, Dictionary<string, SystemUnitFamilyType> classes, IdSpace ids,
        EflFlow flow, Dictionary<string, ExternalInterfaceType> ports, IReadOnlyList<EflPoint> polyline)
    {
        var element = EflWrite.CreateInstance(classes, EflNames.Flow);
        element.ID = EflIds.DistinctElement(flow.Id, ids);
        EflWrite.Append(diagram, element);
        EflWrite.SetDisplayName(element, flow.Name, flow.Id, created: true);
        EflWrite.SetIdentification(element, flow.Id, flow.Name);
        EflWrite.ReplaceWaypoints(element, flow.Waypoints);

        var source = EflWrite.EnsureInterface(element, "Source",
            EflIds.Interface(element.ID!, "Source"), EflNames.InterfaceClass(EflNames.FlowSource));
        var target = EflWrite.EnsureInterface(element, "Target",
            EflIds.Interface(element.ID!, "Target"), EflNames.InterfaceClass(EflNames.FlowTarget));

        if (polyline.Count > 0) EflWrite.SetPortCoordinate(source, polyline[0]);
        if (polyline.Count > 1) EflWrite.SetPortCoordinate(target, polyline[^1]);

        if (ports.TryGetValue(PortKey(flow.SourceId, flow.Id, outgoing: true), out var sourcePort))
            EflWrite.EnsureLink(diagram, element.Name + "_source", EflIds.Link(element.ID!, "source"), source, sourcePort);

        if (ports.TryGetValue(PortKey(flow.TargetId, flow.Id, outgoing: false), out var targetPort))
            EflWrite.EnsureLink(diagram, element.Name + "_target", EflIds.Link(element.ID!, "target"), target, targetPort);
    }

    /// <summary>
    /// The name of a node's port. A node carries one port per incident flow,
    /// named after the flow, so two flows between the same pair of nodes stay
    /// apart.
    /// </summary>
    internal static string PortName(EflFlow flow, bool outgoing) => (outgoing ? "Out_" : "In_") + flow.Id;

    /// <summary>
    /// The port of one flow end on one node. The direction is part of the key:
    /// a flow from a node to itself has both ends on the same node, and without
    /// it both links attached to a single port.
    /// </summary>
    internal static string PortKey(string nodeId, string flowId, bool outgoing) =>
        nodeId + "|" + flowId + (outgoing ? "|out" : "|in");

    /// <summary>
    /// Every flow end at a node, with its direction. A self loop yields two
    /// ends, one outgoing and one incoming.
    /// </summary>
    internal static IEnumerable<(EflFlow Flow, bool Outgoing)> EndsAt(EflModel model, string nodeId)
    {
        foreach (var flow in model.Flows)
        {
            if (flow.SourceId == nodeId) yield return (flow, true);
            if (flow.TargetId == nodeId) yield return (flow, false);
        }
    }
}
