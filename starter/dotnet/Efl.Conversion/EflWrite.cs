using System.Globalization;
using Aml.Engine.CAEX;
using Aml.Engine.CAEX.Extensions;

namespace Efl.Conversion;

/// <summary>
/// The CAEX writing primitives, shared by the converter that builds a hierarchy
/// from scratch and the updater that edits one that already exists. Both have
/// to produce the same shape, so the operations live here once.
/// </summary>
internal static class EflWrite
{
    /// <summary>
    /// The attributes the language owns on an element. Everything else on an
    /// element is somebody's own addition and is never touched by an update.
    /// This single predicate is what makes a document shared with other tools
    /// survive a sync.
    /// </summary>
    internal static bool IsLanguageOwned(string? attributeName) =>
        attributeName is EflNames.Attributes.Identification
            or EflNames.Attributes.ViewInformation
            or EflNames.Attributes.Duration
            or EflNames.Attributes.Capacity
        || (attributeName?.StartsWith(EflNames.Attributes.WaypointPrefix, StringComparison.Ordinal) ?? false);

    internal static Dictionary<string, SystemUnitFamilyType> Classes(CAEXDocument document)
    {
        var library = document.CAEXFile.SystemUnitClassLib[EflNames.SystemUnitClassLib]
            ?? throw new InvalidOperationException($"The library '{EflNames.SystemUnitClassLib}' is missing.");

        // First class of a name wins. A library edited by hand can carry one
        // twice, and ToDictionary would throw halfway through an update.
        var classes = new Dictionary<string, SystemUnitFamilyType>(StringComparer.Ordinal);
        foreach (var suc in library.SystemUnitClass)
            if (!string.IsNullOrEmpty(suc.Name)) classes.TryAdd(suc.Name, suc);
        return classes;
    }

    internal static InternalElementType CreateInstance(Dictionary<string, SystemUnitFamilyType> classes, string className)
    {
        if (!classes.TryGetValue(className, out var suc))
            throw new InvalidOperationException($"The class '{className}' is not in the library.");

        var element = (InternalElementType)suc.CreateClassInstance(className);

        // CreateClassInstance copies only the first supported role into a role
        // requirement. Anything beyond the first has to be added by hand.
        var present = new HashSet<string?>(element.RoleRequirements.Select(r => r.RefBaseRoleClassPath));
        foreach (var supported in suc.SupportedRoleClass)
            if (!present.Contains(supported.RefRoleClassPath))
                element.RoleRequirements.Append().RefBaseRoleClassPath = supported.RefRoleClassPath;

        return element;
    }

    /// <summary>
    /// asFirst defaults to true in Aml.Engine, which reverses document order on
    /// every insert. Always append.
    /// </summary>
    internal static void Append(InternalElementType parent, InternalElementType child) => parent.Insert(child, false);

    internal static void Append(InstanceHierarchyType parent, InternalElementType child) => parent.Insert(child, false);

    /// <summary>
    /// Sets the CAEX name, which is what the editor's tree shows. The name the
    /// language means lives in Identification/name.
    /// </summary>
    /// <param name="created">
    /// Whether the element was just instantiated. A fresh instance carries the
    /// class name, so there is nothing of the document's to keep. On an element
    /// that was already there, a name another tool chose is kept unless the
    /// language's own name actually changed.
    /// </param>
    internal static void SetDisplayName(InternalElementType element, string? name, string fallback, bool created)
    {
        if (!created)
        {
            var stored = element.Attribute[EflNames.Attributes.Identification]?
                .Attribute[EflNames.Attributes.Name]?.Value;
            if (string.Equals(Trimmed(stored), Trimmed(name), StringComparison.Ordinal)) return;
        }

        element.Name = string.IsNullOrWhiteSpace(name) ? fallback : name!;
    }

    internal static void SetIdentification(InternalElementType element, string id, string? name)
    {
        var identification = element.Attribute[EflNames.Attributes.Identification]
            ?? Typed(element, EflNames.Attributes.Identification, EflNames.AttributeType("EFL_Identification"));

        var idAttribute = identification.Attribute[EflNames.Attributes.Id]
            ?? String(identification, EflNames.Attributes.Id);
        idAttribute.Value = id;

        var nameAttribute = identification.Attribute[EflNames.Attributes.Name]
            ?? String(identification, EflNames.Attributes.Name);
        nameAttribute.Value = name ?? string.Empty;
    }

    internal static void WriteNode(InternalElementType element, EflNode node, bool created)
    {
        RemoveForeignLanguageAttributes(element, node.Kind);
        SetDisplayName(element, node.Name, node.Id, created);
        SetIdentification(element, node.Id, node.Name);
        SetBounds(element, node.Bounds);

        var valueAttribute = node.Kind == EflNodeKind.Step
            ? EflNames.Attributes.Duration
            : EflNames.Attributes.Capacity;

        if (node.Value == null)
        {
            element.Attribute[valueAttribute]?.Remove();
            return;
        }

        var attribute = element.Attribute[valueAttribute] ?? element.Attribute.Append(valueAttribute);
        attribute.AttributeDataType = "xs:double";
        attribute.Value = Format(node.Value.Value);
    }

