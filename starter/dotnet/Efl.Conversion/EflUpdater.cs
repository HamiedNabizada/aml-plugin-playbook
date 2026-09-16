using Aml.Engine.CAEX;

namespace Efl.Conversion;

/// <summary>What an update changed, for the log and for a confirmation dialog.</summary>
public sealed record EflUpdateSummary(int Added, int Updated, int Removed)
{
    /// <summary>Things the caller should tell the user about, beyond the counts.</summary>
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();

    public override string ToString() => $"{Added} added, {Updated} updated, {Removed} removed";
}

/// <summary>
/// Brings an existing hierarchy in line with a model instead of replacing it.
///
/// This is the class that decides whether the plugin is usable on a document
/// somebody else also works on. Replacing the hierarchy would throw away every
/// attribute a user added, every link into another hierarchy and every foreign
/// element that shares the diagram. Matching by the language's own id and
/// writing only what the language owns keeps all of it.
///
/// Both connection encodings are supported. The style is read from the document
/// rather than chosen by the caller: a hierarchy written as links stays links.
/// </summary>
public static class EflUpdater
{
    public static EflUpdateSummary UpdateInPlace(
        CAEXDocument document, InstanceHierarchyType hierarchy, EflModel model, int diagramIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(hierarchy);
        ArgumentNullException.ThrowIfNull(model);

        var notes = new List<string>();

        // Everything that can fail is checked before the first write. An update
        // writes element by element, so a failure halfway leaves a document
        // that is part old and part new, and the next save stores exactly that.
        CheckFlowEnds(model);
        var classes = EflWrite.Classes(document);

        var diagrams = hierarchy.InternalElement.Where(e => CaexToEfl.ClassOf(e) == EflNames.Diagram).ToList();
        var diagram = diagramIndex >= 0 && diagramIndex < diagrams.Count ? diagrams[diagramIndex] : null;

        var added = 0;
        var updated = 0;
        var removed = 0;

        if (diagram == null)
        {
            diagram = EflWrite.CreateInstance(classes, EflNames.Diagram);
            diagram.ID = EflIds.DistinctElement(model.Id, new IdSpace(document));
            EflWrite.Append(hierarchy, diagram);
            EflWrite.SetDisplayName(diagram, model.Name, model.Id, created: true);
            added++;
        }
        else
        {
            EflWrite.SetDisplayName(diagram, model.Name, model.Id, created: false);
            updated++;
        }
        EflWrite.SetIdentification(diagram, model.Id, model.Name);

        var style = StyleOf(diagram);

        // Read before the first write: which elements the canvas never showed.
        // The model that comes back from the canvas cannot say that itself.
        var unresolved = CaexToEfl.UnresolvedIn(diagram);

        // The elements the document has, by the language's own id. Anything
        // without one, or of a foreign type, is not ours and stays untouched.
        var existing = new Dictionary<string, InternalElementType>(StringComparer.Ordinal);
        foreach (var child in diagram.InternalElement)
        {
            var kind = CaexToEfl.ClassOf(child);
            if (kind is not (EflNames.Step or EflNames.Store or EflNames.Flow)) continue;

            var id = child.Attribute[EflNames.Attributes.Identification]?
                .Attribute[EflNames.Attributes.Id]?.Value ?? child.Name;
            if (!string.IsNullOrEmpty(id)) existing[id!] = child;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ids = new IdSpace(document);
        var ports = new Dictionary<string, ExternalInterfaceType>(StringComparer.Ordinal);

        foreach (var node in model.Nodes)
        {
            var wanted = node.Kind == EflNodeKind.Step ? EflNames.Step : EflNames.Store;
            var element = Reuse(diagram, classes, existing, ids, node.Id, wanted, ref added, ref updated, out var created);
            seen.Add(node.Id);
            EflWrite.WriteNode(element, node, created);

            var keep = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (flow, outgoing) in EflToCaex.EndsAt(model, node.Id))
            {
                var portName = EflToCaex.PortName(flow, outgoing);
                var portClass = style == EflConnectionStyle.Link
                    ? EflNames.InterfaceClass(outgoing ? EflNames.FlowOut : EflNames.FlowIn)
                    : EflNames.InterfaceClass(EflNames.FlowEnd);

                var port = EflWrite.EnsureInterface(element, portName, EflIds.Interface(element.ID!, portName), portClass);
                ports[EflToCaex.PortKey(node.Id, flow.Id, outgoing)] = port;
                keep.Add(portName);
            }

            // Ports of flows the diagram no longer has, and the links that used
            // them. Ports of other classes belong to somebody else.
            // The ports of a flow the canvas could not show stay too: removing them
            // would cut the half of the flow that still exists and make it harder
            // to repair.
            foreach (var stale in element.ExternalInterface
                         .Where(i => i.Name != null && !keep.Contains(i.Name) && IsFlowPort(i)
                                     && !BelongsToUnresolved(i.Name, unresolved))
                         .ToList())
            {
                RemoveLinksTouching(document, diagram, stale.ID, notes, element.Name);
                stale.Remove();
            }
        }

        foreach (var flow in model.Flows)
        {
            var polyline = EflGeometry.Polyline(model, flow);

            if (style == EflConnectionStyle.Link)
            {
                UpdateFlowAsLink(diagram, flow, ports, polyline);
                continue;
            }

            var element = Reuse(diagram, classes, existing, ids, flow.Id, EflNames.Flow, ref added, ref updated, out var created);
            seen.Add(flow.Id);

            EflWrite.SetDisplayName(element, flow.Name, flow.Id, created);
            EflWrite.SetIdentification(element, flow.Id, flow.Name);
            EflWrite.ReplaceWaypoints(element, flow.Waypoints);

            var source = EflWrite.EnsureInterface(element, "Source",
                EflIds.Interface(element.ID!, "Source"), EflNames.InterfaceClass(EflNames.FlowSource));
            var target = EflWrite.EnsureInterface(element, "Target",
                EflIds.Interface(element.ID!, "Target"), EflNames.InterfaceClass(EflNames.FlowTarget));

            if (polyline.Count > 0) EflWrite.SetPortCoordinate(source, polyline[0]);
            if (polyline.Count > 1) EflWrite.SetPortCoordinate(target, polyline[^1]);

            if (ports.TryGetValue(EflToCaex.PortKey(flow.SourceId, flow.Id, outgoing: true), out var sourcePort))
                EflWrite.EnsureLink(diagram, element.Name + "_source", EflIds.Link(element.ID!, "source"), source, sourcePort);
            if (ports.TryGetValue(EflToCaex.PortKey(flow.TargetId, flow.Id, outgoing: false), out var targetPort))
                EflWrite.EnsureLink(diagram, element.Name + "_target", EflIds.Link(element.ID!, "target"), target, targetPort);
        }

        if (style == EflConnectionStyle.Link) RemoveStaleFlowLinks(diagram, model, notes);

        foreach (var (id, element) in existing)
        {
            if (seen.Contains(id)) continue;
            if (unresolved.Contains(id))
            {
                notes.Add($"Kept '{element.Name}': the diagram could not show it because its links do not resolve. Repair or delete it in the document.");
                continue;
            }
            foreach (var port in element.ExternalInterface.Select(i => i.ID).ToList())
                RemoveLinksTouching(document, diagram, port, notes, element.Name);
            element.Remove();
            removed++;
        }

        return new EflUpdateSummary(added, updated, removed) { Notes = notes };
    }

