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
Drath, Ocker, Fay; submitted to at Automatisierungstechnik), with earlier steps published at EKA 2026
and ETFA 2026.

---

## 1. CAEX 3.0 in one page

A CAEX file is one `CAEXFile` root with children in a fixed order. The schema order is
`SuperiorStandardVersion`, `SourceDocumentInformation` (at least one, required), `ExternalReference`,
`InstanceHierarchy`, `InterfaceClassLib`, `RoleClassLib`, `SystemUnitClassLib`, `AttributeTypeLib`
(the CAEX 3.0 schema `CAEX_ClassModel_V.3.0.xsd`, element `CAEXFile`; the same order is spelled out in
`AMLPetriNet: dotnet/PtMapper.Conversion/PtSelfContained.cs` (`ChildOrder`)).

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
(AMLPetriNet: `dotnet/PtMapper.Conversion/PtNames.cs`, `AmlBase`). The same class can therefore be named
two ways in two documents, depending on whether the library is carried inline or referenced. Your
reader must accept both (section 9) and your writer must follow whatever the document already does
(section 2.8).

A minimal instance written by the PT mapper looks like this (shape documented in
the class comment of `AMLPetriNet: dotnet/PtMapper.Conversion/PtNetToCaex.cs` (`PtNetToCaex`)):

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
(AMLPetriNet: `dotnet/PtMapper.Conversion/PtNames.cs`, constants `InterfaceClassLib`, `RoleClassLib`,
`SystemUnitClassLib`, `AttributeTypeLib`), `VDI_FPD_*`
(fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpbMappings.cs`, `LibNames`), `EFL_*`
(`starter/dotnet/Efl.Conversion/EflNames.cs`, same four constants). Give the role class, the system unit class and
the concept the same name (`PT_Place` is both), and put every name and path in one static class so
the generator, the reader, the updater and the tests cannot drift apart
(AMLPetriNet: `dotnet/PtMapper.Conversion/PtNames.cs`, `PtNames`).

Why: the FPB plugin once carried its own copy of the conversion layer with `FPD_*` paths while the
mapper had moved on to `VDI_FPD_*`; the drift cost hours of diagnosis and ended with the plugin
referencing the mapper project instead of copying it. Renaming classes later means touching documents
that already exist, so spend the minute on names up front (`starter/dotnet/Efl.Conversion/EflNames.cs`, class comment of `EflNames`).

Put a `Version` on every library and class and bump it when the emitted shape changes
(fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpbMappings.cs`, `LibNames.Version`).

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
(AMLPetriNet: `libraries/ISO_PT_DomainLibrary_v0.3.aml`, `SystemUnitClass` `PT_Net`)

The FPD library does the same and adds AML base roles where one fits: `FPD_Product` also supports
`AutomationMLBaseRole/Product`, `FPD_ProcessOperator` supports `Process`, `FPD_TechnicalResource`
supports `Resource`, while Energy and Information have no base equivalent
(fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpdLibraries.cs`, `EnsureSystemUnitClassLib`).

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
(AMLPetriNet: `dotnet/PtMapper.Conversion/PtWrite.cs`, `CreateInstance`; the same fix in
fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs`, `CreateInstance`, and
`starter/dotnet/Efl.Conversion/EflWrite.cs`, `CreateInstance`)

### 2.3 Shared attributes on one abstract base

Put what every element has on one abstract base role and base SUC: `PT_Element` carries
`Identification` and `ViewInformation`, and `PT_Place`, `PT_Transition`, `PT_Arc` inherit via
`RefBaseClassPath` (AMLPetriNet: `dotnet/PtMapper.Conversion/PtLibraries.cs`, `EnsureRoleClassLib` for roles,
`EnsureSystemUnitClassLib` for SUCs). FPD uses `FPD_Object` with `Identification`, `Characteristics`, `ViewInformation`,
then `FPD_State` below it and the three concrete states below that
(fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpdLibraries.cs`, `EnsureRoleClassLib`). The FPD RoleClassLib is flat
(every role a direct child of the library) with inheritance expressed only through
`RefBaseClassPath`; the SUC library mirrors the hierarchy (class comment of `FpdLibraries`).

Keep the container role separate (`PT_Net` derives from AML `Structure`, not from `PT_Element`), so
container attributes such as a net-level `refObj` do not leak onto nodes.

Store the language's own id and name in an `Identification` compound, not in the CAEX `Name`. The
CAEX `Name` is a display name that people and other tools rename. ISO_PT added `Identification` to
`PT_Net` in v0.3 because without it a round trip had nowhere to put the net id and renamed the net
after its label (AMLPetriNet: `dotnet/PtMapper.Conversion/PtLibraries.cs`, `EnsureRoleClassLib`, the
`AddIdentification(net)` call). For FPD the
compound follows VDI 3682 (uniqueIdent, longName, shortName, versionNumber, revisionNumber), with the
field list kept in one class (fpb-aml-mapper: `dotnet/FpbMapper.Conversion/IdentificationSchema.cs`, `IdentificationSchema`).

**Elements without a name.** Markers, connections and pseudo elements often have an id but no name in the language. Give such an element its language id as CAEX `Name` and leave `Identification/name` empty. A derived label (`"start [doorClosed]"`) as CAEX `Name` looks friendlier in the tree, but the updater then rewrites it on every update and overwrites a name someone gave the element by hand.

### 2.4 AttributeTypeLib: small, typed, with defaults

Declare only what the language really has. ISO_PT declares a single `PT_Identification` type
because ISO/IEC 15909-1 has a small ontology (AMLPetriNet: `dotnet/PtMapper.Conversion/PtLibraries.cs`, `EnsureAttributeTypeLib`);
FPD needs a large `FPD_Characteristic` structure because VDI 3682 Part 2 specifies one
(fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpdLibraries.cs`, `EnsureAttributeTypeLib`).

Use precise XML Schema types and a `DefaultValue` where the language defines one:
`InitialMarking` is `xs:nonNegativeInteger` with default `0`, `Weight` is `xs:positiveInteger` with
default `1` (AMLPetriNet: `dotnet/PtMapper.Conversion/PtLibraries.cs`, `AddInitialMarking`, `AddWeight`). If you declare a
default, your reader must fall back to it (section 9).

Do not declare layout types in your own AttributeTypeLib. Use the shared `OMG_DD_AttributeTypeLib`
(section 4). The starter already does: it ships the published file in `starter/libraries/`, embeds it
as an assembly resource and writes every layout type path through `EflDiagramInterchange`, whose
comment states the reason (`starter/dotnet/Efl.Conversion/EflDiagramInterchange.cs`, class comment of `EflDiagramInterchange`).

**Optional attributes: absent or empty.** Decide per optional attribute whether the writer leaves it out or writes it with an empty value, and make the reader treat both as "not set". Removing an attribute the class declares makes instances in the same document look different in the AutomationML Editor (some states show an entry action field, some do not); the trial run of this playbook chose "keep, empty" for optional texts.

### 2.5 InterfaceClassLib: an abstract port, direction in the type

Derive one abstract port from the AML base `Port` and give it the docking point attribute. Derive the
concrete interfaces from it:

- Link encoding (FPD, EFL link style): a pair per connection kind, the direction in the class name.
  `FPD_FlowOut`/`FPD_FlowIn`, `FPD_ParallelFlowOut`/`FPD_ParallelFlowIn`,
  `FPD_AlternativeFlowOut`/`FPD_AlternativeFlowIn`, and one symmetric `FPD_Usage`
  (fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpdLibraries.cs`, `EnsureInterfaceClassLib`, mapping table in
  `FpbMappings.cs`, `FlowToInterface`).
- Reified encoding (ISO_PT, EFL element style): node side and connection side are distinct classes.
  `PT_NodeArcEnd` on places and transitions, `PT_ArcSource` and `PT_ArcTarget` on the arc
  (AMLPetriNet: `dotnet/PtMapper.Conversion/PtLibraries.cs`, `EnsureInterfaceClassLib`).

Why direction in the type: a reader can tell the ends of a connection apart without following the
link, and a validator can check "flows are directed" by interface class alone (the OCL invariant
`FlowDirected` in fpb-aml-mapper: `dotnet/FpbMapper.Conversion/Rules/vdi3682-pure-rules.ocl`).
The starter states the same reasoning (`starter/dotnet/Efl.Conversion/EflLibraries.cs`, comment in `EnsureInterfaceClassLib`).

Give every node one interface per connection end, named after the connection:
`Out_<arcId>` and `In_<arcId>` (AMLPetriNet: `dotnet/PtMapper.Conversion/PtWrite.cs`, `EndpointName`;
`starter/dotnet/Efl.Conversion/EflToCaex.cs`, `PortName`). Two parallel connections between the same pair
of nodes then stay apart. The FPD mapper instead names interfaces after their class and numbers
duplicates (`FPD_FlowOut`, `FPD_FlowOut_2`); if you do that, seed the counter from the highest suffix
already present, not from the count, or a later add mints a name that already exists
(fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs`, `SeedIfaceCounters`).

**One interface per connection end, not per connection.** Key a node's ports by node, connection and direction. A connection from a node to itself has both ends on that node; keyed without the direction, the writer created one port and attached both links to it (found in the trial run, fixed in `starter/dotnet/Efl.Conversion/EflToCaex.cs` with `PortKey` and `EndsAt`, used by the writer in `AppendInto` and by the updater in `starter/dotnet/Efl.Conversion/EflUpdater.cs` (`UpdateInPlace`), with the test `AFlowFromANodeToItselfGetsTwoPortsAndSurvivesTheRoundTrip` in `starter/dotnet/Efl.Tests/RoundTripTests.cs`).

