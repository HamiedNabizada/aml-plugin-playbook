# Snippets

## In short

- Copy-paste code for the tasks a builder does most often, grouped by layer: mapper (C# with Aml.Engine), modeler (JavaScript with diagram-js), plugin (C# with WPF and WebView2), web app, CI.
- Every snippet names the starter file and the symbols it comes from. The excerpts are trimmed; `// ...` marks left-out code.
- **The starter is the source of truth.** When a snippet and the starter disagree, the starter wins, because the starter is built and tested (`dotnet test`, `npm test`, `npm run test:webapp`, plugin package tests). Fix this page, not the starter.
- Names in the snippets use the EFL prefix (`Efl`, `EFL_`, `efl:`). After `tools/new-language.mjs` they carry your prefix.
- "Watch out" bullets point to entries in [09-pitfalls.md](09-pitfalls.md) by their id.

Read fully when: never. Look up the task in the table, copy the snippet, then open the cited starter file to see the context.

## Task to section

| Task | Section |
|---|---|
| **Mapper (C#, Aml.Engine)** | |
| Create a CAEX document with SourceDocumentInformation | [SN-01](#sn-01-create-a-caex-document-with-sourcedocumentinformation) |
| Add an ExternalReference with an alias | [SN-02](#sn-02-add-an-externalreference-with-an-alias) |
| Embed a published shared library and follow the document's path form | [SN-49](#sn-49-embed-a-published-shared-library-and-follow-the-documents-path-form) |
| RoleClass with base class and a typed attribute | [SN-03](#sn-03-roleclass-with-base-class-and-a-typed-attribute) |
| SystemUnitClass with SupportedRoleClass | [SN-04](#sn-04-systemunitclass-with-supportedroleclass) |
| InterfaceClass hierarchy | [SN-05](#sn-05-interfaceclass-hierarchy) |
| Stamp deterministic class ids | [SN-06](#sn-06-stamp-deterministic-class-ids) |
| Deterministic UUID from parts | [SN-07](#sn-07-deterministic-uuid-from-parts) |
| Instance from a SystemUnitClass with a deterministic id | [SN-08](#sn-08-instance-from-a-systemunitclass-with-a-deterministic-id) |
| Insert keeping document order | [SN-09](#sn-09-insert-keeping-document-order) |
| Nested attribute values with invariant culture | [SN-10](#sn-10-nested-attribute-values-with-invariant-culture) |
| Bounds, port coordinates and waypoints | [SN-11](#sn-11-bounds-port-coordinates-and-waypoints) |
| ExternalInterface and InternalLink, both encodings | [SN-12](#sn-12-externalinterface-and-internallink-in-both-encodings) |
| Read class, attributes, interfaces and links tolerantly | [SN-13](#sn-13-read-class-attributes-interfaces-and-links-tolerantly) |
| Find a hierarchy by id, then by name | [SN-14](#sn-14-find-a-hierarchy-by-id-then-by-name) |
| Update in place | [SN-15](#sn-15-update-in-place) |
| Load AML text safely | [SN-16](#sn-16-load-aml-text-safely) |
| Save without schema clutter | [SN-17](#sn-17-save-without-schema-clutter) |
| Validator rule with element id | [SN-18](#sn-18-validator-rule-with-element-id) |
| Layered layout for nodes without position | [SN-19](#sn-19-layered-layout-for-nodes-without-position) |
| **Modeler (JavaScript, diagram-js)** | |
| Diagram with a module list | [SN-20](#sn-20-diagram-with-a-module-list) |
| Custom renderer with getShapePath | [SN-21](#sn-21-custom-renderer-with-getshapepath) |
| Element factory with random ids | [SN-22](#sn-22-element-factory-with-random-ids) |
| Layouter with cropping | [SN-23](#sn-23-layouter-with-cropping) |
| Rule provider | [SN-24](#sn-24-rule-provider) |
| Palette provider | [SN-25](#sn-25-palette-provider) |
| Context pad provider | [SN-26](#sn-26-context-pad-provider) |
| Command handler for property updates | [SN-27](#sn-27-command-handler-for-property-updates) |
| Direct editing provider | [SN-28](#sn-28-direct-editing-provider) |
| Import a model | [SN-29](#sn-29-import-a-model) |
| Export with rounding and bend points only | [SN-30](#sn-30-export-with-rounding-and-bend-points-only) |
| SVG export without editor furniture | [SN-31](#sn-31-svg-export-without-editor-furniture) |
| Bridge: debounced changes, import queue, echo baseline | [SN-32](#sn-32-bridge-with-debounced-changes-import-queue-and-echo-baseline) |
| Playwright harness driving services | [SN-33](#sn-33-playwright-harness-driving-services) |
| **Plugin (C#, WPF, WebView2)** | |
| PluginViewBase skeleton | [SN-34](#sn-34-pluginviewbase-skeleton) |
| DocumentLoaded | [SN-35](#sn-35-documentloaded) |
| WebView2 init with virtual host and handler removal | [SN-36](#sn-36-webview2-init-with-virtual-host-and-handler-removal) |
| Ready handshake with buffered push | [SN-37](#sn-37-ready-handshake-with-buffered-push) |
| Request and response with timeout | [SN-38](#sn-38-request-and-response-with-timeout) |
| Echo baseline and pending model | [SN-39](#sn-39-echo-baseline-and-pending-model) |
| Update button flow | [SN-40](#sn-40-update-button-flow) |
| Settings in APPDATA | [SN-41](#sn-41-settings-in-appdata) |
| Log to TEMP and a tab | [SN-42](#sn-42-log-to-temp-and-a-tab) |
| csproj packaging | [SN-43](#sn-43-csproj-packaging) |
| Package tests | [SN-44](#sn-44-package-tests) |
| **Web app** | |
| Minimal API with 400 and 500 separated | [SN-45](#sn-45-minimal-api-with-400-and-500-separated) |
| ASCII-safe info header | [SN-46](#sn-46-ascii-safe-info-header) |
| Staged bundle target | [SN-47](#sn-47-staged-bundle-target) |
| **CI** | |
| Workflow skeleton | [SN-48](#sn-48-workflow-skeleton) |

---

## Mapper (C#, Aml.Engine)

All mapper snippets assume `using Aml.Engine.CAEX;` and, for `CreateClassInstance`, `using Aml.Engine.CAEX.Extensions;`.

### SN-01 Create a CAEX document with SourceDocumentInformation

Use when: the mapper writes a new document from a model.

```csharp
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

    var information = document.CAEXFile.SourceDocumentInformation.FirstOrDefault()
        ?? document.CAEXFile.SourceDocumentInformation.Append();
    information.OriginName = "efl-aml-mapper";
    information.OriginID = "efl-aml-mapper";
    information.OriginVersion = EflNames.LibraryVersion;
    information.LastWritingDateTime = writtenAt ?? DateTime.UtcNow;

    AppendInto(document, model, hierarchyName, style);
    return document;
}
```

Source: `starter/dotnet/Efl.Conversion/EflToCaex.cs` (`Convert`)

Watch out:
- `LastWritingDateTime` differs on every write unless `writtenAt` is given. Golden tests must normalise it (PF-AML-04). Files kept in the repository are written with a fixed value (`eflmap to-aml ... --timestamp 2026-01-01T00:00:00Z`, the `to-aml` command in `Run` of `starter/dotnet/Efl.Tool/Program.cs`, parsed by `Timestamp`), and CI writes them again and fails on `git diff` (SN-48).
- `OriginID` names the tool, not the file; it is the same in every document the editor saves. Do not use it alone as a document identity (PF-CCH-04).

### SN-02 Add an ExternalReference with an alias

Use when: class paths point into a library that is referenced rather than copied (the AutomationML base libraries).

```csharp
public const string BaseAlias = "AutomationMLBaseLibrariesAMLEd22_11_0";

/// A file name, resolved by AML tools through their library search path.
/// Never a URL with credentials in it: the path is written into every
/// document the mapper produces.
private const string BasePath = "AutomationML_Base_Libraries_AMLEd2_2.11.0.aml";

private const string BaseRole = BaseAlias + "@AutomationMLBaseRoleClassLib/AutomationMLBaseRole";
private const string BasePort = BaseAlias + "@AutomationMLInterfaceClassLib/AutomationMLBaseInterface/Port";

private static void EnsureExternalReference(CAEXFileType caex, string alias, string path)
{
    if (caex.ExternalReference.Any(r => r.Alias == alias)) return;

    var reference = caex.ExternalReference.Append();
    reference.Alias = alias;
    reference.Path = path;
}
```

Source: `starter/dotnet/Efl.Conversion/EflLibraries.cs` (`BaseAlias`, `BasePath`, `BaseRole`, `BasePort`, `EnsureExternalReference`; called from `EnsureLibraries`)

Watch out:
- The AutomationML Editor does not follow file references. Small shared libraries (OMG_DD, ObjectReferences) must be embedded into created documents, not referenced (PF-AML-09; recipe §3 "Shared libraries in documents"). The starter embeds OMG_DD from the published file (SN-49); only the library artefact references it.
- The path ends up in every document you write. Reference the base library by file name, as the starter does; never by a URL that contains a token or credentials (PF-AML-13).
- Readers must compare class paths with the alias stripped, because documents choose their own alias (PF-AML-08, see SN-13).

### SN-49 Embed a published shared library and follow the document's path form

Use when: a shared AttributeTypeLib (OMG_DD in the starter; ObjectReferences if your language has references) has to be inside the documents the mapper creates, and every attribute typed with it must use the path form the document already uses.

```xml
<!-- Efl.Conversion.csproj: one copy of the published file, linked into the assembly -->
<EmbeddedResource Include="..\..\libraries\OMG_DD_AttributeTypeLib_v0.1.aml"
                  Link="Libraries\OMG_DD_AttributeTypeLib_v0.1.aml" />
```

```csharp
public static void EnsureIn(CAEXFileType caex)
{
    ArgumentNullException.ThrowIfNull(caex);
    if (caex.AttributeTypeLib[LibName] != null || ReferenceTo(caex) != null) return;

    var library = XDocument.Parse(Xml).Root?
        .Elements(Caex + "AttributeTypeLib")
        .FirstOrDefault(e => (string?)e.Attribute("Name") == LibName)
        ?? throw new InvalidOperationException($"The embedded {FileName} holds no AttributeTypeLib '{LibName}'.");

    caex.Node.Add(new XElement(library));
}

public static string PathOf(object owner, string type) =>
    PathIn((owner as CAEXBasicObject)?.CAEXDocument?.CAEXFile, type);

public static string PathIn(CAEXFileType? caex, string type)
{
    // Inline, or no document at hand: the unqualified path.
    if (caex == null || caex.AttributeTypeLib[LibName] != null) return LibName + "/" + type;

    var reference = ReferenceTo(caex);
    return reference != null
        ? reference.Alias + "@" + LibName + "/" + type
        : LibName + "/" + type;
}

private static ExternalReferenceType? ReferenceTo(CAEXFileType caex) =>
    caex.ExternalReference.FirstOrDefault(r =>
        string.Equals(Path.GetFileName(r.Path ?? ""), FileName, StringComparison.OrdinalIgnoreCase)
        || r.Alias == Alias);
```

Source: `starter/dotnet/Efl.Conversion/Efl.Conversion.csproj` (the `<EmbeddedResource>` item for the OMG_DD file); `starter/dotnet/Efl.Conversion/EflDiagramInterchange.cs` (`EnsureIn`, `PathOf`, `PathIn`, `ReferenceTo`); called from `starter/dotnet/Efl.Conversion/EflLibraries.cs` (`EnsureLibraries`); tests `starter/dotnet/Efl.Tests/DiagramInterchangeTests.cs`

Watch out:
- Take the published file, never types rebuilt from a description; keep a test that the embedded copy equals the file (`TheEmbeddedLibraryIsThePublishedFile` in `starter/dotnet/Efl.Tests/DiagramInterchangeTests.cs`, PF-AML-16). The playbook ships OMG_DD in `starter/libraries/`; obtain ObjectReferences through the library manager of the AutomationML Editor (D-90).
- `caex.Node.Add` appends at the end of `CAEXFile`. That is valid only because `AttributeTypeLib` is last in the CAEX schema order; any other library kind has to be moved into schema order. `ADocumentTheMapperCreatesIsValidAgainstTheCaexSchema` in `starter/dotnet/Efl.Tests/DiagramInterchangeTests.cs` validates created documents against the schema, with a negative control (`TheSchemaCheckRejectsALibraryInAPlaceTheSchemaDoesNotAllow`).
- A document that already carries or references the library is left alone, and paths follow its form, including an alias of its own (`DD@...`). Writing the inline form into a referencing document, or the aliased form into a carrying one, leaves unresolved types (PF-AML-10).
- The published library artefact references the file instead (`ReferenceFrom`, `starter/dotnet/Efl.Conversion/EflDiagramInterchange.cs`), and `eflmap library` writes the file beside it (the `library` command in `Run`, `starter/dotnet/Efl.Tool/Program.cs`).

### SN-03 RoleClass with base class and a typed attribute

Use when: defining an element type of the language (pattern P1: shared attributes once on an abstract base role).

```csharp
// AttributeTypeLib: the type
var identification = atl.AttributeType.Append("EFL_Identification");
identification.AttributeDataType = "xs:string";
AddString(identification, EflNames.Attributes.Id);
AddString(identification, EflNames.Attributes.Name);

// RoleClassLib: abstract base with the shared attributes, then a concrete role
var element = rcl.RoleClass.Append(EflNames.Element);
element.Description = "Abstract base of every element of a diagram.";
element.Version = EflNames.LibraryVersion;
element.RefBaseClassPath = BaseRole;
AddIdentification(element);
AddViewInformation(element, caex);

var step = rcl.RoleClass.Append(EflNames.Step);
step.Version = EflNames.LibraryVersion;
step.RefBaseClassPath = EflNames.RoleClass(EflNames.Element);
AddDuration(step);

// An attribute instance of the type
private static void AddIdentification(IObjectWithAttributes parent)
{
    var attribute = parent.Attribute.Append(EflNames.Attributes.Identification);
    attribute.AttributeDataType = "xs:string";
    attribute.RefAttributeType = EflNames.AttributeType("EFL_Identification");
    AddString(attribute, EflNames.Attributes.Id);
    AddString(attribute, EflNames.Attributes.Name);
}
```

Source: `starter/dotnet/Efl.Conversion/EflLibraries.cs` (`EnsureAttributeTypeLib`, `EnsureRoleClassLib`, `AddIdentification`)

Watch out:
- Every library `Ensure...` method returns early when the library exists (`if (caex.RoleClassLib[...] != null) return;` at the start of `EnsureRoleClassLib`). A changed library therefore needs a new `LibraryVersion`, not a silent rewrite.
- Keep names in `EflNames.cs`; no string literal for a CAEX name elsewhere (recipe §4.2).
- `AddViewInformation` types `ViewInformation` and `position` with `DD_Bounds` and `DD_Point` from OMG_DD, in the document's path form (`EflDiagramInterchange.PathIn`, SN-49; recipe §3 P7). Do not declare layout types of your own.

### SN-04 SystemUnitClass with SupportedRoleClass

Use when: defining the templates instances are created from (pattern P4).

```csharp
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

private static void AddSupportedRole(SystemUnitFamilyType suc, string rolePath) =>
    suc.SupportedRoleClass.Append().RefRoleClassPath = rolePath;
```

Source: `starter/dotnet/Efl.Conversion/EflLibraries.cs` (`EnsureSystemUnitClassLib`, `AddSupportedRole`)

Watch out:
- `CreateClassInstance` copies only the first SupportedRoleClass into a RoleRequirement; the diagram class has two. See SN-08 for the fix.

### SN-05 InterfaceClass hierarchy

Use when: defining connection endpoints, one typed `Out`/`In` pair per directed connection type (pattern P2).

```csharp
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
// ends of a connection apart without following it.
AddInterface(icl, EflNames.FlowOut, "Outgoing end of a flow, on the source node.");
AddInterface(icl, EflNames.FlowIn, "Incoming end of a flow, on the target node.");

private static void AddInterface(InterfaceClassLibType icl, string name, string description)
{
    var ic = icl.InterfaceClass.Append(name);
    ic.Description = description;
    ic.Version = EflNames.LibraryVersion;
    ic.RefBaseClassPath = EflNames.InterfaceClass("EFL_Port");
}
```

Source: `starter/dotnet/Efl.Conversion/EflLibraries.cs` (`EnsureInterfaceClassLib`, `AddInterface`)

Watch out:
- `EnsureInterfaceClassLib` also adds `EFL_FlowEnd`, `EFL_FlowSource` and `EFL_FlowTarget` for the reified encoding. Delete the classes of the encoding your language does not use (recipe §3 P6).

### SN-06 Stamp deterministic class ids

Use when: always, as the last step of building libraries. Aml.Engine gives every created object a random id.

```csharp
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
```

Source: `starter/dotnet/Efl.Conversion/EflLibraries.cs` (`StampClassIds`, `Libraries`, `Path`; called from `EnsureLibraries`)

Watch out:
- Without it, two runs of the same code produce different files and every diff is noise (PF-AML-04, PF-ID-02).
- It works on the `XElement` (`caex.Node`), not on wrappers. That is also the way to compare objects reliably (PF-AML-02).

### SN-07 Deterministic UUID from parts

Use when: any CAEX id derived from model ids (elements, hierarchies, interfaces, links, classes).

```csharp
private const string Namespace = "efl-aml-mapper";
private const string Separator = "\0";

public static string For(string kind, params string[] parts)
{
    var seed = Namespace + Separator + kind + Separator + string.Join(Separator, parts);
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));

    var bytes = new byte[16];
    Array.Copy(hash, bytes, 16);

    // Stamp version 5 and the RFC 4122 variant so the result is a well
    // formed UUID. Note the byte indices: Guid(byte[]) reads the first
    // three fields little endian, so the version nibble that shows up in
    // the rendered form comes from byte 7, not byte 6.
    bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
    bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

    return new Guid(bytes).ToString("B");
}

/// Derived from the owner's CAEX id, not from the model id: two copies of the
/// same model in one document would otherwise share interface ids.
public static string Interface(string ownerCaexId, string interfaceName) =>
    For("interface", ownerCaexId, interfaceName);
```

Source: `starter/dotnet/Efl.Conversion/EflIds.cs` (`For`, `Interface`)

Watch out:
- Version nibble on byte 7, variant on byte 8 (PF-ID-03).
- Salt child ids with the owner's CAEX id (PF-ID-06).
- `ToString("B")` writes braces. The modeler side uses the language id, not the CAEX id; if you ever compare the two forms, compare brace-insensitively (PF-ID-04).

### SN-08 Instance from a SystemUnitClass with a deterministic id

Use when: creating any InternalElement of the language.

```csharp
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

// Caller: create, set the id, insert, then write attributes.
var classes = EflWrite.Classes(document);
var ids = new IdSpace(document);

var element = EflWrite.CreateInstance(classes, node.Kind == EflNodeKind.Step ? EflNames.Step : EflNames.Store);
element.ID = EflIds.DistinctElement(node.Id, ids);
EflWrite.Append(diagram, element);
EflWrite.WriteNode(element, node, created: true);
```

```csharp
/// An id for an element, made distinct if that id is already taken in the document.
public static string DistinctElement(string modelId, IdSpace taken)
{
    var candidate = Element(modelId);
    for (var ordinal = 2; !taken.Claim(candidate); ordinal++)
        candidate = For("element", modelId, ordinal.ToString());
    return candidate;
}

public sealed class IdSpace
{
    private readonly HashSet<string> _taken;

    public IdSpace(CAEXDocument document)
    {
        _taken = new HashSet<string>(StringComparer.Ordinal);
        var root = document.CAEXFile?.Node;
        if (root == null) return;
        foreach (var attribute in root.DescendantsAndSelf().Attributes("ID"))
            if (!string.IsNullOrEmpty(attribute.Value)) _taken.Add(attribute.Value);
    }

    public bool Claim(string id) => _taken.Add(id);
}
```

Source: `starter/dotnet/Efl.Conversion/EflWrite.cs` (`Classes`, `CreateInstance`); caller `starter/dotnet/Efl.Conversion/EflToCaex.cs` (`AppendInto`); `starter/dotnet/Efl.Conversion/EflIds.cs` (`DistinctElement`, `IdSpace`)

Watch out:
- `CreateClassInstance` is an extension method: `using Aml.Engine.CAEX.Extensions;` (PF-AML-05).
- `TryAdd`, not `ToDictionary`, for anything built from a document (PF-AML-12).
- Read all ids once into `IdSpace`, not per element through the engine (PF-ID-12).

### SN-09 Insert keeping document order

Use when: every insert of an InternalElement.

```csharp
/// asFirst defaults to true in Aml.Engine, which reverses document order on
/// every insert. Always append.
internal static void Append(InternalElementType parent, InternalElementType child) => parent.Insert(child, false);

internal static void Append(InstanceHierarchyType parent, InternalElementType child) => parent.Insert(child, false);
```

Source: `starter/dotnet/Efl.Conversion/EflWrite.cs` (`Append`)

Watch out:
- PF-AML-01. Route every insert through this helper; one direct `Insert(child)` reverses order again.

### SN-10 Nested attribute values with invariant culture

Use when: writing a numeric attribute, or a child attribute under a typed parent attribute.

```csharp
private static AttributeType Typed(IObjectWithAttributes parent, string name, string attributeTypePath)
{
    var attribute = parent.Attribute.Append(name);
    attribute.AttributeDataType = "xs:string";
    attribute.RefAttributeType = attributeTypePath;
    return attribute;
}

private static void SetChild(AttributeType parent, string name, double value)
{
    var child = parent.Attribute[name] ?? parent.Attribute.Append(name);
    child.AttributeDataType = "xs:double";
    child.Value = Format(value);
}

/// Numbers with the invariant culture and without a trailing ".0". A comma
/// as the decimal separator would leave a document that other tools read as
/// a different number, or not at all.
internal static string Format(double value)
{
    if (!double.IsFinite(value)) value = 0;
    return value == Math.Floor(value) && Math.Abs(value) < 1e15
        ? ((long)value).ToString(CultureInfo.InvariantCulture)
        : value.ToString("0.##", CultureInfo.InvariantCulture);
}
```

Source: `starter/dotnet/Efl.Conversion/EflWrite.cs` (`Typed`, `SetChild`, `Format`)

Watch out:
- Reuse the existing child (`Attribute[name] ?? Append`) so an update does not append duplicates or move attributes to the end (PF-UPD-08).
- Never write `NaN` or infinity (PF-FMT-19); the reader side checks `double.IsFinite` too (SN-13).
- Exponent notation and more than two decimals break strict grammars on the way out (PF-FMT-18).

### SN-11 Bounds, port coordinates and waypoints

Use when: storing layout (pattern P7) on an element, on a port, or for a connection.

```csharp
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

/// The bend points of a flow, in the order from source to target.
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

internal static void SetPortCoordinate(ExternalInterfaceType port, EflPoint point)
{
    var coordinate = port.Attribute[EflNames.Attributes.PortCoordinate]
        ?? Typed(port, EflNames.Attributes.PortCoordinate, EflDiagramInterchange.PathOf(port, EflDiagramInterchange.Point));

    SetChild(coordinate, EflNames.Attributes.X, point.X);
    SetChild(coordinate, EflNames.Attributes.Y, point.Y);
}
```

Source: `starter/dotnet/Efl.Conversion/EflWrite.cs` (`SetBounds`, `ReplaceWaypoints`, `SetPortCoordinate`)

Watch out:
- Waypoints are bend points only. The docking points go to `PortCoordinate`, computed by `EflGeometry.DockingPoint` against the same outline the renderer's `getShapePath` draws (`starter/dotnet/Efl.Conversion/EflGeometry.cs`, SN-21). Writing docking points as waypoints gives stray bends in other tools (PF-LAY-12).
- A zero-size node makes the ellipse formula divide by zero (PF-LAY-11). `DockingPoint` returns the centre in that case (`starter/dotnet/Efl.Conversion/EflGeometry.cs`); keep that guard when you add shapes.
- The `RefAttributeType` paths point at the `OMG_DD` types in the form the owner's document uses: inline, or under the alias the document gave the file (`EflDiagramInterchange.PathOf`, SN-49, PF-AML-10). Never hard-code one spelling.

### SN-12 ExternalInterface and InternalLink in both encodings

Use when: writing a connection. Pick the encoding per connection type (recipe §3 P6) and delete the other branch.

Shared helpers:

```csharp
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
```

Ports on the nodes, one per connection end:

```csharp
foreach (var (flow, outgoing) in EndsAt(model, node.Id))
{
    var key = PortKey(node.Id, flow.Id, outgoing);
    if (ports.ContainsKey(key)) continue;

    var portName = PortName(flow, outgoing);             // "Out_<flowId>" or "In_<flowId>"
    var portClass = style == EflConnectionStyle.Link
        ? EflNames.InterfaceClass(outgoing ? EflNames.FlowOut : EflNames.FlowIn)
        : EflNames.InterfaceClass(EflNames.FlowEnd);

    ports[key] = EflWrite.EnsureInterface(
        element, portName, EflIds.Interface(element.ID!, portName), portClass);
}

/// The port of one flow end on one node. The direction is part of the key:
/// a flow from a node to itself has both ends on the same node, and without
/// it both links attached to a single port.
internal static string PortKey(string nodeId, string flowId, bool outgoing) =>
    nodeId + "|" + flowId + (outgoing ? "|out" : "|in");

/// Every flow end at a node, with its direction. A self loop yields two
/// ends, one outgoing and one incoming.
internal static IEnumerable<(EflFlow Flow, bool Outgoing)> EndsAt(EflModel model, string nodeId)
{
    foreach (var flow in model.Flows)
    {
        if (flow.SourceId == nodeId) yield return (flow, true);
        if (flow.TargetId == nodeId) yield return (flow, false);
    }
}
```

Encoding "Link" (connection without identity of its own):

```csharp
if (!ports.TryGetValue(PortKey(flow.SourceId, flow.Id, outgoing: true), out var source)) return;
if (!ports.TryGetValue(PortKey(flow.TargetId, flow.Id, outgoing: false), out var target)) return;

if (polyline.Count > 0) EflWrite.SetPortCoordinate(source, polyline[0]);
if (polyline.Count > 1) EflWrite.SetPortCoordinate(target, polyline[^1]);

// Bend points on the source side interface as Waypoint_1..n: an InternalLink
// has no attributes of its own.
EflWrite.ReplaceWaypoints(source, flow.Waypoints);

// The flow's own id becomes the link's id; its name is the label, or the id
// again when the model gives no name.
EflWrite.EnsureLink(diagram, flow.Name ?? flow.Id, flow.Id, source, target);
```

Encoding "Element" (connection with identity and attributes):

```csharp
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
```

Source: `starter/dotnet/Efl.Conversion/EflWrite.cs` (`EnsureInterface`, `EnsureLink`); `starter/dotnet/Efl.Conversion/EflToCaex.cs` (`AppendInto`, `WriteFlowAsLink`, `WriteFlowAsElement`, `PortKey`, `EndsAt`)

Watch out:
- Key the ports by node, flow **and direction**, and create one per connection end. Keyed by node and flow only, a flow from a node to itself got a single port, and both of its links attached to that one port. The starter's validator forbids self loops in EFL (EFL04), but the mapper must not be the part that breaks a language that allows them (test `AFlowFromANodeToItselfGetsTwoPortsAndSurvivesTheRoundTrip`, `starter/dotnet/Efl.Tests/RoundTripTests.cs`). The updater uses the same `EndsAt` and `PortKey` (SN-15).
- `Ensure...` reuses what is there, so a no-op sync does not rewrite interfaces (PF-UPD-02).
- Set `AInterface`/`BInterface` rather than composing `RefPartnerSideA/B` strings (PF-AML-06). Files from other tools may still use `ElementId:InterfaceId`; the starter reads both forms through `CaexToEfl.InterfaceIdOf` (SN-13), in the reader and in the updater (PF-AML-07; test `LinksWrittenAsElementAndInterfaceIdAreReadToo` in `starter/dotnet/Efl.Tests/RoundTripTests.cs`).
- The link lives on the diagram element, so all of the language's links are found under one element.

### SN-13 Read class, attributes, interfaces and links tolerantly

Use when: reading a hierarchy that may have been edited by hand or written by another tool.

```csharp
/// Which EFL class an element belongs to, from the system unit class path
/// and falling back to the role requirement. Null for anything that is not
/// EFL content, so foreign elements in the same hierarchy are skipped.
internal static string? ClassOf(InternalElementType element)
{
    foreach (var candidate in new[] { EflNames.Diagram, EflNames.Step, EflNames.Store, EflNames.Flow })
    {
        if (IsClass(element.RefBaseSystemUnitPath, candidate)) return candidate;
        if (element.RoleRequirements.Any(r => IsClass(r.RefBaseRoleClassPath, candidate))) return candidate;
    }
    return null;
}

/// Compared on the last segment, because the path may carry an alias of the document's choosing.
private static bool IsClass(string? path, string className)
{
    if (string.IsNullOrEmpty(path)) return false;
    var slash = path.LastIndexOf('/');
    var last = slash >= 0 ? path[(slash + 1)..] : path;
    return string.Equals(last, className, StringComparison.Ordinal);
}

private static string? IdentificationId(InternalElementType element) =>
    NonEmpty(element.Attribute[EflNames.Attributes.Identification]?.Attribute[EflNames.Attributes.Id]?.Value);

/// An attribute's value, falling back to its declared default.
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

/// The interface id a link side points at. Files from other tools write
/// either the bare interface id (CAEX 3.0) or "ElementId:InterfaceId"
/// (the CAEX 2.15 form). GUIDs contain no colon, so the part after the last
/// one is the interface.
internal static string? InterfaceIdOf(string? side)
{
    if (string.IsNullOrEmpty(side)) return side;
    var colon = side.LastIndexOf(':');
    return colon >= 0 ? side[(colon + 1)..] : side;
}
```

Links, direction from the interface class:

```csharp
foreach (var link in LinksBelow(diagram))
{
    var a = InterfaceIdOf(link.RefPartnerSideA);
    var b = InterfaceIdOf(link.RefPartnerSideB);
    if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) continue;
    if (!owner.TryGetValue(a!, out var nodeA) || !owner.TryGetValue(b!, out var nodeB)) continue;

    var portA = Port(diagram, a!);
    var portB = Port(diagram, b!);
    var outgoing = portA != null && IsClass(portA.RefBaseClassPath, EflNames.FlowOut);
    if (!outgoing && !(portB != null && IsClass(portB.RefBaseClassPath, EflNames.FlowOut))) continue;

    var source = outgoing ? nodeA : nodeB;
    var target = outgoing ? nodeB : nodeA;
    var sourcePort = outgoing ? portA : portB;
    var id = NonEmpty(link.ID) ?? NonEmpty(link.Name) ?? source + "_" + target;
    // ...
    if (sourcePort != null) flow.Waypoints.AddRange(ReadWaypoints(sourcePort));
    model.Flows.Add(flow);
}
```

What cannot be read becomes a warning, not an exception:

```csharp
if (source == null || target == null)
{
    // Kept as a warning rather than dropped in silence: an update
    // that took silence for "deleted in the diagram" would remove
    // the element from the document next.
    model.Warnings.Add($"Flow '{element.Name}' is not shown: its links do not resolve to two nodes.");
    model.Unresolved.Add(id);
    continue;
}
```

Source: `starter/dotnet/Efl.Conversion/CaexToEfl.cs` (`ClassOf`, `IsClass`, `IdentificationId`, `Number`, `Double`, `InterfaceIdOf`, `ReadFlowLinks`, `ReadFlowElements`)

Watch out:
- Class paths may carry an alias (PF-AML-08); compare on the last segment as above.
- Fall back to `DefaultValue` (PF-AML-11).
- The id fallback to the CAEX name must be the same in reader and updater (PF-ID-08; the updater does it in `UpdateInPlace`, `starter/dotnet/Efl.Conversion/EflUpdater.cs`).
- A skipped element must not be deleted by the next update (PF-UPD-06). The starter reports it as a warning and records its id in `EflModel.Unresolved` (`starter/dotnet/Efl.Conversion/Models.cs`); the updater reads that set through `CaexToEfl.UnresolvedIn` (`starter/dotnet/Efl.Conversion/CaexToEfl.cs`) before it writes and keeps those elements (SN-15).

### SN-14 Find a hierarchy by id then by name

Use when: the plugin remembers which hierarchy the canvas shows.

```csharp
private void Bind(InstanceHierarchyType? hierarchy)
{
    _hierarchyId = hierarchy?.ID;
    _hierarchyName = hierarchy?.Name;
}

private InstanceHierarchyType? CurrentHierarchy()
{
    if (_document == null) return null;
    var all = _document.CAEXFile.InstanceHierarchy;
    // Compared by ID: Aml.Engine wrappers are not reference stable.
    var byId = string.IsNullOrEmpty(_hierarchyId) ? null : all.FirstOrDefault(h => h.ID == _hierarchyId);
    if (byId != null)
    {
        _hierarchyName = byId.Name;
        return byId;
    }
    return _hierarchyName == null ? null : all.FirstOrDefault(h => h.Name == _hierarchyName);
}
```

The id is deterministic when the mapper creates the hierarchy:

```csharp
var hierarchy = document.CAEXFile.InstanceHierarchy.Append(name);
hierarchy.ID = EflIds.For("hierarchy", name);
```

Source: `starter/plugin/Aml.Editor.Plugin.Efl/EflPlugin.xaml.cs` (`Bind`, `CurrentHierarchy`); `starter/dotnet/Efl.Conversion/EflToCaex.cs` (`AppendInto`)

Watch out:
- Binding by name only creates a second hierarchy after a rename in the tree (PF-ID-11).
- Compare by `ID`, never by reference (PF-AML-02).
- The web app and the tool find a hierarchy by name or take the first that holds a diagram (`FindHierarchy` in `starter/dotnet/Efl.Web/Program.cs`); they have no remembered binding.

### SN-15 Update in place

Use when: writing an edited model back into the document it came from. Never regenerate the hierarchy.

Preconditions first, then match by the language id:

```csharp
public static EflUpdateSummary UpdateInPlace(
    CAEXDocument document, InstanceHierarchyType hierarchy, EflModel model, int diagramIndex = 0)
{
    var notes = new List<string>();

    // Everything that can fail is checked before the first write. An update
    // writes element by element, so a failure halfway leaves a document
    // that is part old and part new, and the next save stores exactly that.
    CheckFlowEnds(model);
    var classes = EflWrite.Classes(document);
    // ... find or create the diagram element ...

    var style = StyleOf(diagram);   // read from the document, never from a setting

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
    // ... Reuse(...) per node and flow, WriteNode ...
```

One port per connection end, with the same key as the writer (SN-12), then remove stale ports of this node and the links that used them. Ports of a flow the canvas could not show stay:

```csharp
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

// Ports of flows the diagram no longer has, and the links that used them.
// Ports of other classes belong to somebody else.
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

private static bool BelongsToUnresolved(string portName, HashSet<string> unresolved) =>
    unresolved.Any(id => portName == "Out_" + id || portName == "In_" + id);
```

Remove elements the model no longer has, and every link into them anywhere in the document. Elements the canvas could not show are kept with a note:

```csharp
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

private static void RemoveLinksTouching(
    CAEXDocument document, InternalElementType diagram, string? interfaceId, List<string> notes, string? elementName)
{
    if (string.IsNullOrEmpty(interfaceId)) return;

    foreach (var (link, owner) in AllLinks(document).ToList())
    {
        if (CaexToEfl.InterfaceIdOf(link.RefPartnerSideA) != interfaceId
            && CaexToEfl.InterfaceIdOf(link.RefPartnerSideB) != interfaceId) continue;

        // Compared by ID: Aml.Engine wrappers are not reference stable.
        if (owner.ID != diagram.ID)
            notes.Add($"Removed the link '{link.Name}' on '{owner.Name}': it pointed at '{elementName}', which the diagram no longer has.");

        link.Remove();
    }
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
```

Only language-owned attributes are touched:

```csharp
internal static bool IsLanguageOwned(string? attributeName) =>
    attributeName is EflNames.Attributes.Identification
        or EflNames.Attributes.ViewInformation
        or EflNames.Attributes.Duration
        or EflNames.Attributes.Capacity
    || (attributeName?.StartsWith(EflNames.Attributes.WaypointPrefix, StringComparison.Ordinal) ?? false);
```

Source: `starter/dotnet/Efl.Conversion/EflUpdater.cs` (`UpdateInPlace`, `BelongsToUnresolved`, `RemoveLinksTouching`, `CheckFlowEnds`); `starter/dotnet/Efl.Conversion/EflWrite.cs` (`IsLanguageOwned`)

Watch out:
- An update appends nothing at hierarchy level (PF-UPD-01).
- The CAEX name is written only when the language name changed, or on a fresh instance (`EflWrite.SetDisplayName`, `starter/dotnet/Efl.Conversion/EflWrite.cs`; PF-UPD-04).
- Links elsewhere in the document are removed with a note, not silently (PF-UPD-05).
- Attributes of another kind are removed only if the language owns them (`RemoveForeignLanguageAttributes`, `starter/dotnet/Efl.Conversion/EflWrite.cs`; PF-UPD-08).
- Elements the reader could not show are never deleted (PF-UPD-06); the note tells the user to repair them. Their ports on the nodes stay too, or the update cuts the half of the flow that still exists (the test asserts the `In_f3` port survives).
- Tests to copy: `AnUpdateKeepsWhatSomebodyElseAddedToTheDocument`, `RemovingANodeRemovesItsFlowsAndNothingElse`, `AnUpdateKeepsAFlowTheCanvasCouldNotShow`, `AFlowToANodeThatIsNotThereIsRefusedBeforeAnythingIsWritten`, `WritingTheSameModelBackChangesNoLineOfTheDocument`, `AFlowFromANodeToItselfGetsTwoPortsAndSurvivesTheRoundTrip` (`starter/dotnet/Efl.Tests/RoundTripTests.cs`).

### SN-16 Load AML text safely

Use when: any AML input, in the tool, the web app and tests.

```csharp
public static CAEXDocument Load(string text)
{
    ArgumentNullException.ThrowIfNull(text);

    CAEXDocument? document;
    try
    {
        document = CAEXDocument.LoadFromString(text);
    }
    catch (Exception ex) when (ex is not OutOfMemoryException)
    {
        throw new FormatException("This is not an AML document: " + ex.Message, ex);
    }

    return document?.CAEXFile == null
        ? throw new FormatException("This is not an AML document.")
        : document;
}

/// Read as text on purpose: CAEXDocument.LoadFromFile writes a copy of the
/// CAEX schema (CAEX_ClassModel_V.3.0.xsd) into the folder of the file.
public static CAEXDocument LoadFile(string path) => Load(File.ReadAllText(path));
```

Source: `starter/dotnet/Efl.Conversion/EflDocuments.cs` (`Load`, `LoadFile`)

Watch out:
- `LoadFromString` can return a document whose `CAEXFile` is null; the NullReferenceException then surfaces elsewhere as a 500 (see SN-45).
- `File.ReadAllText` decodes before the XML parser sees the declared encoding. For files that may declare a non-UTF-8 encoding, pass a stream (PF-FMT-13).

### SN-17 Save without schema clutter

Use when: writing a document to text or to a file.

```csharp
public static string ToXml(CAEXDocument document)
{
    using var stream = document.SaveToStream(prettyPrint: true);
    stream.Position = 0;
    using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
    return reader.ReadToEnd();
}

/// Saves through a stream. SaveToFile, like LoadFromFile, drops the CAEX
/// schema next to the file.
public static void SaveFile(CAEXDocument document, string path) => File.WriteAllText(path, ToXml(document));
```

Source: `starter/dotnet/Efl.Conversion/EflDocuments.cs` (`ToXml`, `SaveFile`)

Watch out:
- PF-AML-04. Add `CAEX_ClassModel_V.3.0.xsd` to `.gitignore` anyway, in case something else calls `SaveToFile`.

### SN-18 Validator rule with element id

Use when: adding a structural rule from the Phase 1 worksheet (A3).

```csharp
/// One finding. The element id is the language's own, so a finding can select the element on a canvas.
public sealed record EflFinding(string Rule, EflSeverity Severity, string Message, string? ElementId = null)
{
    public override string ToString() => $"{Severity.ToString().ToUpperInvariant()} {Rule}: {Message}";
}

public static IReadOnlyList<string> RuleIds { get; } =
    new[] { "EFL01", "EFL02", "EFL03", "EFL04", "EFL05", "EFL06", "EFL07" };

private static IEnumerable<EflFinding> CheckFlows(EflModel model)
{
    foreach (var flow in model.Flows)
    {
        if (model.FindNode(flow.SourceId) == null)
            yield return new EflFinding("EFL03", EflSeverity.Error,
                $"Flow '{flow.Id}' starts at '{flow.SourceId}', which is not a node of this diagram.", flow.Id);
        // ...
        if (flow.SourceId == flow.TargetId)
            yield return new EflFinding("EFL04", EflSeverity.Error,
                $"Flow '{flow.Id}' starts and ends at the same node.", flow.Id);
    }
}
```

The test that keeps the rule list honest:

```csharp
private static readonly Dictionary<string, Func<EflModel>> Broken = new()
{
    ["EFL04"] = () =>
    {
        var model = RoundTripTests.Sample();
        model.Flows[0].TargetId = model.Flows[0].SourceId;
        return model;
    },
    // one entry per rule id
};

[Theory]
[MemberData(nameof(RuleIds))]
public void EachRuleFiresOnItsOwnBrokenModelAndOnlyThere(string rule)
{
    Assert.True(Broken.ContainsKey(rule), $"No broken model for {rule}; add one to {nameof(Broken)}.");
    var fired = Rules(Broken[rule]());
    Assert.Contains(rule, fired);
    if (rule != "EFL02") Assert.Equal(new[] { rule }, fired);
}
```

Source: `starter/dotnet/Efl.Conversion/EflValidator.cs` (`EflFinding`, `EflValidator.RuleIds`, `CheckFlows`); `starter/dotnet/Efl.Tests/ValidatorTests.cs` (`Broken`, `EachRuleFiresOnItsOwnBrokenModelAndOnlyThere`)

Watch out:
- Validate the model, not the document, so the canvas and a file are checked the same way. Skip documents that hold no element of the language (PF-PLG-11).
- A rule that fires on good models teaches people to ignore findings. Modelling advice is not a rule.

### SN-19 Layered layout for nodes without position

Use when: a model comes from a file without layout, before it is shown or written (pattern P8).

```csharp
// Web app, tool and plugin all call it the same way:
var model = CaexToEfl.Read(hierarchy).First();
var arranged = EflLayout.ArrangeMissing(model);   // returns how many nodes were placed

/// Positions every node without bounds. Returns how many were placed.
public static int ArrangeMissing(EflModel model)
{
    ArgumentNullException.ThrowIfNull(model);

    var unplaced = model.Nodes.Where(n => n.Bounds == null).ToList();
    if (unplaced.Count == 0) return 0;

    var back = BackEdges(model);            // break cycles first
    var column = Columns(model, back);      // longest path from a source
    var row = Rows(model, column, back);    // two barycentre sweeps

    foreach (var node in unplaced)
    {
        node.Bounds = new EflBounds(
            OriginX + column[node.Id] * ColumnSpacing,
            OriginY + row[node.Id] * RowSpacing,
            NodeWidth,
            NodeHeight);
    }

    RouteFlows(model, unplaced, column, row, back);   // bend points for flows a grid draws badly

    return unplaced.Count;
}
```

Which flows get bend points, and how:

```csharp
// Only flows between two nodes this call placed, and only flows without bend
// points of their own. Sorted by where the flow sits in the drawing, not by
// list position, so the lanes do not change when the same flows arrive in
// another order.
var candidates = model.Flows
    .Where(f => f.Waypoints.Count == 0 && placed.ContainsKey(f.SourceId) && placed.ContainsKey(f.TargetId))
    .OrderBy(f => column[f.SourceId])
    .ThenBy(f => row[f.SourceId])
    .ThenBy(f => column[f.TargetId])
    .ThenBy(f => row[f.TargetId])
    .ThenBy(f => f.Id, StringComparer.Ordinal)
    .ToList();

var routes = new List<Route>();
foreach (var flow in candidates)
{
    var from = column[flow.SourceId];
    var to = column[flow.TargetId];

    if (flow.SourceId == flow.TargetId)
        routes.Add(new Route(flow, RouteKind.SelfLoop, from + 1, -1));    // three bend points above the node
    else if (to <= from || back.Contains((flow.SourceId, flow.TargetId)))
        routes.Add(new Route(flow, RouteKind.Below, from + 1, to));       // own lane below the drawing
    else if (CrossesANode(model, flow))
        routes.Add(new Route(flow, RouteKind.Above, from + 1, to));       // own lane above the drawing
}
// ... AssignChannels: one x per vertical leg, spread over the gap between columns;
//     lanes 30 px apart outside the bounding box of every positioned node ...
```

Source: `starter/dotnet/Efl.Web/Program.cs` (the `/api/to-json` endpoint); `starter/dotnet/Efl.Conversion/EflLayout.cs` (`ArrangeMissing`, `RouteFlows`); tests `starter/dotnet/Efl.Tests/LayoutTests.cs`

Watch out:
- Never move a node that has bounds (PF-LAY-03), and never give bend points to a flow that has some or that touches a node with a position of its own.
- Break cycles before assigning columns, or cyclic models grow absurdly wide (PF-LAY-04). The starter starts at nodes nothing feeds, in id order (`EflLayout.BackEdges`); a language with a semantic start (a marking, an initial state) should start there (PF-LAY-05).
- Placing nodes is not enough. Straight, a back flow runs on top of the forward flow between the same two nodes, a flow that skips a column runs through the node in between, and a self loop has no length. Test the lines (no segment inside a foreign node box, no two flows sharing a segment), not only node overlap, and look at `npm run screenshot -- --arranged` (see [08 Layout](08-layout.md) §4.5).
- Arrange before writing a JSON import into the document, so the document never holds a diagram without layout (`ImportJson` in `starter/plugin/Aml.Editor.Plugin.Efl/EflPlugin.xaml.cs`).

---

## Modeler (JavaScript, diagram-js)

### SN-20 Diagram with a module list

Use when: setting up the modeler class.

```js
import Diagram from 'diagram-js';
import SelectionModule from 'diagram-js/lib/features/selection';
// ... one import per module below ...
import DirectEditingModule from 'diagram-js-direct-editing';

/** The language's own parts, as one didi module. */
const EflModule = {
  __init__: ['eflRenderer', 'eflRules', 'eflPalette', 'eflContextPad', 'eflLabelEditing'],
  eflRenderer: ['type', Renderer],
  eflRules: ['type', Rules],
  eflPalette: ['type', Palette],
  eflContextPad: ['type', ContextPad],
  eflLabelEditing: ['type', LabelEditing],
};

/**
 * The modules a working editor needs. Leaving one out does not fail loudly:
 * without `rules` nothing can be created, without `connection-preview` a flow
 * is drawn without feedback, without `keyboard` the delete key does nothing.
 */
const MODULES = [
  SelectionModule, OutlineModule, MoveCanvasModule, ZoomScrollModule, KeyboardModule,
  EditorActionsModule, RulesModule, ModelingModule, MoveModule, ResizeModule, SnappingModule,
  CreateModule, ConnectModule, ConnectionPreviewModule, BendpointsModule, GlobalConnectModule,
  LassoToolModule, HandToolModule, PaletteModule, ContextPadModule, DirectEditingModule,
  EflModule,
];

export default class EflModeler extends Diagram {
  constructor(options = {}) {
    super({
      canvas: { container: options.container },
      modules: [...MODULES, ...(options.additionalModules || [])],
    });
  }

  /** Replaces the diagram. Resolves with the reader's warnings. */
  async importModel(model) {
    if (typeof model === 'string') model = JSON.parse(model);
    const eventBus = this.get('eventBus');
    eventBus.fire('import.start', { model });

    this.clear();
    let warnings;
    try {
      warnings = importModel(this, model);
    } finally {
      // An import is not an edit: nothing to undo into.
      this.get('commandStack').clear();
    }

    this.get('canvas').zoom('fit-viewport', 'auto');
    eventBus.fire('import.done', { warnings });
    return { warnings };
  }

  async exportModel() {
    return JSON.stringify(exportModel(this), null, 2);
  }
}
```

Source: `starter/web/src/EflModeler.js` (`EflModule`, `MODULES`, `EflModeler`; the module imports at the top of the file)

Watch out:
- `ModelingModule` here is the starter's own module (SN-23), which depends on diagram-js modeling and replaces `elementFactory`, `layouter` and `connectionDocking`.
- Import must return only when the canvas is complete; a delayed import makes an export right after it empty (PF-FMT-06).
- Keep the public surface small (`importModel`, `exportModel`, `saveSVG`, `get`, `on`); the bridge and the tests depend only on it.
- Bundle without minification (the `esbuild.build` call for `efl.esm.js` in `starter/web/build.mjs`); didi falls back to parameter names (PF-DJS-03).

### SN-21 Custom renderer with getShapePath

Use when: drawing the language's shapes and connections.

```js
import BaseRenderer from 'diagram-js/lib/draw/BaseRenderer';
import { append as svgAppend, create as svgCreate } from 'tiny-svg';
import { componentsToPath, createLine } from 'diagram-js/lib/util/RenderUtil';
import Text from 'diagram-js/lib/util/Text';

/** Written onto every text element, so an exported SVG looks the same without the page's stylesheet. */
const LABEL_STYLE = { fontFamily: 'Arial, sans-serif', fontSize: 12, fill: '#222' };

export default class Renderer extends BaseRenderer {
  constructor(eventBus, canvas) {
    // Higher priority than the default renderer, so these types are ours.
    super(eventBus, 2000);
    this._canvas = canvas;
    // bpmn-js wraps this in a 'textRenderer' service; plain diagram-js has no
    // such service, so the renderer owns one.
    this._text = new Text({ style: LABEL_STYLE });
  }

  canRender(element) {
    return [STEP, STORE, FLOW].includes(element.type);
  }

  drawShape(parent, element) {
    const shape = element.type === STORE
      ? svgCreate('ellipse', { cx: element.width / 2, cy: element.height / 2,
        rx: element.width / 2, ry: element.height / 2, fill: '#fff', ...STROKE })
      : svgCreate('rect', { width: element.width, height: element.height, rx: 6, ry: 6, fill: '#fff', ...STROKE });
    svgAppend(parent, shape);

    const bo = element.businessObject || {};
    const lines = [bo.name || ''];
    if (bo.value !== undefined && bo.value !== null && bo.value !== '') lines.push(String(bo.value));
    const text = this._text.createText(lines.join('\n'), {
      box: { width: element.width, height: element.height },
      align: 'center-middle',
      padding: 4,
    });
    svgAppend(parent, text);
    return shape;
  }

  drawConnection(parent, element) {
    this._ensureArrow();
    const line = createLine(element.waypoints, { ...STROKE, fill: 'none', markerEnd: `url(#${ARROW_ID})` });
    svgAppend(parent, line);
    return line;
  }

  /** The outline connection docking crops against. Must match the drawing. */
  getShapePath(element) {
    const { x, y, width, height } = element;
    if (element.type === STORE) {
      const rx = width / 2;
      const ry = height / 2;
      return componentsToPath([
        ['M', x + rx, y],
        ['a', rx, ry, 0, 1, 1, 0, 2 * ry],
        ['a', rx, ry, 0, 1, 1, 0, -2 * ry],
        ['z'],
      ]);
    }
    return componentsToPath([['M', x, y], ['l', width, 0], ['l', 0, height], ['l', -width, 0], ['z']]);
  }

  getConnectionPath(connection) {
    const [first, ...rest] = connection.waypoints;
    return componentsToPath([['M', first.x, first.y], ...rest.map((p) => ['L', p.x, p.y])]);
  }
}

Renderer.$inject = ['eventBus', 'canvas'];
```

Source: `starter/web/src/draw/Renderer.js` (`Renderer`, `getShapePath`, `Renderer.$inject`; arrow marker in `_ensureArrow`)

Watch out:
- A new shape needs `getShapePath` here and `EflGeometry.DockingPoint` in the mapper changed together (recipe §4.3).
- Long names inside a narrow box break mid-word; draw them below the shape (PF-DJS-16).
- Inline styles on text and markers keep the exported SVG readable without the page's CSS (PF-DJS-15).

### SN-22 Element factory with random ids

Use when: always; the default factory's counter ids collide with ids from imported files.

```js
import BaseElementFactory from 'diagram-js/lib/core/ElementFactory';

export default class ElementFactory extends BaseElementFactory {
  create(elementType, attrs = {}) {
    const type = attrs.type || (elementType === 'connection' ? FLOW : undefined);
    const withDefaults = { ...attrs };
    if (type) withDefaults.type = type;

    if (!withDefaults.id) withDefaults.id = newId(type);
    if (!withDefaults.businessObject) withDefaults.businessObject = {};
    if (withDefaults.businessObject.id === undefined) withDefaults.businessObject.id = withDefaults.id;

    if (elementType === 'shape' && isNode(withDefaults)) {
      if (withDefaults.width === undefined) withDefaults.width = NODE_SIZE.width;
      if (withDefaults.height === undefined) withDefaults.height = NODE_SIZE.height;
    }

    return super.create(elementType, withDefaults);
  }
}

/** A short random id, prefixed with the local type name for readable files. */
export function newId(type) {
  const prefix = (type || 'element').split(':').pop().toLowerCase();
  const random = globalThis.crypto?.randomUUID
    ? globalThis.crypto.randomUUID().slice(0, 8)
    : Math.random().toString(16).slice(2, 10);
  return `${prefix}-${random}`;
}
```

Source: `starter/web/src/modeling/ElementFactory.js` (`ElementFactory`, `newId`)

Watch out:
- The element registry refuses a second element with the same id; the symptom is a palette drop that does nothing.
- If your language needs ids that are valid XML names, keep the prefix a letter (PF-ID-07).

### SN-23 Layouter with cropping

Use when: connections must end on the node outline, and user bend points must survive.

```js
import BaseLayouter from 'diagram-js/lib/layout/BaseLayouter';
import { getMid } from 'diagram-js/lib/layout/LayoutUtil';

export default class Layouter extends BaseLayouter {
  constructor(connectionDocking) {
    super();
    this._connectionDocking = connectionDocking;
  }

  layoutConnection(connection, hints = {}) {
    const source = hints.source || connection.source;
    const target = hints.target || connection.target;

    const current = hints.waypoints || connection.waypoints || [];
    const bends = current.length > 2 ? current.slice(1, -1) : [];

    const waypoints = [
      hints.connectionStart || getMid(source),
      ...bends,
      hints.connectionEnd || getMid(target),
    ];

    if (!source || !target) return waypoints;
    return this._connectionDocking.getCroppedWaypoints({ waypoints, source, target }, source, target);
  }
}

Layouter.$inject = ['connectionDocking'];
```

Registered by overriding the core service names:

```js
import ModelingModule from 'diagram-js/lib/features/modeling';
import CroppingConnectionDocking from 'diagram-js/lib/layout/CroppingConnectionDocking';

export default {
  __depends__: [ModelingModule],
  __init__: [registerHandlers],
  // Same names as the core services, so these replace them.
  elementFactory: ['type', ElementFactory],
  layouter: ['type', Layouter],
  connectionDocking: ['type', CroppingConnectionDocking],
};
```

Source: `starter/web/src/modeling/Layouter.js` (`Layouter`); `starter/web/src/modeling/index.js` (`registerHandlers` and the module definition)

Watch out:
- Without cropping, arrow heads end hidden in the target's centre and the stored end points are centres (PF-LAY-12).
- Re-layout only connections of shapes that moved; a global re-layout throws away user bend points (PF-LAY-08).

### SN-24 Rule provider

Use when: deciding what the canvas allows. Without `RulesModule` nothing can be created; without a rule that answers, non-command actions such as `connection.start` are refused.

```js
import RuleProvider from 'diagram-js/lib/features/rules/RuleProvider';

/**
 * How diagram-js decides: an action that is a command (shape.create,
 * elements.move, ...) is allowed when no rule answers, as long as a handler
 * exists. An action that is not a command, such as `connection.start` for the
 * global connect tool, is refused when no rule answers: without the rule below
 * the palette's flow tool does nothing. And a rule that returns `undefined`
 * counts as "no answer", so return `false` to forbid.
 */
export default class Rules extends RuleProvider {
  constructor(eventBus) {
    super(eventBus);
  }

  init() {
    this.addRule('shape.create', ({ shape, target }) => isNode(shape) && isRoot(target));
    this.addRule('elements.move', ({ shapes, target }) =>
      shapes.every((s) => isNode(s) || isFlow(s)) && (!target || isRoot(target)));
    this.addRule('shape.resize', ({ shape }) => isNode(shape));
    // Asked by the global connect tool before a flow is started from a node.
    this.addRule('connection.start', ({ source }) => isNode(source));
    this.addRule('connection.create', ({ source, target }) => canConnect(source, target));
    this.addRule('connection.reconnect', ({ connection, source, target }) =>
      isFlow(connection) && Boolean(canConnect(source, target)));
    this.addRule('connection.updateWaypoints', ({ connection }) => isFlow(connection));
    this.addRule('elements.delete', () => true);
  }
}

Rules.$inject = ['eventBus'];

/** A flow runs between two different nodes (validator rule EFL04). */
export function canConnect(source, target) {
  if (!isNode(source) || !isNode(target) || source === target) return false;
  return { type: FLOW };
}

function isRoot(element) {
  return Boolean(element) && !element.parent;
}
```

Source: `starter/web/src/rules/Rules.js` (`Rules`, `canConnect`, `isRoot`)

Watch out:
- Return `false` to forbid. A rule returning `undefined` counts as no answer: a command is then allowed (PF-DJS-02), a non-command action is refused.
- `connection.start` is not a command. Without its rule the palette's global connect tool silently does nothing (PF-DJS-19); the check 'a flow can be drawn with the palette tool and the mouse' in `starter/web/tools/verify-modeler.mjs` draws a flow with that tool and the mouse to catch it.
- Register `connection.reconnect`, not `reconnectStart`/`reconnectEnd` (PF-DJS-01).
- Every rule here also belongs in the validator (SN-18): a file from another tool never saw the canvas rules.

### SN-25 Palette provider

Use when: adding create entries and tools on the left edge.

```js
export default class Palette {
  constructor(palette, create, elementFactory, lassoTool, handTool, globalConnect) {
    this._create = create;
    this._elementFactory = elementFactory;
    this._lassoTool = lassoTool;
    this._handTool = handTool;
    this._globalConnect = globalConnect;
    palette.registerProvider(this);
  }

  getPaletteEntries() {
    const startCreate = (type) => (event) => {
      const shape = this._elementFactory.createShape({ type });
      this._create.start(event, shape);
    };

    return {
      'hand-tool': {
        group: 'tools',
        imageUrl: icon('hand'),
        title: 'Move the canvas',
        action: { click: (event) => this._handTool.activateHand(event) },
      },
      // 'lasso-tool', 'global-connect-tool', 'tool-separator' ...
      'create-step': {
        group: 'elements',
        imageUrl: icon('step'),
        title: 'Step',
        action: { dragstart: startCreate(STEP), click: startCreate(STEP) },
      },
    };
  }
}

Palette.$inject = ['palette', 'create', 'elementFactory', 'lassoTool', 'handTool', 'globalConnect'];
```

Source: `starter/web/src/palette/Palette.js` (`Palette`)

Watch out:
- Offer both `dragstart` and `click`; one alone is the classic "palette does not work" report.
- Test selectors on palette entries use `data-action` (check 'a node can be dropped from the palette with the mouse' in `starter/web/tools/verify-modeler.mjs`); title and aria attributes change between diagram-js versions (PF-TST-07).

### SN-26 Context pad provider

Use when: adding actions next to a selected element.

```js
export default class ContextPad {
  constructor(contextPad, modeling, connect, rules, commandStack) {
    this._modeling = modeling;
    this._connect = connect;
    this._rules = rules;
    this._commandStack = commandStack;
    contextPad.registerProvider(this);
  }

  getContextPadEntries(element) {
    const entries = {};

    if (isNode(element)) {
      entries.connect = {
        group: 'connect',
        imageUrl: icon('flow'),
        title: 'Draw a flow from here',
        action: {
          click: (event, target) => this._connect.start(event, target),
          dragstart: (event, target) => this._connect.start(event, target),
        },
      };
      entries.value = {
        group: 'edit',
        imageUrl: icon('value'),
        title: `Set ${valueLabel(element.type).toLowerCase()}`,
        action: { click: () => this.editValue(element) },
      };
    }

    if (this._rules.allowed('elements.delete', { elements: [element] })) {
      entries.delete = {
        group: 'edit',
        imageUrl: icon('delete'),
        title: 'Delete',
        action: { click: () => this._modeling.removeElements([element]) },
      };
    }
    return entries;
  }

  /** Asks for a value and stores it; exposed so a test can call it directly. */
  editValue(element, answer) {
    const current = element.businessObject.value;
    const text = answer !== undefined
      ? answer
      : window.prompt(valueLabel(element.type), current === undefined ? '' : String(current));
    if (text === null) return;

    const trimmed = String(text).trim();
    const value = trimmed === '' ? undefined : Number(trimmed.replace(',', '.'));
    if (value !== undefined && !Number.isFinite(value)) return;

    this._commandStack.execute(UPDATE_PROPERTIES, { element, properties: { value } });
  }
}

ContextPad.$inject = ['contextPad', 'modeling', 'connect', 'rules', 'commandStack'];
```

Source: `starter/web/src/context-pad/ContextPad.js` (`ContextPad`, `editValue`)

Watch out:
- The `answer` parameter lets a test drive the entry without a dialog (check 'renaming, setting a value and undoing go through the command stack' in `starter/web/tools/verify-modeler.mjs`).
- More than one or two attributes need a properties panel; bind editors to collection elements, not to the collection (PF-FMT-03).

### SN-27 Command handler for property updates

Use when: any user change to a business object. Never assign to the business object directly.

```js
export default class UpdatePropertiesHandler {
  constructor(eventBus) {
    this._eventBus = eventBus;
  }

  execute(context) {
    const { element, properties } = context;
    const bo = element.businessObject;

    context.oldProperties = {};
    for (const key of Object.keys(properties)) {
      context.oldProperties[key] = bo[key];
      assignOrDelete(bo, key, properties[key]);
    }
    return [element];
  }

  revert(context) {
    const { element, oldProperties } = context;
    const bo = element.businessObject;
    for (const key of Object.keys(oldProperties)) {
      assignOrDelete(bo, key, oldProperties[key]);
    }
    return [element];
  }
}

UpdatePropertiesHandler.$inject = ['eventBus'];

/** An empty value removes the attribute: a cleared name is no name, not an empty string. */
function assignOrDelete(target, key, value) {
  if (value === undefined || value === null || value === '') delete target[key];
  else target[key] = value;
}
```

Registering it:

```js
export const UPDATE_PROPERTIES = 'efl.updateProperties';

function registerHandlers(commandStack) {
  commandStack.registerHandler(UPDATE_PROPERTIES, UpdatePropertiesHandler);
}
registerHandlers.$inject = ['commandStack'];
// module: __init__: [registerHandlers]
```

Source: `starter/web/src/modeling/UpdatePropertiesHandler.js` (`UpdatePropertiesHandler`, `assignOrDelete`); `starter/web/src/modeling/index.js` (`UPDATE_PROPERTIES`, `registerHandlers`)

Watch out:
- Returning `[element]` marks it changed, so it is redrawn and `commandStack.changed` reaches the bridge.
- Every custom command needs `revert` from the start (PF-DJS-05); side effects written in `postExecute` must be recorded on the context too (PF-DJS-06).
- The `$inject` on `registerHandlers` matters as much as on classes (PF-DJS-03).

### SN-28 Direct editing provider

Use when: renaming an element in place on double click.

```js
export default class LabelEditing {
  constructor(eventBus, canvas, directEditing, commandStack) {
    this._canvas = canvas;
    this._commandStack = commandStack;
    directEditing.registerProvider(this);

    eventBus.on('element.dblclick', (event) => {
      if (isNode(event.element)) directEditing.activate(event.element);
    });

    // Leave the editor before anything else happens to the diagram, so a half
    // typed name is not applied to an element that was deleted meanwhile.
    eventBus.on(['commandStack.changed', 'drag.init', 'canvas.viewbox.changing', 'autoPlace'], () => {
      if (directEditing.isActive()) directEditing.complete();
    });
  }

  activate(element) {
    if (!isNode(element)) return undefined;
    const zoom = this._canvas.zoom();
    const { x, y } = this._canvas.viewbox();
    return {
      text: element.businessObject.name || '',
      bounds: {
        x: (element.x - x) * zoom,
        y: (element.y - y) * zoom,
        width: element.width * zoom,
        height: element.height * zoom,
      },
      style: { fontSize: `${12 * zoom}px`, textAlign: 'center' },
      options: { centerVertically: true },
    };
  }

  update(element, newText) {
    const name = newText.trim();
    if ((element.businessObject.name || '') === name) return;
    this._commandStack.execute(UPDATE_PROPERTIES, { element, properties: { name } });
  }
}

LabelEditing.$inject = ['eventBus', 'canvas', 'directEditing', 'commandStack'];
```

Source: `starter/web/src/label/LabelEditing.js` (`LabelEditing`)

Watch out:
- Clearing a name must remove it, not store a default (PF-DJS-04); `assignOrDelete` in SN-27 does that.
- The module list must contain `DirectEditingModule` (SN-20), or `directEditing` cannot be injected.

### SN-29 Import a model

Use when: putting an exchange-format model on an empty canvas.

```js
export function importModel(diagram, model) {
  const canvas = diagram.get('canvas');
  const elementFactory = diagram.get('elementFactory');
  const layouter = diagram.get('layouter');

  const warnings = [];
  if (!model || typeof model !== 'object') throw new Error('The model has to be a JSON object.');

  // A file without the field predates it and is version 1.
  const version = finite(model.formatVersion) ?? 1;
  if (version > FORMAT_VERSION) {
    warnings.push(`The file is format version ${version}; this modeler knows version ${FORMAT_VERSION}. `
      + 'Anything a newer version added is not shown and would be lost on saving.');
  }

  // An explicit root carries the diagram's own id and name. The implicit root
  // canvas creates on demand has no business object at all.
  const businessObject = { id: text(model.id) ?? 'diagram' };
  if (text(model.name)) businessObject.name = text(model.name);
  const root = elementFactory.createRoot({ id: '__root_' + businessObject.id, businessObject });
  canvas.setRootElement(root);

  const shapes = new Map();

  // Shapes first: a connection can only be added once both ends are on the canvas.
  for (const node of array(model.nodes)) {
    const id = text(node?.id);
    if (!id) { warnings.push('A node without an id was left out.'); continue; }
    if (shapes.has(id)) { warnings.push(`The id '${id}' is used more than once; the later node was left out.`); continue; }
    // ... type check, bounds with defaults ...
    const shape = elementFactory.createShape({ id, type, x: x ?? 0, y: y ?? 0,
      width: positive(bounds.width) ?? NODE_SIZE.width,
      height: positive(bounds.height) ?? NODE_SIZE.height,
      businessObject });
    canvas.addShape(shape, root);
    shapes.set(id, shape);
  }

  for (const flow of array(model.flows)) {
    const id = text(flow?.id);
    const source = shapes.get(text(flow?.source));
    const target = shapes.get(text(flow?.target));
    if (!id) { warnings.push('A flow without an id was left out.'); continue; }
    if (!source || !target) {
      warnings.push(`Flow '${id}' runs between nodes the model does not have and was left out.`);
      continue;
    }
    // ...
    const connection = elementFactory.createConnection({ id, type: FLOW, source, target, businessObject });
    connection.waypoints = layouter.layoutConnection(connection, {
      source,
      target,
      waypoints: [centre(source), ...bends, centre(target)],
    });
    canvas.addConnection(connection, root);
  }

  return warnings;
}
```

Source: `starter/web/src/io/json.js` (`FORMAT_VERSION`, `importModel`)

Watch out:
- Read a newer `formatVersion` as far as possible and say so; a missing field means version 1. The C# reader does the same (`EflJson.Read` in `starter/dotnet/Efl.Conversion/EflJson.cs`).
- One bad element is a warning, never an aborted import (PF-FMT-09).
- `canvas.addConnection` does not compute waypoints; call the layouter yourself (PF-LAY-08, PF-LAY-01).
- Clear everything before a second import (`this.clear()` in SN-20), or models mix (PF-FMT-08). Do not mutate the caller's object (PF-FMT-07).

### SN-30 Export with rounding and bend points only

Use when: turning the canvas into the exchange format.

```js
export function exportModel(diagram) {
  const canvas = diagram.get('canvas');
  const elementRegistry = diagram.get('elementRegistry');
  const root = canvas.getRootElement().businessObject;

  const model = { formatVersion: FORMAT_VERSION, id: root.id || 'diagram' };
  if (root.name) model.name = root.name;

  model.nodes = elementRegistry.filter(isNode).map((shape) => {
    const bo = shape.businessObject;
    const node = { id: shape.id, type: kindOfType(shape.type) };
    if (bo.name) node.name = bo.name;
    if (bo.value !== undefined) node.value = bo.value;
    node.bounds = { x: round(shape.x), y: round(shape.y), width: round(shape.width), height: round(shape.height) };
    return node;
  });

  model.flows = elementRegistry.filter(isFlow).map((connection) => {
    const bo = connection.businessObject;
    const flow = { id: connection.id, source: connection.source.id, target: connection.target.id };
    if (bo.name) flow.name = bo.name;
    const bends = connection.waypoints.slice(1, -1);
    if (bends.length) flow.waypoints = bends.map((p) => ({ x: round(p.x), y: round(p.y) }));
    return flow;
  });

  return model;
}

/** Two decimals: short files, no visible movement, no 0.30000000000000004. */
const round = (value) => Math.round(value * 100) / 100;
```

Source: `starter/web/src/io/json.js` (`exportModel`, `round`)

Watch out:
- Key order is fixed and matches `EflJson.Write`, with `formatVersion` first; the echo baseline (SN-39) compares strings, so the same diagram must give the same bytes.
- Export ids and plain values only, never live object graphs (PF-FMT-05).

### SN-31 SVG export without editor furniture

Use when: offering a picture of the diagram.

```js
async saveSVG(margin = 10) {
  const canvas = this.get('canvas');
  const layer = canvas.getActiveLayer();
  const box = layer.getBBox();
  const defs = canvas._svg.querySelector('defs');

  const x = box.x - margin;
  const y = box.y - margin;
  const width = box.width + 2 * margin;
  const height = box.height + 2 * margin;

  // The layer also holds editor furniture: invisible hit areas, selection
  // outlines and bend point handles. They are styled by the page's CSS, which
  // does not travel with the file, so outside the editor they show up as
  // black boxes. Only the drawing goes into the picture.
  const copy = layer.cloneNode(true);
  copy.querySelectorAll('.djs-hit, .djs-outline, .djs-bendpoints, .djs-segment-dragger, .djs-visual.djs-dragger')
    .forEach((node) => node.remove());

  const svg = '<?xml version="1.0" encoding="utf-8"?>\n'
    + `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" `
    + `viewBox="${x} ${y} ${width} ${height}" version="1.1">`
    + (defs ? `<defs>${innerSVG(defs)}</defs>` : '')
    + innerSVG(copy)
    + '</svg>';

  return { svg };
}
```

Source: `starter/web/src/EflModeler.js` (`saveSVG`)

Watch out:
- PF-DJS-15. The defs carry the arrow marker; without them the picture has no arrow heads.
- The test checks labels, markers and the absence of `djs-hit` and `djs-outline` (check 'the SVG picture has the drawing and none of the editor furniture' in `starter/web/tools/verify-modeler.mjs`).

### SN-32 Bridge with debounced changes import queue and echo baseline

Use when: connecting the modeler to a WebView2 host. Change only the payload name.

```js
const CHANGE_DEBOUNCE_MS = 300;

export function post(message) {
  try {
    window.chrome?.webview?.postMessage(message);
  } catch (e) {
    nativeConsole.error('postMessage failed', e);
  }
  window.dispatchEvent(new CustomEvent('efl-bridge-out', { detail: message }));
}

export function connectBridge(modeler, source = window.chrome?.webview) {
  const eventBus = modeler.get('eventBus');
  // ...
  // An import clears the command stack, which fires 'commandStack.changed'.
  // That is not a user edit and must not be reported as one.
  let importing = false;
  let pending = null;

  eventBus.on('commandStack.changed', () => {
    if (importing) return;
    clearTimeout(pending);
    pending = setTimeout(async () => {
      const model = await exportNow();
      if (model !== null) post({ type: 'changed', model });
    }, CHANGE_DEBOUNCE_MS);
  });

  // Imports run one after the other. Two arriving close together would
  // otherwise interleave at the await, and the first one's `finally` would
  // re-enable change reports while the second is still filling the canvas.
  let queue = Promise.resolve();

  const runImport = async (json) => {
    importing = true;
    clearTimeout(pending);
    let warnings;
    try {
      ({ warnings } = await modeler.importModel(json));
    } catch (e) {
      // No 'imported' after a failure. The host treats 'imported' as "the
      // canvas now shows what I sent" and drops its unsaved edits; after a
      // failed import that is not true, and the edits would be lost.
      post({ type: 'error', message: 'Import failed: ' + (e?.stack || e) });
      return;
    } finally {
      importing = false;
    }
    // The page's own export after the import is the host's echo baseline.
    post({ type: 'imported', model: await exportNow(), warnings });
  };

  const handle = async (msg) => {
    if (!msg || typeof msg !== 'object') return;
    try {
      switch (msg.type) {
        case 'importModel':
          queue = queue.then(() => runImport(msg.model));
          await queue;
          break;
        case 'requestSvg':
          post({ type: 'svg', svg: (await modeler.saveSVG()).svg });
          break;
        // 'requestExport', 'selectElement', 'setTheme' ...
        default:
          post({ type: 'log', level: 'warn', message: 'Unknown message type: ' + msg.type });
      }
    } catch (e) {
      post({ type: 'error', message: `Handling ${msg.type} failed: ` + (e?.stack || e) });
    }
  };

  source?.addEventListener('message', (e) => handle(e.data));

  post({ type: 'ready', url: location.href });

  // Returned so a test or a web page without WebView2 can drive the bridge.
  return { handle };
}
```

Page boot, with a visible error when the modeler fails:

```html
<script type="module">
  import { EflModeler, connectBridge, installDiagnostics } from './efl.esm.js';
  try {
    if (window.chrome?.webview) installDiagnostics();
    const modeler = new EflModeler({ container: document.getElementById('canvas') });
    await modeler.createNew();
    const bridge = connectBridge(modeler);
    window.efl = { modeler, bridge };
  } catch (e) {
    // show #boot-error and post { type: 'error' } to the host
  }
</script>
```

Source: `starter/web/src/bridge.js` (`CHANGE_DEBOUNCE_MS`, `post`, `connectBridge` with its `runImport`); `starter/web/index.html` (the inline `<script type="module">`)

Watch out:
- `ready` is posted after the listener is attached, never on navigation completed (PF-WV-01).
- A timer-based `importing` reset stops in hidden tabs; the starter resets in `finally`, not in `requestAnimationFrame` (PF-WV-08).
- The promise queue prevents PF-WV-09. The `imported` baseline prevents PF-WV-07.
- A failed import posts only `error`, never `imported`: the host would take the acknowledgement as "the canvas shows what I sent" and drop its unsaved edits (PF-WV-12; checked by 'a broken import is reported as an error and leaves the page working' in `starter/web/tools/verify-bridge.mjs`).
- `installDiagnostics` (`starter/web/src/bridge.js`) forwards console output and unhandled errors, because WebView2 has no visible console.
- Tests for exactly these timing rules: `starter/web/tools/verify-bridge.mjs`.

### SN-33 Playwright harness driving services

Use when: testing the modeler and the bridge in a real browser.

```js
/** Runs the named checks against a fresh page each and exits non-zero on failure. */
export async function run(title, checks) {
  const server = await serve();          // serves dist/ over http; modules do not load from file://
  const browser = await chromium.launch();
  let failed = 0;

  for (const [name, check] of Object.entries(checks)) {
    const page = await browser.newPage({ viewport: { width: 1200, height: 800 } });
    const errors = [];
    page.on('pageerror', (e) => errors.push(e.message));
    page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });

    // Record everything the page posts to a host before any page script runs.
    await page.addInitScript(() => {
      window.__posted = [];
      window.addEventListener('efl-bridge-out', (e) => window.__posted.push(e.detail));
    });

    try {
      await page.goto(`http://127.0.0.1:${server.address().port}/`);
      await page.waitForFunction(() => window.efl?.modeler, null, { timeout: 10000 });
      await check(page);
      if (errors.length) throw new Error('page errors: ' + errors.join(' | '));
      console.log(`  ok    ${name}`);
    } catch (e) {
      failed++;
      console.log(`  FAIL  ${name}\n        ${String(e.stack || e).split('\n').join('\n        ')}`);
    } finally {
      await page.close();
    }
  }

  await browser.close();
  server.close();
  if (failed) process.exit(1);
}
```

A check that plays the host and reads captured messages:

```js
const posted = (page, type) => page.evaluate((t) => window.__posted.filter((m) => m.type === t), type);
const settle = (page) => page.waitForTimeout(600); // longer than the change debounce

await run('bridge', {
  async 'an import answers with the model as baseline and reports no change'(page) {
    await page.evaluate(() => { window.__posted.length = 0; });
    await page.evaluate((model) => window.efl.bridge.handle({ type: 'importModel', model }), sample);
    await settle(page);

    const imported = await posted(page, 'imported');
    assertEqual(imported.length, 1, 'imported messages');
    assertEqual(JSON.parse(imported[0].model), JSON.parse(sample), 'baseline');
    assertEqual((await posted(page, 'changed')).length, 0, 'changed messages during import');
  },
});
```

A check that drives diagram-js services instead of pixels:

```js
const allowed = await page.evaluate(() => {
  const rules = window.efl.modeler.get('rules');
  const registry = window.efl.modeler.get('elementRegistry');
  const fill = registry.get('fill');
  return {
    other: rules.allowed('connection.create', { source: fill, target: registry.get('cap') }),
    self: rules.allowed('connection.create', { source: fill, target: fill }),
    // Not a command: refused unless a rule answers.
    start: rules.allowed('connection.start', { source: fill }),
  };
});
assertEqual(allowed, { other: { type: 'efl:Flow' }, self: false, start: true }, 'rules');
```

Source: `starter/web/tools/harness.mjs` (`run`); `starter/web/tools/verify-bridge.mjs` (`posted`, `clearPosted`, `settle` and the first checks); `starter/web/tools/verify-modeler.mjs` (check 'rules allow a flow between two nodes and refuse one to itself')

Watch out:
- Position-based clicks are flaky; use services, and keep a few mouse tests for the wiring only (PF-TST-08). The starter has two: dropping a node from the palette and drawing a flow with the connect tool (the first two mouse checks in `starter/web/tools/verify-modeler.mjs`); the second fails without the `connection.start` rule (SN-24), which no API-only test of `connection.create` would notice.
- The page must be served over http, the same reason the plugin uses a virtual host (SN-36).
- Tests do not see a misplaced label or a flow drawn over another. `npm run screenshot` writes a PNG and the SVG export of the example into `web/screenshots/` (ignored by git) through the same harness; `-- --arranged` strips the layout first and places it again with `eflmap arrange` (`starter/web/tools/screenshot.mjs`: the header comment and the `if (arranged)` block).

---

## Plugin (C#, WPF, WebView2)

### SN-34 PluginViewBase skeleton

Use when: creating the plugin view class.

```csharp
public partial class EflPlugin : PluginViewBase, INotifyAMLDocumentLoad, ISupportsThemes
{
    public EflPlugin()
    {
        InitializeComponent();

        // No dots, slashes or spaces: the editor turns DisplayName into a WPF
        // x:Name and an XML element name in its configuration, and both refuse
        // them. The failure is an exception at startup with no hint to the cause.
        DisplayName = "EFLStarter";
        IsReactive = true;

        PluginLog.Init();
        PluginLog.DebugEnabled = _settings.DebugLogging;
        PluginLog.OnLine += AppendLogLine;

        // Created in the constructor, not in Loaded: the editor may call
        // DocumentLoaded before the view is loaded, and without a bridge object
        // that diagram would be lost instead of buffered.
        CreateModeler();

        Loaded += async (_, _) =>
        {
            FillSettings();
            await _modeler!.InitAsync();

            // A view created after the file was opened never gets DocumentLoaded.
            if (_document == null && EditorAccess.TryFindOpenDocument() is { } open) Attach(open);
        };

        // Deliberately no Unloaded handler. The editor fires Loaded and Unloaded
        // on every re-parenting of the visual tree (tab switches, docking,
        // resizing panels). Tearing the WebView down there reloaded the page each
        // time and threw away unsaved edits. DocumentUnLoaded and
        // ApplicationClose end its life.
    }

    public override string PackageName => "Aml.Editor.Plugin.Efl";
    public override DockPositionEnum InitialDockPosition => DockPositionEnum.DockContent;
    public override bool CanClose => true;
}
```

```xml
<!-- Metadata.xml: must match the code and the csproj version -->
<PackageName>Aml.Editor.Plugin.Efl</PackageName>
<DisplayName>EFLStarter</DisplayName>
<Version>0.1.0</Version>
```

Source: `starter/plugin/Aml.Editor.Plugin.Efl/EflPlugin.xaml.cs` (class `EflPlugin`, its constructor, `PackageName`, `InitialDockPosition`, `CanClose`); `starter/plugin/Aml.Editor.Plugin.Efl/Metadata.xml` (`<PackageName>`, `<DisplayName>`, `<Version>`)

Watch out:
- DisplayName letters, digits, underscore (PF-PLG-12). A package test checks it (SN-44).
- No teardown in `Unloaded` (PF-PLG-02).
- Bridge object in the constructor (PF-PLG-03); reflection lookup of the open document in `Loaded` (PF-PLG-04, `starter/plugin/Aml.Editor.Plugin.Efl/Bridge/EditorAccess.cs`, `TryFindOpenDocument`, `FindDocument`).
- Put commands into the view's own menu, not the editor toolbar (PF-PLG-07).

### SN-35 DocumentLoaded

Use when: implementing `INotifyAMLDocumentLoad`.

```csharp
/// Required by the contract, never raised. The editor answers this event by
/// calling DocumentLoaded again, which raised it again.
#pragma warning disable CS0067
public event EventHandler<CAEXDocument>? IsDocumentLoaded;
#pragma warning restore CS0067

public void DocumentLoaded(CAEXDocument document)
{
    if (document == null)
    {
        DocumentUnLoaded();
        return;
    }
    if (ReferenceEquals(document, _document)) return;

    // The editor sometimes hands out a new wrapper for the file that is
    // already open. Reattaching would reload the canvas and drop unsaved
    // edits, so identity is decided by the file, not by the object.
    if (SameFile(document, _document))
    {
        _document = document;
        return;
    }

    Attach(document);
}

/// File name plus OriginID, as AMLPetriNet does. The contract exposes no
/// path, so two files with the same name and origin in different folders
/// still count as one; switching between such files keeps the canvas.
private static bool SameFile(CAEXDocument? a, CAEXDocument? b)
{
    if (a == null || b == null) return false;

    static (string Origin, string File) Identity(CAEXDocument d) => (
        d.CAEXFile?.SourceDocumentInformation?.FirstOrDefault()?.OriginID ?? "",
        d.CAEXFile?.FileName ?? "");

    var (originA, fileA) = Identity(a);
    // Two new, unsaved documents share an empty identity; they are different.
    if (fileA.Length == 0) return false;
    return (originA, fileA) == Identity(b);
}

public void ApplicationClose()
{
    _modeler?.Dispose();
    _modeler = null;
    PluginLog.Shutdown();
}

/// The editor's callbacks are not documented as UI thread only, and WPF and
/// WebView2 are thread affine, so the whole body is marshalled.
private void Attach(CAEXDocument document)
{
    if (!Dispatcher.CheckAccess())
    {
        Dispatcher.Invoke(() => Attach(document));
        return;
    }
    // ...
}
```

Source: `starter/plugin/Aml.Editor.Plugin.Efl/EflPlugin.xaml.cs` (`IsDocumentLoaded`, `DocumentLoaded`, `SameFile`, `DocumentUnLoaded`, `ApplicationClose`, `Attach`)

Watch out:
- Raising `IsDocumentLoaded` loops about forty times a second (PF-PLG-01).
- Same file, new wrapper: keep the canvas (PF-CCH-03). Identity is `FileName` plus `OriginID`; two unsaved documents with an empty file name count as different.
- `DocumentUnLoaded` has no parameter; with several documents open the plugin cannot tell which one closed (PF-PLG-06, open).

### SN-36 WebView2 init with virtual host and handler removal

Use when: booting the WebView2 that shows the modeler.

```csharp
/// A virtual host gives the page a real origin, so ES modules load;
/// file:// URLs refuse module scripts.
private const string VirtualHost = "efl.local";
public const string AssetsFolder = "efl-assets";

public async Task InitAsync()
{
    if (_navigationStarted || _disposed) return;

    try
    {
        await _view.EnsureCoreWebView2Async();
    }
    catch (Exception ex)
    {
        RaiseError("The WebView2 runtime is missing or failed to start: " + ex.Message);
        return;
    }

    // The await can finish after the view was torn down.
    if (_disposed) return;

    var assets = AssetsPath;
    if (!File.Exists(Path.Combine(assets, "index.html")) || !File.Exists(Path.Combine(assets, "efl.esm.js")))
    {
        RaiseError($"The modeler bundle is missing in {assets}. Was 'npm run build' run before packing?");
        return;
    }

    var core = _view.CoreWebView2;
    core.Settings.AreDevToolsEnabled = true;
    core.Settings.IsStatusBarEnabled = false;
    core.SetVirtualHostNameToFolderMapping(VirtualHost, assets, CoreWebView2HostResourceAccessKind.Allow);

    _onMessage = (_, e) => OnMessage(e.WebMessageAsJson);
    _onNavigationStarting = (_, _) => _ready = false;
    _onNavigationCompleted = (_, e) =>
    {
        if (e.IsSuccess) return;
        _ready = false;
        RaiseError("Navigation failed: " + e.WebErrorStatus);
    };
    _onProcessFailed = (_, e) =>
    {
        _ready = false;
        RaiseError($"The WebView2 process failed ({e.ProcessFailedKind}).");
        if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited
            or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
        {
            _view.Dispatcher.BeginInvoke(() => core.Reload());
        }
    };

    core.WebMessageReceived += _onMessage;
    core.NavigationStarting += _onNavigationStarting;
    core.NavigationCompleted += _onNavigationCompleted;
    core.ProcessFailed += _onProcessFailed;

    _navigationStarted = true;
    core.Navigate($"https://{VirtualHost}/index.html");
}

public void Dispose()
{
    if (_disposed) return;
    _disposed = true;
    _ready = false;
    _pendingModel = null;

    try
    {
        var core = _view.CoreWebView2;
        if (core != null)
        {
            if (_onMessage != null) core.WebMessageReceived -= _onMessage;
            if (_onNavigationStarting != null) core.NavigationStarting -= _onNavigationStarting;
            if (_onNavigationCompleted != null) core.NavigationCompleted -= _onNavigationCompleted;
            if (_onProcessFailed != null) core.ProcessFailed -= _onProcessFailed;
        }
    }
    catch
    {
        // The core may already be gone; there is nothing left to unsubscribe.
    }

    _svgRequest?.TrySetResult(null);
}
```

Source: `starter/plugin/Aml.Editor.Plugin.Efl/Bridge/ModelerView.cs` (`VirtualHost`, `AssetsFolder`, `InitAsync`, `Dispose`)

Watch out:
- Check the disposed flag after every await (PF-WV-04).
- `NavigationStarting` drops readiness (PF-WV-02); `ProcessFailed` drops readiness and reloads (PF-WV-03).
- `AssetsPath` resolves next to the plugin assembly (`ModelerView.AssetsPath`); the bundle must be packed there (SN-43, PF-PLG-21).
- A plugin with several views should also dispose the WebView2 control itself when a view is torn down (PF-WV-05).

### SN-37 Ready handshake with buffered push

Use when: sending a model to the page.

```csharp
/// Returns false when it was only buffered because the page is not ready yet.
public bool ImportModel(string json)
{
    if (_disposed || string.IsNullOrEmpty(json)) return false;
    if (!_ready)
    {
        _pendingModel = json;     // only the newest push is kept
        return false;
    }
    if (_inReadyHandler) _importsDuringReady++;
    Post(new { type = MessageType.ImportModel, model = json });
    return true;
}

private void Post(object message) =>
    _view.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(message));

// in OnMessage:
case MessageType.Ready:
    _ready = true;

    // The host's handler runs first: it knows whether there are
    // unsaved edits to bring back. A diagram buffered before the boot
    // goes out only if the handler sent nothing, because the buffer is
    // older than whatever the handler decided just now.
    var buffered = _pendingModel;
    _pendingModel = null;
    _importsDuringReady = 0;
    _inReadyHandler = true;
    try
    {
        Ready?.Invoke();
    }
    finally
    {
        _inReadyHandler = false;
    }
    if (buffered != null && _importsDuringReady == 0) ImportModel(buffered);

    if (_pendingTheme is { } theme)
    {
        _pendingTheme = null;
        SetTheme(theme);
    }
    break;
```

Source: `starter/plugin/Aml.Editor.Plugin.Efl/Bridge/ModelerView.cs` (`ImportModel`, `Post`, the `MessageType.Ready` case in `OnMessage`)

Watch out:
- The model travels as a JSON string inside a serialised envelope; never build the envelope by string concatenation (PF-WV-06).
- Flushing the buffer after the handler's own push overwrote fresh edits (PF-WV-10). The counters `_inReadyHandler`/`_importsDuringReady` decide it, so the host's Ready handler needs no flag of its own (SN-39).
- Message type names live once per side: `MessageType` in `starter/plugin/Aml.Editor.Plugin.Efl/Bridge/BridgeMessage.cs` and the header of `starter/web/src/bridge.js` (PF-WV-11).

### SN-38 Request and response with timeout

Use when: the host needs an answer from the page (SVG, export).

```csharp
public async Task<string?> RequestSvgAsync(TimeSpan timeout)
{
    if (_disposed || !_ready) return null;

    var answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
    _svgRequest = answer;
    try
    {
        Post(new { type = MessageType.RequestSvg });
        var first = await Task.WhenAny(answer.Task, Task.Delay(timeout));
        return first == answer.Task ? await answer.Task : null;
    }
    finally
    {
        _svgRequest = null;
    }
}

// in OnMessage:
case MessageType.Svg:
    _svgRequest?.TrySetResult(message.Svg);
    break;
```

The caller, an `async void` event handler with everything guarded:

```csharp
private async void ExportSvg_Click(object sender, RoutedEventArgs e)
{
    // async void is right for an event handler, and only there; everything
    // inside is guarded because an exception here would take the editor down.
    try
    {
        var svg = _modeler == null ? null : await _modeler.RequestSvgAsync(TimeSpan.FromSeconds(10));
        if (string.IsNullOrEmpty(svg))
        {
            SetStatus("The canvas did not answer the request for a picture.");
            return;
        }
        // ... SaveFileDialog, File.WriteAllText ...
    }
    catch (Exception ex)
    {
        PluginLog.Error("SVG export failed", ex);
        SetStatus("SVG export failed: " + ex.Message);
    }
}
```

Source: `starter/plugin/Aml.Editor.Plugin.Efl/Bridge/ModelerView.cs` (`RequestSvgAsync`, the `MessageType.Svg` case in `OnMessage`); `starter/plugin/Aml.Editor.Plugin.Efl/EflPlugin.xaml.cs` (`ExportSvg_Click`)

Watch out:
- `RunContinuationsAsynchronously` keeps the continuation off the WebView2 message callback.
- `Dispose` completes a waiting request with null (SN-36), so a closed view never hangs the caller.

### SN-39 Echo baseline and pending model

Use when: deciding whether a `changed` message is a user edit.

```csharp
/// Every page boot: the first, a reload, a recovery after a renderer crash.
/// Unsaved edits win over the document.
private void OnModelerReady()
{
    if (_pendingModel != null)
    {
        _restoring = true;
        _modeler!.ImportModel(_pendingModel);
        PluginLog.Info("Page booted; restored the unsaved diagram.");
    }
    else if (CurrentHierarchy() is { } hierarchy)
    {
        // Read from the document now. Whatever was buffered before the boot
        // is older; the bridge drops it because this handler sent something.
        Show(hierarchy);
    }

    if (_theme != null) _modeler!.SetTheme(_theme);
}

private void OnImported(string? baseline, IReadOnlyList<string> warnings)
{
    _echoBaseline = baseline;
    // A restored canvas shows unsaved edits, so its baseline is not what the
    // document holds.
    _baselineIsDocument = !_restoring;
    if (_restoring) _restoring = false;
    else _pendingModel = null;
    foreach (var warning in warnings) PluginLog.Warn("[page] " + warning);
    UpdatePendingLabel();
}

private void OnChanged(string json)
{
    // Compared by content, not by a time window: a window long enough to
    // swallow import fallout also swallows a quick real edit.
    if (string.Equals(json, _echoBaseline, StringComparison.Ordinal))
    {
        // Back where the import left the canvas: import fallout, or edits
        // undone. If that state is the document's, nothing is pending any
        // more; keeping the older edit would write an undone change on the
        // next Update.
        _pendingModel = _baselineIsDocument ? null : json;
        UpdatePendingLabel();
        return;
    }

    _pendingModel = json;
    UpdatePendingLabel();
    // ... run the validator on the edit (SN-18) ...
}
```

Source: `starter/plugin/Aml.Editor.Plugin.Efl/EflPlugin.xaml.cs` (`OnModelerReady`, `OnImported`, `OnChanged`)

Watch out:
- PF-WV-07. The comparison only works because the page's export is deterministic (SN-30).
- An edit undone back to the baseline arrives as a `changed` equal to the baseline. When the baseline is the document's state (`_baselineIsDocument`), the pending model is cleared, so Update does not write the undone edit (PF-UPD-11). After a restore the baseline holds unsaved edits, and those stay pending.
- The Ready handler always sends something when there is something to show; it does not ask the bridge about buffered pushes. The bridge drops an older buffered push itself (SN-37).

### SN-40 Update button flow

Use when: writing the canvas back into the document.

```csharp
private void UpdateButton_Click(object sender, RoutedEventArgs e)
{
    if (_document == null) { SetStatus("No AML document is open."); return; }
    if (_pendingModel == null) { SetStatus("Nothing to write back: the diagram has no unsaved changes."); return; }

    try
    {
        var model = EflJson.Read(_pendingModel);
        var hierarchy = CurrentHierarchy();
        if (hierarchy != null && !LargeUpdateConfirmed(hierarchy, model))
        {
            SetStatus("Update cancelled; the diagram keeps its changes.");
            return;
        }

        EflUpdateSummary summary;
        if (hierarchy == null)
        {
            hierarchy = EflToCaex.AppendInto(_document, model, UniqueName("EFL_Diagrams"), _settings.ConnectionStyle);
            summary = new EflUpdateSummary(model.Nodes.Count + model.Flows.Count, 0, 0);
        }
        else
        {
            // In place, never regenerate.
            summary = EflUpdater.UpdateInPlace(_document, hierarchy, model, _diagramIndex);
        }

        Bind(hierarchy);
        _pendingModel = null;
        _echoBaseline = null;
        // ... picker, findings, log notes ...

        var saved = _settings.SaveAfterUpdate && EditorAccess.TrySave();
        SetStatus($"Updated '{hierarchy.Name}': {summary}."
                  + (summary.Notes.Count > 0 ? $" {summary.Notes.Count} note(s) in Diagnostics." : "")
                  + (saved ? " Saved." : " Press Ctrl+S to save."));
    }
    catch (Exception ex)
    {
        PluginLog.Error("Update failed", ex);
        SetStatus("Update failed: " + ex.Message);
    }
}

/// Counted by id, so moving and renaming never ask.
private bool LargeUpdateConfirmed(InstanceHierarchyType hierarchy, EflModel incoming)
{
    if (!_settings.AskBeforeLargeUpdates) return true;
    var stored = CaexToEfl.Read(hierarchy).ElementAtOrDefault(_diagramIndex);
    if (stored == null) return true;

    static HashSet<string> Ids(EflModel m) =>
        m.Nodes.Select(n => n.Id).Concat(m.Flows.Select(f => f.Id)).ToHashSet(StringComparer.Ordinal);

    var before = Ids(stored);
    var after = Ids(incoming);
    var added = after.Except(before).Count();
    var removed = before.Except(after).Count();
    if (added + removed <= _settings.LargeUpdateThreshold) return true;

    return MessageBox.Show(
        $"This update adds {added} element(s) and removes {removed}. Removed elements are gone from the document, "
        + "together with anything attached to them.\n\nApply it?",
        "Update", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;
}
```

Save by reflection, trying every candidate:

```csharp
private static readonly string[] SaveCommands =
    { "SaveAMLCommand", "SaveCommand", "SaveActiveDocumentCommand", "SaveCurrentAMLFileCommand" };

public static bool TrySave()
{
    try
    {
        var viewModel = Application.Current?.MainWindow?.DataContext;
        if (viewModel == null) return false;

        foreach (var name in SaveCommands)
        {
            if (viewModel.GetType().GetProperty(name, Members)?.GetValue(viewModel) is not ICommand command) continue;
            // A later candidate may be executable when this one is not.
            if (!command.CanExecute(null)) continue;

            command.Execute(null);
            PluginLog.Info($"Ran the editor's {name}.");
            return true;
        }

        PluginLog.Warn($"No save command found on {viewModel.GetType().FullName}; the editor may have renamed it.");
        return false;
    }
    catch (Exception ex)
    {
        PluginLog.Error("Running the editor's save command failed", ex);
        return false;
    }
}
```

Source: `starter/plugin/Aml.Editor.Plugin.Efl/EflPlugin.xaml.cs` (`UpdateButton_Click`, `LargeUpdateConfirmed`); `starter/plugin/Aml.Editor.Plugin.Efl/Bridge/EditorAccess.cs` (`SaveCommands`, `TrySave`)

Watch out:
- The contract cannot mark a document dirty; save by reflection is best effort, and the log says the command ran, not that the file was written (PF-PLG-08).
- Reflection paths break silently with a new editor version; pin the editor packages (PF-PLG-09).
- A modal dialog pumps the dispatcher; a plugin with timers must suppress them while the dialog is open (PF-UPD-09). The starter has no timers.
- Mapping runs on the UI thread; log timings before a large model makes the editor freeze (PF-UPD-13).

### SN-41 Settings in APPDATA

Use when: remembering user choices between sessions.

```csharp
public sealed class PluginSettings
{
    [JsonPropertyName("save_after_update")] public bool SaveAfterUpdate { get; set; } = true;
    [JsonPropertyName("ask_before_large_updates")] public bool AskBeforeLargeUpdates { get; set; } = true;
    [JsonPropertyName("large_update_threshold")] public int LargeUpdateThreshold { get; set; } = 5;

    [JsonPropertyName("connection_style")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EflConnectionStyle ConnectionStyle { get; set; } = EflConnectionStyle.Link;

    [JsonPropertyName("debug_logging")] public bool DebugLogging { get; set; }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AutomationMLEditor", "EflPlugin", "settings.json");

    public static PluginSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<PluginSettings>(File.ReadAllText(FilePath), Json) ?? new PluginSettings()
                : new PluginSettings();
        }
        catch
        {
            return new PluginSettings();   // a preference is not worth an error dialog
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception ex)
        {
            PluginLog.Debug("Could not save the settings: " + ex.Message);
        }
    }
}
```

Source: `starter/plugin/Aml.Editor.Plugin.Efl/Diagnostics/PluginSettings.cs` (`PluginSettings`)

Watch out:
- Filling the settings controls raises their change events; guard with a flag (`_fillingSettings` in `FillSettings` and `Settings_Changed`, `starter/plugin/Aml.Editor.Plugin.Efl/EflPlugin.xaml.cs`).
- The connection style setting only applies to new hierarchies; the updater reads the style from the document (SN-15). Delete the setting if your language has one encoding per connection type (recipe §3 P6).

### SN-42 Log to TEMP and a tab

Use when: every plugin; a blank panel without a log cannot be diagnosed.

```csharp
public static class PluginLog
{
    private const long MaxBytes = 5 * 1024 * 1024;
    private static readonly object Gate = new();
    private static StreamWriter? _writer;

    /// <summary>Every emitted line, for the Diagnostics tab.</summary>
    public static event Action<string>? OnLine;

    public static string LogDirectory => Path.Combine(Path.GetTempPath(), "efl-plugin");
    public static string FilePath => Path.Combine(LogDirectory, "efl-plugin.log");

    public static void Init()
    {
        lock (Gate)
        {
            if (_writer != null) return;
            try
            {
                Directory.CreateDirectory(LogDirectory);
                // One previous file is kept; older history is not worth the disk.
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                    File.Move(FilePath, FilePath + ".old", overwrite: true);

                var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
                _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                _writer.WriteLine($"==== plugin start {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====");
            }
            catch
            {
                _writer = null;   // no file log; the tab still works
            }
        }
    }

    private static void Emit(string level, string message)
    {
        // A line from the page may contain line breaks; keep one record per line.
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message.Replace("\r", "").Replace("\n", " | ")}";
        lock (Gate)
        {
            try { _writer?.WriteLine(line); } catch { _writer = null; }
        }
        try { OnLine?.Invoke(line); } catch { /* a UI handler must not break logging */ }
    }
}
```

The tab, capped and marshalled:

```csharp
private void AppendLogLine(string line) =>
    Dispatcher.BeginInvoke(() =>
    {
        LogBox.AppendText(line + Environment.NewLine);
        if (LogBox.LineCount > MaxLogLines)
            LogBox.Text = LogBox.Text[LogBox.GetCharacterIndexFromLineIndex(LogBox.LineCount - MaxLogLines)..];
        LogBox.ScrollToEnd();
    });
```

Source: `starter/plugin/Aml.Editor.Plugin.Efl/Diagnostics/PluginLog.cs` (`PluginLog`); `starter/plugin/Aml.Editor.Plugin.Efl/EflPlugin.xaml.cs` (`AppendLogLine`)

Watch out:
- Subscribed once in the constructor and never removed in `Unloaded`, so the tab keeps updating after tab switches (PF-PLG-20).
- Page console output reaches this log through the bridge's `log` messages (SN-32, the `MessageType.Log` case in `ModelerView.OnMessage`).

### SN-43 csproj packaging

Use when: building the nupkg the editor installs.

```xml
<PropertyGroup>
  <TargetFramework>net8.0-windows7.0</TargetFramework>
  <UseWPF>true</UseWPF>
  <!-- The editor loads plugins into their own load context. -->
  <EnableDynamicLoading>true</EnableDynamicLoading>
  <!-- Keep in step with Metadata.xml; the plugin tests check that they agree. -->
  <Version>0.1.0</Version>
  <!-- Anchored at this file: SolutionDir is empty when the project is built on its own. -->
  <BaseOutputPath>$(MSBuildThisFileDirectory)..\build\$(MSBuildProjectName)</BaseOutputPath>
  <GeneratePackageOnBuild>True</GeneratePackageOnBuild>
  <EflDistDir>$(MSBuildThisFileDirectory)..\..\web\dist</EflDistDir>
</PropertyGroup>

<ItemGroup>
  <!-- The Contract assembly must NOT be shipped: the editor has its own copy,
       and a second one next to the plugin makes the editor skip the plugin
       without a message. PrivateAssets keeps it out of the package. -->
  <PackageReference Include="Aml.Editor.Plugin.Contract" Version="[4.3.0]">
    <PrivateAssets>All</PrivateAssets>
  </PackageReference>
  <!-- Provided by the editor at runtime. -->
  <PackageReference Include="Aml.Engine" Version="[4.*, 5.0.0)">
    <ExcludeAssets>runtime</ExcludeAssets>
  </PackageReference>
  <PackageReference Include="Aml.Editor.API" Version="[2.3.0]">
    <ExcludeAssets>runtime</ExcludeAssets>
  </PackageReference>
  <!-- Not provided by the editor: packed below. -->
  <PackageReference Include="Microsoft.Web.WebView2" Version="[1.0.2903.40]" GeneratePathProperty="true">
    <PrivateAssets>all</PrivateAssets>
  </PackageReference>
</ItemGroup>

<ItemGroup>
  <ProjectReference Include="..\..\dotnet\Efl.Conversion\Efl.Conversion.csproj">
    <PrivateAssets>all</PrivateAssets>
  </ProjectReference>
</ItemGroup>

<ItemGroup>
  <None Include="$(OutputPath)$(AssemblyName).deps.json" Pack="true" PackagePath="lib\$(TargetFramework)" />
  <None Include="$(OutputPath)Efl.Conversion.dll" Pack="true" PackagePath="lib\$(TargetFramework)" />
  <None Include="Metadata.xml" CopyToOutputDirectory="PreserveNewest" />

  <!-- WebView2 assemblies from the NuGet cache. Link makes
       CopyToOutputDirectory work for an absolute Include. -->
  <None Include="$(PkgMicrosoft_Web_WebView2)\lib\net462\Microsoft.Web.WebView2.Core.dll"
        Link="Microsoft.Web.WebView2.Core.dll" CopyToOutputDirectory="PreserveNewest" Visible="false"
        Pack="true" PackagePath="lib\$(TargetFramework)" />
  <!-- ... Microsoft.Web.WebView2.Wpf.dll and Microsoft.Web.WebView2.Core.winmd the same way ... -->

  <!-- NuGet.Pack drops items under runtimes/win-x64/native/, so the loader is
       staged under obj/ first and packed from there. -->
  <None Include="$(IntermediateOutputPath)bundled\WebView2Loader.dll"
        Link="WebView2Loader.dll" CopyToOutputDirectory="PreserveNewest" Visible="false"
        Pack="true" PackagePath="lib\$(TargetFramework)\WebView2Loader.dll" />
</ItemGroup>

<!-- The modeler bundle, shipped as content next to the plugin -->
<ItemGroup>
  <None Include="$(EflDistDir)\**\*"
        Link="efl-assets\%(RecursiveDir)%(Filename)%(Extension)"
        CopyToOutputDirectory="PreserveNewest"
        Pack="true"
        PackagePath="lib\$(TargetFramework)\efl-assets\%(RecursiveDir)%(Filename)%(Extension)" />
</ItemGroup>

<!-- A plugin without its bundle loads and shows a blank panel. Fail the build instead. -->
<Target Name="VerifyModelerBundle" BeforeTargets="BeforeBuild">
  <Error Condition="!Exists('$(EflDistDir)\efl.esm.js')"
         Text="The modeler bundle is missing. Run 'npm install' and 'npm run build' in starter/web." />
</Target>

<Target Name="StageWebView2Loader" BeforeTargets="BeforeResolveReferences">
  <Copy SourceFiles="$(PkgMicrosoft_Web_WebView2)\runtimes\win-x64\native\WebView2Loader.dll"
        DestinationFolder="$(IntermediateOutputPath)bundled\"
        SkipUnchangedFiles="true" />
</Target>
```

Source: `starter/plugin/Aml.Editor.Plugin.Efl/Aml.Editor.Plugin.Efl.csproj` (the whole project file, with `<Target Name="VerifyModelerBundle">` and `<Target Name="StageWebView2Loader">`)

Watch out:
- Contract DLL in the package or plugin folder: the plugin vanishes without a message (PF-CCH-01).
- A `ProjectReference` without `PrivateAssets` becomes an unresolvable NuGet dependency (PF-PLG-13).
- WebView2 loader dropped by pack (PF-PLG-14); `Link` for items outside the project (PF-PLG-15).
- `BaseOutputPath` anchored at the project file (PF-PLG-17).
- Bump the version in the csproj and `Metadata.xml` together (PF-PLG-18).

### SN-44 Package tests

Use when: checking the nupkg before anybody installs it.

```csharp
private const string Framework = "lib/net8.0-windows7.0/";

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
    Assert.DoesNotContain(PackageEntries(), e => e.EndsWith("Aml.Editor.Plugin.Contract.dll", StringComparison.OrdinalIgnoreCase));
}

[Fact]
public void ThePackageDoesNotShipWhatTheEditorAlreadyLoads()
{
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
    Assert.Contains(Framework + file, PackageEntries());
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
    var metadata = XDocument.Load(Path.Combine(PluginFolder(), "Metadata.xml"));
    var shown = metadata.Descendants("DisplayName").Single().Value;
    Assert.Matches(new Regex("^[A-Za-z_][A-Za-z0-9_]*$"), shown);

    var code = File.ReadAllText(Path.Combine(PluginFolder(), "EflPlugin.xaml.cs"));
    Assert.Contains($"DisplayName = \"{shown}\";", code);
}
```

Source: `starter/plugin/Aml.Editor.Plugin.Efl.Tests/PackageTests.cs` (`PackageTests`)

Watch out:
- These tests read the package the last build produced. Run them after `dotnet build` of the plugin (CI does it through `dotnet test` on the solution, SN-48).
- Each check stands for a failure that is silent inside the editor: PF-CCH-01, PF-PLG-12, PF-PLG-14, PF-PLG-18, PF-PLG-21.

---

## Web app

### SN-45 Minimal API with 400 and 500 separated

Use when: exposing the mapper over HTTP.

```csharp
var builder = WebApplication.CreateBuilder(args);

// Diagrams are small. The cap keeps a stray upload from filling the memory.
const int MaxBodyBytes = 8 * 1024 * 1024;
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = MaxBodyBytes);

var app = builder.Build();
// ...
app.MapPost("/api/to-json", async (HttpContext ctx) => await Guarded(ctx, async () =>
{
    var document = EflDocuments.Load(await Body(ctx));
    var hierarchy = FindHierarchy(document, ctx.Request.Query["hierarchy"]);
    // ...
}));

/// Caller errors are 400 with a readable message; everything else is a 500 that
/// is also logged, so a server fault never looks like a bad file.
static async Task Guarded(HttpContext ctx, Func<Task> action)
{
    try
    {
        await action();
    }
    catch (BadRequest bad)
    {
        await Fail(ctx, StatusCodes.Status400BadRequest, bad.Message);
    }
    catch (Exception ex) when (ex is System.Xml.XmlException or FormatException or JsonException
                                  or InvalidOperationException or ArgumentException or BadHttpRequestException)
    {
        await Fail(ctx, StatusCodes.Status400BadRequest, "This file could not be read: " + ex.Message);
    }
    catch (Exception ex)
    {
        ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Efl.Web")
            .LogError(ex, "Unhandled failure in {Path}", ctx.Request.Path);
        await Fail(ctx, StatusCodes.Status500InternalServerError, "Conversion failed: " + ex.Message);
    }
}

static async Task Fail(HttpContext ctx, int status, string message)
{
    if (ctx.Response.HasStarted) return;
    ctx.Response.StatusCode = status;
    await ctx.Response.WriteAsJsonAsync(new { error = message });
}

file sealed class BadRequest(string message) : Exception(message);

/// <summary>Visible to WebApplicationFactory in tests.</summary>
public partial class Program;
```

Source: `starter/dotnet/Efl.Web/Program.cs` (the builder setup with `MaxBodyBytes`, the `/api/to-json` endpoint, `Guarded`, `Fail`)

Watch out:
- The 400 path depends on `EflDocuments.Load` turning non-AML text into a `FormatException` (SN-16). The end-to-end test posts `<not aml` and expects 400 (check 'a broken file is a 400 with a message, not a 500' in `starter/web/tools/verify-webapp.mjs`).
- `/api/update` is the lossless direction; `/api/to-aml` builds a new document and loses everything else in the original (the header comment of `starter/dotnet/Efl.Web/Program.cs`).

### SN-46 ASCII-safe info header

Use when: returning metadata (warnings, summary, hierarchy names) next to a file body.

```csharp
// Headers must stay ASCII; System.Text.Json escapes everything else.
ctx.Response.Headers["X-Efl-Info"] = JsonSerializer.Serialize(new
{
    hierarchy = hierarchy.Name,
    hierarchies = document.CAEXFile.InstanceHierarchy
        .Where(CaexToEfl.ContainsDiagram).Select(h => h.Name).ToArray(),
    arrangedNodes = arranged,
    warnings = model.Warnings.Take(5).ToArray(),
    warningCount = model.Warnings.Count,
});

await Text(ctx, "application/json", EflJson.Write(model));
```

Source: `starter/dotnet/Efl.Web/Program.cs` (the `X-Efl-Info` header in the `/api/to-json` endpoint)

Watch out:
- The default `JsonSerializer` encoder escapes non-ASCII characters, which is what keeps a name with an umlaut from breaking the header. Do not switch to a relaxed encoder here.
- Cap the list (`Take(5)`) and send the count; headers have size limits.

### SN-47 Staged bundle target

Use when: serving the modeler bundle from the web app's `wwwroot`.

```xml
<PropertyGroup>
  <EflDistDir>$(MSBuildThisFileDirectory)..\..\web\dist</EflDistDir>
</PropertyGroup>

<!-- Copied rather than linked: the Web SDK serves wwwroot from the content
     root, so linked items are found in a publish but not by `dotnet run`,
     and the local page silently loses the modeler. The staged files are
     ignored by git. Failing the build when the bundle is missing beats a
     page that loads without a canvas. -->
<Target Name="StageModeler" BeforeTargets="BeforeBuild">
  <Error Condition="!Exists('$(EflDistDir)\efl.esm.js')"
         Text="The modeler bundle is missing. Run 'npm install' and 'npm run build' in starter/web." />
  <Copy SourceFiles="$(EflDistDir)\efl.esm.js;$(EflDistDir)\efl.css"
        DestinationFolder="$(MSBuildThisFileDirectory)wwwroot"
        SkipUnchangedFiles="true" />
  <!-- Cleaned first: a renamed or deleted example would otherwise stay in
       wwwroot and keep being served. -->
  <RemoveDir Directories="$(MSBuildThisFileDirectory)wwwroot\examples" />
  <Copy SourceFiles="$(MSBuildThisFileDirectory)..\..\examples\bottling-line.json"
        DestinationFolder="$(MSBuildThisFileDirectory)wwwroot\examples"
        SkipUnchangedFiles="true" />
</Target>
```

With revalidation, so a deploy is not hidden behind a cached bundle:

```csharp
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
});
```

Source: `starter/dotnet/Efl.Web/Efl.Web.csproj` (`<Target Name="StageModeler">` and the `<PropertyGroup>` before it); `starter/dotnet/Efl.Web/Program.cs` (`UseDefaultFiles`, `UseStaticFiles`)

Watch out:
- Build order: `npm run build` before any `dotnet build` (PF-CI-05).
- Add the staged files to `.gitignore`.
- Clean a staged folder before copying into it. A copy only adds and overwrites, so an example that was renamed or deleted in the source stays in `wwwroot` and keeps being served.

---

## CI

### SN-48 Workflow skeleton

Use when: the repository made from the starter gets CI.

```yaml
name: build and test

on:
  push:
    branches: [main]
    tags: ['v*']
  pull_request:
    branches: [main]

permissions:
  contents: write   # the release step on a version tag

jobs:
  windows:
    # Windows: the plugin targets net8.0-windows and WPF.
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x

      - uses: actions/setup-node@v4
        with:
          node-version: 20
          cache: npm
          cache-dependency-path: web/package-lock.json

      # First: the web app and the plugin refuse to build without the bundle.
      - name: Build the modeler bundle
        working-directory: web
        run: |
          npm ci
          npm run build
          npx playwright install chromium

      - name: Test the mapper
        working-directory: dotnet
        run: dotnet test Efl.sln -c Release

      # The example files are written by the tool with a fixed timestamp. If
      # writing them again changes a byte, the mapper changed and the examples
      # were not written again.
      - name: Check that the example files are current
        working-directory: dotnet
        shell: bash
        run: |
          T="dotnet run --project Efl.Tool -c Release --"
          $T to-aml ../examples/bottling-line.json ../examples/bottling-line.links.aml --timestamp 2026-01-01T00:00:00Z
          $T to-aml ../examples/bottling-line.json ../examples/bottling-line.elements.aml --style element --timestamp 2026-01-01T00:00:00Z
          git diff --exit-code -- ../examples

      - name: Test the modeler and the bridge in a browser
        working-directory: web
        run: npm test

      - name: Test the web app end to end
        working-directory: web
        run: npm run test:webapp

      # Builds the Release package and checks what is in it.
      - name: Build and test the plugin package
        working-directory: plugin
        run: dotnet test Aml.Editor.Plugin.Efl.sln -c Release

      - uses: actions/upload-artifact@v4
        with:
          name: plugin-nupkg
          path: plugin/build/Aml.Editor.Plugin.Efl/Release/*.nupkg

      - name: Publish a release for a version tag
        if: startsWith(github.ref, 'refs/tags/v')
        uses: softprops/action-gh-release@v2
        with:
          files: plugin/build/Aml.Editor.Plugin.Efl/Release/*.nupkg
          generate_release_notes: true
          fail_on_unmatched_files: true
```

Source: `starter/.github/workflows/ci.yml` (the explanatory comment at the top left out)

Watch out:
- Run on every push and pull request, not only on tags (PF-CI-02).
- Bundle first (PF-CI-05). Steps that pass globs to tools need `shell: bash` on Windows runners (PF-CI-04).
- The example check needs `*.aml -text` in `.gitattributes` (`starter/.gitattributes`), or git converts line endings on a Windows checkout and `git diff` reports files the mapper did not change.
- If the repository references sibling checkouts, reproduce the local layout in the workflow (PF-CI-01). The starter layout has none.
- Disable xUnit parallelisation in every test assembly that touches Aml.Engine: `[assembly: CollectionBehavior(DisableTestParallelization = true)]` (`starter/dotnet/Efl.Tests/AssemblyInfo.cs`; PF-AML-03).