    private static bool BelongsToUnresolved(string portName, HashSet<string> unresolved) =>
        unresolved.Any(id => portName == "Out_" + id || portName == "In_" + id);

    /// <summary>
    /// Which encoding this hierarchy uses. Read from the document, so an
    /// update never changes the shape of a file it did not create.
    /// </summary>
    private static EflConnectionStyle StyleOf(InternalElementType diagram) =>
        diagram.InternalElement.Any(e => CaexToEfl.ClassOf(e) == EflNames.Flow)
            ? EflConnectionStyle.Element
            : EflConnectionStyle.Link;

    private static bool IsFlowPort(ExternalInterfaceType port)
    {
        var path = port.RefBaseClassPath ?? "";
        return path.EndsWith("/" + EflNames.FlowOut, StringComparison.Ordinal)
            || path.EndsWith("/" + EflNames.FlowIn, StringComparison.Ordinal)
            || path.EndsWith("/" + EflNames.FlowEnd, StringComparison.Ordinal);
    }

    private static void UpdateFlowAsLink(
        InternalElementType diagram, EflFlow flow,
        Dictionary<string, ExternalInterfaceType> ports, IReadOnlyList<EflPoint> polyline)
    {
        if (!ports.TryGetValue(EflToCaex.PortKey(flow.SourceId, flow.Id, outgoing: true), out var source)) return;
        if (!ports.TryGetValue(EflToCaex.PortKey(flow.TargetId, flow.Id, outgoing: false), out var target)) return;

        if (polyline.Count > 0) EflWrite.SetPortCoordinate(source, polyline[0]);
        if (polyline.Count > 1) EflWrite.SetPortCoordinate(target, polyline[^1]);
        EflWrite.ReplaceWaypoints(source, flow.Waypoints);

        EflWrite.EnsureLink(diagram, flow.Name ?? flow.Id, flow.Id, source, target);
    }