### 2.6 Cross-element and cross-diagram references: the refObj family

Use the official `AutomationML_ObjectReferences_AttributeTypeLib` (AutomationML e.V., v1.1.1-beta)
for references between objects. It defines an abstract `refObj` (`xs:IDREF`) and four derived types:
`refBaseObj` (aspect object points to its base object, same logical object), `refAspectObj` (the
reverse), `refDetailObj` (abstract object points to a more detailed representation), `refAbstractObj`
(the reverse) (AMLPetriNet: `libraries/AutomationML_ObjectReferences_AttributeTypeLib_AMLEd2_1.1.1-beta.aml`,
attribute types `refObj`, `refBaseObj`, `refAspectObj`, `refDetailObj`, `refAbstractObj`).
Reference it with alias `ObjectReferences` (AMLPetriNet: `dotnet/PtMapper.Conversion/PtNames.cs`, `ObjectReferencesLibrary`).

The attribute *name* and the attribute *type* are separate decisions. FPD keeps the VDI 3682 names
and chooses the semantics through `RefAttributeType`:

| Attribute on | Name | Typed as | Meaning |
|---|---|---|---|
| `FPD_ProcessOperator` | `refProcess` | `refDetailObj` | the sub-process is the detailed representation |
| `FPD_Process` (sub-process) | `refObj` | `refAbstractObj` | back to the decomposed operator |
| `FPD_State` (boundary copy) | `refObj` | `refBaseObj` | same logical state as the top-level original |

(fpb-aml-mapper: `dotnet/FpbMapper.Conversion/MapperOptions.cs`, `UseObjectReferencesLibrary` and
`EffectiveRefProcessAttributeTypePath`, `EffectiveSubProcessRefObjAttributeTypePath`,
`EffectiveBoundaryStateRefObjAttributeTypePath`; written by `FpdLibraries.cs`, `AddRefAttr`.) A legacy
switch keeps the v0.5 layout with a local `xs:string` `refObj` (`MapperOptions.cs`, `UseObjectReferencesLibrary`
set to false; test `UpdateInPlace_OnLegacyDocument_KeepsLegacyLayout` in
`dotnet/FpbMapper.Tests/ObjectReferencesLibraryTests.cs`).

Rules for reference values:

- Store the target's id without braces. The FPD rule `RefObjResolvable` compares `refObj` against
  `identification.uniqueIdent`, which is brace-free; a braced value made it fail
  (fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs`, `AddProcessImpl`, rule in
  `Rules/vdi3682-pure-rules.ocl`). Any tool that writes reference values should strip braces the
  same way, or its references fail the same rule.
- Clearing a reference must write an empty value. An early setter ignored empty strings, so a parent
  operator kept `refProcess` pointing at a removed sub-process (fpb-aml-mapper:
  `dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs`, `SetAttrValue`).
- On read, accept the base name and the derived names as attribute names, refObj first
  (fpb-aml-mapper: `dotnet/FpbMapper.Conversion/ReferenceTypes.cs`, `GetRefObjOrDerived`).
- Recognise a reference by its `RefAttributeType` *after stripping the alias*. A reader that compares
  `RefAttributeType` against `AutomationML_ObjectReferences_AttributeTypeLib/` with `StartsWith` does
  not claim the alias-qualified form `ObjectReferences@...` that fpb-aml-mapper writes
  (fpb-aml-mapper: `dotnet/FpbMapper.Conversion/MapperOptions.cs`, `ObjectReferencesLibrary.QualifiedLibName`).
  Earlier drafts of the reference types used `refExtendedObj` and `refComposedObj`; the published
  1.1.1-beta file has `refAspectObj`, `refDetailObj` and `refAbstractObj`. Pin your language to the
  published file and treat the file as the authority.
- `xs:IDREF` only resolves inside one document. Cross-document references are not solved by the
  library; a tool that checks references has to report them as unresolved.

### 2.7 Flat with refObj, or nesting

CAEX lets you nest InternalElements, and it is tempting to put a sub-diagram inside the element it
refines. Both source projects decided otherwise where the relation is a view relation rather than
composition:

- FPD puts every `FPD_Process` (top level and every decomposition layer) directly under the
  InstanceHierarchy and connects the layers with `refProcess` on the operator and `refObj` on the
  sub-process (fpb-aml-mapper: `dotnet/FpbMapper.Conversion/CaexToFpbJson.cs`, `Convert`, written in
  `FpbJsonToCaex.cs`, `BuildProcess`). A layer is then addressable on its own, can be
  removed without touching its parent's subtree, and a decompose, compose, decompose cycle does not
  move elements between parents.
- Inside a process the states, operators and the system limit are siblings: `FPD_SystemLimit` is
  described as "Peer aggregate of the process, not a container"
  (`dotnet/FpbMapper.Conversion/FpdLibraries.cs`, `EnsureRoleClassLib`). A state's containment in the system limit
  is geometric, not structural, so moving a state across the border never re-parents an element.
- ISO_PT nests nodes and arcs one level below `PT_Net`, because they are parts of the net, and puts
  the arc-to-node links on the net element (AMLPetriNet: `dotnet/PtMapper.Conversion/PtNetToCaex.cs`, `WriteNet`).

Rule of thumb: nest what the language treats as composition, flatten and reference what the language
treats as another view on the same thing. If you flatten, provide the reverse-lookup fallback on read:
externally authored files often fill only one direction (section 9).

### 2.8 External references: what to reference, what to carry

1. Reference the AML base libraries, never inline them. Every AML tool knows them and inlining would
   dwarf the document (AMLPetriNet: `dotnet/PtMapper.Conversion/PtLibraries.cs`, `EnsureLibraries`). Reference
   them by file name, resolved through the tool's library search path, never by a URL that contains a
   token or credentials: the `Path` is written into every document the mapper produces and reaches
   everyone who opens one. fpb-aml-mapper (`dotnet/FpbMapper.Conversion/FpbMappings.cs`, `AmlBase.Path`) and
   the starter (`starter/dotnet/Efl.Conversion/EflLibraries.cs`, `BasePath`) write the file name.
   AMLPetriNet writes the file name too, and the comment on its path gives the reason: a share link
   with an access token in it would travel with every file (`AMLPetriNet: dotnet/PtMapper.Conversion/PtNames.cs`,
   `AmlBase.Path`). The published FPD library file references the base libraries by file name as well
   (`fpb-aml-mapper: public/VDI_FPD_DomainLibrary_v0.7.aml`, the `ExternalReference`). The AML Editor loads no referenced document in any form and takes the base libraries
   from its library manager, so a URL buys nothing there.
2. Carry your own library and the two small shared libraries (OMG_DD, ObjectReferences) inside any
   document a person will open. The AML Editor does not follow file references: a document saved away
   from the library files opened with geometry and `refObj` unresolved, which users see as a broken
   file (AMLPetriNet: `dotnet/PtMapper.Conversion/PtLibraries.cs`, `EmbedSharedLibraries`;
   `dotnet/PtMapper.Conversion/PtSelfContained.cs`, class comment of `PtSelfContained`). PT embeds the ObjectReferences file as an
   assembly resource for exactly this, because the plugin has no library folder in reach
   (`dotnet/PtMapper.Conversion/ObjectReferencesLibraryFile.cs`, class comment of `ObjectReferencesLibraryFile`; the
   `EmbeddedResource` item in `dotnet/PtMapper.Conversion/PtMapper.Conversion.csproj`). The starter carries OMG_DD the
   same way: one copy of the published file in `starter/libraries/`, linked into the assembly as an
   `EmbeddedResource` (`starter/dotnet/Efl.Conversion/Efl.Conversion.csproj`), put into a
   document that neither carries nor references it by `EflDiagramInterchange.EnsureIn`
   (`starter/dotnet/Efl.Conversion/EflDiagramInterchange.cs`), and a test checks that the
   embedded copy equals the file (`TheEmbeddedLibraryIsThePublishedFile` in
   `starter/dotnet/Efl.Tests/DiagramInterchangeTests.cs`). Do
   the same for ObjectReferences if your language needs references: take the published file, never
   rebuild its types from a description (see [09 Pitfalls](09-pitfalls.md), PF-AML-16).
3. When you inline a library, strip the alias prefix from the paths *inside your own libraries only*.
   Paths in another hierarchy are not yours to rewrite (AMLPetriNet:
   `dotnet/PtMapper.Conversion/PtLibraries.cs`, `StripAliasInOwnLibraries`).
4. Respect a document that references its libraries. AMLPetriNet's `PtLibraryLoan` detects the alias
   the document uses, adds the class definitions for the duration of the write so that
   `CreateClassInstance` works, removes them again and requalifies the new paths with the document's
   alias (`dotnet/PtMapper.Conversion/PtLibraryLoan.cs`, class comment, `Open`, `Close`, `AliasInUse`). It reads the alias
   from paths in use, not from the `ExternalReference` list, because an alias nobody points at says
   nothing about the convention (comment of `AliasInUse`). Without it, editing one node wrote four
   class libraries into a file whose author kept them outside. Tests:
   `SyncingAReferencingDocumentWritesNoLibrariesIntoIt` and `AnElementAddedToItFollowsTheSameClassPaths`
   in `AMLPetriNet: dotnet/PtMapper.Tests/ForeignConventionTests.cs`.
5. Follow the document's form for shared type paths. `PtDiPaths` resolves once per write whether the
   OMG_DD library is inline (`OMG_DD_AttributeTypeLib/DD_Point`), referenced under some alias
   (`<alias>@OMG_DD_AttributeTypeLib/DD_Point`) or absent (then it adds the reference). Writing the
   aliased form into a self-contained document left a dangling alias on every attribute a sync touched
   and the round-trip check refused the result (AMLPetriNet: `dotnet/PtMapper.Conversion/PtDiPaths.cs`, class comment and `Resolve`).
   The starter's `EflDiagramInterchange.PathOf` and `PathIn` do the same: the unqualified path when
   the library is inline, `<alias>@...` when the document references the file under any alias, found
   by file name (`starter/dotnet/Efl.Conversion/EflDiagramInterchange.cs`, `PathOf`, `PathIn`, `ReferenceTo`). The published
   library artefact references the file instead of carrying it (`ReferenceFrom`, called
   from `starter/dotnet/Efl.Conversion/EflLibraries.cs` in `EnsureLibraries` and `CreateArtefact`). Tests:
   `AnUpdateFollowsADocumentThatReferencesTheLayoutLibraryUnderItsOwnAlias` and
   `TheLibraryArtefactReferencesTheLayoutLibraryInsteadOfCarryingIt`
   (`starter/dotnet/Efl.Tests/DiagramInterchangeTests.cs`).
6. Only touch the shared references when you create your libraries in this call. A document that
   already carries your libraries keeps its layout (AMLPetriNet: `dotnet/PtMapper.Conversion/PtLibraries.cs`, `EnsureLibraries`;
   fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpdLibraries.cs`, `EnsureLibraries`).
