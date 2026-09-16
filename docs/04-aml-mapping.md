# 04 Mapping a language to AutomationML with Aml.Engine

## In short

- Four libraries with one language prefix, every name and path in one static names class, fixed class ids, `Version` everywhere; publish the library as an `.aml` file and golden-test code against it (see §2.1, §2.9).
- Roles carry meaning, SUCs are templates; `CreateClassInstance` copies only the first `SupportedRoleClass`, append the rest yourself (see §2.2).
- Keep the language id and name in an `Identification` compound; the CAEX `Name` is a display name other tools rename (see §2.3, §8.4).
- Choose the connection encoding per connection kind: InternalLink between two typed ports, or a reified connection element with two links. The starter writes both (see §3).
- Layout goes into the shared `OMG_DD` types (`DD_Bounds`, `DD_Point`, `DD_Waypoint`), numbers with the invariant culture, waypoints replaced wholesale (see §4).
- Aml.Engine traps: `using Aml.Engine.CAEX.Extensions`, `Insert(child, false)`, wrappers are not reference stable, fresh GUIDs on create, process-wide caches (disable test parallelisation), `LoadFromString` may return a document with a null `CAEXFile`, `LoadFromFile`/`SaveToFile` drop the schema file next to the document (see §5).
- CAEX ids are name-based UUIDs from a namespaced seed, version nibble on byte 7, claimed against the whole document; interface and link ids derive from the owner's CAEX id (see §6).
- One logical element shown in several places becomes one CAEX element per place, linked with a `refObj` subtype and reunified on read (see §7).
- Update in place from day one: all preconditions before the first write, match by language id, write and remove only language-owned attributes, keep foreign content, unchanged model leaves the document byte-identical (see §8).
- Read tolerantly: alias-agnostic type recognition, id and default fallbacks, warnings instead of exceptions (see §9).
- Validate the model with stable rule ids and the modeler's element ids; validate the mapper with a round-trip check (see §10).
- Find your hierarchies by content and make every write target one explicitly (see §11).

Read fully when: you design the domain library, write the first converter, or implement update in place.
Skim when: you already have a working mapper and only need the Aml.Engine trap table (§5) or the checklist.

This chapter covers the mapper: the .NET library that turns a model of your graphical language into
CAEX 3.0 (AutomationML) and back, and that keeps an existing AML document in step with the editor
without destroying what other people put into it. It explains how to design the domain library
(roles, system unit classes, interfaces, attribute types, references), how to encode connections,
where layout goes, which Aml.Engine 4.x behaviours bite, how to make ids deterministic, how to
update in place, how to read tolerantly, how to validate and how to deal with several diagrams in
one document. Read it before you write the first `CAEXDocument.New_CAEXDocument()`, and again before
you implement update in place. The exchange format between the web modeler and the mapper is the
subject of [03 Exchange format](03-exchange-format.md); the plugin that calls the mapper is
[05 Editor plugin](05-editor-plugin.md); the tests that keep the mapper honest are in
[07 Testing and CI](07-testing-and-ci.md).

Two source projects did this for real. AMLPetriNet (ISO/IEC 15909-1 place/transition nets, library
`ISO_PT`) is the newer and cleaner one. fpb-aml-mapper (VDI/VDE 3682 formalised process
description, library `VDI_FPD`) is older and larger and carries more hard-won fixes, and the AMLFPB.js
plugin references its `FpbMapper.Conversion` project. The starter in this repository
(`starter/dotnet/Efl.Conversion/`) condenses the patterns of both for the toy language EFL and writes
both connection encodings side by side. The library design follows the three-phase method (Nabizada,
Drath, Fay; at Automatisierungstechnik, 2026, forthcoming), with earlier steps published at EKA 2026
and ETFA 2026.

---

## 1. CAEX 3.0 in one page

A CAEX file is one `CAEXFile` root with children in a fixed order. The schema order is
`SuperiorStandardVersion`, `SourceDocumentInformation` (at least one, required), `ExternalReference`,
`InstanceHierarchy`, `InterfaceClassLib`, `RoleClassLib`, `SystemUnitClassLib`, `AttributeTypeLib`
(AMLPetriNet: `libraries/CAEX_ClassModel_V.3.0.xsd:13-117`, and the same order is spelled out in
`AMLPetriNet: dotnet/PtMapper.Conversion/PtSelfContained.cs:31-36`).

| CAEX construct | What it is | What a language mapper uses it for |
|---|---|---|
| `InstanceHierarchy` (IH) | A tree of concrete objects | One diagram, or one model with several diagrams |
| `InternalElement` (IE) | A node in an IH, may nest | Every language element that has an identity (node, container, reified connection) |
| `RoleClassLib` / `RoleClass` | Semantic types, single inheritance via `RefBaseClassPath` | What an element *means* (PT_Place, FPD_ProcessOperator) |
| `SystemUnitClassLib` / `SystemUnitClass` (SUC) | Reusable templates with attributes, interfaces, children; `SupportedRoleClass` names the roles a template can play | What an element is *instantiated from* |
| `RoleRequirements` on an IE | The role an instance plays (`RefBaseRoleClassPath`) | Lets a reader recognise an element even when it was not created from your SUC |
| `RefBaseSystemUnitPath` on an IE | The SUC an instance was created from | The primary type signal for your reader |
| `InterfaceClassLib` / `InterfaceClass` | Types of connection points | Typed ports for connections (FlowOut, FlowIn, ArcSource, ...) |
| `ExternalInterface` on an IE | A port instance with an `ID` | Endpoint of an `InternalLink` |
| `InternalLink` | Joins two interfaces by id (`RefPartnerSideA`, `RefPartnerSideB`, both required) | A connection, or the coupling between a reified connection and its nodes |
| `AttributeTypeLib` / `AttributeType` | Reusable, nestable attribute structures | Identification compound, diagram interchange types, reference types |
| `Attribute` with `RefAttributeType` | A typed attribute, may nest | Every value your language stores |
| `ExternalReference` (`Alias`, `Path`) | Another CAEX file whose classes this file uses | AML base libraries, shared libraries (OMG_DD, ObjectReferences), your own library when it is kept outside |

Class paths are `LibraryName/Class/SubClass`. A path into a file behind an `ExternalReference` is
prefixed with the alias and `@`: `AutomationMLBaseLibrariesAMLEd22_11_0@AutomationMLBaseRoleClassLib/AutomationMLBaseRole/Structure`
(AMLPetriNet: `dotnet/PtMapper.Conversion/PtNames.cs:98-109`). The same class can therefore be named
two ways in two documents, depending on whether the library is carried inline or referenced. Your
reader must accept both (section 9) and your writer must follow whatever the document already does
(section 2.8).

A minimal instance written by the PT mapper looks like this (shape documented in
`AMLPetriNet: dotnet/PtMapper.Conversion/PtNetToCaex.cs:6-26`):

```
InstanceHierarchy "PetriNets"            ID stamped, see section 11
  PT_Net                                 RefBaseSystemUnitPath=ISO_PT_SystemUnitClassLib/PT_Net
    PT_Place / PT_Transition             one PT_NodeArcEnd interface per incident arc
    PT_Arc                               PT_ArcSource and PT_ArcTarget interfaces
    InternalLink                         arc end to node end, two per arc
```

---

## 2. Design the domain library

### 2.1 Four libraries, one prefix, one names class

Create one library per CAEX library kind, prefixed with the language: `ISO_PT_InterfaceClassLib`,
`ISO_PT_RoleClassLib`, `ISO_PT_SystemUnitClassLib`, `ISO_PT_AttributeTypeLib`
(AMLPetriNet: `dotnet/PtMapper.Conversion/PtNames.cs:17-20`), `VDI_FPD_*`
(fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpbMappings.cs:106-121`), `EFL_*`
(`starter/dotnet/Efl.Conversion/EflNames.cs:16-19`). Give the role class, the system unit class and
the concept the same name (`PT_Place` is both), and put every name and path in one static class so
the generator, the reader, the updater and the tests cannot drift apart
(AMLPetriNet: `dotnet/PtMapper.Conversion/PtNames.cs:3-38`).

Why: the FPB plugin once carried its own copy of the conversion layer with `FPD_*` paths while the
mapper had moved on to `VDI_FPD_*`; the drift cost hours of diagnosis and ended with the plugin
referencing the mapper project instead of copying it. Renaming classes later means touching documents
that already exist, so spend the minute on names up front (`starter/dotnet/Efl.Conversion/EflNames.cs:3-10`).

Put a `Version` on every library and class and bump it when the emitted shape changes
(fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpbMappings.cs:115-121`).

### 2.2 Roles carry meaning, system unit classes are templates

Declare the semantics in the RoleClassLib and mirror them in the SystemUnitClassLib. Each SUC names
its role through `SupportedRoleClass`, may add AML base roles as further supported roles, and repeats
the attributes the role declares so that instantiation brings them along:

```xml
<SystemUnitClass Name="PT_Net" ID="c1a8d000-0001-4000-8000-000000000001">
  <Version>1.0.0</Version>
  <Attribute Name="Identification" AttributeDataType="xs:string" RefAttributeType="ISO_PT_AttributeTypeLib/PT_Identification">
    <Attribute Name="id" AttributeDataType="xs:string" />
    <Attribute Name="name" AttributeDataType="xs:string" />
  </Attribute>
  <Attribute Name="refObj" AttributeDataType="xs:IDREF" RefAttributeType="ObjectReferences@AutomationML_ObjectReferences_AttributeTypeLib/refObj" />
  <SupportedRoleClass RefRoleClassPath="ISO_PT_RoleClassLib/PT_Net" />
  <SupportedRoleClass RefRoleClassPath="AutomationMLBaseLibrariesAMLEd22_11_0@AutomationMLBaseRoleClassLib/AutomationMLBaseRole/Structure" />
</SystemUnitClass>
```
(AMLPetriNet: `libraries/ISO_PT_DomainLibrary_v0.3.aml:95-104`)

