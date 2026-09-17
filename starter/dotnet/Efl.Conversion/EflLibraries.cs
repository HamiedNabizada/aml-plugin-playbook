using System.Xml.Linq;
using Aml.Engine.CAEX;

namespace Efl.Conversion;

/// <summary>
/// Builds the EFL domain library inside a CAEX document.
///
/// Four libraries, one per CAEX library kind, following the three phase
/// methodology: the RoleClassLib carries the meaning, the InterfaceClassLib the
/// connection endpoints, the AttributeTypeLib the data types, and the
/// SystemUnitClassLib the templates that instances are created from. A document
/// that already carries a library is left alone: its author chose that shape.
///
/// A published language should ship this as an .aml artefact with a version,
/// and keep the code and the artefact in step with a golden test. The reference
/// projects do exactly that (ISO_PT_DomainLibrary in AMLPetriNet, the VDI FPD
/// library in AMLFPB.js).
/// </summary>
public static class EflLibraries
{
    /// <summary>
    /// The AutomationML base libraries. Referenced, never copied: every AML
    /// tool knows them, and inlining them would dwarf the document.
    /// </summary>
    public const string BaseAlias = "AutomationMLBaseLibrariesAMLEd22_11_0";

    /// <summary>
    /// A file name, resolved by AML tools through their library search path.
    /// Never a URL with credentials in it: the path is written into every
    /// document the mapper produces, and a share token in it reaches everyone
    /// who opens one of them (fpb-aml-mapper removed exactly that in e866fad).
    /// </summary>
    private const string BasePath = "AutomationML_Base_Libraries_AMLEd2_2.11.0.aml";

    private const string BaseRole = BaseAlias + "@AutomationMLBaseRoleClassLib/AutomationMLBaseRole";
    private const string BaseStructure = BaseRole + "/Structure";
    private const string BasePort = BaseAlias + "@AutomationMLInterfaceClassLib/AutomationMLBaseInterface/Port";

    /// <param name="embedDiagramInterchange">
    /// True for documents people open: the layout library is carried inside
    /// (the AutomationML Editor does not follow file references). False for the
    /// published library artefact, which references the OMG_DD file published
    /// beside it. A document that already carries or references it keeps its form.
    /// </param>
    public static void EnsureLibraries(CAEXFileType caex, bool embedDiagramInterchange = true)
    {
        ArgumentNullException.ThrowIfNull(caex);

        EnsureExternalReference(caex, BaseAlias, BasePath);
        if (embedDiagramInterchange) EflDiagramInterchange.EnsureIn(caex);
        else EflDiagramInterchange.ReferenceFrom(caex);
        EnsureAttributeTypeLib(caex);
        EnsureInterfaceClassLib(caex);
        EnsureRoleClassLib(caex);
        EnsureSystemUnitClassLib(caex);
        StampClassIds(caex);
    }

    /// <summary>
    /// Gives every class and attribute type an id derived from its path.
    ///
    /// Aml.Engine hands out a fresh GUID to every object it creates, so a
    /// library built twice comes out with different ids and a document written
    /// twice differs in a hundred places. Ids of the classes belong to the
    /// artefact, not to the run that produced it: a published library keeps
    /// them from version to version, and anything that references a class by id
    /// keeps working.
    /// </summary>
    private static void StampClassIds(CAEXFileType caex)
    {
        foreach (var library in Libraries(caex))
        {
            foreach (var element in library.DescendantsAndSelf())
            {
                if (element.Attribute("ID") == null) continue;
                element.SetAttributeValue("ID", EflIds.For("class", Path(element)));
            }
        }
    }

    private static IEnumerable<XElement> Libraries(CAEXFileType caex)
    {
        var names = new[]
        {
            EflNames.AttributeTypeLib, EflNames.InterfaceClassLib,
            EflNames.RoleClassLib, EflNames.SystemUnitClassLib,
        };

        return caex.Node.Elements()
            .Where(e => names.Contains((string?)e.Attribute("Name")));
    }

    /// <summary>The names from the library down to the element, as one path.</summary>
    private static string Path(XElement element)
    {
        var parts = new List<string>();
        for (var current = element; current != null; current = current.Parent)
        {
            var name = (string?)current.Attribute("Name");
            if (name != null) parts.Insert(0, current.Name.LocalName + ":" + name);
        }
        return string.Join("/", parts);
    }

    /// <summary>The library artefact as a document of its own, for publishing.</summary>
    /// <param name="writtenAt">A fixed writing time for a file kept in a repository; see <see cref="EflDocuments.StampSource"/>.</param>
    public static CAEXDocument CreateArtefact(DateTime? writtenAt = null)
    {
        var document = CAEXDocument.New_CAEXDocument();
        EflDocuments.StampSource(document, "efl-aml-mapper", EflNames.LibraryVersion, writtenAt);
        document.CAEXFile.FileName = "EFL_DomainLibrary_v" + EflNames.LibraryVersion + ".aml";
        EnsureLibraries(document.CAEXFile, embedDiagramInterchange: false);
        return document;
    }