7. Defer library creation in an update until something actually has to be instantiated. A sync that
   only changes an attribute must not write class libraries (AMLPetriNet:
   `dotnet/PtMapper.Conversion/PtNetUpdater.cs`, `Update`).
8. If you add library XML with `XElement` directly instead of through the engine, put the children of
   `CAEXFile` back into schema order afterwards; appended libraries otherwise land after the
   InstanceHierarchy, which CAEX 3.0 forbids (AMLPetriNet: `dotnet/PtMapper.Conversion/PtSelfContained.cs`, `Reorder`).
   Appending an `AttributeTypeLib` at the end is safe because it is last in the order
   (`ObjectReferencesLibraryFile.cs`, `EnsureIn`).

### 2.9 Publish the library as a file, keep code and file in step

Ship the library as a versioned `.aml` artefact and make it the authority. AMLPetriNet reproduces
`ISO_PT_DomainLibrary_v0.3.aml` in code and a golden test compares interface classes, role classes,
SUCs, fixed SUC ids, inheritance, attributes, descriptions, external references and idempotence of
`EnsureLibraries` against the file (AMLPetriNet: `dotnet/PtMapper.Tests/PtLibraryGoldenTests.cs`, class
`PtLibraryGoldenTests`, from `InterfaceClassesMatchThePublishedLibrary` on).
Give classes fixed ids (AMLPetriNet: `dotnet/PtMapper.Conversion/PtNames.cs`, `PtNames.ClassIds`); the starter stamps
ids derived from the class path because the engine otherwise gives every class a fresh GUID on every
build (`starter/dotnet/Efl.Conversion/EflLibraries.cs`, `StampClassIds`). It stamps only its own four
libraries; the embedded OMG_DD library keeps the ids of the published file.

Write conventions that live on instances (not on classes) into the class description, so a reader of
the library learns them. The `PT_Arc` description states that routing is stored as
`Waypoint_1..n` typed `DD_Waypoint` and label positions as `DD_Point`
(AMLPetriNet: `libraries/ISO_PT_DomainLibrary_v0.3.aml`, `Description` of `RoleClass` `PT_Arc`).

---

## 3. The two connection encodings

This is the decision that shapes everything else (the starter calls it pattern P6 of the method,
`starter/dotnet/Efl.Conversion/EflNames.cs`, `EflConnectionStyle`).

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
(fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs`, `BuildProcess`; starter equivalent
`starter/dotnet/Efl.Conversion/EflToCaex.cs`, `WriteFlowAsLink`)

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
(AMLPetriNet: `dotnet/PtMapper.Conversion/PtNetToCaex.cs`, `WriteNet`; starter equivalent
`starter/dotnet/Efl.Conversion/EflToCaex.cs`, `WriteFlowAsElement`)

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
(`starter/dotnet/Efl.Conversion/EflUpdater.cs`, `StyleOf`).

### 3.4 Lessons that apply to both

- Use the interface ids from the `RefPartnerSideA`/`B` attributes and compare them exactly. CAEX 3.0
  writes the interface id there. AMLPetriNet deliberately does not split at a colon, because an id may
  contain one (`dotnet/PtMapper.Conversion/PtCaex.cs`, `PartnerId`); fpb-aml-mapper tolerates the older
  `elementId:interfaceName` form for externally authored files
  (`dotnet/FpbMapper.Conversion/CaexToFpbJson.cs`, `ExtractInterfaceId`; `FpbJsonToCaex.cs`, `SideId` in
  `RemoveOrphanedConnections`). The starter
  accepts both the bare interface id and `ElementId:InterfaceId`, taking the part after the last colon
  because its GUIDs contain none (`starter/dotnet/Efl.Conversion/CaexToEfl.cs`, `InterfaceIdOf`), and uses that
  helper in the reader (`CaexToEfl.cs`, `ReadFlowElements`, `ReadFlowLinks`) and the updater
  (`starter/dotnet/Efl.Conversion/EflUpdater.cs`, `IsOurLink`, `RemoveLinksTouching`); test
  `LinksWrittenAsElementAndInterfaceIdAreReadToo` (`starter/dotnet/Efl.Tests/RoundTripTests.cs`).
  Pick one policy and put it in one helper.
- Links may sit on any element. Walk links at and below the container rather than only on it
  (AMLPetriNet: `dotnet/PtMapper.Conversion/PtCaex.cs`, `LinksBelow`).
- When a link is removed, remove the interfaces it used unless a surviving link still uses them.
  Leaving them leaked two corpse interfaces per deleted flow in the FPD mapper
  (fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs`, `RemoveOrphanedConnections`).
- For the reified encoding, map interface ids only to *node* owners when resolving endpoints. If arc
  interfaces are in the owner map, a link between two arcs resolves an arc as an endpoint and the
  modeler drops both arcs at import while reporting success (AMLPetriNet:
  `dotnet/PtMapper.Conversion/CaexToPtNet.cs`, `ReadNet`).