The FPD library does the same and adds AML base roles where one fits: `FPD_Product` also supports
`AutomationMLBaseRole/Product`, `FPD_ProcessOperator` supports `Process`, `FPD_TechnicalResource`
supports `Resource`, while Energy and Information have no base equivalent
(fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpdLibraries.cs:241-266`).

Why both: the role is what makes an element recognisable to any AML tool and what a vendor's own SUC
can also claim; the SUC is what `CreateClassInstance` copies attributes and role requirements from.
Your reader should accept either signal (section 9).

Trap: `CreateClassInstance` turns only the first `SupportedRoleClass` into a `RoleRequirements`
entry. Add the rest yourself, or the AML base role silently disappears from every instance:

```csharp
var ie = (InternalElementType)suc.CreateClassInstance(className);

// CreateClassInstance only copies the first SupportedRoleClass into a
// RoleRequirement; PT_Net has two, so the rest are added here.
var present = new HashSet<string>(ie.RoleRequirements.Select(r => r.RefBaseRoleClassPath));
foreach (var supported in suc.SupportedRoleClass)
{
    if (!present.Contains(supported.RefRoleClassPath))
        ie.RoleRequirements.Append().RefBaseRoleClassPath = supported.RefRoleClassPath;
}
```
(AMLPetriNet: `dotnet/PtMapper.Conversion/PtWrite.cs:84-95`; the same fix in
fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs:1845-1861` and
`starter/dotnet/Efl.Conversion/EflWrite.cs:40-55`)

### 2.3 Shared attributes on one abstract base

Put what every element has on one abstract base role and base SUC: `PT_Element` carries
`Identification` and `ViewInformation`, and `PT_Place`, `PT_Transition`, `PT_Arc` inherit via
`RefBaseClassPath` (AMLPetriNet: `dotnet/PtMapper.Conversion/PtLibraries.cs:204-237` for roles,
`260-286` for SUCs). FPD uses `FPD_Object` with `Identification`, `Characteristics`, `ViewInformation`,
then `FPD_State` below it and the three concrete states below that
(fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpdLibraries.cs:102-126`). The FPD RoleClassLib is flat
(every role a direct child of the library) with inheritance expressed only through
`RefBaseClassPath`; the SUC library mirrors the hierarchy (`FpdLibraries.cs:6-11`).

Keep the container role separate (`PT_Net` derives from AML `Structure`, not from `PT_Element`), so
container attributes such as a net-level `refObj` do not leak onto nodes.

Store the language's own id and name in an `Identification` compound, not in the CAEX `Name`. The
CAEX `Name` is a display name that people and other tools rename. ISO_PT added `Identification` to
`PT_Net` in v0.3 because without it a round trip had nowhere to put the net id and renamed the net
after its label (AMLPetriNet: `dotnet/PtMapper.Conversion/PtLibraries.cs:197-200`). For FPD the
compound follows VDI 3682 (uniqueIdent, longName, shortName, versionNumber, revisionNumber), with the
field list kept in one class (fpb-aml-mapper: `dotnet/FpbMapper.Conversion/IdentificationSchema.cs:3-29`).

**Elements without a name.** Markers, connections and pseudo elements often have an id but no name in the language. Give such an element its language id as CAEX `Name` and leave `Identification/name` empty. A derived label (`"start [doorClosed]"`) as CAEX `Name` looks friendlier in the tree, but the updater then rewrites it on every update and overwrites a name someone gave the element by hand.

### 2.4 AttributeTypeLib: small, typed, with defaults

Declare only what the language really has. ISO_PT declares a single `PT_Identification` type
because ISO/IEC 15909-1 has a small ontology (AMLPetriNet: `dotnet/PtMapper.Conversion/PtLibraries.cs:296-315`);
FPD needs a large `FPD_Characteristic` structure because VDI 3682 Part 2 specifies one
(fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpdLibraries.cs:145-200`).

Use precise XML Schema types and a `DefaultValue` where the language defines one:
`InitialMarking` is `xs:nonNegativeInteger` with default `0`, `Weight` is `xs:positiveInteger` with
default `1` (AMLPetriNet: `dotnet/PtMapper.Conversion/PtLibraries.cs:347-361`). If you declare a
default, your reader must fall back to it (section 9).

Do not declare layout types in your own AttributeTypeLib. Use the shared `OMG_DD_AttributeTypeLib`
(section 4). The starter already does: it ships the published file in `starter/libraries/`, embeds it
as an assembly resource and writes every layout type path through `EflDiagramInterchange`, whose
comment states the reason (`starter/dotnet/Efl.Conversion/EflDiagramInterchange.cs:7-28`).

**Optional attributes: absent or empty.** Decide per optional attribute whether the writer leaves it out or writes it with an empty value, and make the reader treat both as "not set". Removing an attribute the class declares makes instances in the same document look different in the AutomationML Editor (some states show an entry action field, some do not); the trial run of this playbook chose "keep, empty" for optional texts.

### 2.5 InterfaceClassLib: an abstract port, direction in the type

Derive one abstract port from the AML base `Port` and give it the docking point attribute. Derive the
concrete interfaces from it:

- Link encoding (FPD, EFL link style): a pair per connection kind, the direction in the class name.
  `FPD_FlowOut`/`FPD_FlowIn`, `FPD_ParallelFlowOut`/`FPD_ParallelFlowIn`,
  `FPD_AlternativeFlowOut`/`FPD_AlternativeFlowIn`, and one symmetric `FPD_Usage`
  (fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpdLibraries.cs:49-75`, mapping table in
  `FpbMappings.cs:47-58`).
- Reified encoding (ISO_PT, EFL element style): node side and connection side are distinct classes.
  `PT_NodeArcEnd` on places and transitions, `PT_ArcSource` and `PT_ArcTarget` on the arc
  (AMLPetriNet: `dotnet/PtMapper.Conversion/PtLibraries.cs:147-171`).

Why direction in the type: a reader can tell the ends of a connection apart without following the
link, and a validator can check "flows are directed" by interface class alone (the OCL invariant
`FlowDirected` in fpb-aml-mapper: `dotnet/FpbMapper.Conversion/Rules/vdi3682-pure-rules.ocl:42-43`).
The starter states the same reasoning (`starter/dotnet/Efl.Conversion/EflLibraries.cs:155-157`).

Give every node one interface per connection end, named after the connection:
`Out_<arcId>` and `In_<arcId>` (AMLPetriNet: `dotnet/PtMapper.Conversion/PtWrite.cs:108-113`,
`starter/dotnet/Efl.Conversion/EflToCaex.cs:180-185`). Two parallel connections between the same pair
of nodes then stay apart. The FPD mapper instead names interfaces after their class and numbers
duplicates (`FPD_FlowOut`, `FPD_FlowOut_2`); if you do that, seed the counter from the highest suffix
already present, not from the count, or a later add mints a name that already exists
(fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs:782-814`).

**One interface per connection end, not per connection.** Key a node's ports by node, connection and direction. A connection from a node to itself has both ends on that node; keyed without the direction, the writer created one port and attached both links to it (found in the trial run, fixed in `starter/dotnet/Efl.Conversion/EflToCaex.cs:187-206`, `PortKey` and `EndsAt`, used by the writer at `:89-101` and by the updater at `starter/dotnet/Efl.Conversion/EflUpdater.cs:97-107`, with the test `AFlowFromANodeToItselfGetsTwoPortsAndSurvivesTheRoundTrip` at `starter/dotnet/Efl.Tests/RoundTripTests.cs:190-214`).

### 2.6 Cross-element and cross-diagram references: the refObj family

Use the official `AutomationML_ObjectReferences_AttributeTypeLib` (AutomationML e.V., v1.1.1-beta)
for references between objects. It defines an abstract `refObj` (`xs:IDREF`) and four derived types:
`refBaseObj` (aspect object points to its base object, same logical object), `refAspectObj` (the
reverse), `refDetailObj` (abstract object points to a more detailed representation), `refAbstractObj`
(the reverse) (AMLPetriNet: `libraries/AutomationML_ObjectReferences_AttributeTypeLib_AMLEd2_1.1.1-beta.aml:8-27`).
Reference it with alias `ObjectReferences` (AMLPetriNet: `dotnet/PtMapper.Conversion/PtNames.cs:111-122`).

The attribute *name* and the attribute *type* are separate decisions. FPD keeps the VDI 3682 names
and chooses the semantics through `RefAttributeType`:

| Attribute on | Name | Typed as | Meaning |
|---|---|---|---|
| `FPD_ProcessOperator` | `refProcess` | `refDetailObj` | the sub-process is the detailed representation |
| `FPD_Process` (sub-process) | `refObj` | `refAbstractObj` | back to the decomposed operator |
| `FPD_State` (boundary copy) | `refObj` | `refBaseObj` | same logical state as the top-level original |

(fpb-aml-mapper: `dotnet/FpbMapper.Conversion/MapperOptions.cs:21-43` and `60-86`; written by
`FpdLibraries.cs:310-323`.) A legacy switch keeps the v0.5 layout with a local `xs:string` `refObj`
(`MapperOptions.cs:39-43`, test `dotnet/FpbMapper.Tests/ObjectReferencesLibraryTests.cs:108`).

Rules for reference values:

- Store the target's id without braces. The FPD rule `RefObjResolvable` compares `refObj` against
  `identification.uniqueIdent`, which is brace-free; a braced value made it fail
  (fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs:1225-1228`, rule in
  `Rules/vdi3682-pure-rules.ocl:72-73`). The objectreferences plugin strips braces on write too
  (objectreferences-aml-editor-plugin: `Aml.Editor.Plugin.ObjectReferences/Core/ReferenceService.cs:32-36`).
- Clearing a reference must write an empty value. An early setter ignored empty strings, so a parent
  operator kept `refProcess` pointing at a removed sub-process (fpb-aml-mapper:
  `dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs:1867-1893`).
- On read, accept the base name and the derived names as attribute names, refObj first
  (fpb-aml-mapper: `dotnet/FpbMapper.Conversion/ReferenceTypes.cs:139-156`).
- Recognise a reference by its `RefAttributeType` *after stripping the alias*. The objectreferences
  plugin compares `RefAttributeType` against `AutomationML_ObjectReferences_AttributeTypeLib/` with
  `StartsWith` (objectreferences-aml-editor-plugin: `Aml.Editor.Plugin.ObjectReferences/Core/ReferenceScanner.cs:95-102`),
  so the alias-qualified form `ObjectReferences@...` that fpb-aml-mapper writes
  (`dotnet/FpbMapper.Conversion/MapperOptions.cs:133-140`) is not claimed by it. Also note that the
  plugin's catalogue still carries the earlier type set `refExtendedObj`/`refComposedObj`
  (objectreferences-aml-editor-plugin: `Aml.Editor.Plugin.ObjectReferences/Model/ReferenceKind.cs:48-62`),
  while the published 1.1.1-beta file has `refAspectObj`, `refDetailObj`, `refAbstractObj`. Pin your
  language to the published file and treat the file as the authority.
- `xs:IDREF` only resolves inside one document. Cross-document references are not solved by the
  library; the objectreferences plugin shows them as orphans.

### 2.7 Flat with refObj, or nesting

CAEX lets you nest InternalElements, and it is tempting to put a sub-diagram inside the element it
refines. Both source projects decided otherwise where the relation is a view relation rather than
composition:

- FPD puts every `FPD_Process` (top level and every decomposition layer) directly under the
  InstanceHierarchy and connects the layers with `refProcess` on the operator and `refObj` on the
  sub-process (fpb-aml-mapper: `dotnet/FpbMapper.Conversion/CaexToFpbJson.cs:50-53`, written in
  `FpbJsonToCaex.cs:1601-1604` and `1707-1719`). A layer is then addressable on its own, can be
  removed without touching its parent's subtree, and a decompose, compose, decompose cycle does not
  move elements between parents.
- Inside a process the states, operators and the system limit are siblings: `FPD_SystemLimit` is
  described as "Peer aggregate of the process, not a container"
  (`dotnet/FpbMapper.Conversion/FpdLibraries.cs:95-100`). A state's containment in the system limit
  is geometric, not structural, so moving a state across the border never re-parents an element.
- ISO_PT nests nodes and arcs one level below `PT_Net`, because they are parts of the net, and puts
  the arc-to-node links on the net element (AMLPetriNet: `dotnet/PtMapper.Conversion/PtNetToCaex.cs:99-157`).

Rule of thumb: nest what the language treats as composition, flatten and reference what the language
treats as another view on the same thing. If you flatten, provide the reverse-lookup fallback on read:
externally authored files often fill only one direction (section 9).

### 2.8 External references: what to reference, what to carry

1. Reference the AML base libraries, never inline them. Every AML tool knows them and inlining would
   dwarf the document (AMLPetriNet: `dotnet/PtMapper.Conversion/PtLibraries.cs:36-38`). Reference
   them by file name, resolved through the tool's library search path, never by a URL that contains a
   token or credentials: the `Path` is written into every document the mapper produces and reaches
   everyone who opens one. fpb-aml-mapper (`dotnet/FpbMapper.Conversion/FpbMappings.cs:124-131`) and
   the starter (`starter/dotnet/Efl.Conversion/EflLibraries.cs:28-34`) write the file name.
   AMLPetriNet still writes a share URL with an embedded token
   (`AMLPetriNet: dotnet/PtMapper.Conversion/PtNames.cs:103-104`), a known issue.
2. Carry your own library and the two small shared libraries (OMG_DD, ObjectReferences) inside any
   document a person will open. The AML Editor does not follow file references: a document saved away
   from the library files opened with geometry and `refObj` unresolved, which users see as a broken
   file (AMLPetriNet: `dotnet/PtMapper.Conversion/PtLibraries.cs:58-72`,
   `dotnet/PtMapper.Conversion/PtSelfContained.cs:5-19`). PT embeds the ObjectReferences file as an
   assembly resource for exactly this, because the plugin has no library folder in reach
   (`dotnet/PtMapper.Conversion/ObjectReferencesLibraryFile.cs:7-22`, csproj entry
   `dotnet/PtMapper.Conversion/PtMapper.Conversion.csproj:11-20`). The starter carries OMG_DD the
   same way: one copy of the published file in `starter/libraries/`, linked into the assembly as an
   `EmbeddedResource` (`starter/dotnet/Efl.Conversion/Efl.Conversion.csproj:13-18`), put into a
   document that neither carries nor references it by `EflDiagramInterchange.EnsureIn`
   (`starter/dotnet/Efl.Conversion/EflDiagramInterchange.cs:45-60`), and a test checks that the
   embedded copy equals the file (`starter/dotnet/Efl.Tests/DiagramInterchangeTests.cs:86-95`). Do
   the same for ObjectReferences if your language needs references: take the published file, never
   rebuild its types from a description (see [09 Pitfalls](09-pitfalls.md), PF-AML-16).
3. When you inline a library, strip the alias prefix from the paths *inside your own libraries only*.
   Paths in another hierarchy are not yours to rewrite (AMLPetriNet:
   `dotnet/PtMapper.Conversion/PtLibraries.cs:104-125`).
4. Respect a document that references its libraries. AMLPetriNet's `PtLibraryLoan` detects the alias
   the document uses, adds the class definitions for the duration of the write so that
   `CreateClassInstance` works, removes them again and requalifies the new paths with the document's
   alias (`dotnet/PtMapper.Conversion/PtLibraryLoan.cs:6-19`, `58-77`, `79-103`). It reads the alias
   from paths in use, not from the `ExternalReference` list, because an alias nobody points at says
   nothing about the convention (`PtLibraryLoan.cs:79-83`). Without it, editing one node wrote four
   class libraries into a file whose author kept them outside. Tests:
   `AMLPetriNet: dotnet/PtMapper.Tests/ForeignConventionTests.cs:64` and `:80`.
5. Follow the document's form for shared type paths. `PtDiPaths` resolves once per write whether the
   OMG_DD library is inline (`OMG_DD_AttributeTypeLib/DD_Point`), referenced under some alias
   (`<alias>@OMG_DD_AttributeTypeLib/DD_Point`) or absent (then it adds the reference). Writing the
   aliased form into a self-contained document left a dangling alias on every attribute a sync touched
   and the round-trip check refused the result (AMLPetriNet: `dotnet/PtMapper.Conversion/PtDiPaths.cs:7-23`, `54-76`).
   The starter's `EflDiagramInterchange.PathOf` and `PathIn` do the same: the unqualified path when
   the library is inline, `<alias>@...` when the document references the file under any alias, found
   by file name (`starter/dotnet/Efl.Conversion/EflDiagramInterchange.cs:76-99`). The published
   library artefact references the file instead of carrying it (`ReferenceFrom`, `:62-74`, called
   from `starter/dotnet/Efl.Conversion/EflLibraries.cs:46-52` and `:106-113`). Tests:
   `AnUpdateFollowsADocumentThatReferencesTheLayoutLibraryUnderItsOwnAlias` and
   `TheLibraryArtefactReferencesTheLayoutLibraryInsteadOfCarryingIt`
   (`starter/dotnet/Efl.Tests/DiagramInterchangeTests.cs:97-128`).
6. Only touch the shared references when you create your libraries in this call. A document that
   already carries your libraries keeps its layout (AMLPetriNet: `dotnet/PtMapper.Conversion/PtLibraries.cs:32-34`,
   fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpdLibraries.cs:17-27`).
7. Defer library creation in an update until something actually has to be instantiated. A sync that
   only changes an attribute must not write class libraries (AMLPetriNet:
   `dotnet/PtMapper.Conversion/PtNetUpdater.cs:121-133`).
8. If you add library XML with `XElement` directly instead of through the engine, put the children of
   `CAEXFile` back into schema order afterwards; appended libraries otherwise land after the
   InstanceHierarchy, which CAEX 3.0 forbids (AMLPetriNet: `dotnet/PtMapper.Conversion/PtSelfContained.cs:106-124`).
   Appending an `AttributeTypeLib` at the end is safe because it is last in the order
   (`ObjectReferencesLibraryFile.cs:34-50`).

### 2.9 Publish the library as a file, keep code and file in step

Ship the library as a versioned `.aml` artefact and make it the authority. AMLPetriNet reproduces
`ISO_PT_DomainLibrary_v0.3.aml` in code and a golden test compares interface classes, role classes,
SUCs, fixed SUC ids, inheritance, attributes, descriptions, external references and idempotence of
`EnsureLibraries` against the file (AMLPetriNet: `dotnet/PtMapper.Tests/PtLibraryGoldenTests.cs:32-168`).
Give classes fixed ids (AMLPetriNet: `dotnet/PtMapper.Conversion/PtNames.cs:87-95`); the starter stamps
ids derived from the class path because the engine otherwise gives every class a fresh GUID on every
build (`starter/dotnet/Efl.Conversion/EflLibraries.cs:60-80`). It stamps only its own four
libraries; the embedded OMG_DD library keeps the ids of the published file.

Write conventions that live on instances (not on classes) into the class description, so a reader of
the library learns them. The `PT_Arc` description states that routing is stored as
`Waypoint_1..n` typed `DD_Waypoint` and label positions as `DD_Point`
(AMLPetriNet: `libraries/ISO_PT_DomainLibrary_v0.3.aml:83-90`).

---

## 3. The two connection encodings

This is the decision that shapes everything else (the starter calls it pattern P6 of the method,
`starter/dotnet/Efl.Conversion/EflNames.cs:83-105`).

### 3.1 Encoding A: InternalLink between two node interfaces

The connection *is* the `InternalLink`. The source node gets an `...Out` interface, the target node an
`...In` interface, and the link joins them. The connection's identity lives in the link's `ID`, its
label in the link's `Name`, its geometry on the interfaces.