    /// <summary>
    /// Drops attributes of the language that do not belong on this kind of
    /// element, for instance a Duration left behind on an element that became a
    /// Store. Attributes somebody else added are never candidates.
    /// </summary>
    private static void RemoveForeignLanguageAttributes(InternalElementType element, EflNodeKind kind)
    {
        var foreign = kind == EflNodeKind.Step ? EflNames.Attributes.Capacity : EflNames.Attributes.Duration;
        element.Attribute[foreign]?.Remove();
    }

    internal static void SetBounds(InternalElementType element, EflBounds? bounds)
    {
        if (bounds == null) return;

        var view = element.Attribute[EflNames.Attributes.ViewInformation]
            ?? Typed(element, EflNames.Attributes.ViewInformation, EflDiagramInterchange.PathOf(element, EflDiagramInterchange.Bounds));

        var position = view.Attribute[EflNames.Attributes.Position]
            ?? Typed(view, EflNames.Attributes.Position, EflDiagramInterchange.PathOf(element, EflDiagramInterchange.Point));

        SetChild(position, EflNames.Attributes.X, bounds.X);
        SetChild(position, EflNames.Attributes.Y, bounds.Y);
        SetChild(view, EflNames.Attributes.Width, bounds.Width);
        SetChild(view, EflNames.Attributes.Height, bounds.Height);
    }

    /// <summary>The bend points of a flow, in the order from source to target.</summary>
    internal static void ReplaceWaypoints(IObjectWithAttributes owner, IReadOnlyList<EflPoint> waypoints)
    {
        foreach (var existing in owner.Attribute
                     .Where(a => a.Name?.StartsWith(EflNames.Attributes.WaypointPrefix, StringComparison.Ordinal) == true)
                     .ToList())
        {
            existing.Remove();
        }

        for (var i = 0; i < waypoints.Count; i++)
        {
            var waypoint = Typed(owner, EflNames.Attributes.WaypointPrefix + (i + 1).ToString(CultureInfo.InvariantCulture),
                EflDiagramInterchange.PathOf(owner, EflDiagramInterchange.Waypoint));
            var position = Typed(waypoint, EflNames.Attributes.Position, EflDiagramInterchange.PathOf(owner, EflDiagramInterchange.Point));
            SetChild(position, EflNames.Attributes.X, waypoints[i].X);
            SetChild(position, EflNames.Attributes.Y, waypoints[i].Y);
        }
    }

    internal static ExternalInterfaceType EnsureInterface(
        InternalElementType owner, string name, string id, string classPath)
    {
        var existing = owner.ExternalInterface.FirstOrDefault(i => i.Name == name);
        if (existing != null) return existing;

        var created = owner.ExternalInterface.Append(name);
        created.ID = id;
        created.RefBaseClassPath = classPath;
        return created;
    }

    internal static void SetPortCoordinate(ExternalInterfaceType port, EflPoint point)
    {
        var coordinate = port.Attribute[EflNames.Attributes.PortCoordinate]
            ?? Typed(port, EflNames.Attributes.PortCoordinate, EflDiagramInterchange.PathOf(port, EflDiagramInterchange.Point));

        SetChild(coordinate, EflNames.Attributes.X, point.X);
        SetChild(coordinate, EflNames.Attributes.Y, point.Y);
    }

    internal static InternalLinkType EnsureLink(
        InternalElementType owner, string name, string id, ExternalInterfaceType a, ExternalInterfaceType b)
    {
        var existing = owner.InternalLink.FirstOrDefault(l => l.ID == id);
        if (existing != null) return existing;

        var link = owner.InternalLink.Append(name);
        link.ID = id;
        // Through the interface objects, not by composing strings: Aml.Engine
        // then writes the form CAEX 3.0 resolves. Hand-built
        // "ElementId:InterfaceId" strings did not resolve in fpb-aml-mapper.
        link.AInterface = a;
        link.BInterface = b;
        return link;
    }

    // ── small helpers ───────────────────────────────────────────────────────

    private static AttributeType Typed(IObjectWithAttributes parent, string name, string attributeTypePath)
    {
        var attribute = parent.Attribute.Append(name);
        attribute.AttributeDataType = "xs:string";
        attribute.RefAttributeType = attributeTypePath;
        return attribute;
    }

    private static AttributeType String(IObjectWithAttributes parent, string name)
    {
        var attribute = parent.Attribute.Append(name);
        attribute.AttributeDataType = "xs:string";
        return attribute;
    }

    private static void SetChild(AttributeType parent, string name, double value)
    {
        var child = parent.Attribute[name] ?? parent.Attribute.Append(name);
        child.AttributeDataType = "xs:double";
        child.Value = Format(value);
    }

    /// <summary>
    /// Numbers with the invariant culture and without a trailing ".0". A comma
    /// as the decimal separator would leave a document that other tools read as
    /// a different number, or not at all.
    /// </summary>
    internal static string Format(double value)
    {
        if (!double.IsFinite(value)) value = 0;
        return value == Math.Floor(value) && Math.Abs(value) < 1e15
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