- An existing link whose endpoints changed (reversed or rerouted) must be detected by comparing
  owner ids, then dropped and recreated (fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs`,
  `UpdateInPlace` and `LinkEndpointsMatch`).

---

## 4. Diagram interchange: layout in AML

### 4.1 One shared, language-agnostic type library

Store layout with the three types of `OMG_DD_AttributeTypeLib` v0.1, structured after the OMG Diagram
Definition: `DD_Bounds` (position as `DD_Point`, width, height; cf. DC::Bounds), `DD_Point` (x, y;
DC::Point) and `DD_Waypoint` (position; DI::Waypoint)
(AMLPetriNet: `libraries/OMG_DD_AttributeTypeLib_v0.1.aml`, attribute types `DD_Bounds`, `DD_Point`,
`DD_Waypoint`). Both mappers emit it from an
identical `DiagramInterchangeLibrary` class, deliberately duplicated so the two repositories stay
independent, with the instruction to keep the emitted library byte identical
(AMLPetriNet: `dotnet/PtMapper.Conversion/DiagramInterchangeLibrary.cs`, class comment,
fpb-aml-mapper: `dotnet/FpbMapper.Conversion/DiagramInterchangeLibrary.cs`). Reference it with alias
`OMG_DD`, or carry it inline in documents people open (section 2.8). The starter does not generate the
library in code; it embeds the published file from `starter/libraries/`
(`starter/dotnet/Efl.Conversion/EflDiagramInterchange.cs`, `Xml`, `ReadEmbedded`), so code and file cannot drift. Two languages in one document then describe layout the same way, which matters when a
bridging document holds an FPD and a Petri net side by side.

### 4.2 Where each piece of geometry goes

| Geometry | Attribute | Typed | On | Source file | Symbol |
|---|---|---|---|---|---|
| Node bounds | `ViewInformation` { `position` {x,y}, `width`, `height` } | `DD_Bounds` | node IE (declared on the base class) | AMLPetriNet: `dotnet/PtMapper.Conversion/PtWrite.cs` | `SetViewInformation` |
| Docking point of a connection | `PortCoordinate` {x,y} | `DD_Point` | the interface (declared on the abstract port class) | AMLPetriNet: `PtWrite.cs`; fpb-aml-mapper: `FpbJsonToCaex.cs` | `SetPortCoordinate`; `AddPortCoordinate` |
| Bend points | `Waypoint_1..n` { `position` {x,y} }, ordered source to target | `DD_Waypoint` | A: source-side interface; B: the connection element | fpb-aml-mapper: `FpbJsonToCaex.cs`; AMLPetriNet: `PtWrite.cs` | `AddWaypointAttr`; `ReplaceWaypoints` |
| Label offset relative to element centre | `LabelOffset` {x,y} | `DD_Point` | element (instance attribute, not on the class) | AMLPetriNet: `dotnet/PtMapper.Conversion/PtNames.cs`, `PtWrite.cs` | `Attributes.LabelOffset`; `SetLabelOffset` |

Notes:

- Waypoints are instance attributes, not class attributes, because their number varies. Parse the
  index from the name and sort numerically; `Waypoint_10` sorts before `Waypoint_2` as a string
  (AMLPetriNet: `dotnet/PtMapper.Conversion/CaexToPtNet.cs`, `ReadWaypoints`, `WaypointIndex`).
- Replace the whole waypoint set on every write. A shortened polyline otherwise keeps its tail
  (AMLPetriNet: `dotnet/PtMapper.Conversion/PtWrite.cs`, `ReplaceWaypoints`, test
  `AShortenedPolylineDoesNotKeepItsTail` in `dotnet/PtMapper.Tests/UpdateInPlaceTests.cs`).
- A label moved back to its default position must remove the offset attribute, or the next import
  moves it away again (`PtWrite.cs`, `SetLabelOffset`, test `ALabelMovedBackLosesItsOffset` in `UpdateInPlaceTests.cs`).
- For encoding B, the inherited `ViewInformation` rectangle stays unused on connection elements; do not
  write an empty one (AMLPetriNet: `dotnet/PtMapper.Conversion/PtNames.cs`, comment of `Attributes.WaypointPrefix`, test
  `AnArcCarriesNoEmptyBoundsRectangle` in `UpdateInPlaceTests.cs`).

### 4.3 Explicit and implicit layout

Distinguish what is stored (explicit) from what is derived or computed on import (implicit), and make the
round trip stable in both cases.

- Derived geometry: PNML carries no docking points, so AMLPetriNet computes them from node shape
  (ellipse for places, rectangle for transitions) the same way in C# and in the web modeler, and
  writes them as `PortCoordinate` (`dotnet/PtMapper.Conversion/PtGeometry.cs`, `DockingPoint`, `Polyline`). If two sides
  compute the same value, keep both implementations in step and say so in both files. Crop against
  the outline the modeler actually draws: the starter crops a Store against the ellipse inside its box,
  because cropping against the box put the stored end point beside the circle and the document
  disagreed with the canvas for every flow off an axis (`starter/dotnet/Efl.Conversion/EflGeometry.cs`, `DockingPoint`,
  test `AFlowEndsOnTheCircleOfAStoreNotOnItsBox` in `starter/dotnet/Efl.Tests/ExampleAndGeometryTests.cs`). A node without size docks at its
  centre instead of writing NaN (`EflGeometry.cs`, `DockingPoint`, test
  `ANodeWithoutSizeDocksAtItsCentreInsteadOfNaN` in `starter/dotnet/Efl.Tests/ExampleAndGeometryTests.cs`).
- Computed layout: a model without positions gets a deterministic layered layout on import, and
  existing bounds are never moved (AMLPetriNet: `dotnet/PtMapper.Conversion/PtLayout.cs`, class comment of `PtLayout`). The
  starter's `EflLayout.ArrangeMissing` also gives bend points to the flows between the nodes it placed:
  back flows in lanes below the drawing, forward flows that would cross a node in lanes above, three
  bend points for a self loop; flows that already have waypoints keep them
  (`starter/dotnet/Efl.Conversion/EflLayout.cs`, `RouteFlows`). Those bend points are written as
  `Waypoint_n` like any drawn route. See [08 Layout](08-layout.md) §4.5.
- Missing layout is arranged in one place, never invented in two. The two source tool chains chose
  different places. AMLPetriNet's mapper arranges with `PtLayout`. In the FPD chain the modeler
  arranges: FPB.JS lays out whatever lacks visual information before its importer builds shapes
  (FPB.JS: `app/fpb/importer/JSONImporter.js`, `needsLayout` and `layoutImportData` in the
  `IMPORT_EVENTS.IMPORT_REQUEST` handler; `app/fpb/layout/AutoLayout.js`), and fpb-aml-mapper emits
  only the layout the AML stores: a link without `PortCoordinate` or `Waypoint_n` gets no visual
  entry, and an element without visual data, the system limit included, gets no `ViewInformation`
  (fpb-aml-mapper: `dotnet/FpbMapper.Conversion/CaexToFpbJson.cs`, `ParseProcess`;
  `FpbJsonToCaex.cs`, comments in `AddElement` and `BuildProcess`; tests
  `Convert_WithoutConnectionLayout_ConnectionsGetNoVisual_ShapesKeepTheirs` and
  `Convert_WithoutAnyLayout_EmitsNoVisualInformationButAllData` in
  `dotnet/FpbMapper.Tests/LayoutFreeConversionTests.cs`). The mapper once did the opposite: straight
  centre lines for links and default bounds 100,100,600,400 for a system limit, because the importer
  aborted on a connection without visual entry. That kept the canvas from staying empty, but the
  invented geometry looked like drawn geometry, nothing arranged it, and the next sync stored it in
  the document (from the project history). Recommendation: decide whether the modeler or the mapper
  arranges missing layout, and let the other side pass the gap through untouched.
- Default bounds for a new container: an empty diagram created in an existing document still gets a
  default system limit box, because there is nothing to arrange yet
  (fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs`, `CreateEmptyFpdInstanceHierarchy`).
- Report layout that a sync adds instead of failing on it: opening a net without layout and syncing
  it writes the arranged layout into the document. The PT round-trip comparison reports the count of
  graphical values before and after rather than demanding equality
  (AMLPetriNet: `dotnet/PtMapper.Conversion/PtRoundTripCheck.cs`, `Compare`).

### 4.4 Number formatting

Write every number with `CultureInfo.InvariantCulture`. On a German Windows the current culture
writes `12,5`, which other tools read as a different number or not at all
(`starter/dotnet/Efl.Conversion/EflWrite.cs`, `Format`). Read with `NumberStyles.Float` and the
invariant culture as well (AMLPetriNet: `dotnet/PtMapper.Conversion/CaexToPtNet.cs`, `ReadDouble`). Round to
a fixed precision so re-serialisation does not create diffs
(AMLPetriNet: `dotnet/PtMapper.Conversion/PtWrite.cs`, `SetChild` uses `"0.##"`;
`PtGeometry.cs`, `Round` rounds to two decimals).

---

## 5. Aml.Engine 4.x: API specifics and traps