```csharp
var link = procIE.InternalLink.Append(linkName);
// Stamp the link with the FPB.JS flow ID (B-format) so a CaexToFpbJson
// round-trip via NormalizeId(link.ID) recovers the same FPB-side ID.
link.ID = NormalizeId(flowId);
link.AInterface = ifs.OutIf;
link.BInterface = ifs.InIf;
```
(fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs:1832-1837`; starter equivalent
`starter/dotnet/Efl.Conversion/EflToCaex.cs:121-147`)

Used by: VDI_FPD for Flow, ParallelFlow, AlternativeFlow, Usage.

### 3.2 Encoding B: reified connection element with two links

The connection is an `InternalElement` of its own class with two interfaces (source end, target end).
Two `InternalLink`s couple those ends to one interface on each node. The links carry no identity of
the connection.

```csharp
var sourceEnd = PtWrite.EnsureInterface(ie, "Source",
    PtIds.Interface(ie.ID!, "Source"), PtNames.Interface(PtNames.ArcSource));
if (polyline.Count > 0) PtWrite.SetPortCoordinate(sourceEnd, polyline[0]);

var targetEnd = PtWrite.EnsureInterface(ie, "Target",
    PtIds.Interface(ie.ID!, "Target"), PtNames.Interface(PtNames.ArcTarget));
if (polyline.Count > 1) PtWrite.SetPortCoordinate(targetEnd, polyline[^1]);

if (endpoints.TryGetValue(EndpointKey(arc.SourceId, arc.Id), out var sourceNodeEnd))
    PtWrite.EnsureLink(netIE, $"{ie.Name}_source", PtIds.Link(ie.ID!, "source"), sourceEnd, sourceNodeEnd);

if (endpoints.TryGetValue(EndpointKey(arc.TargetId, arc.Id), out var targetNodeEnd))
    PtWrite.EnsureLink(netIE, $"{ie.Name}_target", PtIds.Link(ie.ID!, "target"), targetEnd, targetNodeEnd);
```
(AMLPetriNet: `dotnet/PtMapper.Conversion/PtNetToCaex.cs:143-155`; starter equivalent
`starter/dotnet/Efl.Conversion/EflToCaex.cs:149-178`)

Used by: ISO_PT for arcs.

### 3.3 Comparison

| Aspect | A: InternalLink | B: reified element |
|---|---|---|
| Objects per connection | 1 link + 2 interfaces | 1 IE + 2 own interfaces + 2 node interfaces + 2 links |
| Identity | Only the link `ID`; no `Identification` attribute possible | Full: `Identification`, CAEX `ID`, role, SUC |
| Attributes on the connection | None on the link; must be parked on an interface | Anywhere on the element (PT `Weight`, label offset) |
| Target of a `refObj` from elsewhere | Awkward: a link is not an InternalElement | Natural |
| Where waypoints live | On the source-side interface (`Waypoint_n`) | On the element (`Waypoint_n`) |
| Visible in the AML Editor tree | Only in the link list of the owner | As an element with a name |
| Validation typing | Type is derived from the interface classes | Type is the element's role/SUC |
| Reader complexity | Low: one pass over links | Higher: resolve both links, guard against links between two connection ends |
| Update complexity | Link removal must also remove the two interfaces it used | Element, two ends and two links; node interfaces may be shared with foreign links |
| Fits languages where | Connections are pure relations with a type | Connections have attributes, identity or are referenced (Petri net arcs, weighted edges) |

Decide per connection kind, not per language, if the language mixes both. The starter implements both
styles in one writer, reader and updater, and reads the style of an existing hierarchy from the
document so an update never changes the shape of a file it did not create
(`starter/dotnet/Efl.Conversion/EflUpdater.cs:177-184`).

### 3.4 Lessons that apply to both

- Use the interface ids from the `RefPartnerSideA`/`B` attributes and compare them exactly. CAEX 3.0
  writes the interface id there. AMLPetriNet deliberately does not split at a colon, because an id may
  contain one (`dotnet/PtMapper.Conversion/PtCaex.cs:24-33`); fpb-aml-mapper tolerates the older
  `elementId:interfaceName` form for externally authored files
  (`dotnet/FpbMapper.Conversion/CaexToFpbJson.cs:923-934`, `FpbJsonToCaex.cs:1067-1078`). The starter
  accepts both the bare interface id and `ElementId:InterfaceId`, taking the part after the last colon
  because its GUIDs contain none (`starter/dotnet/Efl.Conversion/CaexToEfl.cs:167-179`), and uses that
  helper in the reader (`CaexToEfl.cs:96-97`, `130-131`) and the updater
  (`starter/dotnet/Efl.Conversion/EflUpdater.cs:235-236`, `282-283`); test
  `LinksWrittenAsElementAndInterfaceIdAreReadToo` (`starter/dotnet/Efl.Tests/RoundTripTests.cs:137`).
  Pick one policy and put it in one helper.
- Links may sit on any element. Walk links at and below the container rather than only on it
  (AMLPetriNet: `dotnet/PtMapper.Conversion/PtCaex.cs:35-49`).
- When a link is removed, remove the interfaces it used unless a surviving link still uses them.
  Leaving them leaked two corpse interfaces per deleted flow in the FPD mapper
  (fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs:1079-1101`).
- For the reified encoding, map interface ids only to *node* owners when resolving endpoints. If arc
  interfaces are in the owner map, a link between two arcs resolves an arc as an endpoint and the
  modeler drops both arcs at import while reporting success (AMLPetriNet:
  `dotnet/PtMapper.Conversion/CaexToPtNet.cs:59-63`).
- An existing link whose endpoints changed (reversed or rerouted) must be detected by comparing
  owner ids, then dropped and recreated (fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs:327-352`
  and `840-871`).

---

## 4. Diagram interchange: layout in AML

### 4.1 One shared, language-agnostic type library

Store layout with the three types of `OMG_DD_AttributeTypeLib` v0.1, structured after the OMG Diagram
Definition: `DD_Bounds` (position as `DD_Point`, width, height; cf. DC::Bounds), `DD_Point` (x, y;
DC::Point) and `DD_Waypoint` (position; DI::Waypoint)
(AMLPetriNet: `libraries/OMG_DD_AttributeTypeLib_v0.1.aml:8-31`). Both mappers emit it from an
identical `DiagramInterchangeLibrary` class, deliberately duplicated so the two repositories stay
independent, with the instruction to keep the emitted library byte identical
(AMLPetriNet: `dotnet/PtMapper.Conversion/DiagramInterchangeLibrary.cs:5-21`,
fpb-aml-mapper: `dotnet/FpbMapper.Conversion/DiagramInterchangeLibrary.cs`). Reference it with alias
`OMG_DD`, or carry it inline in documents people open (section 2.8). The starter does not generate the
library in code; it embeds the published file from `starter/libraries/`
(`starter/dotnet/Efl.Conversion/EflDiagramInterchange.cs:29-43`), so code and file cannot drift. Two languages in one document then describe layout the same way, which matters when a
bridging document holds an FPD and a Petri net side by side.

### 4.2 Where each piece of geometry goes

| Geometry | Attribute | Typed | On | Source |
|---|---|---|---|---|
| Node bounds | `ViewInformation` { `position` {x,y}, `width`, `height` } | `DD_Bounds` | node IE (declared on the base class) | AMLPetriNet: `dotnet/PtMapper.Conversion/PtWrite.cs:224-238` |
| Docking point of a connection | `PortCoordinate` {x,y} | `DD_Point` | the interface (declared on the abstract port class) | AMLPetriNet: `PtWrite.cs:257-263`; fpb-aml-mapper: `FpbJsonToCaex.cs:2067-2074` |
| Bend points | `Waypoint_1..n` { `position` {x,y} }, ordered source to target | `DD_Waypoint` | A: source-side interface; B: the connection element | fpb-aml-mapper: `FpbJsonToCaex.cs:2087-2097`; AMLPetriNet: `PtWrite.cs:265-288` |
| Label offset relative to element centre | `LabelOffset` {x,y} | `DD_Point` | element (instance attribute, not on the class) | AMLPetriNet: `dotnet/PtMapper.Conversion/PtNames.cs:64-70`, `PtWrite.cs:240-255` |

Notes:

- Waypoints are instance attributes, not class attributes, because their number varies. Parse the
  index from the name and sort numerically; `Waypoint_10` sorts before `Waypoint_2` as a string
  (AMLPetriNet: `dotnet/PtMapper.Conversion/CaexToPtNet.cs:282-304`).
- Replace the whole waypoint set on every write. A shortened polyline otherwise keeps its tail
  (AMLPetriNet: `dotnet/PtMapper.Conversion/PtWrite.cs:265-277`, test
  `dotnet/PtMapper.Tests/UpdateInPlaceTests.cs:188`).
- A label moved back to its default position must remove the offset attribute, or the next import
  moves it away again (`PtWrite.cs:240-249`, test `UpdateInPlaceTests.cs:207`).
- For encoding B, the inherited `ViewInformation` rectangle stays unused on connection elements; do not
  write an empty one (AMLPetriNet: `dotnet/PtMapper.Conversion/PtNames.cs:78-83`, test
  `UpdateInPlaceTests.cs:280`).

### 4.3 Explicit and implicit layout

Distinguish what is stored (explicit) from what is derived or computed on import (implicit), and make the
round trip stable in both cases.

- Derived geometry: PNML carries no docking points, so AMLPetriNet computes them from node shape
  (ellipse for places, rectangle for transitions) the same way in C# and in the web modeler, and
  writes them as `PortCoordinate` (`dotnet/PtMapper.Conversion/PtGeometry.cs:5-72`). If two sides
  compute the same value, keep both implementations in step and say so in both files. Crop against
  the outline the modeler actually draws: the starter crops a Store against the ellipse inside its box,
  because cropping against the box put the stored end point beside the circle and the document
  disagreed with the canvas for every flow off an axis (`starter/dotnet/Efl.Conversion/EflGeometry.cs:29-69`,
  test `starter/dotnet/Efl.Tests/ExampleAndGeometryTests.cs:56`). A node without size docks at its
  centre instead of writing NaN (`EflGeometry.cs:50-52`, test
  `ANodeWithoutSizeDocksAtItsCentreInsteadOfNaN` at `starter/dotnet/Efl.Tests/ExampleAndGeometryTests.cs:68`).
- Computed layout: a model without positions gets a deterministic layered layout on import, and
  existing bounds are never moved (AMLPetriNet: `dotnet/PtMapper.Conversion/PtLayout.cs:5-20`). The
  starter's `EflLayout.ArrangeMissing` also gives bend points to the flows between the nodes it placed:
  back flows in lanes below the drawing, forward flows that would cross a node in lanes above, three
  bend points for a self loop; flows that already have waypoints keep them
  (`starter/dotnet/Efl.Conversion/EflLayout.cs:175-289`). Those bend points are written as
  `Waypoint_n` like any drawn route. See [08 Layout](08-layout.md) §4.5.
- Fallback geometry on read: a layout-free AML (hand-authored engineering data) must still produce a
  connection visual with at least two points, because the FPB.JS importer dereferences it
  unconditionally and a missing entry aborted the whole import. The fallback is a straight line
  between shape centres, and the points carry the same `original` substructure as port-derived
  points so that the echo cycle stays idempotent (fpb-aml-mapper:
  `dotnet/FpbMapper.Conversion/CaexToFpbJson.cs:492-501`, `837-878`; tests
  `dotnet/FpbMapper.Tests/WaypointFallbackTests.cs:124`).
- Default bounds for containers: a freshly created FPD system limit without visual data rendered as a
  0 by 0 box, so the mapper writes 100,100,600,400 (fpb-aml-mapper:
  `dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs:460-467`).
- Report layout that a sync adds instead of failing on it: opening a net without layout and syncing
  it writes the arranged layout into the document. The PT round-trip comparison reports the count of
  graphical values before and after rather than demanding equality
  (AMLPetriNet: `dotnet/PtMapper.Conversion/PtRoundTripCheck.cs:126-134`, `174-187`).

### 4.4 Number formatting

Write every number with `CultureInfo.InvariantCulture`. On a German Windows the current culture
writes `12,5`, which other tools read as a different number or not at all
(`starter/dotnet/Efl.Conversion/EflWrite.cs:231-242`). Read with `NumberStyles.Float` and the
invariant culture as well (AMLPetriNet: `dotnet/PtMapper.Conversion/CaexToPtNet.cs:325-326`). Round to
a fixed precision so re-serialisation does not create diffs
(AMLPetriNet: `dotnet/PtMapper.Conversion/PtWrite.cs:351-356` uses `"0.##"`;
`PtGeometry.cs:74-75` rounds to two decimals).

---

## 5. Aml.Engine 4.x: API specifics and traps

All three .NET projects reference `Aml.Engine` `4.*` on net8.0
(AMLPetriNet: `dotnet/PtMapper.Conversion/PtMapper.Conversion.csproj`,
fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpbMapper.Conversion.csproj`,
`starter/dotnet/Efl.Conversion/Efl.Conversion.csproj`).