    /// <summary>Links of flows the model no longer has. Links to anything else are left alone.</summary>
    private static void RemoveStaleFlowLinks(InternalElementType diagram, EflModel model, List<string> notes)
    {
        var wanted = model.Flows.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var link in diagram.InternalLink.ToList())
        {
            if (string.IsNullOrEmpty(link.ID) || wanted.Contains(link.ID!)) continue;

            // Only links this mapper created carry an id derived that way, so a
            // link somebody else put here survives.
            if (!IsOurLink(diagram, link)) continue;

            notes.Add($"Removed the flow '{link.Name}': the diagram no longer has it.");
            link.Remove();
        }
    }

    private static bool IsOurLink(InternalElementType diagram, InternalLinkType link)
    {
        var ports = diagram.InternalElement
            .SelectMany(e => e.ExternalInterface)
            .Where(IsFlowPort)
            .Select(i => i.ID)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        var sideA = CaexToEfl.InterfaceIdOf(link.RefPartnerSideA);
        var sideB = CaexToEfl.InterfaceIdOf(link.RefPartnerSideB);
        return sideA != null && sideB != null && ports.Contains(sideA) && ports.Contains(sideB);
    }

    private static InternalElementType Reuse(
        InternalElementType diagram, Dictionary<string, SystemUnitFamilyType> classes,
        Dictionary<string, InternalElementType> existing, IdSpace ids,
        string modelId, string wantedClass, ref int added, ref int updated, out bool created)
    {
        created = true;

        if (existing.TryGetValue(modelId, out var element))
        {
            if (CaexToEfl.ClassOf(element) == wantedClass)
            {
                updated++;
                created = false;
                return element;
            }

            // The type changed, which the modeler cannot do but a hand edit
            // can. Replaced rather than reinterpreted.
            element.Remove();
            existing.Remove(modelId);
        }

        var fresh = EflWrite.CreateInstance(classes, wantedClass);
        fresh.ID = EflIds.DistinctElement(modelId, ids);
        EflWrite.Append(diagram, fresh);
        added++;
        return fresh;
    }

    /// <summary>
    /// Removes every link that used an interface, wherever in the document it
    /// lives. Scanning the whole document is deliberate: a link from another
    /// hierarchy into a deleted element would otherwise dangle. Every such
    /// removal is reported.
    /// </summary>
    private static void RemoveLinksTouching(
        CAEXDocument document, InternalElementType diagram, string? interfaceId, List<string> notes, string? elementName)
    {
        if (string.IsNullOrEmpty(interfaceId)) return;

        foreach (var (link, owner) in AllLinks(document).ToList())
        {
            if (CaexToEfl.InterfaceIdOf(link.RefPartnerSideA) != interfaceId
                && CaexToEfl.InterfaceIdOf(link.RefPartnerSideB) != interfaceId) continue;

            // Links the diagram owns are the language's own and go without a
            // word. A link somebody else placed elsewhere in the document is
            // removed too (it would dangle otherwise), but the user is told.
            // Compared by ID: Aml.Engine wrappers are not reference stable.
            if (owner.ID != diagram.ID)
                notes.Add($"Removed the link '{link.Name}' on '{owner.Name}': it pointed at '{elementName}', which the diagram no longer has.");

            link.Remove();
        }
    }

    private static IEnumerable<(InternalLinkType Link, InternalElementType Owner)> AllLinks(CAEXDocument document)
    {
        IEnumerable<InternalElementType> Descend(IEnumerable<InternalElementType> elements)
        {
            foreach (var element in elements)
            {
                yield return element;
                foreach (var nested in Descend(element.InternalElement)) yield return nested;
            }
        }

        foreach (var hierarchy in document.CAEXFile.InstanceHierarchy)
        foreach (var element in Descend(hierarchy.InternalElement))
        foreach (var link in element.InternalLink)
            yield return (link, element);
    }

    private static void CheckFlowEnds(EflModel model)
    {
        var nodes = model.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var flow in model.Flows)
        {
            if (nodes.Contains(flow.SourceId) && nodes.Contains(flow.TargetId)) continue;

            var missing = nodes.Contains(flow.SourceId) ? flow.TargetId : flow.SourceId;
            throw new ArgumentException(
                $"Flow '{flow.Id}' names the node '{missing}', which the model does not have. Nothing was written.",
                nameof(model));
        }
    }
}