All three .NET projects reference `Aml.Engine` `4.*` on net8.0
(AMLPetriNet: `dotnet/PtMapper.Conversion/PtMapper.Conversion.csproj`,
fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpbMapper.Conversion.csproj`,
`starter/dotnet/Efl.Conversion/Efl.Conversion.csproj`).

| # | Trap | Symptom | Do this | Evidence file | Evidence symbol |
|---|---|---|---|---|---|
| 1 | `CreateClassInstance(string name)` is an extension method | Does not compile, or you end up with the overload that takes a `CAEXDocument` and no name | `using Aml.Engine.CAEX.Extensions;` | AMLPetriNet: `dotnet/PtMapper.Conversion/PtWrite.cs`; the overload is in the engine's XML docs | the `using Aml.Engine.CAEX.Extensions` directive, `CreateInstance`; `InheritanceExtensions.CreateClassInstance<T>(IInstantiable<T>, string)` |
| 2 | `CreateClassInstance` copies only the first `SupportedRoleClass` | AML base role missing on instances | Append the rest (section 2.2) | `PtWrite.cs` | `CreateInstance` |
| 3 | The engine assigns fresh GUIDs to objects it creates | Every run produces a different document; updates cannot match; references dangle | Set `ID` explicitly right after creation, derived deterministically (section 6) | `starter/dotnet/Efl.Conversion/EflLibraries.cs` | `StampClassIds` |
| 4 | `Insert(child)` defaults to inserting first | Document order reversed on every insert | `parent.Insert(child, false)` | AMLPetriNet: `dotnet/PtMapper.Conversion/PtWrite.cs`; fpb-aml-mapper still calls the one-argument form, for example in `FpbJsonToCaex.cs` | `Append`; `BuildProcess` |
| 5 | Wrappers are not reference stable | `ReferenceEquals` on two lookups of the same element is false; every link looked rerouted, so each FPD update tore down and recreated all links and leaked their interfaces | Compare by `ID`, or compare the underlying `XElement` via `.Node` | fpb-aml-mapper: `FpbJsonToCaex.cs`; AMLPetriNet: `PtNetUpdater.cs` | `LinkEndpointsMatch`; `RemoveLinksTouching` |
| 6 | Engine keeps process-wide caches that are not thread safe | Intermittent "Collection was modified" from `CheckReferences`, ids reassigned, count mismatches in round-trip tests | Disable xUnit parallelisation in every test assembly that builds documents | AMLPetriNet: `dotnet/PtMapper.Tests/TestCollection.cs`; fpb-aml-mapper: `dotnet/FpbMapper.Tests/TestCollection.cs`; AMLFPB.js: `Aml.Editor.Plugin.FPB.Tests/TestCollection.cs`; `starter/dotnet/Efl.Tests/AssemblyInfo.cs` | the assembly attribute `CollectionBehavior(DisableTestParallelization = true)` in each |
| 7 | `CAEXObject.NewGUID` is a property | `NewGUID()` does not compile | `ie.ID = CAEXObject.NewGUID;` (only where randomness is really wanted) | Aml.Engine API (the `CAEXObject.NewGUID` property) | `CAEXObject.NewGUID` |
| 8 | Culture-sensitive number formatting | Commas in coordinates | Invariant culture everywhere (section 4.4) | `starter/dotnet/Efl.Conversion/EflWrite.cs` | `Format` |
| 9 | Collection indexers return null for a missing name | NullReferenceException deep in a writer | `ie.Attribute[name] ?? ie.Attribute.Append(name)` | AMLPetriNet: `PtWrite.cs` | `SetValue` |
| 10 | `ToDictionary` over classes throws on a duplicate name | Update aborts halfway on a hand-edited library | Build the lookup with `TryAdd`, first wins | AMLPetriNet: `PtWrite.cs` | `SystemUnitClasses` |
| 11 | A removed element's wrapper still exists | Later passes mutate a detached element with no effect on the saved file | After `Remove()`, drop it from every index; check `CAEXParent == null` before mutating | fpb-aml-mapper: `FpbJsonToCaex.cs` | `PurgeSubtreeFromIndex`, `SyncDecompositionNamesPostPass` |
| 12 | Per-element engine lookups are slow | A per-element id check doubled the time to write a 2000 element net | Read all ids once from the XML (`caex.Node.DescendantsAndSelf().Attributes("ID")`) | AMLPetriNet: `dotnet/PtMapper.Conversion/PtIds.cs` | `CaexIdSpace` |
| 13 | Copying a library between documents | Ids change or references break | `CAEXDocument.LoadFromString(xml)`, then `doc.CAEXFile.AttributeTypeLib.Insert(srcLib)` deep-copies and keeps ids | AMLPetriNet: `dotnet/PtMapper.Conversion/ObjectReferencesLibraryFile.cs` (copies the element instead, which keeps ids too) | `EnsureIn` |
| 14 | `Insert` of an object that already has a parent inserts a copy | Edits to the original do not show up | Create fresh via `Append`/`CreateClassInstance`, or re-fetch after insert | engine XML docs | `InstanceHierarchyType.Insert(CAEXWrapper, bool, bool)` |
| 15 | Working below the engine with `XElement` | Schema order violated, engine index unaware of new ids | Reorder children, and scan ids from XML rather than asking the engine | AMLPetriNet: `PtSelfContained.cs`, `PtIds.cs` | `Reorder`; `CaexIdSpace` |
| 16 | `CAEXDocument.LoadFromString` does not always reject text that is not AML | A document with a null `CAEXFile`; the NullReferenceException appears later, and a web API reported the caller's broken file as a 500 | Check `CAEXFile != null` once in a load helper and throw a `FormatException` | `starter/dotnet/Efl.Conversion/EflDocuments.cs` | `Load` |
| 17 | `CAEXDocument.LoadFromFile` and `SaveToFile` write a copy of `CAEX_ClassModel_V.3.0.xsd` into the folder of the file | Schema files appear in example folders and get committed | Read the file as text and load from the string; save through `SaveToStream` | `starter/dotnet/Efl.Conversion/EflDocuments.cs` | `LoadFile`, `ToXml`, `SaveFile` |

Use `link.AInterface = iface` / `link.BInterface = iface` when you hold interface wrappers; the engine
fills `RefPartnerSideA`/`B` with the interface ids (AMLPetriNet: `dotnet/PtMapper.Conversion/PtWrite.cs`, `EnsureLink`).
The starter writes links the same way, because hand-built `ElementId:InterfaceId` strings did not
resolve in fpb-aml-mapper (`starter/dotnet/Efl.Conversion/EflWrite.cs`, `EnsureLink`).

---

## 6. Identity: deterministic ids

### 6.1 Why

The same model must produce the same CAEX ids on every run. Otherwise an update replaces every
element instead of touching the changed ones, and every reference a user added into the hierarchy
dangles (AMLPetriNet: `dotnet/PtMapper.Conversion/PtIds.cs`, class comment of `PtIds`). Tests assert that replacing the
hierarchy twice keeps the same ids (AMLPetriNet: `dotnet/PtMapper.Tests/IdentityTests.cs`,
`ReplacingTheHierarchyTwiceKeepsTheSameIds`) and that
ids stay stable across updates (`dotnet/PtMapper.Tests/UpdateInPlaceTests.cs`, `ElementIdsStayStableAcrossUpdates`).

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
(AMLPetriNet: `dotnet/PtMapper.Conversion/PtIds.cs`, `For`; same in `starter/dotnet/Efl.Conversion/EflIds.cs`, `For`)

Details that matter:

- The namespace string (`"iso-pt-aml-mapper"`) keeps your ids from colliding with another tool's
  (`PtIds.cs`, `Namespace`).
- The separator is `"\0"`, a character no id contains, so `For("a","b")` and `For("ab")` differ. Write
  it as an escape; a literal NUL byte makes the source file binary to git and invisible to grep
  (`PtIds.cs`, `Separator`).
- The `kind` discriminator keeps an element, its interfaces and its links apart:
  `Element(pnmlId)`, `Interface(ownerCaexId, name)`, `Link(arcCaexId, side)`, `Net(netId)`,
  `Hierarchy(name)` (`PtIds.cs`, same method names).
- Derive interface and link ids from the *owner's CAEX id*, not from the language id. When the
  language id was the seed, the same file imported twice produced duplicate interface ids, and
  deleting an arc in one copy removed the other copy's links (`PtIds.cs`, comment of `Interface`).
- The byte index matters. fpb-aml-mapper's `DeriveSubProcessId` and `DeriveBoundaryStateId` hash with
  SHA-1 and set the version on byte 6 (`dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs`). Per
  the little-endian layout explained above, that nibble does not land in the version position of the
  rendered GUID. The ids are still deterministic and unique, so nothing breaks, but they are not
  well-formed version 5 UUIDs. AMLPetriNet tests well-formedness
  (`dotnet/PtMapper.Tests/IdentityTests.cs`, `DerivedIdsAreWellFormedUuids`).

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
(AMLPetriNet: `dotnet/PtMapper.Conversion/PtIds.cs`, `Distinct`, id space from the XML in `CaexIdSpace`)

Two cases need it: invalid input with duplicate language ids (reported by rule PT01, but it must not
produce two elements with one CAEX id), and the same model imported twice, which is legitimate
(`PtIds.cs`, comment of `DistinctElement`). fpb-aml-mapper's green-field builder tracks used ids only within one run
(`dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs`, `usedAmlIds` in `AppendInto`), so importing the same FPB.JS project twice
into one document yields duplicate CAEX ids; do the document-wide check from the start.

Also make the language ids themselves distinct before writing, and report what was renumbered
(AMLPetriNet: `dotnet/PtMapper.Conversion/PtIdText.cs`, `MakeIdsDistinct`, applied before anything is written in
`PtNetToCaex.cs`, `AppendInto`).

### 6.4 Two strategies seen in the projects

| | AMLPetriNet (derive everything) | fpb-aml-mapper (reuse editor ids) |
|---|---|---|
| Element CAEX id | Hash of language id, distinct in document | Editor GUID wrapped in braces (`FpbJsonToCaex.cs`, `NormalizeId`, `WrapBraces` in `AddElement`) |
| Interface id | Hash of owner CAEX id and interface name | Random `NewId()` (`FpbJsonToCaex.cs`, `AddConnection`, `BuildProcess`) |
| Link id | Hash of arc CAEX id and side | Editor flow id in braces (`FpbJsonToCaex.cs`, `BuildProcess`) |
| Process / net container id | Hash of net id | Random on green field (`FpbJsonToCaex.cs`, `AppendInto`), derived for sub-processes created during update (`AddProcessImpl`) |
| Hierarchy id | Hash of name, made distinct (`PtNetToCaex.cs`, `AppendInto`) | Random (`FpbJsonToCaex.cs`, `AppendInto`) |
| Language id in document | `Identification/id` | CAEX `ID` itself, brace-stripped on read (`CaexToFpbJson.cs`, `NormalizeId`) |

Reusing editor ids is simpler as long as the editor already produces GUIDs and every element exists
once. It breaks as soon as one logical element needs several CAEX elements (section 7) or the same
model is imported twice. Deriving everything needs a language id stored in `Identification`, but
survives both.

### 6.5 Compare ids with one policy

AMLPetriNet compares CAEX ids ordinal and exactly: CAEX declares ID as a string and XML treats `Start`
and `start` as different names, and mixing tolerant and exact comparisons once let the reader resolve a
link the updater then failed to find (`dotnet/PtMapper.Conversion/PtCaex.cs`, `Ids`). fpb-aml-mapper
strips braces and compares case-insensitively everywhere
(fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs`, `IdsEqual`). Either
works if it is one `StringComparer` in one place and every index uses it.

### 6.6 Language ids that are not valid in the editor

Documents in the wild put braced CAEX GUIDs into `Identification/id`, and a modeler parser may accept
only `[a-z_][\w-.]*`. Normalise ids on read into a valid name, deterministically, leaving valid names
untouched, and never overwrite a stored id that differs only by that normalisation
(AMLPetriNet: `dotnet/PtMapper.Conversion/PtIdText.cs`, class comment of `PtIdText` and `SameAfterNormalisation`; guarded write in
`PtWrite.cs`, `SetIdentification`). The updater must normalise its lookup keys the same way and in the same order
as the reader, or two elements that reduce to one name pair up differently
(`PtNetUpdater.cs`, `Update`).

---

## 7. One logical element, several CAEX elements (layers, views)

FPB.JS models decomposition in layers. A boundary state appears on the parent layer and inside the
sub-process layer and carries *one* id in the editor. In CAEX two InternalElements with the same id are
not conformant, and a compound key was a dead end. The fix that holds (fpb-aml-mapper, July 2026):