| # | Trap | Symptom | Do this | Evidence |
|---|---|---|---|---|
| 1 | `CreateClassInstance(string name)` is an extension method | Does not compile, or you end up with the overload that takes a `CAEXDocument` and no name | `using Aml.Engine.CAEX.Extensions;` | AMLPetriNet: `dotnet/PtMapper.Conversion/PtWrite.cs:3`, `:84`; the overload is `InheritanceExtensions.CreateClassInstance<T>(IInstantiable<T>, string)` in the engine's XML docs |
| 2 | `CreateClassInstance` copies only the first `SupportedRoleClass` | AML base role missing on instances | Append the rest (section 2.2) | `PtWrite.cs:86-95` |
| 3 | The engine assigns fresh GUIDs to objects it creates | Every run produces a different document; updates cannot match; references dangle | Set `ID` explicitly right after creation, derived deterministically (section 6) | `starter/dotnet/Efl.Conversion/EflLibraries.cs:60-80` |
| 4 | `Insert(child)` defaults to inserting first | Document order reversed on every insert | `parent.Insert(child, false)` | AMLPetriNet: `dotnet/PtMapper.Conversion/PtWrite.cs:98-104`; fpb-aml-mapper still calls the one-argument form, for example `FpbJsonToCaex.cs:1701` |
| 5 | Wrappers are not reference stable | `ReferenceEquals` on two lookups of the same element is false; every link looked rerouted, so each FPD update tore down and recreated all links and leaked their interfaces | Compare by `ID`, or compare the underlying `XElement` via `.Node` | fpb-aml-mapper: `FpbJsonToCaex.cs:859-870`; AMLPetriNet: `PtNetUpdater.cs:522-525`; objectreferences-aml-editor-plugin: `Aml.Editor.Plugin.ObjectReferences/Core/ReferenceScanner.cs:16-19` |
| 6 | Engine keeps process-wide caches that are not thread safe | Intermittent "Collection was modified" from `CheckReferences`, ids reassigned, count mismatches in round-trip tests | Disable xUnit parallelisation in every test assembly that builds documents | AMLPetriNet: `dotnet/PtMapper.Tests/TestCollection.cs:3-7`; fpb-aml-mapper: `dotnet/FpbMapper.Tests/TestCollection.cs:3-7`; AMLFPB.js: `Aml.Editor.Plugin.FPB.Tests/TestCollection.cs:1-5`; objectreferences-aml-editor-plugin: `Aml.Editor.Plugin.ObjectReferences.Tests/AssemblyInfo.cs:1-5`; `starter/dotnet/Efl.Tests/AssemblyInfo.cs:1-6` |
| 7 | `CAEXObject.NewGUID` is a property | `NewGUID()` does not compile | `ie.ID = CAEXObject.NewGUID;` (only where randomness is really wanted) | objectreferences-aml-editor-plugin: `Aml.Editor.Plugin.ObjectReferences/Core/ReferenceLibrary.cs:89-91`, `Aml.Editor.Plugin.ObjectReferences.Tests/TestHelpers.cs:13` |
| 8 | Culture-sensitive number formatting | Commas in coordinates | Invariant culture everywhere (section 4.4) | `starter/dotnet/Efl.Conversion/EflWrite.cs:231-242` |
| 9 | Collection indexers return null for a missing name | NullReferenceException deep in a writer | `ie.Attribute[name] ?? ie.Attribute.Append(name)` | AMLPetriNet: `PtWrite.cs:358-362` |
| 10 | `ToDictionary` over classes throws on a duplicate name | Update aborts halfway on a hand-edited library | Build the lookup with `TryAdd`, first wins | AMLPetriNet: `PtWrite.cs:65-77` |
| 11 | A removed element's wrapper still exists | Later passes mutate a detached element with no effect on the saved file | After `Remove()`, drop it from every index; check `CAEXParent == null` before mutating | fpb-aml-mapper: `FpbJsonToCaex.cs:1191-1208`, `1307-1314` |
| 12 | Per-element engine lookups are slow | A per-element id check doubled the time to write a 2000 element net | Read all ids once from the XML (`caex.Node.DescendantsAndSelf().Attributes("ID")`) | AMLPetriNet: `dotnet/PtMapper.Conversion/PtIds.cs:119-139` |
| 13 | Copying a library between documents | Ids change or references break | `CAEXDocument.LoadFromString(xml)`, then `doc.CAEXFile.AttributeTypeLib.Insert(srcLib)` deep-copies and keeps ids | objectreferences-aml-editor-plugin: `Aml.Editor.Plugin.ObjectReferences/Core/ReferenceLibrary.cs:35-44` |
| 14 | `Insert` of an object that already has a parent inserts a copy | Edits to the original do not show up | Create fresh via `Append`/`CreateClassInstance`, or re-fetch after insert | engine XML docs for `InstanceHierarchyType.Insert(CAEXWrapper, bool, bool)` |
| 15 | Working below the engine with `XElement` | Schema order violated, engine index unaware of new ids | Reorder children, and scan ids from XML rather than asking the engine | AMLPetriNet: `PtSelfContained.cs:106-124`, `PtIds.cs:119-124` |
| 16 | `CAEXDocument.LoadFromString` does not always reject text that is not AML | A document with a null `CAEXFile`; the NullReferenceException appears later, and a web API reported the caller's broken file as a 500 | Check `CAEXFile != null` once in a load helper and throw a `FormatException` | `starter/dotnet/Efl.Conversion/EflDocuments.cs:10-36` |
| 17 | `CAEXDocument.LoadFromFile` and `SaveToFile` write a copy of `CAEX_ClassModel_V.3.0.xsd` into the folder of the file | Schema files appear in example folders and get committed | Read the file as text and load from the string; save through `SaveToStream` | `starter/dotnet/Efl.Conversion/EflDocuments.cs:38-58` |

Use `link.AInterface = iface` / `link.BInterface = iface` when you hold interface wrappers; the engine
fills `RefPartnerSideA`/`B` with the interface ids (AMLPetriNet: `dotnet/PtMapper.Conversion/PtWrite.cs:315-332`).
The starter writes links the same way, because hand-built `ElementId:InterfaceId` strings did not
resolve in fpb-aml-mapper (`starter/dotnet/Efl.Conversion/EflWrite.cs:191-205`).

---

## 6. Identity: deterministic ids

### 6.1 Why

The same model must produce the same CAEX ids on every run. Otherwise an update replaces every
element instead of touching the changed ones, and every reference a user added into the hierarchy
dangles (AMLPetriNet: `dotnet/PtMapper.Conversion/PtIds.cs:7-16`). Tests assert that replacing the
hierarchy twice keeps the same ids (AMLPetriNet: `dotnet/PtMapper.Tests/IdentityTests.cs:85`) and that
ids stay stable across updates (`dotnet/PtMapper.Tests/UpdateInPlaceTests.cs:112`).

### 6.2 How: name-based UUIDs from a namespaced seed

```csharp
public static string For(string kind, params string[] parts)
{
    var seed = Namespace + Separator + kind + Separator + string.Join(Separator, parts);
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));

    var bytes = new byte[16];
    Array.Copy(hash, bytes, 16);

    // Stamp version 5 and the RFC 4122 variant so the result is a well-formed
    // UUID. Note the byte indices: Guid(byte[]) reads the first three fields
    // little-endian, so the version nibble that shows up in the third group of
    // the rendered form comes from byte 7, not byte 6. The variant nibble does
    // come from byte 8, because the last eight bytes are read in order.
    bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
    bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

    return new Guid(bytes).ToString("B");
}
```
(AMLPetriNet: `dotnet/PtMapper.Conversion/PtIds.cs:30-47`; same in `starter/dotnet/Efl.Conversion/EflIds.cs:30-46`)

Details that matter:

- The namespace string (`"iso-pt-aml-mapper"`) keeps your ids from colliding with another tool's
  (`PtIds.cs:19-20`).
- The separator is `"\0"`, a character no id contains, so `For("a","b")` and `For("ab")` differ. Write
  it as an escape; a literal NUL byte makes the source file binary to git and invisible to grep
  (`PtIds.cs:22-28`).
- The `kind` discriminator keeps an element, its interfaces and its links apart:
  `Element(pnmlId)`, `Interface(ownerCaexId, name)`, `Link(arcCaexId, side)`, `Net(netId)`,
  `Hierarchy(name)` (`PtIds.cs:49-70`).
- Derive interface and link ids from the *owner's CAEX id*, not from the language id. When the
  language id was the seed, the same file imported twice produced duplicate interface ids, and
  deleting an arc in one copy removed the other copy's links (`PtIds.cs:51-62`).
- The byte index matters. fpb-aml-mapper's `DeriveSubProcessId` and `DeriveBoundaryStateId` hash with
  SHA-1 and set the version on byte 6 (`dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs:529-565`). Per
  the little-endian layout explained above, that nibble does not land in the version position of the
  rendered GUID. The ids are still deterministic and unique, so nothing breaks, but they are not
  well-formed version 5 UUIDs. AMLPetriNet tests well-formedness
  (`dotnet/PtMapper.Tests/IdentityTests.cs:54`).

### 6.3 Distinct within the whole document

CAEX ids are unique per document, not per hierarchy. Claim every id against all ids already in the
file and add a deterministic ordinal on collision:

```csharp
private static string Distinct(string kind, string seed, IIdSpace taken)
{
    ArgumentNullException.ThrowIfNull(taken);

    var candidate = For(kind, seed);
    for (var ordinal = 2; !taken.Claim(candidate); ordinal++)
        candidate = For(kind, seed, ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture));

    return candidate;
}
```
(AMLPetriNet: `dotnet/PtMapper.Conversion/PtIds.cs:91-100`, id space from the XML in `114-142`)

Two cases need it: invalid input with duplicate language ids (reported by rule PT01, but it must not
produce two elements with one CAEX id), and the same model imported twice, which is legitimate
(`PtIds.cs:72-84`). fpb-aml-mapper's green-field builder tracks used ids only within one run
(`dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs:1524-1525`), so importing the same FPB.JS project twice
into one document yields duplicate CAEX ids; do the document-wide check from the start.

