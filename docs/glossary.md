# Glossary

## In short

- Terms a builder meets in this playbook, sorted alphabetically (case ignored; a leading `$` or `_` is ignored for sorting).
- Each entry is one or two sentences and links to the chapter where the term matters.
- API names were checked against the code of the source projects and the XML documentation of `Aml.Editor.Plugin.Contract` 4.3.0, `Aml.Engine` 4.5.2 and `Microsoft.Web.WebView2` 1.0.2903.40.
- Reasons behind the rules named here are in [decisions.md](decisions.md).

Read fully when: never; look a term up.

---

**A1 to A4**: See *Analysis items*.

**Alias** (of an `ExternalReference`): The short name a CAEX file gives another CAEX file it uses; class paths into that file are written `Alias@Library/Class`. The same class can therefore be named with or without an alias depending on whether the library is referenced or carried inline, and readers must accept both. [04](04-aml-mapping.md)

**AML (AutomationML)**: The data exchange format of IEC 62714, whose top-level structure is CAEX 3.0. The playbook treats it as a representation framework for graphical languages, not as their metamodel. [01](01-architecture.md)

**Aml.Editor.Plugin.Contract**: The NuGet package with the interfaces and base classes an AutomationML Editor plugin implements (`PluginViewBase`, `INotifyAMLDocumentLoad`, `ISupportsThemes`, `IToolBarIntegration`, ...). Pin it exactly and never ship its DLL in the plugin package. [05](05-editor-plugin.md)

**Aml.Engine**: The .NET library that reads, writes and manipulates CAEX documents; the mapper uses nothing else. It wraps the XML in objects that are not reference stable and keeps process-wide caches, so tests must run without parallelisation. [04](04-aml-mapping.md)

**Analysis items (A1 to A4)**: The Phase 1 worksheet of the three-phase method: A1 element types, A2 connection types, A3 structural rules, A4 information model. A2 decides the connection encoding. [10](10-new-language-recipe.md)

**ApplicationClose**: The `INotifyAMLDocumentLoad` callback the editor calls when the application closes; together with `DocumentUnLoaded` it is the real end of a plugin view's life. [05](05-editor-plugin.md)

**AttributeType / AttributeTypeLib**: A reusable, nestable attribute definition in an `AttributeTypeLib`; an attribute instance points at it with `RefAttributeType`. Languages use it for the `Identification` compound, layout types and reference types. [04](04-aml-mapping.md)

**Bridge**: The message protocol between the page in WebView2 (`web/src/bridge.js`) and the plugin host (`PtWebView`, `ModelerView`): typed JSON messages such as `importPNML`, `ready`, `imported`, `changed`. [01](01-architecture.md), [05](05-editor-plugin.md)

**businessObject**: The object a diagram-js element carries for its semantics (id, name, attributes); diagram-js does not care what it is. The starter uses plain objects, bpmn-js and the reused Petri net modeler use moddle objects. [02](02-modeler-diagram-js.md)

**CAEX**: Computer Aided Engineering Exchange (IEC 62424), the XML schema at the core of AutomationML: instance hierarchies plus role, system unit, interface and attribute type libraries. The source projects use CAEX 3.0. [04](04-aml-mapping.md)

**CAEXDocument / CAEXFile**: Aml.Engine's wrapper of a whole document and its `CAEXFile` root element. `LoadFromString` can return a document whose `CAEXFile` is null for non-AML text, and `LoadFromFile`/`SaveToFile` write the schema file next to the document, so load and save through one helper. [04](04-aml-mapping.md)

**Command handler**: A diagram-js class registered on the command stack under a command name, with `execute` and `revert` (and optionally `preExecute`/`postExecute`). Every edit that should be undoable goes through one, never through direct assignment to a business object. [02](02-modeler-diagram-js.md)