1. Write each copy with its own deterministic id derived from (shared id, sub-process id):
   `"fpd-boundary-state:" + shared + "@" + subProcess` (`dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs`, `DeriveBoundaryStateId`).
   Before this the copy got a random id, was not found again by update, was removed as an orphan and
   lost its position.
2. Link the copy to the original with `refObj`, typed `refBaseObj` (section 2.6), brace-free
   (`FpbJsonToCaex.cs`, `BuildProcess`).
3. On read, reunify: a state with a `refObj` is surfaced under the referenced id, so the editor sees
   one logical state on both layers (`dotnet/FpbMapper.Conversion/CaexToFpbJson.cs`, `ParseProcess`).
4. On update, translate before anything else. A pre-pass rewrites the sub-process copies in the
   incoming model (element ids, flow endpoints, visuals) to their per-layer CAEX ids. The whole
   remaining pipeline (index lookup, add, connections, orphan removal) then runs on distinct ids
   without any layer awareness (`FpbJsonToCaex.cs`, called from `UpdateInPlace`, implemented in
   `RemapSubProcessBoundaryStateIds`).
5. Resolve the existing copy primarily through its `refObj` back link under the sub-process, and only
   fall back to re-deriving the id. Re-deriving matched only files created by the derive scheme and
   orphaned the boundary copies of every other file on each update (`FpbJsonToCaex.cs`, `RemapSubProcessBoundaryStateIds`).
6. Detect duplicate ids when indexing and keep the first rather than silently overwriting it
   (`FpbJsonToCaex.cs`, `BuildElementIndex`).

Tests in `dotnet/FpbMapper.Tests/ConversionTests.cs`: `UpdateInPlace_PreservesSubProcessBoundaryStates_AcrossIdempotentSync`
(idempotent sync keeps boundary states) and `UpdateInPlace_SubProcessBoundaryPositions_StayStable`
(sub-process boundary positions stay stable).

Generalise: whenever your editor shows one object in several places (layers, views, detail diagrams),
write one CAEX element per place, give each a deterministic id derived from (logical id, place), link
them with the matching `refObj` subtype, and reunify on read.

---

## 8. Update in place

A plugin that replaces the hierarchy on every save destroys every attribute a user added, every link
into another hierarchy and every foreign element sharing the diagram
(class comments of AMLPetriNet: `dotnet/PtMapper.Conversion/PtNetUpdater.cs` (`PtNetUpdater`) and
`starter/dotnet/Efl.Conversion/EflUpdater.cs` (`EflUpdater`)). Implement an updater from day one. Share the
writing primitives between the green-field writer and the updater so both produce the same shape
(AMLPetriNet: `dotnet/PtMapper.Conversion/PtWrite.cs`, class comment of `PtWrite`).

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
(AMLPetriNet: `dotnet/PtMapper.Conversion/PtNetUpdater.cs`, `UpdateInPlace`)

Typical preconditions: every connection names two existing nodes (`PtNetUpdater.cs`, `CheckArcEnds`), a
document that carries your SUC library carries every class you may instantiate
(`PtNetUpdater.cs`, `CheckCarriedClasses`), ids are distinct. Throw with "Nothing was written" in the message.

### 8.2 Match by the language id, within your own element kinds

Build the index of existing elements from `Identification/id`, falling back to the CAEX name for
elements written against an older library, and only for your own element kinds; anything without an id
or of a foreign type stays untouched (`PtNetUpdater.cs`, `Update`). The name fallback is not cosmetic:
an updater that only looked at `Identification` recognised nothing in older documents and added a
second copy of every element (`dotnet/PtMapper.Conversion/CaexToPtNet.cs`, `PnmlIdOf`).

When the kind changed (a place edited into a transition by hand), remove and recreate rather than
reinterpret, and forget any wiring read from the removed element, or arcs reuse interfaces no longer in
the document and end up unlinked (`PtNetUpdater.cs`, `Wiring.Forget`, `Reuse`; test
`APlaceThatBecameATransitionIsRebuilt` in `dotnet/PtMapper.Tests/UpdateInPlaceTests.cs`).

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
(AMLPetriNet: `dotnet/PtMapper.Conversion/PtWrite.cs`, `IsLanguageOwned`; starter `starter/dotnet/Efl.Conversion/EflWrite.cs`, `IsLanguageOwned`)

Clean up language attributes that do not belong on the element's kind (left by a type change or an
older mapper version), but never touch a user's attribute (`PtWrite.cs`, `BelongsOn`, `RemoveForeignLanguageAttributes`). Check each rule
against every kind: excluding arcs from the label offset rule removed and re-appended the attribute on
every update, moving it to the end of the element (`PtWrite.cs`, comment in `BelongsOn`).

fpb-aml-mapper's `UpdateExistingElement` limits itself to Name, Identification, ViewInformation and
Characteristics, and rewrites `Characteristic_N` entries idempotently
(`dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs`, `UpdateExistingElement`, `SetCharacteristics`). Test:
`UpdateInPlace_PreservesCustomAttributeOnExistingElement` in `dotnet/FpbMapper.Tests/ConversionTests.cs`.

### 8.4 Do not rewrite what nobody changed

- CAEX `Name`: only write it when the language name actually differs from what the document stores in
  `Identification/name`. Another tool may have given the element a more telling display name
  (AMLPetriNet: `dotnet/PtMapper.Conversion/PtWrite.cs`, `SetDisplayName`, test
  `ANameAnotherToolChoseSurvivesASyncThatRenamedNothing` in `dotnet/PtMapper.Tests/ForeignConventionTests.cs`). fpb-aml-mapper overwrites the name whenever
  the editor's differs from the CAEX name (`FpbJsonToCaex.cs`, `UpdateExistingElement`), which is simpler but loses
  foreign display names.
- A value missing in the incoming model is not necessarily a deletion. The PT modeler never shows or
  edits an arc name, so an arc arriving without a name keeps the stored one; that loss once hit every
  arc of the paper example (`PtWrite.cs`, `WriteArc`).
- An unchanged model must leave the document byte-identical
  (`dotnet/PtMapper.Tests/UpdateInPlaceTests.cs`, `UpdatingWithoutChangesTouchesNothing`), and repeated updates converge
  (`RepeatedUpdatesConverge`).

### 8.5 Reuse existing wiring

Read how the document already connects things before writing. Another tool names node interfaces
`NodeArcEnd_1` rather than `Out_<arc>`; renaming them, or worse reassigning their ids, breaks
everything that points at them (`PtNetUpdater.cs`, `Update`, `ReadWiring`). When an interface is found by
name, keep the id and class the document gave it (`PtWrite.cs`, `EnsureInterface`). Create a link only if no
link already joins the two interfaces, compared order-independently (`PtNetUpdater.cs`, `Update`,
`Wiring.LinkKey`).

### 8.6 Keep foreign content, remove only your stale content

- Foreign attributes, interfaces, links and elements survive (tests
  `AUsersOwnAttributeSurvives`, `AUsersOwnInterfaceAndLinkSurvive`, `AForeignElementInTheHierarchyIsLeftAlone` in
  `dotnet/PtMapper.Tests/UpdateInPlaceTests.cs`; `RefObjOnTheNetSurvivesASync`,
  `AReferenceAttributeAddedToANodeSurvivesASync`, `ALinkFromAnotherHierarchyIntoTheNetSurvivesASync` in
  `dotnet/PtMapper.Tests/ReferenceSurvivalTests.cs`).
- Remove only interfaces of *your* endpoint class that the model no longer needs, together with every
  link that used them (`PtNetUpdater.cs`, `RemoveStaleInterfaces`; `starter/dotnet/Efl.Conversion/EflUpdater.cs`, `UpdateInPlace`).
  Keep the node interfaces of a connection the reader could not resolve: they belong to the half of it
  that still exists (`starter/dotnet/Efl.Conversion/EflUpdater.cs`, `UpdateInPlace`, `BelongsToUnresolved`).
- Remove links touching a deleted element *anywhere in the document*, not only in your container. A
  link from a plant element in another hierarchy into a deleted transition would otherwise dangle.
  Report every removal outside your container, and leave a link alone if the interface id it names is
  carried by more than one element (`PtNetUpdater.cs`, `RemoveLinksTouching`, test
  `DeletingANodeDoesNotLeaveALinkFromAnotherHierarchyDangling` in `dotnet/PtMapper.Tests/ReferenceSurvivalTests.cs`). The starter does the same and adds a note
  only when the link's owner is not the diagram, comparing owner and diagram by `ID`
  (`starter/dotnet/Efl.Conversion/EflUpdater.cs`, `RemoveLinksTouching`).
- In encoding A, a link has no class of its own. fpb-aml-mapper removes links whose id is not in the
  incoming model and assumes the editor owns all links in the IH (`FpbJsonToCaex.cs`, `RemoveOrphanedConnections`). The
  starter is stricter: it removes a link only if both ends are its own flow ports
  (`starter/dotnet/Efl.Conversion/EflUpdater.cs`, `RemoveStaleFlowLinks`, `IsOurLink`). Prefer the stricter rule.