Also make the language ids themselves distinct before writing, and report what was renumbered
(AMLPetriNet: `dotnet/PtMapper.Conversion/PtIdText.cs:161-212`, applied before anything is written in
`PtNetToCaex.cs:88-93`).

### 6.4 Two strategies seen in the projects

| | AMLPetriNet (derive everything) | fpb-aml-mapper (reuse editor ids) |
|---|---|---|
| Element CAEX id | Hash of language id, distinct in document | Editor GUID wrapped in braces (`FpbJsonToCaex.cs:2108-2114`, `447`) |
| Interface id | Hash of owner CAEX id and interface name | Random `NewId()` (`FpbJsonToCaex.cs:997-1006`, `1747`) |
| Link id | Hash of arc CAEX id and side | Editor flow id in braces (`FpbJsonToCaex.cs:1832-1835`) |
| Process / net container id | Hash of net id | Random on green field (`FpbJsonToCaex.cs:1496-1498`), derived for sub-processes created during update (`1217-1222`) |
| Hierarchy id | Hash of name, made distinct (`PtNetToCaex.cs:78-86`) | Random (`FpbJsonToCaex.cs:1519-1521`) |
| Language id in document | `Identification/id` | CAEX `ID` itself, brace-stripped on read (`CaexToFpbJson.cs:938-957`) |

Reusing editor ids is simpler as long as the editor already produces GUIDs and every element exists
once. It breaks as soon as one logical element needs several CAEX elements (section 7) or the same
model is imported twice. Deriving everything needs a language id stored in `Identification`, but
survives both.

### 6.5 Compare ids with one policy

AMLPetriNet compares CAEX ids ordinal and exactly: CAEX declares ID as a string and XML treats `Start`
and `start` as different names, and mixing tolerant and exact comparisons once let the reader resolve a
link the updater then failed to find (`dotnet/PtMapper.Conversion/PtCaex.cs:12-22`). fpb-aml-mapper
and the objectreferences plugin strip braces and compare case-insensitively everywhere
(fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs:873-877`,
objectreferences-aml-editor-plugin: `Aml.Editor.Plugin.ObjectReferences/Core/CaexNav.cs:31-46`). Either
works if it is one `StringComparer` in one place and every index uses it.

### 6.6 Language ids that are not valid in the editor

Documents in the wild put braced CAEX GUIDs into `Identification/id`, and a modeler parser may accept
only `[a-z_][\w-.]*`. Normalise ids on read into a valid name, deterministically, leaving valid names
untouched, and never overwrite a stored id that differs only by that normalisation
(AMLPetriNet: `dotnet/PtMapper.Conversion/PtIdText.cs:5-18`, `99-105`; guarded write in
`PtWrite.cs:214-217`). The updater must normalise its lookup keys the same way and in the same order
as the reader, or two elements that reduce to one name pair up differently
(`PtNetUpdater.cs:165-187`).

---

## 7. One logical element, several CAEX elements (layers, views)

FPB.JS models decomposition in layers. A boundary state appears on the parent layer and inside the
sub-process layer and carries *one* id in the editor. In CAEX two InternalElements with the same id are
not conformant, and a compound key was a dead end. The fix that holds (fpb-aml-mapper, July 2026):

1. Write each copy with its own deterministic id derived from (shared id, sub-process id):
   `"fpd-boundary-state:" + shared + "@" + subProcess` (`dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs:544-565`).
   Before this the copy got a random id, was not found again by update, was removed as an orphan and
   lost its position.
2. Link the copy to the original with `refObj`, typed `refBaseObj` (section 2.6), brace-free
   (`FpbJsonToCaex.cs:1720-1727`).
3. On read, reunify: a state with a `refObj` is surfaced under the referenced id, so the editor sees
   one logical state on both layers (`dotnet/FpbMapper.Conversion/CaexToFpbJson.cs:328-339`).
4. On update, translate before anything else. A pre-pass rewrites the sub-process copies in the
   incoming model (element ids, flow endpoints, visuals) to their per-layer CAEX ids. The whole
   remaining pipeline (index lookup, add, connections, orphan removal) then runs on distinct ids
   without any layer awareness (`FpbJsonToCaex.cs:191-198`, `567-671`).
5. Resolve the existing copy primarily through its `refObj` back link under the sub-process, and only
   fall back to re-deriving the id. Re-deriving matched only files created by the derive scheme and
   orphaned the boundary copies of every other file on each update (`FpbJsonToCaex.cs:607-656`).
6. Detect duplicate ids when indexing and keep the first rather than silently overwriting it
   (`FpbJsonToCaex.cs:1433-1459`).

Tests: `dotnet/FpbMapper.Tests/ConversionTests.cs:924` (idempotent sync keeps boundary states) and
`:992` (sub-process boundary positions stay stable).

Generalise: whenever your editor shows one object in several places (layers, views, detail diagrams),
write one CAEX element per place, give each a deterministic id derived from (logical id, place), link
them with the matching `refObj` subtype, and reunify on read.

---

## 8. Update in place

A plugin that replaces the hierarchy on every save destroys every attribute a user added, every link
into another hierarchy and every foreign element sharing the diagram
(AMLPetriNet: `dotnet/PtMapper.Conversion/PtNetUpdater.cs:22-36`,
`starter/dotnet/Efl.Conversion/EflUpdater.cs:14-25`). Implement an updater from day one. Share the
writing primitives between the green-field writer and the updater so both produce the same shape
(AMLPetriNet: `dotnet/PtMapper.Conversion/PtWrite.cs:8-13`).

### 8.1 Check every precondition before the first write

```csharp
// Everything that can make the update fail is checked before the first
// write. An update writes element by element, so a failure halfway left
// a document that was part old, part new, and the next Ctrl+S saved
// exactly that.
notes.AddRange(PtIdText.MakeIdsDistinct(net));
CheckArcEnds(net);
CheckCarriedClasses(doc);
```
(AMLPetriNet: `dotnet/PtMapper.Conversion/PtNetUpdater.cs:56-64`)

Typical preconditions: every connection names two existing nodes (`PtNetUpdater.cs:80-95`), a
document that carries your SUC library carries every class you may instantiate
(`PtNetUpdater.cs:97-114`), ids are distinct. Throw with "Nothing was written" in the message.

### 8.2 Match by the language id, within your own element kinds

Build the index of existing elements from `Identification/id`, falling back to the CAEX name for
elements written against an older library, and only for your own element kinds; anything without an id
or of a foreign type stays untouched (`PtNetUpdater.cs:155-187`). The name fallback is not cosmetic:
an updater that only looked at `Identification` recognised nothing in older documents and added a
second copy of every element (`dotnet/PtMapper.Conversion/CaexToPtNet.cs:205-214`).

When the kind changed (a place edited into a transition by hand), remove and recreate rather than
reinterpret, and forget any wiring read from the removed element, or arcs reuse interfaces no longer in
the document and end up unlinked (`PtNetUpdater.cs:327-347`, `416-459`; test
`dotnet/PtMapper.Tests/UpdateInPlaceTests.cs:311`).

### 8.3 Write only what the language owns

One predicate decides which attributes belong to the language. Only those are written, and only those
are ever removed:

```csharp
internal static bool IsLanguageOwned(string? attributeName) =>
    attributeName != null
    && (attributeName == PtNames.Attributes.Identification
        || attributeName == PtNames.Attributes.ViewInformation
        || attributeName == PtNames.Attributes.LabelOffset
        || attributeName == PtNames.Attributes.InitialMarking
        || attributeName == PtNames.Attributes.Weight
        || attributeName == PtNames.Attributes.IsSilent
        || attributeName.StartsWith(PtNames.Attributes.WaypointPrefix, StringComparison.Ordinal));
```
(AMLPetriNet: `dotnet/PtMapper.Conversion/PtWrite.cs:16-28`; starter `starter/dotnet/Efl.Conversion/EflWrite.cs:14-25`)

Clean up language attributes that do not belong on the element's kind (left by a type change or an
older mapper version), but never touch a user's attribute (`PtWrite.cs:30-61`). Check each rule
against every kind: excluding arcs from the label offset rule removed and re-appended the attribute on
every update, moving it to the end of the element (`PtWrite.cs:39-43`).

fpb-aml-mapper's `UpdateExistingElement` limits itself to Name, Identification, ViewInformation and
Characteristics, and rewrites `Characteristic_N` entries idempotently
(`dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs:1244-1276`, `1912-1933`). Test:
`dotnet/FpbMapper.Tests/ConversionTests.cs:419`.

### 8.4 Do not rewrite what nobody changed

- CAEX `Name`: only write it when the language name actually differs from what the document stores in
  `Identification/name`. Another tool may have given the element a more telling display name
  (AMLPetriNet: `dotnet/PtMapper.Conversion/PtWrite.cs:118-143`, test
  `dotnet/PtMapper.Tests/ForeignConventionTests.cs:129`). fpb-aml-mapper overwrites the name whenever
  the editor's differs from the CAEX name (`FpbJsonToCaex.cs:1260-1264`), which is simpler but loses
  foreign display names.
- A value missing in the incoming model is not necessarily a deletion. The PT modeler never shows or
  edits an arc name, so an arc arriving without a name keeps the stored one; that loss once hit every
  arc of the paper example (`PtWrite.cs:188-194`).
- An unchanged model must leave the document byte-identical
  (`dotnet/PtMapper.Tests/UpdateInPlaceTests.cs:41`), and repeated updates converge (`:268`).

### 8.5 Reuse existing wiring

Read how the document already connects things before writing. Another tool names node interfaces
`NodeArcEnd_1` rather than `Out_<arc>`; renaming them, or worse reassigning their ids, breaks
everything that points at them (`PtNetUpdater.cs:192-196`, `363-414`). When an interface is found by
name, keep the id and class the document gave it (`PtWrite.cs:292-313`). Create a link only if no
link already joins the two interfaces, compared order-independently (`PtNetUpdater.cs:265-275`,
`358-360`).

### 8.6 Keep foreign content, remove only your stale content

- Foreign attributes, interfaces, links and elements survive (tests
  `dotnet/PtMapper.Tests/UpdateInPlaceTests.cs:54`, `:70`, `:99`;
  `dotnet/PtMapper.Tests/ReferenceSurvivalTests.cs:32`, `:50`, `:73`).
- Remove only interfaces of *your* endpoint class that the model no longer needs, together with every
  link that used them (`PtNetUpdater.cs:461-477`, `starter/dotnet/Efl.Conversion/EflUpdater.cs:109-121`).
  Keep the node interfaces of a connection the reader could not resolve: they belong to the half of it
  that still exists (`starter/dotnet/Efl.Conversion/EflUpdater.cs:111-116`, `:174-175`).
- Remove links touching a deleted element *anywhere in the document*, not only in your container. A
  link from a plant element in another hierarchy into a deleted transition would otherwise dangle.
  Report every removal outside your container, and leave a link alone if the interface id it names is
  carried by more than one element (`PtNetUpdater.cs:492-541`, test
  `dotnet/PtMapper.Tests/ReferenceSurvivalTests.cs:106`). The starter does the same and adds a note
  only when the link's owner is not the diagram, comparing owner and diagram by `ID`
  (`starter/dotnet/Efl.Conversion/EflUpdater.cs:275-294`).
- In encoding A, a link has no class of its own. fpb-aml-mapper removes links whose id is not in the
  incoming model and assumes the editor owns all links in the IH (`FpbJsonToCaex.cs:1053-1057`). The
  starter is stricter: it removes a link only if both ends are its own flow ports
  (`starter/dotnet/Efl.Conversion/EflUpdater.cs:208-238`). Prefer the stricter rule.
- Do not delete what the reader could not show. An arc whose links do not resolve was never in the
  diagram, so its absence from the incoming model is not a deletion. Compute the unresolved set before
  removing any interface (`PtNetUpdater.cs:198-203`, `278-291`). The starter reads the unresolved
  flow elements from the document before its first write and keeps them with a note asking the user
  to repair or delete them (`starter/dotnet/Efl.Conversion/EflUpdater.cs:67-69`, `157-164`; test
  `AnUpdateKeepsAFlowTheCanvasCouldNotShow` at `starter/dotnet/Efl.Tests/RoundTripTests.cs:165`).
- Keep indexes consistent with the tree after every removal (`FpbJsonToCaex.cs:503-510`, `1191-1208`).

### 8.7 Order of work

1. Preconditions (8.1), read the document's library convention (2.8), refresh DI paths.
2. Translate ids that differ between editor and CAEX (section 7).
3. Index existing elements, existing wiring, unresolved elements.
4. Pass 1: containers and elements. Pass 2: connections. fpb-aml-mapper switched to two passes
   because a flow listed before its source element in the payload failed with "not found in AML"
   (`FpbJsonToCaex.cs:217-223`).
5. Remove stale elements, interfaces and links.
6. Post-passes for derived consistency (FPD: boundary `refObj` sync and operator to sub-process name
   sync, `FpbJsonToCaex.cs:399-414`).
7. Return a summary with counts and notes for the confirmation dialog and the log
   (AMLPetriNet: `dotnet/PtMapper.Conversion/PtNetUpdater.cs:7-20`).

---

## 9. Read back tolerantly

The document may have been edited by hand in the AML Editor between two visits. A reader that throws
turns one odd element into an unusable document (`starter/dotnet/Efl.Conversion/CaexToEfl.cs:6-15`).

- Recognise element types by SUC path or by role requirement, comparing the last path segment so any
  alias works (AMLPetriNet: `dotnet/PtMapper.Conversion/CaexToPtNet.cs:216-230`, `PtCaex.cs:70-77`).
  fpb-aml-mapper strips the alias prefix before comparing full paths
  (`dotnet/FpbMapper.Conversion/FpbMappings.cs:32-45`; documents that referenced the FPD libraries
  through an alias were not recognised before; test `dotnet/FpbMapper.Tests/AliasToleranceTests.cs:65`).
  Return null for foreign elements and skip them.
- Language id from `Identification/id`, falling back to CAEX `Name` (`CaexToPtNet.cs:205-214`).
- Label from `Identification/name`. When the mapper wrote the element (id present), an empty name
  stays empty; it must not come back as the id. When a user created the element by hand (no id), the
  CAEX name is the label. Since the class brings an empty `Identification` along, test whether the id
  is filled, not whether the attribute exists (`CaexToPtNet.cs:237-253`).
- Values fall back to `DefaultValue` (`CaexToPtNet.cs:306-318`); a declared default of 5 must not read
  as 0.
- Links may be on any element below the container (`PtCaex.cs:35-49`), and the links are the truth for
  connectivity, not interface names, because a user may have rewired them (`CaexToPtNet.cs:147-182`).
- Unresolvable connections are reported as warnings and remembered as unresolved, not silently dropped
  (`CaexToPtNet.cs:124-133`).
- One-sided references: derive a missing sub-process `refObj` from the operator's `refProcess`, with a
  warning. Without this the whole decomposition read flat with zero warnings
  (fpb-aml-mapper: `dotnet/FpbMapper.Conversion/CaexToFpbJson.cs:83-100`).
- Dangling references are not exported to the editor (`CaexToFpbJson.cs:400-413`).
- Normalise ids for the editor (section 6.6) and never leak a braced id where bare ones are expected;
  a braced fallback id once made the next update miss every connection
  (`CaexToFpbJson.cs:944-950`).
- Missing layout gets fallbacks (section 4.3).
- Several models in one hierarchy or document are read in document order (`CaexToPtNet.cs:18-34`).

---

## 10. Validation

Separate "is this a valid model of the language" from "did the mapper preserve the document".

### 10.1 Rules on the model (AMLPetriNet, starter)

`PtValidator` works on the net model, not on CAEX, so the same rules cover a net that came from PNML
and never reached AML (AMLPetriNet: `dotnet/PtMapper.Conversion/PtValidator.cs:24-49`). Findings carry
a stable rule id, a severity (Error: violates the standard; Warning: legal but almost certainly
unintended) and the element id the modeler uses, so a finding can select its element
(`PtValidator.cs:5-22`). Rule ids: PT01 duplicate id, PT02 missing id, PT03 not bipartite, PT04 arc
endpoint not a node, PT05 parallel arcs, PT06 negative marking, PT07 weight below 1, PT08 isolated
node, PT09 transition without input, PT10 transition without output, PT11 id not a valid XML name
(`PtValidator.cs:51-189`). Keep the set small: rules that are really modelling advice do not belong
in a conformance check (`PtValidator.cs:27-31`). The starter has rules EFL01 to EFL07 (EFL07: a node
without any flow) and publishes the ids in a list so a report can say which rules passed
(`starter/dotnet/Efl.Conversion/EflValidator.cs:41-43`); give every distinct check its own id. Its
test iterates that list and fails when a rule id has no broken model that triggers it, so a rule
cannot be added and forgotten (`starter/dotnet/Efl.Tests/ValidatorTests.cs:69-80`).

### 10.2 Rules on CAEX with OCL (AMLFPB.js, fpb-aml-mapper)

The FPD rules are OCL invariants shipped as embedded resources of `FpbMapper.Conversion`, so the
plugin, the web backend and CI run the same constraints
(fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpbValidationRules.cs:5-16`, csproj
`dotnet/FpbMapper.Conversion/FpbMapper.Conversion.csproj:10-22`). Three files: 26 executable
structural rules, `def:` helper operations for geometry, and phase-2 rules on characteristics that are
not yet executable against the CAEX binding and are shipped so the engine reports "context type
unknown" instead of passing them vacuously (`Rules/vdi3682-phase2-rules.ocl:3-8`). Example:

```
context FPD_Object inv RefObjResolvable:
  self.refObj->notEmpty() implies self.project.containedElement->exists(e | e.identification.uniqueIdent = self.refObj)

context FPD_ProcessOperator inv RefProcessResolvable:
  self.refProcess->notEmpty() implies self.project.process->exists(p | p.id = self.refProcess)
```
(fpb-aml-mapper: `dotnet/FpbMapper.Conversion/Rules/vdi3682-pure-rules.ocl:72-76`)

The rules are executed by OCL.NET, a domain-free OCL engine with a CAEX binding. The binding maps
CAEX to OCL types, including connections that are only InternalLinks: the flow type of a link is read
from its A-side interface class. That is how encoding A still gets typed connections in OCL contexts
such as `FPD_Flow`.

In the plugin, rule ids are `VDI3682.OCL.<InvariantName>`; severities are assigned per invariant in a
catalogue keyed A1..I3; invariants already covered by hard-coded C# rules are skipped to avoid double
findings; the compiled rule set is cached with `LazyThreadSafetyMode.PublicationOnly` so a transient
failure is retried instead of frozen for the session
(AMLFPB.js: `Aml.Editor.Plugin.FPB/Validation/Vdi3682OclRuleSet.cs:25-26`, `28-61`, `63-73`, `75-84`).
The pass never throws; an engine failure becomes one diagnostic finding. It returns early for
documents without any FPD process (otherwise `ProjectMinimumProcess` flags every foreign AML file) and
reports elements inside an FPD process that no rule can classify, because they are invisible to every
rule (`Vdi3682OclRuleSet.cs:155-189`). Severity tuning is part of the work: `LongNameMandatory` and
`VersionRevisionPresent` flooded every element because FPB.JS does not maintain those fields, and were
lowered to Warning and Info (`Vdi3682OclRuleSet.cs:47-52`).

### 10.3 Which to choose

| | Rules on the model | OCL on CAEX |
|---|---|---|
| Covers files that never became AML | Yes | No |
| Covers hand edits in the AML Editor | Only after reading | Yes, directly |
| Rules readable by non-programmers, portable | No | Yes |
| Dependency | None | OCL engine plus a type binding for your library |
| Silent-pass risk | Low | Needs guards for unknown contexts and unclassified elements |

A new language can start with model rules (fast, no dependency) and add OCL when the rules are meant
to be published with the library.

### 10.4 Validate the mapper, not only the model

AMLPetriNet's `PtRoundTripCheck` runs AML to PNML to AML and PNML to AML to PNML on a real document
and compares identity (CAEX ids, `Identification`, names), markings and weights, connections
(which arc runs from where to where, because counts and ids stay equal when only an endpoint moves),
geometry including docking points, links into other hierarchies, external references, class
libraries, and everything outside the net's own content, line by line
(`dotnet/PtMapper.Conversion/PtRoundTripCheck.cs:20-124`, `238-246`, `348-363`). It runs in the test
suite and on the command line against files saved by the AML Editor. Details in
[07 Testing and CI](07-testing-and-ci.md).

---

## 11. Several instance hierarchies and diagrams in one document

- Find your hierarchies by content, not by position: every IH that contains at least one of your
  container elements (fpb-aml-mapper: `dotnet/FpbMapper.Conversion/CaexToFpbJson.cs:26-38`,
  AMLPetriNet: `dotnet/PtMapper.Conversion/CaexToPtNet.cs:36-37`). Leave every other IH alone.