    // ── AttributeTypeLib ────────────────────────────────────────────────────

    private static void EnsureAttributeTypeLib(CAEXFileType caex)
    {
        if (caex.AttributeTypeLib[EflNames.AttributeTypeLib] != null) return;

        var atl = caex.AttributeTypeLib.Append(EflNames.AttributeTypeLib);
        atl.Description = "Data types of the Example Flow Language.";
        atl.Version = EflNames.LibraryVersion;

        var identification = atl.AttributeType.Append("EFL_Identification");
        identification.Description = "The element's own identifier and name, as the language means them.";
        identification.AttributeDataType = "xs:string";
        AddString(identification, EflNames.Attributes.Id);
        AddString(identification, EflNames.Attributes.Name);

    }

    // ── InterfaceClassLib ───────────────────────────────────────────────────

    private static void EnsureInterfaceClassLib(CAEXFileType caex)
    {
        if (caex.InterfaceClassLib[EflNames.InterfaceClassLib] != null) return;

        var icl = caex.InterfaceClassLib.Append(EflNames.InterfaceClassLib);
        icl.Description =
            "Connection endpoints of the Example Flow Language. The first pair is used when a flow is an "
            + "InternalLink between nodes, the second group when a flow is an element of its own.";
        icl.Version = EflNames.LibraryVersion;

        var port = icl.InterfaceClass.Append("EFL_Port");
        port.Description = "Abstract base port. Carries the docking point of a connection.";
        port.Version = EflNames.LibraryVersion;
        port.RefBaseClassPath = BasePort;
        var coordinate = port.Attribute.Append(EflNames.Attributes.PortCoordinate);
        coordinate.AttributeDataType = "xs:string";
        coordinate.RefAttributeType = EflDiagramInterchange.PathIn(caex, EflDiagramInterchange.Point);
        AddDouble(coordinate, EflNames.Attributes.X);
        AddDouble(coordinate, EflNames.Attributes.Y);

        // Direction is in the type, not in an attribute: a reader can tell the
        // ends of a connection apart without following it. This is the pattern
        // the FPD library uses for its flows.
        AddInterface(icl, EflNames.FlowOut, "Outgoing end of a flow, on the source node.");
        AddInterface(icl, EflNames.FlowIn, "Incoming end of a flow, on the target node.");

        AddInterface(icl, EflNames.FlowEnd, "Port on a node that a flow element attaches to.");
        AddInterface(icl, EflNames.FlowSource, "Source end of a flow element.");
        AddInterface(icl, EflNames.FlowTarget, "Target end of a flow element.");
    }

    private static void AddInterface(InterfaceClassLibType icl, string name, string description)
    {
        var ic = icl.InterfaceClass.Append(name);
        ic.Description = description;
        ic.Version = EflNames.LibraryVersion;
        ic.RefBaseClassPath = EflNames.InterfaceClass("EFL_Port");
    }

    // ── RoleClassLib ────────────────────────────────────────────────────────

    private static void EnsureRoleClassLib(CAEXFileType caex)
    {
        if (caex.RoleClassLib[EflNames.RoleClassLib] != null) return;

        var rcl = caex.RoleClassLib.Append(EflNames.RoleClassLib);
        rcl.Description = "Meaning of the Example Flow Language: what an element is, independent of how it is created.";
        rcl.Version = EflNames.LibraryVersion;

        var diagram = rcl.RoleClass.Append(EflNames.Diagram);
        diagram.Description = "One diagram. Holds the elements of a model.";
        diagram.Version = EflNames.LibraryVersion;
        diagram.RefBaseClassPath = BaseStructure;
        AddIdentification(diagram);

        // Everything shared sits on one abstract base, so an attribute that
        // every element has is declared once.
        var element = rcl.RoleClass.Append(EflNames.Element);
        element.Description = "Abstract base of every element of a diagram.";
        element.Version = EflNames.LibraryVersion;
        element.RefBaseClassPath = BaseRole;
        AddIdentification(element);
        AddViewInformation(element, caex);

        var step = rcl.RoleClass.Append(EflNames.Step);
        step.Description = "An activity that takes time.";
        step.Version = EflNames.LibraryVersion;
        step.RefBaseClassPath = EflNames.RoleClass(EflNames.Element);
        AddDuration(step);

        var store = rcl.RoleClass.Append(EflNames.Store);
        store.Description = "A place where something waits.";
        store.Version = EflNames.LibraryVersion;
        store.RefBaseClassPath = EflNames.RoleClass(EflNames.Element);
        AddCapacity(store);

        // Only instantiated by the reified encoding. Declaring the role anyway
        // costs nothing and keeps both encodings describable in one library.
        var flow = rcl.RoleClass.Append(EflNames.Flow);
        flow.Description =
            "A directed connection from one element to another. Written either as an InternalLink between "
            + "interfaces of the two nodes, or as an element of its own; see the mapper's connection style.";
        flow.Version = EflNames.LibraryVersion;
        flow.RefBaseClassPath = EflNames.RoleClass(EflNames.Element);
    }