- Do not delete what the reader could not show. An arc whose links do not resolve was never in the
  diagram, so its absence from the incoming model is not a deletion. Compute the unresolved set before
  removing any interface (`PtNetUpdater.cs`, `Update`). The starter reads the unresolved
  flow elements from the document before its first write and keeps them with a note asking the user
  to repair or delete them (`starter/dotnet/Efl.Conversion/EflUpdater.cs`, `UpdateInPlace`; test
  `AnUpdateKeepsAFlowTheCanvasCouldNotShow` in `starter/dotnet/Efl.Tests/RoundTripTests.cs`).
- Keep indexes consistent with the tree after every removal (`FpbJsonToCaex.cs`, `RemoveOrphanedFpdElements`, `PurgeSubtreeFromIndex`).

### 8.7 Order of work

1. Preconditions (8.1), read the document's library convention (2.8), refresh DI paths.
2. Translate ids that differ between editor and CAEX (section 7).
3. Index existing elements, existing wiring, unresolved elements.
4. Pass 1: containers and elements. Pass 2: connections. fpb-aml-mapper switched to two passes
   because a flow listed before its source element in the payload failed with "not found in AML"
   (`FpbJsonToCaex.cs`, `UpdateInPlace`).
5. Remove stale elements, interfaces and links.
6. Post-passes for derived consistency (FPD: boundary `refObj` sync and operator to sub-process name
   sync, `FpbJsonToCaex.cs`, `UpdateInPlace` and `SyncDecompositionNamesPostPass`).
7. Return a summary with counts and notes for the confirmation dialog and the log
   (AMLPetriNet: `dotnet/PtMapper.Conversion/PtNetUpdater.cs`, `PtUpdateSummary`).

---

## 9. Read back tolerantly

The document may have been edited by hand in the AML Editor between two visits. A reader that throws
turns one odd element into an unusable document (`starter/dotnet/Efl.Conversion/CaexToEfl.cs`, class comment of `CaexToEfl`).

- Recognise element types by SUC path or by role requirement, comparing the last path segment so any
  alias works (AMLPetriNet: `dotnet/PtMapper.Conversion/CaexToPtNet.cs`, `ElementKind`; `PtCaex.cs`, `IsClass`).
  fpb-aml-mapper strips the alias prefix before comparing full paths
  (`dotnet/FpbMapper.Conversion/FpbMappings.cs`, `StripAlias`; documents that referenced the FPD libraries
  through an alias were not recognised before; test `Convert_AliasQualifiedDocument_MatchesUnaliasedResult` in
  `dotnet/FpbMapper.Tests/AliasToleranceTests.cs`).
  Return null for foreign elements and skip them.
- Language id from `Identification/id`, falling back to CAEX `Name` (`CaexToPtNet.cs`, `PnmlIdOf`).
- Label from `Identification/name`. When the mapper wrote the element (id present), an empty name
  stays empty; it must not come back as the id. When a user created the element by hand (no id), the
  CAEX name is the label. Since the class brings an empty `Identification` along, test whether the id
  is filled, not whether the attribute exists (`CaexToPtNet.cs`, `IdentificationName`).
- Values fall back to `DefaultValue` (`CaexToPtNet.cs`, `ValueOf`); a declared default of 5 must not read
  as 0.
- Links may be on any element below the container (`PtCaex.cs`, `LinksBelow`), and the links are the truth for
  connectivity, not interface names, because a user may have rewired them (`CaexToPtNet.cs`, `ResolveEndpoints`).
- Unresolvable connections are reported as warnings and remembered as unresolved, not silently dropped
  (`CaexToPtNet.cs`, `ReadNet`).
- One-sided references: derive a missing sub-process `refObj` from the operator's `refProcess`, with a
  warning. Without this the whole decomposition read flat with zero warnings
  (fpb-aml-mapper: `dotnet/FpbMapper.Conversion/CaexToFpbJson.cs`, reverse-lookup fallback in `Convert`).
- Dangling references are not exported to the editor (`CaexToFpbJson.cs`, `ParseProcess`).
- Normalise ids for the editor (section 6.6) and never leak a braced id where bare ones are expected;
  a braced fallback id once made the next update miss every connection
  (`CaexToFpbJson.cs`, remarks of `NormalizeId`).
- Missing layout is passed through or arranged, in one place only (section 4.3).
- Several models in one hierarchy or document are read in document order (`CaexToPtNet.cs`, `ReadAll`, `Read`).

---

## 10. Validation

Separate "is this a valid model of the language" from "did the mapper preserve the document".

### 10.1 Rules on the model (AMLPetriNet, starter)

`PtValidator` works on the net model, not on CAEX, so the same rules cover a net that came from PNML
and never reached AML (AMLPetriNet: `dotnet/PtMapper.Conversion/PtValidator.cs`, `PtValidator`, `Validate`). Findings carry
a stable rule id, a severity (Error: violates the standard; Warning: legal but almost certainly
unintended) and the element id the modeler uses, so a finding can select its element
(`PtValidator.cs`, `PtSeverity`, `PtFinding`). Rule ids: PT01 duplicate id, PT02 missing id, PT03 not bipartite, PT04 arc
endpoint not a node, PT05 parallel arcs, PT06 negative marking, PT07 weight below 1, PT08 isolated
node, PT09 transition without input, PT10 transition without output, PT11 id not a valid XML name
(`PtValidator.cs`, `CheckIdentifiers`, `CheckBipartite`, `CheckArcEndpoints`, `CheckValues`, `CheckIsolatedNodes`).
Keep the set small: rules that are really modelling advice do not belong
in a conformance check (`PtValidator.cs`, class comment). The starter has rules EFL01 to EFL07 (EFL07: a node
without any flow) and publishes the ids in a list so a report can say which rules passed
(`starter/dotnet/Efl.Conversion/EflValidator.cs`, `RuleIds`); give every distinct check its own id. Its
test iterates that list and fails when a rule id has no broken model that triggers it, so a rule
cannot be added and forgotten (`starter/dotnet/Efl.Tests/ValidatorTests.cs`, `EachRuleFiresOnItsOwnBrokenModelAndOnlyThere`).

### 10.2 Rules on CAEX with OCL (AMLFPB.js, fpb-aml-mapper)

