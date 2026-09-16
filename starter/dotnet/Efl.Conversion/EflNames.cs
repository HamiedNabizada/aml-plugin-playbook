namespace Efl.Conversion;

/// <summary>
/// Every name the language uses inside a CAEX document, in one place.
///
/// Renaming a class later means touching documents that are already out there,
/// so the names are worth a minute of thought. The scheme here is the one both
/// reference projects use: a prefix per language, one library per CAEX library
/// kind, and the element name without the prefix inside the class paths.
/// </summary>
public static class EflNames
{
    /// <summary>Version of the library artefact. Write it into every document.</summary>
    public const string LibraryVersion = "0.1.0";

    public const string RoleClassLib = "EFL_RoleClassLib";
    public const string SystemUnitClassLib = "EFL_SystemUnitClassLib";
    public const string InterfaceClassLib = "EFL_InterfaceClassLib";
    public const string AttributeTypeLib = "EFL_AttributeTypeLib";

    // ── classes ─────────────────────────────────────────────────────────────

    /// <summary>The diagram container.</summary>
    public const string Diagram = "EFL_Diagram";

    /// <summary>Abstract base of everything in a diagram.</summary>
    public const string Element = "EFL_Element";

    public const string Step = "EFL_Step";
    public const string Store = "EFL_Store";

    /// <summary>Only used by the reified encoding; see <see cref="EflConnectionStyle"/>.</summary>
    public const string Flow = "EFL_Flow";

    // ── interfaces ──────────────────────────────────────────────────────────

    public const string FlowOut = "EFL_FlowOut";
    public const string FlowIn = "EFL_FlowIn";

    /// <summary>The two ends of a reified flow, on the flow element itself.</summary>
    public const string FlowSource = "EFL_FlowSource";
    public const string FlowTarget = "EFL_FlowTarget";

    /// <summary>The port a reified flow is attached to, on a node.</summary>
    public const string FlowEnd = "EFL_FlowEnd";

    // ── attributes ──────────────────────────────────────────────────────────

    public static class Attributes
    {
        public const string Identification = "Identification";
        public const string Id = "id";
        public const string Name = "name";

        /// <summary>Seconds on a Step.</summary>
        public const string Duration = "Duration";

        /// <summary>Items on a Store.</summary>
        public const string Capacity = "Capacity";

        // Layout. The attribute names are the language's; their types come from
        // the shared OMG_DD_AttributeTypeLib, see EflDiagramInterchange.
        public const string ViewInformation = "ViewInformation";
        public const string Position = "position";
        public const string Width = "width";
        public const string Height = "height";
        public const string X = "x";
        public const string Y = "y";
        public const string PortCoordinate = "PortCoordinate";
        public const string WaypointPrefix = "Waypoint_";
    }

    /// <summary>Full path of a class inside its library, as CAEX writes it.</summary>
    public static string SystemUnitClass(string name) => $"{SystemUnitClassLib}/{name}";

    public static string RoleClass(string name) => $"{RoleClassLib}/{name}";

    public static string InterfaceClass(string name) => $"{InterfaceClassLib}/{name}";

    public static string AttributeType(string name) => $"{AttributeTypeLib}/{name}";
}

/// <summary>
/// How a flow is written into the document. This is the decision the at-2026
/// methodology calls pattern P6, and it is the one that shapes everything else.
///
/// <list type="bullet">
/// <item><see cref="Link"/>: the flow is an InternalLink between a typed
/// interface on the source node and one on the target node. Fewer elements, and
/// the natural choice when the connection has no identity and no attributes of
/// its own. This is what the FPD library does for its flows.</item>
/// <item><see cref="Element"/>: the flow is an InternalElement of its own with
/// two interfaces, joined to the nodes by two InternalLinks. Needed as soon as
/// a connection carries attributes, is referenced from elsewhere, or has to
/// survive as an object. This is what the ISO_PT library does for arcs.</item>
/// </list>
///
/// Both are written and read by this mapper, so the difference can be seen in
/// one document rather than described.
/// </summary>
public enum EflConnectionStyle
{
    Link,
    Element,
}