    // ── SystemUnitClassLib ──────────────────────────────────────────────────

    private static void EnsureSystemUnitClassLib(CAEXFileType caex)
    {
        if (caex.SystemUnitClassLib[EflNames.SystemUnitClassLib] != null) return;

        var sucl = caex.SystemUnitClassLib.Append(EflNames.SystemUnitClassLib);
        sucl.Description =
            "Templates instances are created from. Each one names its role class through SupportedRoleClass, "
            + "so another template from a vendor catalogue can fulfil the same role.";
        sucl.Version = EflNames.LibraryVersion;

        var diagram = sucl.SystemUnitClass.Append(EflNames.Diagram);
        diagram.Version = EflNames.LibraryVersion;
        AddIdentification(diagram);
        AddSupportedRole(diagram, EflNames.RoleClass(EflNames.Diagram));
        AddSupportedRole(diagram, BaseStructure);

        var element = sucl.SystemUnitClass.Append(EflNames.Element);
        element.Version = EflNames.LibraryVersion;
        AddIdentification(element);
        AddViewInformation(element, caex);
        AddSupportedRole(element, EflNames.RoleClass(EflNames.Element));

        var step = sucl.SystemUnitClass.Append(EflNames.Step);
        step.Version = EflNames.LibraryVersion;
        step.RefBaseClassPath = EflNames.SystemUnitClass(EflNames.Element);
        AddDuration(step);
        AddSupportedRole(step, EflNames.RoleClass(EflNames.Step));

        var store = sucl.SystemUnitClass.Append(EflNames.Store);
        store.Version = EflNames.LibraryVersion;
        store.RefBaseClassPath = EflNames.SystemUnitClass(EflNames.Element);
        AddCapacity(store);
        AddSupportedRole(store, EflNames.RoleClass(EflNames.Store));

        var flow = sucl.SystemUnitClass.Append(EflNames.Flow);
        flow.Version = EflNames.LibraryVersion;
        flow.RefBaseClassPath = EflNames.SystemUnitClass(EflNames.Element);
        AddSupportedRole(flow, EflNames.RoleClass(EflNames.Flow));
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static void AddSupportedRole(SystemUnitFamilyType suc, string rolePath) =>
        suc.SupportedRoleClass.Append().RefRoleClassPath = rolePath;

    private static void AddIdentification(IObjectWithAttributes parent)
    {
        var attribute = parent.Attribute.Append(EflNames.Attributes.Identification);
        attribute.AttributeDataType = "xs:string";
        attribute.RefAttributeType = EflNames.AttributeType("EFL_Identification");
        AddString(attribute, EflNames.Attributes.Id);
        AddString(attribute, EflNames.Attributes.Name);
    }

    private static void AddViewInformation(IObjectWithAttributes parent, CAEXFileType caex)
    {
        var attribute = parent.Attribute.Append(EflNames.Attributes.ViewInformation);
        attribute.AttributeDataType = "xs:string";
        attribute.RefAttributeType = EflDiagramInterchange.PathIn(caex, EflDiagramInterchange.Bounds);

        var position = attribute.Attribute.Append(EflNames.Attributes.Position);
        position.AttributeDataType = "xs:string";
        position.RefAttributeType = EflDiagramInterchange.PathIn(caex, EflDiagramInterchange.Point);
        AddDouble(position, EflNames.Attributes.X);
        AddDouble(position, EflNames.Attributes.Y);

        AddDouble(attribute, EflNames.Attributes.Width);
        AddDouble(attribute, EflNames.Attributes.Height);
    }

    private static void AddDuration(IObjectWithAttributes parent)
    {
        var attribute = parent.Attribute.Append(EflNames.Attributes.Duration);
        attribute.AttributeDataType = "xs:double";
        attribute.Unit = "s";
        attribute.DefaultValue = "0";
    }

    private static void AddCapacity(IObjectWithAttributes parent)
    {
        var attribute = parent.Attribute.Append(EflNames.Attributes.Capacity);
        attribute.AttributeDataType = "xs:double";
        attribute.DefaultValue = "0";
    }

    private static void AddString(IObjectWithAttributes parent, string name) =>
        parent.Attribute.Append(name).AttributeDataType = "xs:string";

    private static void AddDouble(IObjectWithAttributes parent, string name) =>
        parent.Attribute.Append(name).AttributeDataType = "xs:double";

    private static void EnsureExternalReference(CAEXFileType caex, string alias, string path)
    {
        if (caex.ExternalReference.Any(r => r.Alias == alias)) return;

        var reference = caex.ExternalReference.Append();
        reference.Alias = alias;
        reference.Path = path;
    }
}