**Command stack**: The diagram-js service that executes commands, keeps undo and redo, and fires `commandStack.changed`, which the bridge debounces into `changed` messages. [02](02-modeler-diagram-js.md)

**Connection** (diagram-js): A diagram element with `source`, `target` and `waypoints`, drawn by the renderer's `drawConnection`. [02](02-modeler-diagram-js.md)

**Connection encoding**: How a connection of the language is written into CAEX (pattern P6): the *link encoding* (an `InternalLink` between an `Out` and an `In` interface) for connections without identity, or the *reified encoding* (an `InternalElement` with source and target interfaces and two links) for connections with identity or attributes. The starter names them `EflConnectionStyle.Link` and `EflConnectionStyle.Element`. [04](04-aml-mapping.md), [10](10-new-language-recipe.md)

**Context pad**: The small per-element menu diagram-js shows next to a selected element; a provider registers with `contextPad.registerProvider` and returns `getContextPadEntries(element)`. [02](02-modeler-diagram-js.md)

**CreateClassInstance**: The Aml.Engine extension method (`Aml.Engine.CAEX.Extensions`) that instantiates a class with a name, copying attributes, interfaces and the first `SupportedRoleClass` as a role requirement. Append the remaining supported roles and set a deterministic id afterwards. [04](04-aml-mapping.md)

**Cropping**: Shortening a connection's end points to the outline of the shapes it joins. `CroppingConnectionDocking` crops against the path the renderer's `getShapePath` returns, so the drawn outline and that path must match. [08](08-layout.md)

**DD_Bounds, DD_Point, DD_Waypoint**: See *OMG DD*.

**Deterministic id**: A CAEX id derived from a namespaced seed (language id, kind, owner) with SHA-256 and rendered as a version 5 UUID, so the same model always gives the same document. Interface and link ids derive from the owner's CAEX id; the version nibble belongs on byte 7. [04](04-aml-mapping.md)

**didi**: The dependency injection container behind diagram-js. It resolves services by name from `$inject` annotations and falls back to constructor parameter names, which is why a minified bundle can die at boot. [02](02-modeler-diagram-js.md)

**Direct editing**: In-place text editing of labels on the canvas, from the npm package `diagram-js-direct-editing`; a provider registers with `directEditing.registerProvider`. [02](02-modeler-diagram-js.md)

**DisplayName**: The plugin's name in the editor's plugin menu and tab. The editor turns it into a WPF name and an XML element name, so it may contain letters, digits and underscore only. [05](05-editor-plugin.md)

**Docking**: Where a connection meets a shape. diagram-js computes it with a `connectionDocking` service; the mapper computes the same points and stores them as `PortCoordinate`. [08](08-layout.md)

**DocumentLoaded / DocumentUnLoaded**: The `INotifyAMLDocumentLoad` callbacks the editor calls when a document is loaded or the current one is unloaded. The same file can arrive as a new `CAEXDocument` wrapper, so compare document identity, not references. [05](05-editor-plugin.md)

**Echo baseline**: The page's own export of a model right after it imported it, sent to the host in the `imported` message. A later `changed` with exactly this content is import fallout; anything else is a user edit. [05](05-editor-plugin.md)

**EFL (Example Flow Language)**: The toy language of the starter: `Step` (rectangle, Duration), `Store` (ellipse, Capacity) and a directed `Flow`. It exists to exercise every layer, including both connection encodings. [10](10-new-language-recipe.md)

**Element registry**: The diagram-js service that maps element ids to canvas elements (`elementRegistry.get(id)`, `filter`). It refuses a second element with an existing id. [02](02-modeler-diagram-js.md)

**Event bus**: The diagram-js publish and subscribe service (`eventBus.on(event, priority, callback)`); listeners with a lower priority run later. [02](02-modeler-diagram-js.md)

**Exchange format**: The text format that travels between the browser modeler and the .NET mapper: PNML in AMLPetriNet, FPB.JS JSON in AMLFPB.js, a small JSON in the starter. It is transient; the AML document is the master. [03](03-exchange-format.md)