The FPD rules are OCL invariants shipped as embedded resources of `FpbMapper.Conversion`, so every
consumer that references the library runs the same constraints
(fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpbValidationRules.cs`, class comment of `FpbValidationRules`; the
`EmbeddedResource` items in `dotnet/FpbMapper.Conversion/FpbMapper.Conversion.csproj`). Three files: 26 executable
structural rules, `def:` helper operations for geometry, and phase-2 rules on characteristics that are
not yet executable against the CAEX binding and are shipped so the engine reports "context type
unknown" instead of passing them vacuously (header comment of `Rules/vdi3682-phase2-rules.ocl`). Example:

```
context FPD_Object inv RefObjResolvable:
  self.refObj->notEmpty() implies self.project.containedElement->exists(e | e.identification.uniqueIdent = self.refObj)

context FPD_ProcessOperator inv RefProcessResolvable:
  self.refProcess->notEmpty() implies self.project.process->exists(p | p.id = self.refProcess)
```
(fpb-aml-mapper: `dotnet/FpbMapper.Conversion/Rules/vdi3682-pure-rules.ocl`, `RefObjResolvable`, `RefProcessResolvable`)

The rules are executed by OCL.NET, a domain-free OCL engine with a CAEX binding. The binding maps
CAEX to OCL types, including connections that are only InternalLinks: the flow type of a link is read
from its A-side interface class. That is how encoding A still gets typed connections in OCL contexts
such as `FPD_Flow`.

The AMLFPB.js plugin runs these invariants next to hard-coded C# rules. Each C# rule is one class with
a stable id of the form `VDI3682.<Name>`, registered in one list; a rule that throws becomes an Error
finding instead of ending the pass; callers can disable rules by id, filter by minimum severity and
switch the OCL pass off (AMLFPB.js: `Aml.Editor.Plugin.FPB/Validation/Vdi3682Validator.cs`,
`DefaultRules`, `ValidateStructured`, `ValidationOptions.UseOclEngine`;
`Aml.Editor.Plugin.FPB/Validation/IValidationRule.cs`, `IValidationRule`, `ValidationFinding`).
In the OCL pass, rule ids are `VDI3682.OCL.<InvariantName>`; severities are assigned per invariant in a
catalogue keyed A1..I3; invariants already covered by hard-coded C# rules are skipped to avoid double
findings; the compiled rule set is cached with `LazyThreadSafetyMode.PublicationOnly` so a transient
failure is retried instead of frozen for the session
(AMLFPB.js: `Aml.Editor.Plugin.FPB/Validation/Vdi3682OclRuleSet.cs`, `RuleIdPrefix`,
`CatalogueSeverity`, `CoveredByHardcoded`, `Compiled`). The pass never throws; an engine failure
becomes one diagnostic finding. It returns early for documents without any FPD process (otherwise
`ProjectMinimumProcess` flags every foreign AML file) and reports elements inside an FPD process that no
rule can classify, because they are invisible to every rule (`Append`, `UnclassifiedRuleId`,
`IsInsideFpdProcess`). Severity tuning is part of the work: `LongNameMandatory` and
`VersionRevisionPresent` flooded every element because FPB.JS does not maintain those fields, and were
lowered to Warning and Info (comments on those entries in `CatalogueSeverity`). A test proves that the
OCL invariants and the hard-coded rules report the same findings on a model that violates them
(AMLFPB.js: `Aml.Editor.Plugin.FPB.Tests/SideBySideValidationTests.cs`,
`Violated_model_produces_identical_findings`).

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
(`dotnet/PtMapper.Conversion/PtRoundTripCheck.cs`, `Run`, `Connections`, `Outside`). It runs in the test
suite and on the command line against files saved by the AML Editor. Details in
[07 Testing and CI](07-testing-and-ci.md).

---

## 11. Several instance hierarchies and diagrams in one document

- Find your hierarchies by content, not by position: every IH that contains at least one of your
  container elements (fpb-aml-mapper: `dotnet/FpbMapper.Conversion/CaexToFpbJson.cs`, `FindFpdInstanceHierarchies`;
  AMLPetriNet: `dotnet/PtMapper.Conversion/CaexToPtNet.cs`, `ContainsNet`). Leave every other IH alone.
- Make every write target an explicit hierarchy. fpb-aml-mapper grew
  `UpdateInPlace(doc, json, targetIh)` and `Convert(doc, ih)` for the multi-IH plugin
  (`dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs` and `CaexToFpbJson.cs`, those overloads); the
  parameterless variants that take the first matching IH are kept only for legacy callers
  (`FpbJsonToCaex.cs`, `FindFpdInstanceHierarchy`; `CaexToFpbJson.cs`, `Convert(doc)`). If no hierarchy is found, the FPD updater
  appends a fresh one and warns (`FpbJsonToCaex.cs`, `UpdateInPlace`).
- Several diagrams per hierarchy are legitimate. AMLPetriNet writes all nets of a PNML file into one
  IH instead of dropping all but the first, and the updater takes a `netIndex`; an index past the end
  appends rather than overwriting the wrong net (`dotnet/PtMapper.Conversion/PtNetToCaex.cs`, `AppendInto`;
  `PtNetUpdater.cs`, `UpdateInPlace`).
- Stamp an id on every IH you create, derived and distinct, so the plugin can find its hierarchy again
  after someone renames it (`PtIds.cs`, `Hierarchy`; `PtNetToCaex.cs`, `AppendInto`). The PT plugin binds by id and
  name and follows a rename (AMLPetriNet: `Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs`, `CurrentHierarchy`).
- Importing appends a new IH and keeps imports isolated (`FpbJsonToCaex.cs`, `AppendInto`). Combined with
  document-wide distinct ids (section 6.3) this is safe; without it, a second import duplicates ids.
- Offer a way to create an empty diagram in an existing document, with sensible default layout for the
  container (`FpbJsonToCaex.cs`, `CreateEmptyFpdInstanceHierarchy`).
- Links between hierarchies are the point of bridging documents (a plant signal linked to a transition
  of a behaviour model). Treat them as foreign content that must survive (section 8.6), and check them
  in the round-trip comparison (`PtRoundTripCheck.cs`, `ForeignCoupling`).

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
- [ ] Layout-free documents read completely; missing layout arranged in one place (modeler or mapper), never invented in two; computed layout reported, not hidden.
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

| Topic | File | Symbols |
|---|---|---|
| Names and paths in one place | AMLPetriNet: `dotnet/PtMapper.Conversion/PtNames.cs`; fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpbMappings.cs`; `starter/dotnet/Efl.Conversion/EflNames.cs` | `PtNames`; `FpbMappings.LibNames`; `EflNames` |
| Library generator | AMLPetriNet: `dotnet/PtMapper.Conversion/PtLibraries.cs`; fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpdLibraries.cs`; `starter/dotnet/Efl.Conversion/EflLibraries.cs` | `EnsureLibraries` in each |
| Published library artefacts | AMLPetriNet: `libraries/ISO_PT_DomainLibrary_v0.3.aml`, `libraries/OMG_DD_AttributeTypeLib_v0.1.aml`, `libraries/AutomationML_ObjectReferences_AttributeTypeLib_AMLEd2_1.1.1-beta.aml` | `ISO_PT_SystemUnitClassLib` and the other ISO_PT libraries; `OMG_DD_AttributeTypeLib`; `AutomationML_ObjectReferences_AttributeTypeLib` |
| Library golden test | AMLPetriNet: `dotnet/PtMapper.Tests/PtLibraryGoldenTests.cs` | `PtLibraryGoldenTests` |
| Referenced vs carried libraries | AMLPetriNet: `dotnet/PtMapper.Conversion/PtLibraryLoan.cs`, `PtSelfContained.cs`, `PtDiPaths.cs`, `ObjectReferencesLibraryFile.cs` | `PtLibraryLoan`; `PtSelfContained.Inline`, `Reorder`; `PtDiPaths.Resolve`; `ObjectReferencesLibraryFile.EnsureIn` |
| Diagram interchange library | AMLPetriNet: `dotnet/PtMapper.Conversion/DiagramInterchangeLibrary.cs`; fpb-aml-mapper: `dotnet/FpbMapper.Conversion/DiagramInterchangeLibrary.cs`; `starter/libraries/OMG_DD_AttributeTypeLib_v0.1.aml`, `starter/dotnet/Efl.Conversion/EflDiagramInterchange.cs`, `starter/dotnet/Efl.Tests/DiagramInterchangeTests.cs` | `DiagramInterchangeLibrary.BuildDocument`, `EnsureIn` (both mappers); `EflDiagramInterchange.EnsureIn`, `PathOf`, `ReferenceFrom`; `DiagramInterchangeTests` |
| Reference types and options | fpb-aml-mapper: `dotnet/FpbMapper.Conversion/ReferenceTypes.cs`, `MapperOptions.cs`; AMLPetriNet: `dotnet/PtMapper.Conversion/ObjectReferencesLibraryFile.cs` | `ReferenceTypes`, `GetRefObjOrDerived`; `MapperOptions`; `ObjectReferencesLibraryFile.EnsureIn` |
| Green-field writer, encoding B | AMLPetriNet: `dotnet/PtMapper.Conversion/PtNetToCaex.cs`, `PtWrite.cs` | `AppendInto`, `WriteNet`; `PtWrite` |
| Green-field writer, encoding A | fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs` | `BuildProcess` |
| Both encodings side by side | `starter/dotnet/Efl.Conversion/EflToCaex.cs`, `CaexToEfl.cs`, `EflUpdater.cs` | `WriteFlowAsLink`, `WriteFlowAsElement`; `ReadFlowLinks`, `ReadFlowElements`; `StyleOf` |
| Deterministic ids | AMLPetriNet: `dotnet/PtMapper.Conversion/PtIds.cs`, `PtIdText.cs`; `starter/dotnet/Efl.Conversion/EflIds.cs` | `PtIds.For`, `CaexIdSpace`; `PtIdText.MakeIdsDistinct`; `EflIds.For` |
| Update in place | AMLPetriNet: `dotnet/PtMapper.Conversion/PtNetUpdater.cs`; fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs` | `PtNetUpdater.UpdateInPlace`; `FpbJsonToCaex.UpdateInPlace` |
| Boundary states across layers | fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs`, `CaexToFpbJson.cs` | `DeriveBoundaryStateId`, `RemapSubProcessBoundaryStateIds`; reunification in `ParseProcess` |
| Tolerant reader | AMLPetriNet: `dotnet/PtMapper.Conversion/CaexToPtNet.cs`, `PtCaex.cs`; fpb-aml-mapper: `dotnet/FpbMapper.Conversion/CaexToFpbJson.cs` | `CaexToPtNet.ReadAll`; `PtCaex`; `CaexToFpbJson.Convert` |
| Geometry and layout | AMLPetriNet: `dotnet/PtMapper.Conversion/PtGeometry.cs`, `PtLayout.cs`; `starter/dotnet/Efl.Conversion/EflGeometry.cs`, `EflLayout.cs` | `PtGeometry.DockingPoint`; `PtLayout.ArrangeMissing`; `EflGeometry.DockingPoint`; `EflLayout.ArrangeMissing` |
| Loading and saving AML safely | `starter/dotnet/Efl.Conversion/EflDocuments.cs` | `Load`, `LoadFile`, `ToXml`, `SaveFile` |
| Model validator | AMLPetriNet: `dotnet/PtMapper.Conversion/PtValidator.cs`; `starter/dotnet/Efl.Conversion/EflValidator.cs` | `PtValidator.Validate`; `EflValidator.Validate`, `RuleIds` |
| OCL rules and plugin integration | fpb-aml-mapper: the `.ocl` files in `dotnet/FpbMapper.Conversion/Rules/`, `FpbValidationRules.cs`; AMLFPB.js: `Aml.Editor.Plugin.FPB/Validation/Vdi3682OclRuleSet.cs`, `Vdi3682Validator.cs` | `FpbValidationRules.PureRules`, `Helpers`, `Phase2Rules`; `Vdi3682OclRuleSet.Append`, `LoadRuleSpecs`; `Vdi3682Validator.DefaultRules`, `ValidateStructured` |
| Round-trip check | AMLPetriNet: `dotnet/PtMapper.Conversion/PtRoundTripCheck.cs` | `Run`, `Compare` |
| Update and survival tests | AMLPetriNet: `dotnet/PtMapper.Tests/UpdateInPlaceTests.cs`, `ReferenceSurvivalTests.cs`, `ForeignConventionTests.cs`, `IdentityTests.cs`; fpb-aml-mapper: `dotnet/FpbMapper.Tests/ConversionTests.cs`, `AliasToleranceTests.cs`, `ObjectReferencesLibraryTests.cs` | the test classes of the same names (`ConversionTests.cs` holds `UpdateInPlaceTests` and `UpdateInPlaceEditTests`) |
| Hierarchy binding in a plugin | AMLPetriNet: `Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs` | `CurrentHierarchy` |