- Make every write target an explicit hierarchy. fpb-aml-mapper grew
  `UpdateInPlace(doc, json, targetIh)` and `Convert(doc, ih)` for the multi-IH plugin
  (`dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs:123-132`, `CaexToFpbJson.cs:40-56`); the
  parameterless variants that take the first matching IH are kept only for legacy callers
  (`FpbJsonToCaex.cs:1417-1426`, `CaexToFpbJson.cs:12-24`). If no hierarchy is found, the FPD updater
  appends a fresh one and warns (`FpbJsonToCaex.cs:174-181`).
- Several diagrams per hierarchy are legitimate. AMLPetriNet writes all nets of a PNML file into one
  IH instead of dropping all but the first, and the updater takes a `netIndex`; an index past the end
  appends rather than overwriting the wrong net (`dotnet/PtMapper.Conversion/PtNetToCaex.cs:58-62`,
  `PtNetUpdater.cs:39-48`).
- Stamp an id on every IH you create, derived and distinct, so the plugin can find its hierarchy again
  after someone renames it (`PtIds.cs:66-70`, `PtNetToCaex.cs:78-86`). The PT plugin binds by id and
  name and follows a rename (AMLPetriNet: `Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:431-453`).
- Importing appends a new IH and keeps imports isolated (`FpbJsonToCaex.cs:1518-1522`). Combined with
  document-wide distinct ids (section 6.3) this is safe; without it, a second import duplicates ids.
- Offer a way to create an empty diagram in an existing document, with sensible default layout for the
  container (`FpbJsonToCaex.cs:42-88`).
- Links between hierarchies are the point of bridging documents (a plant signal linked to a transition
  of a behaviour model). Treat them as foreign content that must survive (section 8.6), and check them
  in the round-trip comparison (`PtRoundTripCheck.cs:300-329`).

---

## Checklist

Library
- [ ] One names class holds every library, class, interface and attribute name and path.
- [ ] Four libraries with a language prefix, `Version` on libraries and classes.
- [ ] Roles carry meaning; SUCs mirror them with `SupportedRoleClass`, plus AML base roles where one fits.
- [ ] Shared attributes (`Identification`, `ViewInformation`) on one abstract base role and base SUC.
- [ ] Language id and name in `Identification`, CAEX `Name` treated as display name.
- [ ] Precise `xs:` data types and `DefaultValue` where the language defines one.
- [ ] Abstract port derived from AML base `Port`, carrying `PortCoordinate`; direction encoded in interface classes.
- [ ] References use the official ObjectReferences library, with the subtype that states the semantics; values brace-free.
- [ ] Decided per relation: nesting (composition) or flat with `refObj` (views, layers).
- [ ] AML base libraries referenced by file name, never by a URL with a token; own library, OMG_DD and (if the language has references) ObjectReferences carried in documents people open, embedded from the published files.
- [ ] A document that references its libraries keeps doing so after a sync; aliases in use are followed.
- [ ] Library published as `.aml` with fixed class ids; golden test compares code and file.

Connections and layout
- [ ] Connection encoding chosen per connection kind (link or reified element) and written into the class descriptions.
- [ ] One interface per incident connection, names unique per element.
- [ ] Removing a connection removes its now unused interfaces.
- [ ] Layout uses `DD_Bounds`, `DD_Point`, `DD_Waypoint`; waypoints ordered by numeric index and replaced wholesale.
- [ ] Label offsets removed when reset; no empty bounds on connection elements.
- [ ] Layout-free documents read with fallback geometry; computed layout reported, not hidden.
- [ ] All numbers written and parsed with the invariant culture, fixed precision.

Aml.Engine
- [ ] `using Aml.Engine.CAEX.Extensions;` for `CreateClassInstance(name)`.
- [ ] Remaining `SupportedRoleClass` entries appended as role requirements.
- [ ] `ID` set explicitly after every create.
- [ ] `Insert(child, false)` everywhere.
- [ ] No `ReferenceEquals` on wrappers; compare ids or `.Node`.
- [ ] Test parallelisation disabled in every test assembly that builds documents.
- [ ] Document ids read once from XML, not per element through the engine.
- [ ] One load helper that rejects a document with a null `CAEXFile`; files loaded and saved as text, not through `LoadFromFile`/`SaveToFile`.

Identity
- [ ] Deterministic name-based UUIDs with namespace, NUL separator, kind discriminator; version nibble on byte 7.
- [ ] Interface and link ids derived from the owner's CAEX id.
- [ ] Ids claimed against the whole document; deterministic ordinal on collision.
- [ ] One id comparison policy in one place.
- [ ] Logical elements shown in several places get one CAEX element per place, linked by `refObj`, reunified on read.

Update in place
- [ ] All preconditions checked before the first write.
- [ ] Existing elements indexed by language id (with name fallback), own kinds only.
- [ ] One `IsLanguageOwned` predicate; nothing else written or removed.
- [ ] Display names and missing values not overwritten without a real change.
- [ ] Existing wiring reused; interfaces found by name keep their id.
- [ ] Foreign attributes, interfaces, links, elements survive (tested).
- [ ] Links into deleted elements removed document-wide and reported.
- [ ] Unresolved elements not deleted.
- [ ] Elements before connections; summary with counts and notes.
- [ ] Unchanged model leaves the document byte-identical (tested).

Reading and validation
- [ ] Types recognised by SUC path or role, alias-tolerant; foreign elements skipped.
- [ ] Label and id fallbacks, `DefaultValue` fallback, links anywhere below the container.
- [ ] Unresolvable content reported as warnings, never thrown.
- [ ] Rules have stable ids, severities and the element id the modeler uses.
- [ ] Validator guards against foreign documents and unclassified elements.
- [ ] Round-trip check runs in tests and against editor-saved files.

Multiple diagrams
- [ ] Hierarchies found by content; every write targets an explicit hierarchy.
- [ ] Several diagrams per hierarchy supported, index past the end appends.
- [ ] Hierarchy ids stamped so a rename does not lose the binding.

---

## Where to look

| Topic | File |
|---|---|
| Names and paths in one place | AMLPetriNet: `dotnet/PtMapper.Conversion/PtNames.cs`; fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpbMappings.cs`; `starter/dotnet/Efl.Conversion/EflNames.cs` |
| Library generator | AMLPetriNet: `dotnet/PtMapper.Conversion/PtLibraries.cs`; fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpdLibraries.cs`; `starter/dotnet/Efl.Conversion/EflLibraries.cs` |
| Published library artefacts | AMLPetriNet: `libraries/ISO_PT_DomainLibrary_v0.3.aml`, `libraries/OMG_DD_AttributeTypeLib_v0.1.aml`, `libraries/AutomationML_ObjectReferences_AttributeTypeLib_AMLEd2_1.1.1-beta.aml` |
| Library golden test | AMLPetriNet: `dotnet/PtMapper.Tests/PtLibraryGoldenTests.cs` |
| Referenced vs carried libraries | AMLPetriNet: `dotnet/PtMapper.Conversion/PtLibraryLoan.cs`, `PtSelfContained.cs`, `PtDiPaths.cs`, `ObjectReferencesLibraryFile.cs` |
| Diagram interchange library | AMLPetriNet: `dotnet/PtMapper.Conversion/DiagramInterchangeLibrary.cs`; fpb-aml-mapper: `dotnet/FpbMapper.Conversion/DiagramInterchangeLibrary.cs`; `starter/libraries/OMG_DD_AttributeTypeLib_v0.1.aml`, `starter/dotnet/Efl.Conversion/EflDiagramInterchange.cs`, `starter/dotnet/Efl.Tests/DiagramInterchangeTests.cs` |
| Reference types and options | fpb-aml-mapper: `dotnet/FpbMapper.Conversion/ReferenceTypes.cs`, `MapperOptions.cs`; objectreferences-aml-editor-plugin: `Aml.Editor.Plugin.ObjectReferences/Core/ReferenceLibrary.cs`, `Core/ReferenceScanner.cs` |
| Green-field writer, encoding B | AMLPetriNet: `dotnet/PtMapper.Conversion/PtNetToCaex.cs`, `PtWrite.cs` |
| Green-field writer, encoding A | fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs` (`BuildProcess`) |
| Both encodings side by side | `starter/dotnet/Efl.Conversion/EflToCaex.cs`, `CaexToEfl.cs`, `EflUpdater.cs` |
| Deterministic ids | AMLPetriNet: `dotnet/PtMapper.Conversion/PtIds.cs`, `PtIdText.cs`; `starter/dotnet/Efl.Conversion/EflIds.cs` |
| Update in place | AMLPetriNet: `dotnet/PtMapper.Conversion/PtNetUpdater.cs`; fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs` (`UpdateInPlace`) |
| Boundary states across layers | fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs` (`DeriveBoundaryStateId`, `RemapSubProcessBoundaryStateIds`), `CaexToFpbJson.cs` (reunification) |
| Tolerant reader | AMLPetriNet: `dotnet/PtMapper.Conversion/CaexToPtNet.cs`, `PtCaex.cs`; fpb-aml-mapper: `dotnet/FpbMapper.Conversion/CaexToFpbJson.cs` |
| Geometry and layout | AMLPetriNet: `dotnet/PtMapper.Conversion/PtGeometry.cs`, `PtLayout.cs`; `starter/dotnet/Efl.Conversion/EflGeometry.cs`, `EflLayout.cs` |
| Loading and saving AML safely | `starter/dotnet/Efl.Conversion/EflDocuments.cs` |
| Model validator | AMLPetriNet: `dotnet/PtMapper.Conversion/PtValidator.cs`; `starter/dotnet/Efl.Conversion/EflValidator.cs` |
| OCL rules and plugin integration | fpb-aml-mapper: `dotnet/FpbMapper.Conversion/Rules/*.ocl`, `FpbValidationRules.cs`; AMLFPB.js: `Aml.Editor.Plugin.FPB/Validation/Vdi3682OclRuleSet.cs`, `Vdi3682Validator.cs` |
| Round-trip check | AMLPetriNet: `dotnet/PtMapper.Conversion/PtRoundTripCheck.cs` |
| Update and survival tests | AMLPetriNet: `dotnet/PtMapper.Tests/UpdateInPlaceTests.cs`, `ReferenceSurvivalTests.cs`, `ForeignConventionTests.cs`, `IdentityTests.cs`; fpb-aml-mapper: `dotnet/FpbMapper.Tests/ConversionTests.cs`, `AliasToleranceTests.cs`, `WaypointFallbackTests.cs`, `ObjectReferencesLibraryTests.cs` |
| Hierarchy binding in a plugin | AMLPetriNet: `Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs` |