**ExternalInterface**: An interface instance with an `ID` on an `InternalElement`, typed by an `InterfaceClass`; it is what an `InternalLink` connects. [04](04-aml-mapping.md)

**ExternalReference**: An entry of `CAEXFile` naming another CAEX file (`Alias`, `Path`) whose classes this document uses. The AutomationML Editor does not follow file references, so the small shared libraries are embedded in documents the mapper creates. [04](04-aml-mapping.md)

**Flow routing**: The layout step that gives bend points to connections a grid would draw badly: back edges, forward edges that would cross a node, self loops. Each routed connection gets a lane outside the drawing and its own channel between columns. In the starter: `EflLayout.RouteFlows`. [08](08-layout.md)

**Format version**: The number a file of an own exchange format carries so that a reader can tell it meets a newer version (`"formatVersion": 1` as the first key in the starter). A missing field means 1; a newer version is read as far as possible with a warning. [03](03-exchange-format.md)

**Identification**: The attribute compound that carries the language's own id and name on an element (`id`, `name` in ISO_PT and EFL; the VDI 3682 fields in FPD). The CAEX `Name` is only a display name. [04](04-aml-mapping.md)

**`$inject`**: The static array on a diagram-js service or `__init__` function that lists its dependencies in parameter order. Every class needs one; didi falls back to parameter names otherwise. [02](02-modeler-diagram-js.md)

**Insert** (Aml.Engine): Inserts a CAEX object into its sequence; the `asFirst` parameter defaults to true, which reverses document order, so always call `Insert(child, false)`. [04](04-aml-mapping.md)

**InstanceHierarchy**: A tree of concrete objects in a CAEX document. A language's diagrams live in one or more instance hierarchies next to foreign ones; find yours by content and bind by id. [04](04-aml-mapping.md)

**InterfaceClass / InterfaceClassLib**: The type of a connection point, derived from the AutomationML base `Port` in the source projects. Direction goes into the type (`FlowOut`, `FlowIn`) because an `InternalLink` is undirected (pattern P2). [04](04-aml-mapping.md)

**InternalElement**: A node of an instance hierarchy; every language element with an identity becomes one, and so does a reified connection. [04](04-aml-mapping.md)

**InternalLink**: A CAEX relation joining exactly two interfaces through the required attributes `RefPartnerSideA` and `RefPartnerSideB`. In the link encoding it is the connection itself; in the reified encoding it couples the connection element to its nodes. [04](04-aml-mapping.md)

**IsDocumentLoaded**: The event of `INotifyAMLDocumentLoad` documented as "the plugin has loaded a new document which should be loaded into the editor". The editor answers it by calling `DocumentLoaded` again, so declare it and never raise it from `DocumentLoaded`. [05](05-editor-plugin.md)

**IsReactive**: The plugin property that makes the editor call `ChangeSelectedObject` on tree selection and `ChangeAMLFilePath` when a file is opened. Both source plugins set it to true. [05](05-editor-plugin.md)

