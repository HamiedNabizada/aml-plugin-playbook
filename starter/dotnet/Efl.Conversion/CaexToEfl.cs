using System.Globalization;
using Aml.Engine.CAEX;

namespace Efl.Conversion;

/// <summary>
/// Reads models back out of a CAEX document.
///
/// Deliberately tolerant. The document may have been edited by hand between two
/// visits, so an element is recognised by its class path or by its role
/// requirement, a missing Identification falls back to the CAEX name, and both
/// connection encodings are read. What cannot be read is reported in
/// <see cref="EflModel.Warnings"/> instead of throwing: a reader that throws
/// turns one odd element into an unusable document.
/// </summary>
public static class CaexToEfl
{
    /// <summary>Every EFL diagram in the document, in document order.</summary>
    public static List<EflModel> ReadAll(CAEXDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.CAEXFile.InstanceHierarchy.SelectMany(Read).ToList();
    }

    /// <summary>The diagrams of one instance hierarchy.</summary>
    public static List<EflModel> Read(InstanceHierarchyType hierarchy)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);
        return hierarchy.InternalElement.Where(IsDiagram).Select(ReadDiagram).ToList();
    }

    public static bool ContainsDiagram(InstanceHierarchyType hierarchy) => hierarchy.InternalElement.Any(IsDiagram);

    /// <summary>What reading this diagram leaves off the canvas; see <see cref="EflModel.Unresolved"/>.</summary>
    internal static HashSet<string> UnresolvedIn(InternalElementType diagram) => ReadDiagram(diagram).Unresolved;

    private static EflModel ReadDiagram(InternalElementType diagram)
    {
        var model = new EflModel
        {
            Id = IdentificationId(diagram) ?? diagram.Name ?? "diagram",
            Name = IdentificationName(diagram),
        };

        // Interface id to the node that owns it, so the links below resolve.
        var owner = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var child in diagram.InternalElement)
        {
            var kind = ClassOf(child);
            if (kind is not (EflNames.Step or EflNames.Store)) continue;

            var id = IdentificationId(child) ?? child.Name;
            if (string.IsNullOrEmpty(id)) continue;

            model.Nodes.Add(new EflNode
            {
                Id = id,
                Name = IdentificationName(child),
                Kind = kind == EflNames.Step ? EflNodeKind.Step : EflNodeKind.Store,
                Value = Number(child, kind == EflNames.Step ? EflNames.Attributes.Duration : EflNames.Attributes.Capacity),
                Bounds = ReadBounds(child),
            });

            foreach (var port in child.ExternalInterface)
                if (!string.IsNullOrEmpty(port.ID)) owner[port.ID!] = id;
        }

        ReadFlowElements(diagram, owner, model);
        ReadFlowLinks(diagram, owner, model);

        return model;
    }

    /// <summary>The reified encoding: a flow is an element with two interfaces.</summary>
    private static void ReadFlowElements(
        InternalElementType diagram, Dictionary<string, string> owner, EflModel model)
    {
        var flowElements = diagram.InternalElement.Where(e => ClassOf(e) == EflNames.Flow).ToList();
        if (flowElements.Count == 0) return;

        var links = LinksBelow(diagram).ToList();

        foreach (var element in flowElements)
        {
            var id = IdentificationId(element) ?? element.Name;
            if (string.IsNullOrEmpty(id)) continue;

            string? Resolve(string interfaceClass)
            {
                var end = element.ExternalInterface.FirstOrDefault(i => IsClass(i.RefBaseClassPath, interfaceClass));
                if (end?.ID == null) return null;

                foreach (var link in links)
                {
                    var sideA = InterfaceIdOf(link.RefPartnerSideA);
                    var sideB = InterfaceIdOf(link.RefPartnerSideB);
                    var other = sideA == end.ID ? sideB
                        : sideB == end.ID ? sideA
                        : null;
                    if (other != null && owner.TryGetValue(other, out var node)) return node;
                }
                return null;
            }

            var source = Resolve(EflNames.FlowSource);
            var target = Resolve(EflNames.FlowTarget);
            if (source == null || target == null)
            {
                // Kept as a warning rather than dropped in silence: an update
                // that took silence for "deleted in the diagram" would remove
                // the element from the document next.
                model.Warnings.Add($"Flow '{element.Name}' is not shown: its links do not resolve to two nodes.");
                model.Unresolved.Add(id);
                continue;
            }

            var flow = new EflFlow { Id = id, Name = IdentificationName(element), SourceId = source, TargetId = target };
            flow.Waypoints.AddRange(ReadWaypoints(element));
            model.Flows.Add(flow);
        }
    }

    /// <summary>The link encoding: a flow is an InternalLink between two node interfaces.</summary>
    private static void ReadFlowLinks(
        InternalElementType diagram, Dictionary<string, string> owner, EflModel model)
    {
        foreach (var link in LinksBelow(diagram))
        {
            var a = InterfaceIdOf(link.RefPartnerSideA);
            var b = InterfaceIdOf(link.RefPartnerSideB);
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) continue;
            if (!owner.TryGetValue(a!, out var nodeA) || !owner.TryGetValue(b!, out var nodeB)) continue;

            // Direction comes from the interface classes, so a reader does not
            // have to follow the link to know which end is which.
            var portA = Port(diagram, a!);
            var portB = Port(diagram, b!);
            var outgoing = portA != null && IsClass(portA.RefBaseClassPath, EflNames.FlowOut);
            if (!outgoing && !(portB != null && IsClass(portB.RefBaseClassPath, EflNames.FlowOut))) continue;

            var source = outgoing ? nodeA : nodeB;
            var target = outgoing ? nodeB : nodeA;
            var sourcePort = outgoing ? portA : portB;

            var id = NonEmpty(link.ID) ?? NonEmpty(link.Name) ?? source + "_" + target;
            var flow = new EflFlow
            {
                Id = id,
                // The name is the label. A link named after its own id carries
                // no label, so that comes back as no name rather than as a name
                // that the user never typed.
                Name = link.Name == id ? null : NonEmpty(link.Name),
                SourceId = source,
                TargetId = target,
            };
            if (sourcePort != null) flow.Waypoints.AddRange(ReadWaypoints(sourcePort));
            model.Flows.Add(flow);
        }
    }

    private static ExternalInterfaceType? Port(InternalElementType diagram, string id) =>
        diagram.InternalElement
            .SelectMany(e => e.ExternalInterface)
            .FirstOrDefault(i => i.ID == id);

    /// <summary>
    /// The interface id a link side points at. Files from other tools write
    /// either the bare interface id (CAEX 3.0) or "ElementId:InterfaceId"
    /// (the CAEX 2.15 form); reading only one of them loses every connection of
    /// the other kind. GUIDs contain no colon, so the part after the last one is
    /// the interface.
    /// </summary>
    internal static string? InterfaceIdOf(string? side)
    {
        if (string.IsNullOrEmpty(side)) return side;
        var colon = side.LastIndexOf(':');
        return colon >= 0 ? side[(colon + 1)..] : side;
    }

    private static IEnumerable<InternalLinkType> LinksBelow(InternalElementType element)
    {
        foreach (var link in element.InternalLink) yield return link;
        foreach (var child in element.InternalElement)
        foreach (var link in LinksBelow(child))
            yield return link;
    }

    // ── typing ──────────────────────────────────────────────────────────────

    private static bool IsDiagram(InternalElementType element) => ClassOf(element) == EflNames.Diagram;

    /// <summary>
    /// Which EFL class an element belongs to, from the system unit class path
    /// and falling back to the role requirement. Null for anything that is not
    /// EFL content, so foreign elements in the same hierarchy are skipped.
    /// </summary>
    internal static string? ClassOf(InternalElementType element)
    {
        foreach (var candidate in new[] { EflNames.Diagram, EflNames.Step, EflNames.Store, EflNames.Flow })
        {
            if (IsClass(element.RefBaseSystemUnitPath, candidate)) return candidate;
            if (element.RoleRequirements.Any(r => IsClass(r.RefBaseRoleClassPath, candidate))) return candidate;
        }
        return null;
    }

    /// <summary>
    /// Whether a class path ends in this class name. Compared on the last
    /// segment, because the path may carry an alias of the document's choosing.
    /// </summary>
    private static bool IsClass(string? path, string className)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var slash = path.LastIndexOf('/');
        var last = slash >= 0 ? path[(slash + 1)..] : path;
        return string.Equals(last, className, StringComparison.Ordinal);
    }

    // ── attributes ──────────────────────────────────────────────────────────

    private static string? IdentificationId(InternalElementType element) =>
        NonEmpty(element.Attribute[EflNames.Attributes.Identification]?.Attribute[EflNames.Attributes.Id]?.Value);

    /// <summary>
    /// The element's label. Identification/name is authoritative when the
    /// mapper wrote the element, even when it is empty. An element a user added
    /// by hand has no id of its own, and then the CAEX name is the label.
    /// </summary>
    private static string? IdentificationName(InternalElementType element)
    {
        var name = NonEmpty(element.Attribute[EflNames.Attributes.Identification]?
            .Attribute[EflNames.Attributes.Name]?.Value);
        if (name != null) return name;
        return IdentificationId(element) != null ? null : NonEmpty(element.Name);
    }

    private static EflBounds? ReadBounds(InternalElementType element)
    {
        var view = element.Attribute[EflNames.Attributes.ViewInformation];
        var position = view?.Attribute[EflNames.Attributes.Position];
        if (position == null) return null;

        var x = Double(position.Attribute[EflNames.Attributes.X]?.Value);
        var y = Double(position.Attribute[EflNames.Attributes.Y]?.Value);
        if (x == null || y == null) return null;

        var width = Double(view!.Attribute[EflNames.Attributes.Width]?.Value) ?? 100;
        var height = Double(view.Attribute[EflNames.Attributes.Height]?.Value) ?? 60;
        return new EflBounds(x.Value, y.Value, width, height);
    }

    private static IEnumerable<EflPoint> ReadWaypoints(IObjectWithAttributes owner) =>
        owner.Attribute
            .Where(a => a.Name?.StartsWith(EflNames.Attributes.WaypointPrefix, StringComparison.Ordinal) == true)
            .Select(a => (Index: Index(a.Name!), Attribute: a))
            .Where(entry => entry.Index > 0)
            .OrderBy(entry => entry.Index)
            .Select(entry =>
            {
                var position = entry.Attribute.Attribute[EflNames.Attributes.Position];
                var x = Double(position?.Attribute[EflNames.Attributes.X]?.Value);
                var y = Double(position?.Attribute[EflNames.Attributes.Y]?.Value);
                return x == null || y == null ? null : new EflPoint(x.Value, y.Value);
            })
            .Where(point => point != null)
            .Select(point => point!);

    private static int Index(string attributeName)
    {
        var suffix = attributeName[EflNames.Attributes.WaypointPrefix.Length..];
        return int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) ? index : 0;
    }

    /// <summary>
    /// An attribute's value, falling back to its declared default. Reading only
    /// the value would turn a declared default of 5 into nothing.
    /// </summary>
    private static double? Number(InternalElementType element, string name)
    {
        var attribute = element.Attribute[name];
        if (attribute == null) return null;
        return Double(NonEmpty(attribute.Value) ?? NonEmpty(attribute.DefaultValue));
    }

    private static double? Double(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed)
            ? parsed
            : null;

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