**LabelOffset**: An instance attribute typed `DD_Point` that stores where a moved label sits relative to its element (for an arc, relative to the middle of the polyline's middle segment). It is removed again when the label returns to its default place. [08](08-layout.md)

**Language-owned attribute**: An attribute name the language claims (`Identification`, `ViewInformation`, `Waypoint_n`, its own values). Update in place writes and removes only these, decided by one predicate (`IsLanguageOwned`); every other attribute belongs to someone else. [04](04-aml-mapping.md)

**Layouter**: The diagram-js service that computes a connection's waypoints (`layoutConnection`). The starter's layouter keeps bend points and crops the end points against the shape outlines. [02](02-modeler-diagram-js.md), [08](08-layout.md)

**Library loan**: AMLPetriNet's handling of a document that references its class libraries by alias: the definitions are added for the duration of a write, removed afterwards, and new paths are requalified with the document's alias (`PtLibraryLoan`). [04](04-aml-mapping.md)

**Link encoding**: See *Connection encoding*.

**Metadata.xml**: The `PlugInPackageMetaData` file next to the plugin assembly with `PackageName`, `DisplayName`, `Version`, `Author`, `Description` and `MinimumEditorVersion`. Its version must equal the csproj version; the PlugIn Manager itself shows what the nuspec says. [05](05-editor-plugin.md)

**Mirror object**: A CAEX object (InternalElement, ExternalInterface or Attribute) that stands for a master object elsewhere in the document. Aml.Engine exposes it through `IsMirror`, `Master` and `MasterID`; setting `Master` turns an element into a mirror. The ObjectReferences types are the typed alternative for "same logical object in another view". [04](04-aml-mapping.md)

**Module** (diagram-js): An object with `__depends__` (other modules), `__init__` (services created eagerly) and service definitions `name: ['type', Constructor]`. A later definition with the same name replaces an earlier one; a missing module fails silently. [02](02-modeler-diagram-js.md)

**nupkg**: The NuGet package the plugin build produces (`GeneratePackageOnBuild`) and the PlugIn Manager installs. Everything the plugin needs at runtime must be packed into `lib\<tfm>` explicitly; the contract assembly must not be. [05](05-editor-plugin.md)

**ObjectReferences**: See *refObj family*.

**OMG DD (`OMG_DD_AttributeTypeLib`)**: The shared, language-agnostic layout library (alias `OMG_DD`) with three types structured after the OMG Diagram Definition: `DD_Bounds` (top-left position and size, cf. DC::Bounds), `DD_Point` (x, y, cf. DC::Point) and `DD_Waypoint` (a routing point, cf. DI::Waypoint). Pattern P7. The playbook includes the published file (`starter/libraries/OMG_DD_AttributeTypeLib_v0.1.aml`); the starter embeds it into the documents it creates through `EflDiagramInterchange`. [08](08-layout.md), [04](04-aml-mapping.md)

**P1 to P8**: See *Patterns*.

**Palette**: The tool strip of diagram-js; a provider registers with `palette.registerProvider` and returns `getPaletteEntries()`. [02](02-modeler-diagram-js.md)

**Patterns (P1 to P8)**: The Phase 2 and Phase 3 patterns of the three-phase method: P1 role hierarchy with shared base attributes, P2 typed interface pairs, P3 typed cross-diagram references, P4 system unit classes as templates, P5 flat diagram structure, P6 connection encoding, P7 diagram interchange types, P8 explicit or implicit layout. [10](10-new-language-recipe.md)

**Pending model**: The latest full export of the canvas that has not been written into the document (`_pendingPnml` in AMLPetriNet, `_pendingModel` in the starter). Update writes it; Refresh and diagram switches ask before discarding it. [05](05-editor-plugin.md)

**PlugIn Manager**: The AutomationML Editor's dialog that installs plugin packages from a package source (for example a local folder with the nupkg) into the editor's plugin folder. It does not reinstall a version it already has. [05](05-editor-plugin.md)

**PluginViewBase**: The WPF base class in `Aml.Editor.Plugin.WPFBase` for plugin views that are integrated into the editor; the XAML root of the plugin derives from it. [05](05-editor-plugin.md)

**PortCoordinate**: A `DD_Point` attribute on a connection end interface holding the docking point, so an AML tool without a layouter shows the same picture as the canvas. [08](08-layout.md)

**PostWebMessageAsJson**: The `CoreWebView2` method the host uses to send a JSON message to the page, where it arrives as `event.data` on `window.chrome.webview`. A message posted before the page registered its listener is dropped without error. [05](05-editor-plugin.md)

**Ready handshake**: The rule that the host treats the page as usable only after the page posts `ready`; pushes before that are buffered, and readiness is reset on every navigation and renderer failure. [05](05-editor-plugin.md)

**RefAttributeType**: The path on an attribute that names its `AttributeType`, possibly alias-qualified. Attribute name and attribute type are separate: FPD keeps the name `refObj` and chooses the semantics through this path. [04](04-aml-mapping.md)

**RefBaseClassPath**: The inheritance path of a role class, system unit class, interface class or attribute type to its base class; also the type path of an `ExternalInterface`. [04](04-aml-mapping.md)

**RefBaseSystemUnitPath**: The path on an `InternalElement` to the system unit class it was instantiated from; the reader's first type signal, with role requirements as fallback. [04](04-aml-mapping.md)

**refObj family**: The attribute types of the published `AutomationML_ObjectReferences_AttributeTypeLib` (v1.1.1-beta, alias `ObjectReferences`), all `xs:IDREF` (pattern P3). Published by AutomationML and not included in the playbook; obtain it through the library manager of the AutomationML Editor:

- `refObj`: abstract base, a reference with unspecified meaning.
- `refBaseObj`: on an aspect object, points to the base object; both represent the same logical object (FPD boundary state copies).
- `refAspectObj`: on a base object, the optional reverse reference to an aspect object.
- `refDetailObj`: on an abstract object, points to a more detailed representation (FPD operator to sub-process).
- `refAbstractObj`: on a detail object, points back to the abstract representation (FPD sub-process to operator).

[04](04-aml-mapping.md)

**RefPartnerSideA / RefPartnerSideB**: The two required attributes of an `InternalLink`; in CAEX 3.0 each holds the id of an `ExternalInterface`; files from other tools may also write the older `ElementId:InterfaceId` form, so a reader accepts both. Aml.Engine sets them through `AInterface` and `BInterface`. [04](04-aml-mapping.md)

**Reified encoding**: See *Connection encoding*.

**RoleClass / RoleClassLib**: The semantic type of an element ("what it means"), with single inheritance through `RefBaseClassPath`. Every element type of the language becomes one (pattern P1). [04](04-aml-mapping.md)

**RoleRequirements**: The role an `InternalElement` plays (`RefBaseRoleClassPath`); lets a reader recognise an element that was not created from the language's system unit class. [04](04-aml-mapping.md)

**Root element**: The invisible top element of a diagram-js canvas. The implicit root the canvas creates on demand has no business object, so an importer sets an explicit root carrying the diagram's id and name. [02](02-modeler-diagram-js.md)

**Rule provider**: A diagram-js class (subclass of `RuleProvider`) whose `init` registers rules with `addRule(action, callback)`. A rule returns `false` to forbid; `undefined` lets evaluation continue. When no rule answers, a command (`shape.create`, `elements.move`) is allowed, but a non-command action such as `connection.start` is refused, so the connect tool needs an explicit rule. [02](02-modeler-diagram-js.md)

**Service** (diagram-js): A named object created by didi from a module definition (`canvas`, `eventBus`, `modeling`, `elementFactory`, ...) and obtained with `diagram.get(name)` or through `$inject`. [02](02-modeler-diagram-js.md)

**Shape**: A diagram element with bounds (`x`, `y`, `width`, `height`, top-left anchored), drawn by the renderer's `drawShape`. [02](02-modeler-diagram-js.md)

**Starter**: The runnable mini stack in `starter/` (mapper, tests, CLI, web app, modeler, plugin) for EFL. A new language starts from a renamed copy made by `tools/new-language.mjs`. [10](10-new-language-recipe.md)

**Stop point**: A step in the recipe marked **STOP** where the person who owns the language decides (prefix and names, connection encoding, stored layout, exchange format, references, rule list, license) before work continues. [10](10-new-language-recipe.md)

**SupportedRoleClass**: The entry on a system unit class naming a role it can play. A class may list several (language role plus AutomationML base role); `CreateClassInstance` copies only the first into role requirements. [04](04-aml-mapping.md)

**SystemUnitClass / SystemUnitClassLib**: A reusable template with attributes, interfaces and children from which instances are created (pattern P4). [04](04-aml-mapping.md)

**Tolerant reader**: A reader that throws only when nothing can be read and otherwise repairs or skips, recording a warning in words and the ids it could not represent. [03](03-exchange-format.md)

**Unresolved set**: The ids a reader saw but could not put into the model (for example an arc whose link does not resolve). The updater reads it before removing anything and keeps those elements. In the starter: `EflModel.Unresolved`, filled by `CaexToEfl` and read by `EflUpdater`. [03](03-exchange-format.md), [04](04-aml-mapping.md)

**Update in place**: Bringing an existing hierarchy in line with the edited model by matching elements on the language id and writing only language-owned attributes, instead of replacing the hierarchy. All preconditions are checked before the first write. [04](04-aml-mapping.md)

**User data folder**: The folder where WebView2 keeps its browser profile (`CoreWebView2Environment.UserDataFolder`). Without an explicit environment it defaults to a folder next to the host executable, shared by every WebView2 control in the editor process. [05](05-editor-plugin.md)

**ViewInformation**: The `DD_Bounds` attribute that stores a node's position and size in AML. It stays unused on reified connection elements, which store `Waypoint_n` instead. [08](08-layout.md)

**Virtual host mapping**: `CoreWebView2.SetVirtualHostNameToFolderMapping`, which serves a local folder under a host name such as `https://ptnjs.local/`, so the bundled page, ES modules and CSS load without `file://` restrictions. Give every plugin its own host name. [05](05-editor-plugin.md)

**Waypoint_n**: Instance attributes `Waypoint_1` to `Waypoint_n`, typed `DD_Waypoint`, holding a connection's bend points from source to target: on the source-side interface in the link encoding, on the connection element in the reified encoding. Parse the index numerically. [08](08-layout.md)

**WebMessageReceived**: The `CoreWebView2` event raised when the page calls `window.chrome.webview.postMessage`; the host reads `WebMessageAsJson` and dispatches on the message `type`. [05](05-editor-plugin.md)

**Wrapper** (Aml.Engine): The CLR object Aml.Engine creates around a CAEX XML element. Each access can return a new wrapper, so compare `ID` or the underlying `XElement` (`.Node`), never references. [04](04-aml-mapping.md)

---

## Where to look

| Group of terms | Chapter | Source to read |
|---|---|---|
| CAEX constructs, references, Aml.Engine | [04-aml-mapping.md](04-aml-mapping.md) | `AMLPetriNet: dotnet/PtMapper.Conversion/PtWrite.cs`, `dotnet/PtMapper.Conversion/PtCaex.cs`; `AMLPetriNet: libraries/AutomationML_ObjectReferences_AttributeTypeLib_AMLEd2_1.1.1-beta.aml` |
| Layout types | [08-layout.md](08-layout.md) | `starter/libraries/OMG_DD_AttributeTypeLib_v0.1.aml`, `starter/dotnet/Efl.Conversion/EflDiagramInterchange.cs` |
| diagram-js | [02-modeler-diagram-js.md](02-modeler-diagram-js.md) | `starter/web/src/EflModeler.js`, `starter/web/src/rules/Rules.js` |
| Plugin contract, WebView2, bridge | [05-editor-plugin.md](05-editor-plugin.md) | `AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs`, `Aml.Editor.Plugin.PetriNet/Bridge/PtWebView.cs` |
| Exchange format, tolerant reading | [03-exchange-format.md](03-exchange-format.md) | `AMLPetriNet: dotnet/PtMapper.Conversion/Models/PtModels.cs` |
| Method terms, starter, stop points | [10-new-language-recipe.md](10-new-language-recipe.md) | `starter/README.md`, `tools/new-language.mjs` |
