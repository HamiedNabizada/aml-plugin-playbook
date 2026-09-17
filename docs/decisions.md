# Decision history

## In short

- This file records why the source projects (AMLPetriNet, AMLFPB.js with fpb-aml-mapper, FPB.JS) and the playbook starter are built the way they are, which alternatives were tried, and which were rejected.
- Every decision has an id `D-nn`, grouped by area: architecture, exchange format, library design, connection encoding, ids, update in place, layout, modeler, plugin UI and lifecycle, packaging, web app, testing.
- Status is one of **in force**, **superseded by D-xx**, **differs between projects** (both variants are live, with the recommended one named) or **proposed** (agreed direction, not implemented).
- Evidence cites code or docs by file and symbol, with the project prefix. Where a claim rests only on the engineering notes of the projects and no code shows it, the entry says **(notes only)**; where only the history of a project shows it, **(from the project history)**.
- Before reversing a decision, read its evidence. Most of them were taken after a failure that was silent at the time.

Read fully when: you are about to change something that "looks unnecessary" in the starter, or you have to choose between two variants the source projects implement differently.
Skim when: you only need the reason behind one rule; search for its keyword.

This file complements the chapters: the chapters say what to do, this file says why and what else was on the table. The method behind the library decisions is the three-phase method (Nabizada, Drath, Ocker, Fay; submitted to at Automatisierungstechnik), with earlier steps at EKA 2026 and ETFA 2026; its analysis items A1 to A4 and patterns P1 to P8 are summarised in [10-new-language-recipe.md](10-new-language-recipe.md). Terms are in [glossary.md](glossary.md).

---

## How an entry reads

- **Context:** the problem as it appeared, usually a symptom.
- **Options:** what was considered, including what was built first and later removed.
- **Decision:** what the code does now.
- **Consequences:** what follows for a builder, including the costs.
- **Evidence:** file and symbol (class, method, test, function, config key, or a named section or step), with project prefix. Starter files are cited as `starter/...`.
- **Status.**

---

## 1. Architecture

### D-01 The AML document is the master; the exchange text is transient

- **Context:** A plugin that keeps the diagram as a file of its own ends up with two sources of truth, and any edit made in the AML Editor tree is lost the next time the diagram is written.
- **Options:** (a) keep the exchange file next to the AML document and regenerate the hierarchy from it; (b) derive the diagram from the document on every load, keep edits as one pending string, write them back only on an explicit Update.
- **Decision:** (b). The canvas is filled from the document; edits accumulate as one pending snapshot; Update writes it through update in place (D-43). Nothing touches the document while the user draws.
- **Consequences:** The plugin needs a pending label, a confirmation before discarding pending edits, and an echo baseline to tell import fallout from edits (D-62). Automatic writes on every change are ruled out because the plugin cannot undo a document change.
- **Evidence:** `AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs` (`OnDiagramChanged`: pending only on a real edit; `UpdateButton_Click`: Update; `RefreshButton_Click`: Refresh asks first); `starter/plugin/Aml.Editor.Plugin.Efl/EflPlugin.xaml.cs` (header comment on the state the view keeps: `_pendingModel`, `_echoBaseline`).
- **Status:** in force.

### D-02 One UI-free mapper library that every consumer references

- **Context:** The FPD plugin first carried its own copy of the conversion layer. The copy still wrote `FPD_*` class paths after the mapper had moved to `VDI_FPD_*`, and the drift cost hours of diagnosis (notes only for the history).
- **Options:** (a) conversion code inside the plugin; (b) a copy per consumer; (c) one `net8.0` class library referencing only Aml.Engine, used by plugin, web app, CLI and tests.
- **Decision:** (c). The plugin references the mapper as a project, not as a copy.
- **Consequences:** The web app and the tests run without WPF and without Windows; a mapper fix reaches the plugin with the next build. The reference must be `PrivateAssets=all` and the DLL packed explicitly (D-72).
- **Evidence:** `AMLPetriNet: Aml.Editor.Plugin.PetriNet/Aml.Editor.Plugin.PetriNet.csproj` (the `ProjectReference` to `PtMapper.Conversion` with `PrivateAssets`, and the `None` item packing `PtMapper.Conversion.dll`); `AMLFPB.js: Aml.Editor.Plugin.FPB/Aml.Editor.Plugin.FPB.csproj` (the `ProjectReference` to `FpbMapper.Conversion`); `AMLPetriNet: dotnet/PtMapper.Web/Program.cs` (header comment: the same conversion library the plugin uses).
- **Status:** in force.

### D-03 One repository and one solution

- **Context:** AMLFPB.js builds only when the plugin, the mapper, FPB.JS and OCL.NET sit in a fixed folder layout. Its project files reach them through relative paths that assume the developer's folders, so CI has to check out every sibling repository in the same layout. With separate repositories, every new cross-repository reference has to be mirrored in the workflow by hand, and a flat checkout that differs from the local layout makes a relative path point outside the workspace.
- **Options:** (a) separate repositories joined by relative paths (AMLFPB.js); (b) one repository with bundle, mapper, plugin, web app, CLI, tests, libraries and examples (AMLPetriNet).
- **Decision:** (b) for a new language. Keep the modeler in its own repository only if it is a product with users of its own, and then consume a pinned version.
- **Consequences:** CI is one checkout; the bundle is built before the solution. AMLFPB.js keeps (a) and replicates the local layout in CI.
- **Evidence:** `AMLPetriNet: .github/workflows/ci.yml` (job `windows`: one `actions/checkout`, step "Build the modeler bundle" before "Build"); `AMLFPB.js: .github/workflows/build.yml` (the layout comment above step "Checkout plugin"; steps "Checkout plugin", "Checkout mapper", "Checkout FPB.JS" and "Checkout OCL.NET"); `AMLFPB.js: Aml.Editor.Plugin.FPB/Aml.Editor.Plugin.FPB.csproj` (`FpbJsDistDir` and the `ProjectReference` items to the mapper and to OCL.NET).
- **Status:** differs between projects; (b) recommended.

### D-04 Validator and conformance report live in the mapper

- **Context:** The same rules have to run in the plugin, in a web endpoint and in CI.
- **Options:** (a) rule classes, the OCL engine pass and the report in the plugin, only the OCL files in the mapper (AMLFPB.js); (b) validator and report in the mapper (AMLPetriNet, starter).
- **Decision:** (b).
- **Consequences:** With (a) the FPD web API has no validation endpoint and validation tests must reference the WPF plugin. With (b) `ptmap validate` exits non-zero for CI and `/api/validate` exists.
- **Evidence:** `AMLFPB.js: Aml.Editor.Plugin.FPB/Validation/Vdi3682Validator.cs` (`ValidateStructured`); `AMLFPB.js: Aml.Editor.Plugin.FPB/Validation/Vdi3682OclRuleSet.cs` (`Append`); `AMLFPB.js: Aml.Editor.Plugin.FPB/Validation/ConformanceReport.cs` (`ConformanceReportBuilder`); `AMLFPB.js: Aml.Editor.Plugin.FPB.Tests/Aml.Editor.Plugin.FPB.Tests.csproj` (the `ProjectReference` items); `fpb-aml-mapper: dotnet/FpbMapper.Conversion/FpbMapper.Conversion.csproj` (OCL rules as `EmbeddedResource` items under `Rules\`); `AMLPetriNet: dotnet/PtMapper.Web/Program.cs` (endpoint `/api/validate`).
- **Status:** differs between projects; (b) recommended.

### D-05 Reuse an existing diagram-js modeler unchanged, patch from outside

- **Context:** For P/T nets a maintained diagram-js modeler exists; for FPD none did, so FPB.JS was written from scratch.
- **Options:** (a) build a modeler; (b) fork the existing one; (c) depend on the published package at an exact version and customise it from outside (extra module code, guarded prototype patches, a bundler alias for one dependency). The older `ptn-js` package was deprecated in favour of its successor (notes only).
- **Decision:** (c) for AMLPetriNet. The starter is a small modeler of its own, because a toy language has no existing modeler.
- **Consequences:** Every upstream defect found in testing needs a patch with a reason and a guard against double wrapping (D-60). Upgrading the package means re-running the browser checks.
- **Evidence:** `AMLPetriNet: README.md` (section "Modeler"); `AMLPetriNet: web/package.json` (dependency `@bptlab/openbpt-modeler-petri-net`); `AMLPetriNet: web/src/index.js` (the `UpdatePropertyHandler.$inject` patch and the guarded `UpdateLabelHandler.prototype.postExecute` wrapper `postExecuteScopedToArcs`).
- **Status:** in force.

### D-06 A separate plugin and repository per language

- **Context:** The P/T net plugin could have been added to the FPD plugin.
- **Options:** (a) one plugin with several languages; (b) one plugin per language.
- **Decision:** (b) (notes only for the decision itself).
- **Consequences:** Each plugin has its own display name, host name, settings and log folder. The editor shows toolbar commands of one plugin at a time, which later forced D-66.
- **Evidence:** `AMLPetriNet: README.md` (introduction) names AMLFPB.js as the FPD counterpart.
- **Status:** in force.

### D-07 One WebView2 per plugin view, with a picker

- **Context:** AMLFPB.js creates one sub-tab with its own WebView2 per FPD hierarchy. Rebuilding those tabs awaits WebView2 initialisation per hierarchy, and a second rebuild interleaving at an await produced ghost tabs bound to a closed document; closed tabs leaked browser processes.
- **Options:** (a) one view per hierarchy with a generation token (D-69) and explicit disposal; (b) one WebView2 for the view and a picker over all diagrams of all matching hierarchies, hidden when there is one.
- **Decision:** (b) for new plugins.
- **Consequences:** Switching diagrams reimports and must ask before discarding pending edits; no rebuild race exists.
- **Evidence:** `AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs` (`RefreshNetChoices`); `AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs` (`_rebuildGeneration` and its comment, `RebuildTabsForDocumentInner`); `AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs` (`Dispose`, the comment on `WebView?.Dispose()`).
- **Status:** differs between projects; (b) recommended.

### D-08 Manual Refresh before polling the tree

- **Context:** Aml.Engine has no public change event, so the plugin cannot see edits made in the AML Editor tree.
- **Options:** (a) poll: hash the hierarchy XML every 2 s per view and reload on change (AMLFPB.js); (b) a Refresh button that asks before discarding pending edits (AMLPetriNet).
- **Decision:** (b) first. If polling is added, copy the AMLFPB.js conflict handling: back up pending edits to a file before dropping them, and suppress the poll for the whole Update including its dialogs, because `MessageBox.Show` pumps the dispatcher.
- **Consequences:** Polling costs a full serialisation per view per tick on the UI thread; a deleted hierarchy still hashes its detached XML.
- **Evidence:** `AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs` (`RefreshButton_Click`); `AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs` (`_liveSyncTimer`, `LiveSyncPollInterval`; `LiveSyncTick` and `TryWritePendingBackup`, which back up and drop pending edits on a conflict; `UpdateButton_Click`, which sets `_liveSyncSuppressed` before `ConfirmLargeChangeIfNeeded`, with the comment on why).
- **Status:** differs between projects; (b) recommended.

---

## 2. Exchange format

### D-09 A standard interchange format where one exists, otherwise an own JSON

- **Context:** The modeler and the mapper need one format both implement.
- **Options:** (a) the modeler's internal objects serialised as they are (FPB.JS JSON); (b) a standard format with a published grammar (PNML, ISO/IEC 15909-2, for P/T nets); (c) a small designed JSON with an object root when the language has no standard (starter).
- **Decision:** (b) when a standard exists, (c) otherwise. (a) is the anti-pattern: FPB.JS has no JSON writer of its own, and the plugin carries a copy of the export replacer.
- **Consequences:** A standard format gives a grammar to validate against (D-85) and foreign files to test with; an own format needs a version field and a written shape from the first release.
- **Evidence:** `AMLPetriNet: README.md` (introduction); `starter/dotnet/Efl.Conversion/EflJson.cs` (class comment on `EflJson`: shape of the file and the rules worth keeping).
- **Status:** in force.

### D-10 Replace the modeler's PNML converter through a bundler alias

- **Context:** The upstream `pnml-moddle-converter` needs Node builtins (it does not bundle for a browser) and writes neither node dimensions nor arc graphics, so every bend point was lost.
- **Options:** (a) patch the package in `node_modules`; (b) fork; (c) a DOM-based converter in the repository, aliased onto the package name so the modeler's own `importPNML`/`savePNML` use it.
- **Decision:** (c).
- **Consequences:** The repository owns a second PNML implementation that must agree with the C# one; a browser interop test guards the pair.
- **Evidence:** `AMLPetriNet: web/src/pnml/index.js` (header comment: why it replaces the upstream converter); `AMLPetriNet: web/build.mjs` (the `alias` entry for `pnml-moddle-converter`).
- **Status:** in force.

### D-11 PNML graphics conventions: centre position, bend points only, labels relative to a named point

- **Context:** Geometry has different anchors in PNML, in diagram-js and in AML, and label positions drifted when the two sides measured from different points.
- **Options:** store all polyline points including docking points, or only bend points; measure an arc label from the arc centre of gravity, or from the point the modeler uses.
- **Decision:** Node `<position>` is the centre; arcs carry only intermediate bend points, the docking points are recomputed; node label offsets are relative to the node centre; an arc label offset is measured from the middle of the polyline's middle segment, copied from the modeler's own `getWaypointsMid`. The arc label position travels end to end.
- **Consequences:** Both converters implement the same conventions, written at the top of each file. Writing the docking points would show up as stray bends in other tools.
- **Evidence:** `AMLPetriNet: web/src/pnml/index.js` (the conventions in the header comment; `waypointsMid`).
- **Status:** in force.

### D-12 Tolerant readers that report, strict writers

- **Context:** A reader silently skipped an arc whose link was missing; the updater then treated its absence as a deletion and removed the element with its attributes on the next Update.
- **Options:** (a) throw on anything unexpected; (b) skip silently; (c) throw only when nothing can be read, otherwise repair or skip, record a warning in words, and remember the ids that could not be represented.
- **Decision:** (c). The model carries `Warnings` and `Unresolved`; the plugin shows reading warnings in the findings list; the updater never deletes an unresolved element (D-48).
- **Consequences:** Every reader needs a diagnostics channel and every writer a guard against non-finite numbers and half connections.
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Conversion/Models/PtModels.cs` (`Warnings`, `Unresolved`); `AMLPetriNet: dotnet/PtMapper.Conversion/CaexToPtNet.cs` (`ReadNet`, `ResolveEndpoints`); `AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs` (`Revalidate`); `starter/dotnet/Efl.Conversion/EflJson.cs` (class comment on `EflJson`: tolerant reader, strict writer); `starter/dotnet/Efl.Conversion/Models.cs` (`Unresolved`).
- **Status:** in force.

### D-13 Structured values instead of strings for characteristics

- **Context:** The FPD mapper modelled setpoint value, validity limits and actual values as strings. The JSON helper returns an empty string for objects and arrays, so a setpoint of 20 °C arrived in AML empty; the reverse path wrote untyped values the FPB.JS importer skips or crashes on.
- **Options:** keep strings and parse; model the structure end to end with type tags on the way back.
- **Decision:** Structured models, CAEX compounds, and `$type` tags on the reverse path.
- **Consequences:** A kind mismatch in a reader must be a warning, not an empty default.
- **Evidence:** `fpb-aml-mapper: dotnet/FpbMapper.Conversion/Models/FpbModels.cs` (`ValueWithUnit`, `ValidityLimit`); `fpb-aml-mapper: dotnet/FpbMapper.Conversion/CaexToFpbJson.cs` (`ReadValueWithUnit`, `ReadValidityLimits`, `ReadActualValues`, which write the `$type` tags).
- **Status:** in force.

### D-14 The starter's JSON: object root, same key order on both sides

- **Context:** The echo baseline (D-62) compares strings, and files written by the modeler and by the mapper should diff cleanly.
- **Options:** let each side serialise in its own order; fix one order for both.
- **Decision:** The C# writer emits keys in the order of the modeler's export (`formatVersion`, `id`, `name`, `nodes`, `flows`; within a node `id`, `type`, `name`, `value`, `bounds`), flows carry bend points only. The modeler rounds numbers to two decimals; the C# writer writes them as the model holds them.
- **Consequences:** Adding a field means adding it in the same position in `EflJson.Write` and in `exportModel`.
- **Evidence:** `starter/dotnet/Efl.Conversion/EflJson.cs` (`Write`); `starter/web/src/io/json.js` (header comment, `exportModel`, `round`).
- **Status:** in force.

### D-87 The starter's JSON carries a format version

- **Context:** FPB.JS JSON has no version field, so a planned v2 has to tell versions apart by shape. The starter's format had none at first, and every copy would have inherited that.
- **Options:** no field; a version field that refuses newer files; a version field that reads newer files as far as possible and warns.
- **Decision:** `"formatVersion": 1` as the first key on both sides. A missing field means version 1, because such a file predates the field. A newer version is read with a warning that anything the newer version added is not read and would be lost on writing. The number goes up only when an older reader would lose something.
- **Consequences:** Readers keep every older version readable. A copy that changes the format raises the constant in `EflJson.cs` and `json.js` together.
- **Evidence:** `starter/dotnet/Efl.Conversion/EflJson.cs` (`FormatVersion`, `Read`); `starter/web/src/io/json.js` (`FORMAT_VERSION`, `importModel`); tests `AFileFromANewerFormatVersionIsReadWithAWarning` and `AFileWithoutAFormatVersionIsReadAsVersionOne` in `starter/dotnet/Efl.Tests/ExampleAndGeometryTests.cs`; browser check "a file from a newer format version is shown with a warning" in `starter/web/tools/verify-modeler.mjs`.
- **Status:** in force.

### D-15 Tool notions outside the standard travel in its extension point

- **Context:** The modeler can draw a silent transition; ISO/IEC 15909-1 has no such concept.
- **Options:** drop it; add a non-standard element; put it into `<toolspecific>` and keep it out of the class model.
- **Decision:** `<toolspecific>` in PNML, an `IsSilent` instance attribute in AML that is not declared on the class.
- **Consequences:** Foreign readers ignore it; the round trip keeps it.
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Conversion/PtNames.cs` (`IsSilent`).
- **Status:** in force.

### D-16 Two-level identity for a future FPB.JS JSON

- **Context:** FPB.JS shares one id between a boundary state on the parent layer and its copy in the sub-process, and a sub-process aliases its parent operator's id. The golden tests have to canonicalise GUIDs partly because of that.
- **Options:** keep id sharing; give every representation its own id plus a stable object identifier and a typed reference between representations.
- **Decision:** Own ids plus a stable object identifier are the agreed target for a JSON v2; not implemented (notes only).
- **Consequences:** Until then the mapper translates between shared and distinct ids (D-40).
- **Evidence:** `fpb-aml-mapper: dotnet/FpbMapper.Tests/GoldenTests.cs` (`JsonToAml_StructuralDump_MatchesGolden`, comment on the v1 quirk).
- **Status:** proposed.

---

## 3. Library design

### D-17 Four prefixed libraries, names in one class, the library file as authority

- **Context:** Class paths end up in documents other people keep; a generator, a reader, an updater and tests that spell names separately drift apart.
- **Options:** literals where used; one names class; a published `.aml` artefact compared against the generator.
- **Decision:** One InterfaceClassLib, RoleClassLib, SystemUnitClassLib and AttributeTypeLib with a language prefix; every name in one static class; fixed class ids; a golden test compares the library built in code with the published file.
- **Consequences:** A rename is a migration of existing documents. Without fixed class ids every library write gets new random ids (the starter stamps ids derived from the class path).
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Conversion/PtNames.cs` (class comment on `PtNames`, `ClassIds`); `starter/dotnet/Efl.Conversion/EflLibraries.cs` (`EnsureLibraries`, `StampClassIds`); `fpb-aml-mapper: dotnet/FpbMapper.Conversion/FpbMappings.cs` (`ElementToSuc`, the `VDI_` prefixed class paths).
- **Status:** in force.

### D-18 Language id and name in an Identification compound; the CAEX Name is a display name

- **Context:** ISO_PT before v0.3 had no Identification on `PT_Net`; a round trip had nowhere to put the net id and renamed the net after its label. Other tools also give elements more telling CAEX names than the language name.
- **Options:** use the CAEX `Name` as id or name; add an Identification attribute.
- **Decision:** Identification (id, name) on the abstract element base and on the net. The CAEX `Name` is written only on a real rename (D-46).
- **Consequences:** Readers need a fallback for documents without Identification (D-50).
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Conversion/PtLibraries.cs` (`EnsureRoleClassLib`, the Identification on the net added in v0.3).
- **Status:** in force.

### D-19 Roles carry meaning, system unit classes are templates; append the missing role requirements

- **Context:** An element should be recognisable to any AML tool, and a vendor template should be able to claim the same role.
- **Options:** only SUCs; only roles; both, with AML base roles as additional supported roles.
- **Decision:** Both. Instances are created with `CreateClassInstance` from the SUC, then every further `SupportedRoleClass` is appended as a role requirement, because the engine copies only the first.
- **Consequences:** Without the append the AML base role silently disappears from every instance.
- **Evidence:** `fpb-aml-mapper: dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs` (`CreateInstance`: `CreateClassInstance`, then every further `SupportedRoleClass` appended as a role requirement); `fpb-aml-mapper: dotnet/FpbMapper.Conversion/FpdLibraries.cs` (`EnsureRoleClassLib`: AML base inheritance; `EnsureSystemUnitClassLib`: the AML base role as a second `SupportedRoleClass`); `AMLPetriNet: dotnet/PtMapper.Conversion/PtWrite.cs` (`CreateInstance`).
- **Status:** in force.

### D-20 One shared OMG_DD library for layout

- **Context:** The FPD and P/T libraries each had a diagram-interchange AttributeTypeLib of their own with structurally identical types; a bridging document holding both languages described layout twice.
- **Options:** per-language DI types; one language-agnostic library after the OMG Diagram Definition (`DD_Bounds`, `DD_Point`, `DD_Waypoint`, alias `OMG_DD`).
- **Decision:** The shared library. Both mappers emit it from a `DiagramInterchangeLibrary` class that is deliberately duplicated so the repositories stay independent, with the instruction to keep the emitted library byte identical.
- **Consequences:** Two copies of one class must be kept in step by hand.
- **Evidence:** `fpb-aml-mapper: dotnet/FpbMapper.Conversion/DiagramInterchangeLibrary.cs` (class comment on `DiagramInterchangeLibrary`); `AMLPetriNet: dotnet/PtMapper.Conversion/DiagramInterchangeLibrary.cs` (class comment: a deliberate copy, kept byte identical); `fpb-aml-mapper: dotnet/FpbMapper.Conversion/FpdLibraries.cs` (`EnsureLibraries`, which references the OMG_DD file instead of writing an FPD layout library); `AMLFPB.js: Aml.Editor.Plugin.FPB.Tests/FpbJsonToCaexTests.cs` (`Convert_CreatesValidCaexDocument`, which expects an external reference under `DiagramInterchangeLibrary.Alias`, the OMG_DD alias, instead of an inline DI library).
- **Status:** in force.

### D-21 References typed with the published ObjectReferences library

- **Context:** The FPD library declared its own generic `refObj`; the semantics of each reference (decomposition, back link, boundary copy) were only in prose.
- **Options:** keep a local `xs:string` `refObj`; type the existing attribute names with `refDetailObj`, `refAbstractObj` and `refBaseObj` from the official `AutomationML_ObjectReferences_AttributeTypeLib` (`xs:IDREF`, alias `ObjectReferences`).
- **Decision:** The official types, keeping the VDI 3682 attribute names (`refProcess`, `refObj`). A switch keeps the older layout for the ETFA v0.5 artefacts.
- **Consequences:** Attribute name and attribute type are separate decisions; a reader must accept both layouts.
- **Evidence:** `fpb-aml-mapper: dotnet/FpbMapper.Conversion/MapperOptions.cs` (`UseObjectReferencesLibrary`, `ReferenceAttributeDataType`); `fpb-aml-mapper: dotnet/FpbMapper.Conversion/ReferenceTypes.cs` (`ReferenceTypes`, `RefBaseObj`).
- **Status:** in force.

### D-22 Flat diagrams with references instead of nesting

- **Context:** FPD decomposes a process operator into a sub-process, and the system limit is drawn around states.
- **Options:** nest the sub-process inside the operator and the states inside the system limit; keep every process directly under the hierarchy and link layers with references.
- **Decision:** Flat. Every `FPD_Process` sits directly under the hierarchy, linked by `refProcess` and `refObj`; `FPD_SystemLimit` is a peer of the process content, not a container. P/T nodes and arcs are children of `PT_Net` because they are parts of the net (pattern P5).
- **Consequences:** A layer can be removed without touching its parent's subtree; moving a state across the border never re-parents an element. A reader needs a reverse-lookup fallback when a file fills only one direction.
- **Evidence:** `fpb-aml-mapper: dotnet/FpbMapper.Conversion/CaexToFpbJson.cs` (`Convert`, which collects the `FPD_Process` elements flat in the hierarchy); `fpb-aml-mapper: dotnet/FpbMapper.Conversion/FpdLibraries.cs` (`EnsureRoleClassLib`, `FPD_SystemLimit`).
- **Status:** in force.

### D-23 No reference framework for basic P/T nets

- **Context:** The ISO_PT library declares `refObj` on `PT_Net`, and the bridging example links net nodes to plant signals.
- **Options:** fill `refObj` from the mapper; leave references to users and guarantee they survive.
- **Decision:** The mapper never writes `refObj` for basic nets (the method states that nets without hierarchy need no reference framework; hierarchical nets would use the same family as FPD decomposition). Hand-made references and cross-hierarchy links must survive a sync, which tests prove.
- **Consequences:** The ObjectReferences library still has to be present in documents, because the class definition uses its type (D-25).
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Tests/ReferenceSurvivalTests.cs` (class comment on `ReferenceSurvivalTests`).
- **Status:** in force.

### D-24 Variable geometry as instance attributes, documented in the class description

- **Context:** The number of bend points varies per connection; a moved label is a user edit, not part of the language.
- **Options:** declare a fixed set of attributes on the class; declare nothing and write `Waypoint_1..n` and `LabelOffset` on instances by a convention stated in the class description.
- **Decision:** Instance attributes, typed with the OMG_DD types, convention written into the `PT_Arc` description of ISO_PT v0.3. The inherited `ViewInformation` rectangle stays unused on arcs.
- **Consequences:** Readers parse the index from the name and sort numerically; writers replace the whole waypoint set.
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Conversion/PtNames.cs` (`LabelOffset`, `WaypointPrefix`).
- **Status:** in force.

### D-25 Embed the small shared libraries into documents the mapper creates

- **Context:** The AML Editor does not follow file references. A document created in the editor and saved away from the library files opened with geometry and `refObj` unresolved, which users read as a broken file. A first idea, not referencing ObjectReferences at all because the mapper does not fill `refObj`, was wrong: the ISO_PT class definition itself types `refObj` with it (notes only for the rejected idea).
- **Options:** (a) reference both by file; (b) embed OMG_DD and ObjectReferences (a few kilobytes), keep the AML base libraries as an external reference (by file name, D-27), leave a document that already chose a form alone; (c) embed everything.
- **Decision:** (b). The ObjectReferences file ships as an embedded resource because the plugin has no library folder in reach; OMG_DD is built in code. The library artefact itself keeps references.
- **Consequences:** Class paths inside the own libraries lose the alias prefix when a library is embedded, and the writer must follow whichever form a document uses (D-26). fpb-aml-mapper still writes an ExternalReference for OMG_DD (`FpdLibraries.EnsureLibraries`), and the AMLFPB.js tests expect it (`FpbJsonToCaexTests.Convert_CreatesValidCaexDocument`). The starter embeds OMG_DD from the published file rather than building it in code (D-90).
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Conversion/PtLibraries.cs` (`EnsureLibraries`, `EmbedSharedLibraries`); `AMLPetriNet: dotnet/PtMapper.Conversion/ObjectReferencesLibraryFile.cs` (class comment on `ObjectReferencesLibraryFile`); `AMLPetriNet: dotnet/PtMapper.Conversion/PtSelfContained.cs` (class comment on `PtSelfContained`).
- **Status:** in force; recipe §3 prescribes it for new languages.

### D-26 Follow the document's own form for libraries and type paths

- **Context:** Editing one node in a document that referenced its libraries by alias wrote four class libraries into it, and the new element named its classes differently from every element beside it. Writing `OMG_DD@...` paths into a self-contained document left a dangling alias on every attribute a sync touched.
- **Options:** always embed; always reference; read the convention from the document and follow it.
- **Decision:** `PtLibraryLoan` borrows the class definitions for the duration of the write, removes them again and requalifies new paths with the alias in use (read from paths, not from the ExternalReference list); `PtDiPaths` resolves inline, aliased or absent once per write.
- **Consequences:** A loan has to be given back in a `finally` block (D-44).
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Conversion/PtLibraryLoan.cs` (class comment on `PtLibraryLoan`, `AliasInUse`); `AMLPetriNet: dotnet/PtMapper.Conversion/PtDiPaths.cs` (class comment on `PtDiPaths`).
- **Status:** in force.

### D-27 How the AML base libraries are referenced

- **Context:** The FPD mapper wrote a base library path with embedded authentication into every file it wrote, which exposed it to anyone opening the output.
- **Options:** a public URL; a relative file name resolved through the tool's library search path.
- **Decision:** Reference by file name, never by a URL containing a token or credentials. fpb-aml-mapper switched its writer to the relative file name first. AMLPetriNet used a public share URL for a while, with an access token in its user-info part, the same shape the FPD mapper had removed; it now writes the file name as well. The starter uses the file name `AutomationML_Base_Libraries_AMLEd2_2.11.0.aml`.
- **Consequences:** A relative name depends on the reading tool's search path; a URL depends on the host staying up. The AML Editor loads no referenced document in any form and takes the base libraries from its library manager, so a URL gains nothing there. Do not put credentials or tokens into a path that is written into documents.
- **Evidence:** `fpb-aml-mapper: dotnet/FpbMapper.Conversion/FpbMappings.cs` (`AmlBase.Path`, the relative file name); `AMLPetriNet: dotnet/PtMapper.Conversion/PtNames.cs` (`AmlBase.Path`, the file name, with the reason in its comment); `starter/dotnet/Efl.Conversion/EflLibraries.cs` (`BasePath`).
- **Status:** in force in all three; the share URL in AMLPetriNet is history (from the project history).

### D-28 The starter defines its own layout types

- **Context:** The starter had to run from a fresh copy without other library files.
- **Options:** reference OMG_DD from a file next to the examples; embed OMG_DD in code; define `EFL_Point`, `EFL_Bounds`, `EFL_Waypoint`.
- **Decision (former):** Own types, marked in their descriptions as stand-ins for OMG_DD, with the instruction that a real language switches to OMG_DD by hand.
- **Consequences:** Every new language started with stand-in layout types and had to switch before its first document left the machine. In the playbook's trial run, OMG_DD rebuilt from its description gave a second, incompatible library of the same name (PF-AML-16).
- **Evidence:** earlier starter versions; replaced by `starter/dotnet/Efl.Conversion/EflDiagramInterchange.cs`.
- **Status:** superseded by D-90.

### D-90 The playbook ships OMG_DD, not ObjectReferences; the starter embeds OMG_DD from the file

- **Context:** Every language needs layout types, and a starter with its own stand-in types pushed a migration onto every new language (D-28). A layout library generated from a description instead of the published file had guessed ids and attribute layout (PF-AML-16). Cross-diagram references (ObjectReferences) are needed only by some languages.
- **Options:** (a) keep stand-in types; (b) generate OMG_DD in code, as AMLPetriNet and fpb-aml-mapper do; (c) ship the published OMG_DD file in the playbook and embed it; (d) ship the ObjectReferences file as well.
- **Decision:** (c), not (d). `starter/libraries/OMG_DD_AttributeTypeLib_v0.1.aml` is the playbook owner's own library (v0.1, first published with fpb-aml-mapper and AMLPetriNet) and needed by every language, so the playbook carries it: the mapper links it as an embedded resource, embeds it into every document it creates unless the document already carries or references it, writes every layout type path in the form the document uses, and makes the library artefact reference it by file name (`eflmap library` writes the file beside the artefact). The ObjectReferences AttributeTypeLib is a third-party artefact published by AutomationML; the playbook does not redistribute it. A language that needs references obtains it through the library manager of the AutomationML Editor and embeds it the same way.
- **Consequences:** A copy of the starter uses OMG_DD from the first commit and needs no migration. A test keeps the embedded copy equal to the file. A language with references (P3) has one more step: get the published ObjectReferences file, add it as a resource, and follow `EflDiagramInterchange` (`EnsureIn`, path form). Its types are never rebuilt from prose.
- **Evidence:** `starter/dotnet/Efl.Conversion/Efl.Conversion.csproj` (the `EmbeddedResource` item for `OMG_DD_AttributeTypeLib_v0.1.aml`); `starter/dotnet/Efl.Conversion/EflDiagramInterchange.cs` (`EnsureIn`, `ReferenceFrom`, `PathIn`); `starter/dotnet/Efl.Conversion/EflLibraries.cs` (`EnsureLibraries`, `CreateArtefact`); `starter/dotnet/Efl.Tool/Program.cs` (`Run`, command `library`); `starter/dotnet/Efl.Tests/DiagramInterchangeTests.cs` (`ADocumentTheMapperCreatesCarriesTheLayoutLibraryAndResolvesEveryLayoutType`, `TheEmbeddedLibraryIsThePublishedFile`, `TheLibraryArtefactReferencesTheLayoutLibraryInsteadOfCarryingIt`, `AnUpdateFollowsADocumentThatReferencesTheLayoutLibraryUnderItsOwnAlias`).
- **Status:** in force (starter and playbook).

---

## 4. Connection encoding

### D-29 The encoding follows whether a connection has an identity of its own

- **Context:** FPD flows are relations with a type; P/T arcs carry an id and a weight. An intermediate draft of the method used InternalLinks for every language and was corrected (notes only).
- **Options:** InternalLink between typed interfaces for every language; a reified InternalElement per connection for every language; choose per connection type from analysis item A2.
- **Decision:** Per connection type (pattern P6). FPD: InternalLink between an `...Out` interface on the source and an `...In` interface on the target, waypoints on the source-side interface. ISO_PT: `PT_Arc` element with `PT_ArcSource`/`PT_ArcTarget` interfaces and two InternalLinks to `PT_NodeArcEnd` interfaces on the nodes. A language never uses both for one connection type.
- **Consequences:** A link-encoded connection cannot carry attributes or be the target of a `refObj`; a reified one needs twice the objects and a reader that guards against links between two connection ends (D-33).
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Conversion/PtLibraries.cs` (class comment on `PtLibraries`: arcs as first-class elements); `fpb-aml-mapper: dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs` (`AddConnection`); `starter/dotnet/Efl.Conversion/EflNames.cs` (`EflConnectionStyle`).
- **Status:** in force.

### D-30 InternalLink partners name interface ids

- **Context:** The FPD mapper moved its links to `AInterface`/`BInterface` to make the references CAEX-conformant. Later, a helper in the P/T reader claimed to accept `OwnerID:InterfaceName`; that form could never resolve, and a document using it lost every arc on the next update.
- **Options:** the CAEX 2.15 form `OwnerID:InterfaceName`; the interface id in `RefPartnerSideA`/`B`, set through `AInterface`/`BInterface`.
- **Decision:** Interface ids on write. AMLPetriNet does not split at a colon at all, because an id may contain one. The FPD reader and the starter still accept the `ElementId:InterfaceId` form from externally authored files and take the part after the last colon.
- **Consequences:** Pick one policy and keep it in one helper.
- **Evidence:** `fpb-aml-mapper: dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs` (`AddConnection` sets `AInterface`/`BInterface`); `AMLPetriNet: dotnet/PtMapper.Conversion/PtCaex.cs` (`PartnerId`); `fpb-aml-mapper: dotnet/FpbMapper.Conversion/CaexToFpbJson.cs` (`ExtractInterfaceId`); `starter/dotnet/Efl.Conversion/EflWrite.cs` (`EnsureLink`); `starter/dotnet/Efl.Conversion/CaexToEfl.cs` (`InterfaceIdOf`).
- **Status:** in force; read tolerance differs (FPD and starter accept both forms).

### D-31 The starter implements both encodings, for teaching only

- **Context:** A builder has to see both encodings on one small language to choose.
- **Options:** one encoding; both, selectable.
- **Decision:** Both (`EflConnectionStyle.Link` and `Element`), written, read and updated by the same mapper, with a plugin setting and a web parameter for new diagrams. The updater reads the encoding from the existing hierarchy, never from the setting.
- **Consequences:** A real language deletes the branch it does not use, per connection type, including the setting and the `?style=` parameter.
- **Evidence:** `starter/dotnet/Efl.Conversion/EflUpdater.cs` (class comment on `EflUpdater`, `UpdateInPlace`, `StyleOf`); `starter/plugin/Aml.Editor.Plugin.Efl/Diagnostics/PluginSettings.cs` (`ConnectionStyle`); `starter/dotnet/Efl.Web/Program.cs` (endpoint `/api/to-aml` with `?style=element`).
- **Status:** in force (starter only).

### D-32 One node interface per incident connection; reuse existing wiring

- **Context:** Two parallel connections between the same nodes must stay apart. A sync on a document written by another tool renamed its node interfaces (`NodeArcEnd_1` to `Out_<arc>`) and gave them new ids.
- **Options:** interfaces named after their class and numbered (FPD); one interface per connection named after it (`Out_<id>`, `In_<id>`); on update, read how the document already wires things and reuse it.
- **Decision:** Named per connection for new elements, and existing wiring read first and reused; only missing interfaces and links are created.
- **Consequences:** A counter-based naming scheme must be seeded from the highest suffix present, not the count. Make it one interface per connection **end**, with the direction in the lookup key: the starter first keyed its port map by node and flow, and a flow from a node to itself then got one port with both links attached to it. It now keys by node, flow and direction and yields both ends of a self loop, in the writer and in the updater.
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Conversion/PtWrite.cs` (`EndpointName`); `AMLPetriNet: dotnet/PtMapper.Conversion/PtNetUpdater.cs` (`ReadWiring`); `starter/dotnet/Efl.Conversion/EflToCaex.cs` (`PortKey`, `EndsAt`); `starter/dotnet/Efl.Conversion/EflUpdater.cs` (`UpdateInPlace`, the ports per flow end); test `AFlowFromANodeToItselfGetsTwoPortsAndSurvivesTheRoundTrip` in `starter/dotnet/Efl.Tests/RoundTripTests.cs`.
- **Status:** in force.

### D-33 Only node interfaces resolve connection endpoints

- **Context:** With arc interfaces in the owner map, a link between two arcs resolved an arc as an endpoint, and the modeler then dropped both arcs at import while reporting success.
- **Options:** map every interface to its owner; map only node interfaces.
- **Decision:** Only node interfaces.
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Conversion/CaexToPtNet.cs` (`ReadNet`, the map from interface id to owning node).
- **Status:** in force.

---

## 5. Ids

### D-34 Deterministic CAEX ids

- **Context:** Aml.Engine gives every created object a random id. An update then cannot match elements, and every reference a user added dangles.
- **Options:** (a) reuse the editor's GUIDs as CAEX ids and mint random ids for processes and interfaces (FPD); (b) derive every id from a namespaced seed with SHA-256 and stamp UUID version and variant bits (AMLPetriNet, starter).
- **Decision:** (b). The version nibble goes on byte 7 because `Guid(byte[])` reads the first three fields little-endian; this was found by a test on the rendered form. The seed separator is written as the escape `"\0"`, not a raw NUL byte, which made the file binary to git.
- **Consequences:** The FPD derivations use SHA-1 with the version on byte 6: deterministic and unique, but not well-formed version 5 UUIDs. Its golden tests canonicalise GUIDs because `Convert` mints random ones.
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Conversion/PtIds.cs` (class comment on `PtIds`, `Separator`, `For`); `fpb-aml-mapper: dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs` (`DeriveBoundaryStateId`; `WrapBraces`, FPB.JS ids as AML ids); `fpb-aml-mapper: dotnet/FpbMapper.Conversion/CaexToFpbJson.cs` (`NormalizeId`).
- **Status:** differs between projects; (b) recommended.

### D-35 Interface and link ids derive from the owner's CAEX id

- **Context:** Seeded from the PNML id alone, the same net imported twice produced duplicate xs:IDs, and removing an arc in one copy removed the other copy's links.
- **Options:** allocate interface and link ids through the document id space with an ordinal suffix; salt the derivation with the owning element's CAEX id.
- **Decision:** Salt with the owner's CAEX id, which is already distinct per document; single-net documents keep stable ids.
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Conversion/PtIds.cs` (`Interface`, `Link`).
- **Status:** in force.

### D-36 Ids are claimed against the whole document

- **Context:** CAEX ids are unique per document, not per hierarchy. Duplicate language ids and the same model imported twice both collide. A per-element engine lookup doubled the write time of a 2000 element net.
- **Options:** track ids used in one run (FPD green-field builder); read every `ID` attribute of the document once and add a deterministic ordinal on collision.
- **Decision:** The document-wide id space, read once from the XML.
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Conversion/PtIds.cs` (`DistinctElement`, `Distinct`, `CaexIdSpace`).
- **Status:** in force.

### D-37 Normalise ids for the canvas instead of refusing the document

- **Context:** The first editor test with the paper's example failed: other tools store the braced CAEX GUID in `Identification/id`, the modeler's parser accepts only XML names, and the plugin's rule PT11 blocked display. Later an umlaut in a hand-made name passed .NET's name check but was refused by the modeler, whose pattern is ASCII only.
- **Options:** refuse such documents; rewrite ids in the document; normalise ids deterministically for the canvas only, keep the document's ids, match back through the same normalisation.
- **Decision:** Normalise for display; valid names stay untouched; ASCII only, with German letters transliterated and other diacritics stripped; the writer does not overwrite a stored id that differs only by normalisation.
- **Consequences:** The reader and the updater must normalise in the same order, or two elements that reduce to one name pair up differently.
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Conversion/PtIdText.cs` (class comment on `PtIdText`, `IsXmlName`, `Transliterate`, `SameAfterNormalisation`); `AMLPetriNet: dotnet/PtMapper.Conversion/PtWrite.cs` (`SetIdentification`); `AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs` (`PrepareForDisplay`).
- **Status:** in force.

### D-38 Duplicate language ids are repaired and reported

- **Context:** A PNML file from another tool used one arc id twice; both went into AML with the same `Identification/id`, and the next round trip renamed one. Copying an element in the AML Editor produces the same situation.
- **Options:** refuse; keep the invalid ids; renumber later duplicates and say so; delete copies.
- **Decision:** The first element keeps its id, later ones get a number, a note says which. An earlier branch that deleted copies was dead code and removed.
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Conversion/PtIdText.cs` (`MakeIdsDistinct`); `AMLPetriNet: dotnet/PtMapper.Conversion/PtNetUpdater.cs` (`Update`, the `renumbered` map).
- **Status:** in force.

### D-39 One comparison policy for CAEX ids

- **Context:** The P/T reader compared interface ids case-insensitively while link writing and removal compared exactly. A no-op sync then appended extra links, and deleting an arc left dangling ones.
- **Options:** tolerant everywhere; exact everywhere.
- **Decision:** AMLPetriNet compares exactly through one `StringComparer`; a reference differing only in case does not resolve and its arc is kept as unresolved. The id space that allocates new ids stays case-insensitive, so no new id differs from an existing one by case only. fpb-aml-mapper strips braces and compares case-insensitively.
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Conversion/PtCaex.cs` (`Ids`); `AMLPetriNet: dotnet/PtMapper.Conversion/PtIds.cs` (`CaexIdSpace`, its case-insensitive `_taken` set).
- **Status:** differs between projects; one comparer in one place is the rule.

### D-40 Boundary states: one CAEX element per layer, linked and reunified

- **Context:** A boundary state is drawn on the parent layer and in the sub-process and has one id in FPB.JS. The sub-process copy was written with a random id, not found again by update, removed as an orphan and recreated without its position ("copies of states are not saved").
- **Options:** (a) two InternalElements with the same CAEX id (not conformant, rejected); (b) a compound key (dead end, rejected); (c) distinct deterministic ids derived from (shared id, sub-process id), linked with `refObj` typed `refBaseObj`, reunified under the shared id on read, translated back in a pre-pass before update.
- **Decision:** (c).
- **Consequences:** The rest of the update pipeline runs on distinct ids without layer awareness. Removing a state now removes it from every layer.
- **Evidence:** `fpb-aml-mapper: dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs` (`DeriveBoundaryStateId`; `RemapSubProcessBoundaryStateIds`, the pre-pass called from `UpdateInPlace`); `fpb-aml-mapper: dotnet/FpbMapper.Conversion/CaexToFpbJson.cs` (`ParseProcess`, the boundary-state reunification).
- **Status:** in force.

### D-41 Reference values without braces

- **Context:** VDI 3682 rule F1 requires `refObj` to resolve to a `uniqueIdent`, which is brace-free. The mapper wrote braced AML ids in some write paths, and the rule failed.
- **Options:** brace everywhere; brace-free values, brace-insensitive comparison on read.
- **Decision:** Brace-free in every write site; legacy braced values still resolve.
- **Evidence:** `fpb-aml-mapper: dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs` (`StripBraces`, applied to every `refObj` write; `AddProcessImpl`).
- **Status:** in force.

### D-42 Hierarchies carry an id; the plugin binds by id

- **Context:** The plugin bound its hierarchy by name. Renaming it in the editor tree would have made the next Update create a second hierarchy.
- **Options:** bind by name; by position; by an id stamped when the mapper creates the hierarchy, with name as fallback.
- **Decision:** Stamped id, bind by id, follow renames.
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Conversion/PtIds.cs` (`Hierarchy`); `AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs` (`CurrentHierarchy`, `BindHierarchy`).
- **Status:** in force.

---

## 6. Update in place

### D-43 Update in place instead of replacing or appending

- **Context:** The FPD mapper's `ImportInto` appended a new hierarchy on every call, which would fill a document with copies during editing. The first P/T plugin removed the hierarchy and appended a new one on every sync, destroying attributes users added and links into other hierarchies.
- **Options:** regenerate the hierarchy; append; match existing elements by language id and write only what the language owns.
- **Decision:** Update in place from the start. Appending is an explicit plugin decision when no hierarchy is bound. The FPD updater still falls back to appending when it finds no FPD hierarchy.
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Conversion/PtNetUpdater.cs` (class comment on `PtNetUpdater`); `fpb-aml-mapper: dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs` (`UpdateInPlace`, with the fallback to `ImportInto`); `starter/dotnet/Efl.Conversion/EflUpdater.cs` (class comment on `EflUpdater`).
- **Status:** in force.

### D-44 Check every precondition before the first write

- **Context:** An update writes element by element. A class library with a duplicated class threw halfway through; the live editor document was part old and part new, and the next Ctrl+S saved that. A throw between borrowing and returning libraries left them inline.
- **Options:** clone the document, update the clone and swap (safe, one serialise and parse per Update); check everything that can fail first and return borrowed libraries in `finally`.
- **Decision:** Preconditions first (duplicate ids, connection ends, carried classes), loan closed in `finally`, class lookup tolerant of duplicates. Error messages end with "Nothing was written".
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Conversion/PtNetUpdater.cs` (`UpdateInPlace`, `CheckArcEnds`, `CheckCarriedClasses`); `AMLPetriNet: dotnet/PtMapper.Conversion/PtWrite.cs` (`SystemUnitClasses`); `starter/dotnet/Efl.Conversion/EflUpdater.cs` (`UpdateInPlace`).
- **Status:** in force.

### D-45 One predicate for language-owned attributes, checked against every kind

- **Context:** Only attributes the language owns may be written or removed. A per-kind rule excluded arcs from `LabelOffset`; every update removed the attribute and appended it again, moving it to the end of the element.
- **Options:** write attributes by list per element type; one `IsLanguageOwned` predicate plus a per-kind cleanup of language attributes that do not belong.
- **Decision:** One predicate; cleanup touches only names the language claims; `LabelOffset` belongs on arcs.
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Conversion/PtWrite.cs` (`IsLanguageOwned`, `BelongsOn`).
- **Status:** in force.

### D-46 Do not rewrite what nobody changed

- **Context:** After ISO_PT v0.3 made `Identification/name` authoritative, a sync overwrote display names such as `Arc_Idle_to_Start` with `a1`. Setting an empty CAEX name to mark "unnamed" did not work: the engine replaces it with the tag name on insert.
- **Options:** always write the name; write it only when the language name differs from the stored one, with an explicit `created` flag for fresh instances.
- **Decision:** The latter. fpb-aml-mapper overwrites the name whenever the editor's differs from the CAEX name.
- **Consequences:** Measured on a real file afterwards: a sync without edits changed no line.
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Conversion/PtWrite.cs` (`SetDisplayName`).
- **Status:** differs between projects; the AMLPetriNet form recommended.

### D-47 A missing value is not a deletion; a cleared value is

- **Context:** The modeler's PNML importer dropped arc names; one Update renamed every arc of the paper example to its GUID. Separately, clearing a place name in the modeler stored the name `1`.
- **Options:** treat a null incoming arc name as cleared; keep the stored name when the modeler has no way to show or edit it.
- **Decision:** An arc arriving without a name keeps its stored name; a place or transition name the user cleared stays empty (together with D-60).
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Conversion/PtWrite.cs` (`WriteArc`).
- **Status:** in force.

### D-48 Keep what the reader could not show; remove foreign links only where needed

- **Context:** See D-12 for the arc that vanished. Deleting a node left a link from a foreign hierarchy dangling, because link removal looked only inside the net. A link scan by id string also touched other hierarchies' links when ids were ambiguous.
- **Options:** delete everything absent from the incoming model; keep unresolved elements, remove links touching a deleted element anywhere in the document, leave a link alone when its interface id is carried by more than one element, and report every removal outside the container.
- **Decision:** The latter. The unresolved set is read before any interface is removed, and the node ports of an unresolved connection are kept as well: removing them would cut the half of the connection that still exists and make it harder to repair.
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Conversion/PtNetUpdater.cs` (`PtUpdateSummary` with its notes; `Update`, which reads `CaexToPtNet.UnresolvedIn` before removing anything); `AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs` (`UpdateButton_Click`, which reports the notes); `starter/dotnet/Efl.Conversion/CaexToEfl.cs` (`ReadFlowElements`); `starter/dotnet/Efl.Conversion/EflUpdater.cs` (`UpdateInPlace`, `BelongsToUnresolved`, `RemoveLinksTouching`); test `AnUpdateKeepsAFlowTheCanvasCouldNotShow` in `starter/dotnet/Efl.Tests/RoundTripTests.cs`.
- **Status:** in force.

### D-49 Elements before connections

- **Context:** In the FPD updater a flow listed before its source element in the payload failed with "not found in AML"; a sub-process entry before its parent failed likewise.
- **Options:** rely on payload order; two passes.
- **Decision:** Pass 1 processes and elements, pass 2 connections.
- **Evidence:** `fpb-aml-mapper: dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs` (`UpdateInPlace`, the two-pass loop).
- **Status:** in force.

### D-50 Recognise elements a user made by hand, and documents without Identification

- **Context:** A document written against an older library had no Identification; the updater recognised nothing and would have added a second copy of all 13 elements. An element instantiated by hand from v0.3 carries an empty Identification, so "attribute present" said nothing about who wrote it and the label came out empty.
- **Options:** match only by Identification; fall back to the CAEX name for the id; decide "written by the mapper" by a filled id.
- **Decision:** Id falls back to the CAEX name, claimed in the same order as the reader; the label falls back to the CAEX name when the id is empty.
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Conversion/CaexToPtNet.cs` (`PnmlIdOf`, `IdentificationName`, `IdentificationId`).
- **Status:** in force.

### D-51 Create libraries lazily during an update

- **Context:** A sync that only changed an attribute wrote four class libraries into a document that referenced them.
- **Options:** ensure libraries at the start of every update (FPD); only when an element must be instantiated.
- **Decision:** Lazily.
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Conversion/PtNetUpdater.cs` (`Update`, the local function `Classes`).
- **Status:** differs between projects; lazy recommended.

---

## 7. Layout

### D-52 Stored layout for FPD, computed layout for P/T nets when missing

- **Context:** PNML files from other tools and hand-authored AML often carry no layout; every node landed at the origin. For FPD, placement on the system limit carries meaning no layout convention defines.
- **Options:** require stored layout; compute it on import and before display, never moving a node that has bounds (pattern P8).
- **Decision:** P/T nets: arrange missing positions at import and before display, tell the user how many were placed. FPD: layout is stored; for a layout-free document the mapper supplies fallback geometry so that the modeler can import it at all (D-54).
- **Consequences:** An editor session on a layout-free document writes the computed layout into it; that is the one intended difference between an editor round trip and a mapper round trip.
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs` (class comment on `PtLayout`, `ArrangeMissing`); `AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs` (`PrepareForDisplay`).
- **Status:** in force.

### D-53 Layered layout with cycle breaking from marked places

- **Context:** Relaxing longest paths over a cycle laid a ten-node loop out thousands of pixels wide. A later version counted only crossings and reported the paper's extended net as perfect while two arcs ran through four nodes each, drawing the cycle as one row.
- **Options:** longest-path relaxation on the raw graph (superseded); breadth-first columns (used briefly, notes only); DFS cycle breaking, longest path on the acyclic rest, barycentre ordering with neighbour swaps scored by crossings plus arcs through nodes, back edges routed around the drawing.
- **Decision:** The last. Cycle breaking starts at marked places, where a net starts; routed back edges meet named nodes from the side so they do not cross the name drawn below the node.
- **Consequences:** Deterministic output independent of element names. Moving a node in the modeler redraws its arcs straight, which undoes a routed back edge.
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs` (`AssignColumns`, `BackEdges`, `Defects`, `RouteBackEdges`).
- **Status:** in force; earlier variants superseded.

### D-54 A layout-free FPD document gets fallback geometry from the mapper; arrange in one place only

- **Context:** The FPB.JS importer reads the visual entry of every connection unconditionally; a layout-free AML without such entries aborts the whole import and leaves an empty canvas.
- **Options:** have the mapper always emit a visual with at least two waypoints (and write default bounds for a SystemLimit without visual data); let the modeler arrange whatever arrives without layout; both.
- **Decision:** The mapper supplies the geometry. When a link carries no stored waypoints, fpb-aml-mapper emits a straight line from the stored port coordinates, or else from the centres of the two shapes. The fallback points carry the same `original` substructure as port-derived points, because the writer persists them as port coordinates and the next read has to reproduce them, or the echo cycle drifts. A SystemLimit without visual data gets default bounds on write, and so does the SystemLimit of a brand new empty hierarchy. FPB.JS does not arrange missing layout.
- **Consequences:** After the first Update the document holds geometry nobody drew. Invented geometry also stands in the way of a later automatic layout: an import that looks complete is never arranged. Arrange missing layout in exactly one place, either the modeler or the mapper, and never invent geometry in two. If the modeler arranges, the mapper must emit only the layout the document stores; a mapper-only consumer (a CLI, a web converter) then shows such a document without positions until a modeler opens it.
- **Evidence:** `fpb-aml-mapper: dotnet/FpbMapper.Conversion/CaexToFpbJson.cs` (`ParseProcess`, which calls `FallbackWaypoints` when `BuildWaypoints` returns fewer than two points; the comment in `FallbackWaypoints` on the `original` shape); `fpb-aml-mapper: dotnet/FpbMapper.Tests/WaypointFallbackTests.cs` (class comment on `WaypointFallbackTests`, `Convert_WithoutConnectionLayout_EveryConnectionGetsFallbackWaypoints`, `Convert_WithoutAnyLayout_StillEmitsTwoWaypointsPerConnection`); `fpb-aml-mapper: dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs` (`AddElement` and `BuildProcess`, the default SystemLimit bounds; `CreateEmptyFpdInstanceHierarchy`); `FPB.JS: app/fpb/importer/JSONImporter.js` (`buildSystemLimitFlow`, which takes `waypoints` from the visual entry).
- **Status:** in force (fpb-aml-mapper); arranging in exactly one place is the rule.

### D-55 Docking points computed on both sides and stored

- **Context:** PNML carries no docking points, but an AML tool without a layouter should show the canvas picture. Cropping an ellipse against its bounding box puts the stored end point beside the circle for every connection that does not meet it on an axis, and the document disagrees with the canvas.
- **Options:** store only bend points in AML; compute docking points in C# the same way the modeler does and write them as `PortCoordinate`, cropping against the outline the renderer draws.
- **Decision:** The latter. The round-trip comparison counts `PortCoordinate` values as geometry.
- **Consequences:** A new shape needs the renderer outline and the C# geometry changed together.
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Conversion/PtRoundTripCheck.cs` (`DockingPoints`); `starter/dotnet/Efl.Conversion/EflGeometry.cs` (class comment on `EflGeometry`, `DockingPoint`).
- **Status:** in force.

### D-88 The starter routes the flows it places, and its tests look at lines

- **Context:** The starter's layout placed nodes only. In the trial run of the playbook the arranged example drew a back flow exactly on top of the forward flow between the same two nodes and a flow straight through a node; every browser test passed, and the first screenshot showed both.
- **Options:** leave flows straight and document it; route back edges only, as `PtLayout` does; route back edges below the drawing, forward edges that would enter a node above it, and self loops, each in a lane and channel of its own.
- **Decision:** The last, for flows between two nodes the same call placed and without waypoints of their own. Candidates are ordered by position in the drawing, so the result does not depend on list order. Tests check that no segment enters a foreign node and that no two flows share a segment, with sampling arithmetic independent of the clip in the code.
- **Consequences:** A layout-free file opens readable. Flows at nodes that already had positions stay straight, because the gaps around those nodes are not known to be free. Dragging a node in the modeler still redraws its flows straight (D-53). A visual change is not done until a screenshot has been looked at.
- **Evidence:** `starter/dotnet/Efl.Conversion/EflLayout.cs` (`RouteFlows`, `CrossesANode`, `SegmentEntersBox`); `starter/dotnet/Efl.Tests/LayoutTests.cs` (class comment on `LayoutTests`; `AnArrangedCycleHasNoFlowThroughANodeAndNoTwoFlowsOnOneLine`, `AFlowBackAlongTheSameTwoNodesIsNotDrawnOnTopOfTheForwardFlow`, `SeveralBackFlowsGetLanesOfTheirOwn`, `AFlowThatSkipsAColumnDoesNotCrossTheNodeInBetween`, `ASelfLoopGetsBendPointsAndALineWithLength`, `FlowsBetweenNodesThatAlreadyHadPositionsKeepTheirWaypoints`, `ArrangingTheSameModelTwiceGivesTheSameDocument`); `starter/web/tools/screenshot.mjs` (header comment).
- **Status:** in force (starter).

---

## 8. Modeler

### D-56 Minification off

- **Context:** didi reads constructor parameter names when `$inject` is missing. A minified bundle died at boot twice: once because an upstream handler declared no `$inject` (patched), once with `No provider for "l"`, whose cause was not found although all annotations survived minification.
- **Options:** fix every missing annotation and minify; ship unminified.
- **Decision:** Unminified. The bundle is loaded from disk, so size does not matter. The starter follows.
- **Consequences:** Anyone turning minification on must run the bridge test, which reproduces the failure in seconds.
- **Evidence:** `AMLPetriNet: web/build.mjs` (`minify: false` in `shared`, with the comment on both failures); `starter/web/build.mjs` (`minify: false`).
- **Status:** in force.

### D-57 Plain business objects instead of moddle in the starter

- **Context:** FPB.JS mixes moddle business objects and plain objects with a shimmed `$instanceOf`, which breaks supertype checks silently.
- **Options:** moddle with a schema; plain objects with a `type` on the element and all type strings in one file.
- **Decision:** Plain objects; moddle only pays off when the modeler reads and writes an XML format of its own (as the reused Petri net modeler does).
- **Evidence:** `starter/web/src/modeling/ElementFactory.js` (class comment on `ElementFactory`).
- **Status:** in force (starter).

### D-58 Random ids for new canvas elements

- **Context:** diagram-js names new elements `<type>_<counter>` from the same counter on every page load. A file already containing such an id made the element registry refuse the new element: the user dropped a shape and nothing happened.
- **Options:** default factory; random ids with a readable type prefix.
- **Decision:** Random ids.
- **Evidence:** `starter/web/src/modeling/ElementFactory.js` (`ElementFactory`, `newId`).
- **Status:** in force (starter).

### D-59 Change reports are debounced, imports queued, non-edit modes silenced

- **Context:** Imports fire bursts of command stack events; two back-to-back imports interleaved at an `await` and the second one's traffic was reported as edits. Token replay writes markings through the command stack and looked like a user editing every marking.
- **Options:** report every change; debounce 300 ms, serialise imports, suppress reports during replay and refuse `requestExport` then.
- **Decision:** The latter, with the listener registered below the simulator's priority so it sees the restored marking.
- **Evidence:** `AMLPetriNet: web/src/bridge.js` (`CHANGE_DEBOUNCE_MS`, `TOGGLE_SIMULATION_EVENT`, `AFTER_SIMULATOR`; in `connectBridge`: `importing`, `simulating`, `scheduleChange`, `runImport`, `importQueue`, and the `importPNML` and `requestExport` message handlers).
- **Status:** in force.

### D-60 Upstream label behaviour patched from outside

- **Context:** Clearing any label set the text `1` (the default arc weight) for every element type; transition names were drawn inside the 50 px box and broke mid word.
- **Options:** fork; wrap the prototype methods once, guarded against double wrapping.
- **Decision:** The default text applies only to arcs; transition names are drawn below the box, with no change to the model or the PNML.
- **Evidence:** `AMLPetriNet: web/src/index.js` (`postExecuteScopedToArcs` with its `scopedToArcs` guard, `nameTransitionsBelow`).
- **Status:** in force.

---

## 9. Plugin UI and lifecycle

### D-61 No teardown on Unloaded

- **Context:** The editor fires `Loaded`/`Unloaded` on every visual tree reparent, tab switch and panel resize. In AMLFPB.js disposing views there caused an endless rebuild cycle. The P/T plugin shipped with a teardown anyway; its log showed eight re-navigations in one session, each re-importing the document over unsaved edits.
- **Options:** dispose on Unloaded and restore pending edits on Loaded; keep the WebView alive as long as the view and end its life in `DocumentUnLoaded` and `ApplicationClose`.
- **Decision:** No Unloaded handler (AMLPetriNet, starter); AMLFPB.js only detaches the log subscription there.
- **Evidence:** `AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs` (constructor `PetriNetPlugin`, comment "Deliberately no Unloaded handler"); `AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs` (constructor `FpbPlugin`, the `Unloaded` handler); `starter/plugin/Aml.Editor.Plugin.Efl/EflPlugin.xaml.cs` (constructor `EflPlugin`, same comment).
- **Status:** in force.

### D-62 Echo detection by content, not by a time window

- **Context:** AMLFPB.js ignored changes for 3 s after each push. The window swallowed genuine quick edits; it was stamped for buffered pushes too and opened fake windows; the page's `importing` flag reset in `requestAnimationFrame`, which WebView2 pauses in hidden tabs, and stuck.
- **Options:** a time window, widened and patched; the page answers each import with `imported` carrying its own export, and a later `changed` equal to that string is fallout.
- **Decision:** Content baseline. AMLFPB.js keeps the timed window only as a fallback until the baseline arrives or for pages that send none.
- **Consequences:** The export must be deterministic (D-14); the baseline is re-anchored after an Update. A change equal to the baseline also clears the pending edit when the baseline is the document's state, so an undone edit is not written by Update; after restoring unsaved edits into a reloaded page the baseline is those edits, and they stay pending (starter). A page posts `imported` only after a successful import; a failed one posts only an error, because the host drops its pending edits on `imported`.
- **Evidence:** `AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs` (`EchoSuppressionWindow`, `_echoBaseline`; `PushIhToWebView`, which stamps `_lastImportFromHost` only when `ImportJson` posted; `OnDiagramChangedFromJs`); `AMLFPB.js: Aml.Editor.Plugin.FPB/fpbjs-assets/index.html` (`settle` in the `importJSON` branch, run by `requestAnimationFrame` or `setTimeout`); `AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs` (`OnDiagramChanged`); `starter/plugin/Aml.Editor.Plugin.Efl/EflPlugin.xaml.cs` (`OnImported`, `OnChanged`); `starter/web/src/bridge.js` (`runImport`).
- **Status:** in force; the timed window survives in AMLFPB.js only as a fallback.

### D-63 Bridge object in the constructor; ready only from the page; reload after a renderer crash

- **Context:** The editor can call `DocumentLoaded` before `Loaded`; a bridge created in `Loaded` was null and the net was lost while the log said "buffered". A post before the page listener exists is dropped without error. A renderer crash left a white tab with ready still true.
- **Options:** create in `Loaded`, set ready after navigation; create in the constructor, boot in `Loaded`, set ready only on the page's `ready` message, reset it on `NavigationStarting` and `ProcessFailed`, reload after a renderer exit.
- **Decision:** The latter. AMLFPB.js resets ready but asks for a manual refresh instead of reloading.
- **Evidence:** `AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs` (constructor `PetriNetPlugin`, the call to `CreateBridge`); `AMLPetriNet: Aml.Editor.Plugin.PetriNet/Bridge/PtWebView.cs` (`InitAsync`, the `NavigationStarting` and `ProcessFailed` handlers); `AMLFPB.js: Aml.Editor.Plugin.FPB/Bridge/FpbWebView.cs` (`InitAsync`, `_navigationStartingHandler` and `_processFailedHandler`, whose error message asks for a Refresh from AML).
- **Status:** in force.

### D-64 Never raise IsDocumentLoaded from DocumentLoaded

- **Context:** The editor subscribes to `IsDocumentLoaded` and answers by calling `DocumentLoaded` again; 40 calls a second were observed.
- **Options:** raise it to announce the document; declare and never raise.
- **Decision:** Declared, never raised.
- **Evidence:** `AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs` (`DocumentLoaded`, comment "DO NOT invoke IsDocumentLoaded here"); `AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs` (`IsDocumentLoaded`).
- **Status:** in force.

### D-65 Document identity by origin and file name

- **Context:** The editor sometimes hands out a new `CAEXDocument` wrapper for the open file; reattaching on reference inequality reloaded the canvas. AMLFPB.js first deduplicated on `OriginID` alone, which names the authoring tool and is equal across editor-saved files: a second file looked already rebuilt and Update wrote into the wrong document.
- **Options:** reference equality; `OriginID`; `OriginID` plus `FileName`; the full path from `ChangeAMLFilePath`.
- **Decision:** `OriginID` plus `FileName` (AMLPetriNet, starter); two new unsaved documents with an empty `FileName` count as distinct. Two same-named files in different folders still compare equal; the full path is the recommended next step and untested. AMLFPB.js compares `OriginID` plus `FileName` as well, and keys its cache of pending edits by both.
- **Evidence:** `AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs` (`DocumentLoaded`, `IsSameDocument`, `Identity`); `AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs` (`IsAlreadyRebuilt` and its comment; `OriginIdOf`, the key of `_pendingCache`); `starter/plugin/Aml.Editor.Plugin.Efl/EflPlugin.xaml.cs` (`SameFile`, `Identity`).
- **Status:** in force, with a known gap.

### D-66 Commands in the plugin view, not on the editor toolbar

- **Context:** The editor shows the `ToolBarCommands` of one plugin at a time. With both plugins installed the FPD commands appeared inside the Petri net tab; with one plugin New and Import appeared twice.
- **Options:** `IToolBarIntegration`; a toolbar inside the view with Update and Refresh as buttons and occasional commands in one drop-down menu, plus buttons on the empty placeholder.
- **Decision:** In the view, without `IToolBarIntegration` (AMLPetriNet, starter). AMLFPB.js still implements `IToolBarIntegration` and puts New Process and Import FPB.js on the editor toolbar, so with both plugins installed its commands can still show up in the other plugin's tab.
- **Evidence:** `AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs` (constructor `PetriNetPlugin`, `NewNetItem` and `ImportPnmlItem`); `starter/plugin/Aml.Editor.Plugin.Efl/EflPlugin.xaml` (the `ToolBar` and the comment above it); `AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs` (`FpbPlugin` implementing `IToolBarIntegration`, `ToolBarCommands`).
- **Status:** differs between projects; commands in the view recommended.

### D-67 Save after Update as a setting, and a confirmation for large updates

- **Context:** The contract has no "document changed" call, so an update the editor does not save is lost on close; an update the editor saves at once cannot be undone by closing without saving. A stale pending snapshot applied to a reloaded canvas deletes the difference.
- **Options:** always save; never save; a setting. Confirm by comparing element totals (AMLFPB.js) or by counting added and removed ids.
- **Decision:** Saving is a setting; AMLPetriNet and the starter default to on, AMLFPB.js to off. Updates that add or remove more than five ids ask first; moves and renames never ask.
- **Consequences:** Comparing totals misses a snapshot that removes five elements and adds five others. The status line says whether the save ran.
- **Evidence:** `AMLPetriNet: Aml.Editor.Plugin.PetriNet/Diagnostics/PluginSettings.cs` (`SaveAfterUpdate`, `LargeUpdateThreshold`); `AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs` (`LargeUpdateConfirmed`); `AMLFPB.js: Aml.Editor.Plugin.FPB/Diagnostics/PluginSettings.cs` (`AutoSaveAfterUpdate`); `AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs` (`ConfirmLargeChangeIfNeeded`).
- **Status:** differs between projects in the default; id counting recommended.

### D-68 Findings by the name the canvas shows, validation automatic

- **Context:** The findings list showed PNML ids, which are GUIDs for documents from other tools; a Validate button that had to be remembered meant nets were usually shown unchecked.
- **Options:** ids and a manual button; names and validation on load, after Update and while drawing (count only).
- **Decision:** Names, automatic validation; double click selects on the canvas by the canvas id. AMLFPB.js does the same: it validates on every push and after Update, lists findings with the element name, and a double click asks the page to select the element by its brace-free id.
- **Evidence:** `AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs` (`Revalidate`, its local function `NameOf`); `AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs` (`RunVdiValidationIfEnabled`, called from `PushIhToWebView` and `UpdateButton_Click`; `UpdateFindingsUi`, `FindingsGrid_MouseDoubleClick`); `AMLFPB.js: Aml.Editor.Plugin.FPB/Views/FindingRow.cs` (`From`, `LookupName`); `AMLFPB.js: Aml.Editor.Plugin.FPB/Bridge/FpbWebView.cs` (`SelectElement`).
- **Status:** in force.

### D-69 A generation token against rebuild races, not a semaphore

- **Context:** In AMLFPB.js a stale rebuild continuation added ghost tabs (D-07).
- **Options:** a semaphore around the rebuild; a monotonic token claimed after teardown, checked after every await, bumped on unload.
- **Decision:** Token. A semaphore was rejected because a hung WebView2 initialisation would never release it.
- **Evidence:** `AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs` (`_lastFullyRebuilt`, whose comment rejects the semaphore; `_rebuildGeneration` and its comment; `RebuildTabsForDocumentInner`, `DocumentUnLoaded`).
- **Status:** in force (AMLFPB.js); not needed with D-07 (b).

### D-70 Save through the editor's own command by reflection

- **Context:** The plugin contract as first read offered no save call.
- **Options:** write the file directly; reflect on the main view model's save command, trying all candidates and logging drift; `IEditorCommanding` with `EditorCommandBase.SaveCAEXFile`, found later in the contract.
- **Decision:** Reflection, behind a save-after-Update setting, with logging (on by default in AMLPetriNet and the starter, off in AMLFPB.js). `IEditorCommanding` has not been tried in either plugin.
- **Consequences:** The command saves the active document, which may not be the bound one when several are open.
- **Evidence:** `AMLPetriNet: Aml.Editor.Plugin.PetriNet/Bridge/EditorSaver.cs` (header comment, `EditorSaver.TrySaveActiveDocument`).
- **Status:** in force; the commanding path is an open alternative.

---

## 10. Packaging

### D-71 Editor packages pinned exactly; the contract assembly never shipped

- **Context:** Both plugins reflect into editor internals and match enum names by string; a floating version changes that surface without a compile error. A manual copy of the whole build output put `Aml.Editor.Plugin.Contract.dll` into the plugin folder, and the editor ignored the plugin without any message.
- **Options:** floating versions; exact pins with `PrivateAssets` for the contract and `ExcludeAssets=runtime` for engine, API and skins.
- **Decision:** Pin what the plugin reflects against exactly; never ship the contract. The starter's package tests fail when the contract or Aml.Engine is in the package. Both plugins pin `Aml.Editor.Plugin.Contract` and `Aml.Editor.API` exactly, but still let `Aml.Engine` float within major version 4 and `Aml.Skins` within 2, although the comment in AMLFPB.js speaks of exact pins for all editor-side packages.
- **Evidence:** `AMLFPB.js: Aml.Editor.Plugin.FPB/Aml.Editor.Plugin.FPB.csproj` (the `PackageReference` items and the comment above them); `AMLPetriNet: Aml.Editor.Plugin.PetriNet/Aml.Editor.Plugin.PetriNet.csproj` (the same items: `Aml.Editor.Plugin.Contract` with `PrivateAssets`, `Aml.Engine`, `Aml.Editor.API` and `Aml.Skins` with `ExcludeAssets`); `AMLPetriNet: README.md` (section "Plugin", the note on `Aml.Editor.Plugin.Contract.dll`); `starter/plugin/Aml.Editor.Plugin.Efl.Tests/PackageTests.cs` (`ThePackageDoesNotShipTheContractAssembly`, `ThePackageDoesNotShipWhatTheEditorAlreadyLoads`).
- **Status:** in force for the contract; exact pins for engine and skins are proposed in both plugins.

### D-72 Private DLLs and the WebView2 loader packed explicitly

- **Context:** A project reference without `PrivateAssets=all` made the PlugIn Manager fail with "Unable to resolve dependency"; NuGet.Pack silently drops items under `runtimes/win-x64/native/`, so the loader was missing.
- **Options:** nuspec dependencies; explicit `None` items for the mapper DLL, `deps.json`, WebView2 managed DLLs and winmd, and a loader staged under `obj/` and packed by file name.
- **Decision:** Explicit items.
- **Evidence:** `AMLPetriNet: Aml.Editor.Plugin.PetriNet/Aml.Editor.Plugin.PetriNet.csproj` (`PrivateAssets` on `Microsoft.Web.WebView2` and on the `PtMapper.Conversion` project reference; the `None` items for `deps.json`, `PtMapper.Conversion.dll`, the WebView2 DLLs, the winmd and `WebView2Loader.dll`; target `StageWebView2Loader`).
- **Status:** in force.

### D-73 Output path anchored at the project file

- **Context:** `$(SolutionDir)` is empty when the project is built alone; the package landed in a second build tree inside the project folder, and two different artefacts carried one version.
- **Options:** `$(SolutionDir)` (AMLFPB.js); `$(MSBuildThisFileDirectory)`.
- **Decision:** `$(MSBuildThisFileDirectory)`.
- **Evidence:** `AMLPetriNet: Aml.Editor.Plugin.PetriNet/Aml.Editor.Plugin.PetriNet.csproj` (`BaseOutputPath`).
- **Status:** differs between projects; anchored recommended.

### D-74 One self-contained bundle built in the same repository

- **Context:** The FPB.JS library build keeps React external. The plugin vendors React from a CDN build whose `react-dom` carried an absolute import that 404s under the virtual host, patched by an MSBuild target before every build, with an import map in the page.
- **Options:** externals plus vendored files and an import map; one ESM file with every dependency bundled, one CSS file with fonts inlined, `index.html`, and a build error when the bundle is missing.
- **Decision:** One self-contained bundle for new plugins; the web app stages the same files.
- **Evidence:** `AMLFPB.js: Aml.Editor.Plugin.FPB/Aml.Editor.Plugin.FPB.csproj` (target `PatchVendorReactDom`); `AMLFPB.js: Aml.Editor.Plugin.FPB/fpbjs-assets/index.html` (the `importmap` script for `react` and `react-dom`); `AMLPetriNet: Aml.Editor.Plugin.PetriNet/Aml.Editor.Plugin.PetriNet.csproj` (the `None` item that ships `$(PtnJsDistDir)` as content, target `VerifyPtnJsDist`).
- **Status:** differs between projects; self-contained recommended.

### D-75 Display name of identifier characters; one version in csproj and Metadata.xml

- **Context:** A display name with a dot or slash threw on activation and on editor close, because the editor turns it into a WPF name and an XML element name. `Metadata.xml` drifted from the csproj version.
- **Options:** free text; letters, digits and underscore, checked by a test; versions kept equal by hand or by a test.
- **Decision:** Identifier characters; the starter tests both rules on the built package.
- **Evidence:** `AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs` (constructor `PetriNetPlugin`, `DisplayName`); `starter/plugin/Aml.Editor.Plugin.Efl.Tests/PackageTests.cs` (`MetadataAndProjectAgreeOnTheVersion`, `TheDisplayNameIsUsableAsAnXmlAndWpfName`).
- **Status:** in force.

### D-76 A rename script for a new language

- **Context:** A language prefix lives in many places: class and library names, namespaces, project and file names, the plugin asset folder, the WebView2 host name, the settings and log folders, the id namespace, palette type strings. Each missed one either works by accident or fails silently in the editor.
- **Options:** rename by hand following a list; a script that copies the starter and renames prefix spellings with word-boundary rules, leaving the element types for the builder to replace.
- **Decision:** The script `tools/new-language.mjs`.
- **Consequences:** The script also replaces the copied `README.md` with an outline of what the new README needs, because a renamed starter README would describe steps and stores as the new language, and it prints the complete command list that recipe §0 names, so the proofs of the copy are not retyped from memory.
- **Evidence:** `tools/new-language.mjs` (header comment; `renameText`, `renameName`; the README outline written into the copy and the command list printed at the end).
- **Status:** in force.

---

## 11. Web app

### D-77 One process for page and API

- **Context:** The FPD web mapper is a Node reverse proxy serving the page and forwarding `/api/*` to a separate .NET API on another host.
- **Options:** proxy plus API on different origins; one ASP.NET Core app serving page, bundle and API.
- **Decision:** One process; no CORS and no proxy needed, runs on any host with ASP.NET Core, can be put under a path of another site by a reverse proxy.
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Web/Program.cs` (header comment); `fpb-aml-mapper: server.js` (the `/api/:direction` route that proxies to the .NET backend).
- **Status:** differs between projects; one process recommended.

### D-78 A lossless update endpoint

- **Context:** Converting AML to the exchange format and back through `to-aml` yields a document holding the diagram and nothing else. The FPD API offers only `to-aml` and `to-json`.
- **Options:** conversion endpoints only; an `update` endpoint taking the document and the edited diagram and returning the same document updated in place.
- **Decision:** `/api/update` as the main endpoint; the page holds the opened document and sends it back.
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Web/Program.cs` (header comment on `/api/update`, endpoint `/api/update`); `fpb-aml-mapper: dotnet/FpbMapper.Web/Program.cs` (endpoints `/api/to-aml`, `/api/to-json`); `starter/dotnet/Efl.Web/Program.cs` (endpoint `/api/update`).
- **Status:** in force.

### D-79 Forwarded headers and a rate limiter scoped to the API

- **Context:** Behind the hosting front end every client shared one limiter bucket keyed on the front end's address; static files consumed permits; a refused request came back with an empty body.
- **Options:** global limiter on the remote address; forwarded headers before the limiter, limiter only on `/api`, a rejection body.
- **Decision:** The latter.
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Web/Program.cs` (`AddRateLimiter` with `ApiPolicy` and `OnRejected`; `UseForwardedHeaders`; `RequireRateLimiting(ApiPolicy)` on the API endpoints).
- **Status:** in force.

### D-80 One load helper that refuses non-AML text

- **Context:** `CAEXDocument.LoadFromString` returns a document with a null `CAEXFile` for some non-AML input; the NullReferenceException surfaced later, and a web API reported a caller's broken file as a server fault.
- **Options:** check at each call site; one helper that throws a `FormatException`, mapped to 400.
- **Decision:** One helper.
- **Evidence:** `starter/dotnet/Efl.Conversion/EflDocuments.cs` (`EflDocuments.Load`).
- **Status:** in force.

---

## 12. Testing

### D-81 Test parallelisation off wherever Aml.Engine is used

- **Context:** Aml.Engine keeps static caches; parallel document construction gave "Collection was modified" and reassigned ids intermittently.
- **Options:** locks in tests; disable parallelisation per assembly.
- **Decision:** Disabled in every test assembly that builds documents.
- **Evidence:** `fpb-aml-mapper: dotnet/FpbMapper.Tests/TestCollection.cs` and `AMLPetriNet: dotnet/PtMapper.Tests/TestCollection.cs` (assembly attribute `CollectionBehavior(DisableTestParallelization = true)`).
- **Status:** in force.

### D-82 Compare whole documents, against the same file loaded twice

- **Context:** FPD mapper tests checked element counts and missed semantic bugs (wrong container lists, a sub-process pointing at itself) (notes only). Diffing a synced file against the original measured mostly Aml.Engine's reformatting.
- **Options:** counts; property assertions; whole-document comparison against the same file loaded and saved once without changes, with property assertions only to name what broke.
- **Decision:** Whole documents against a second load; an unchanged sync must change no line; an "everything else" aspect fails on any change outside the diagram.
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Tests/PaperRoundTripTests.cs` (class comment on `PaperRoundTripTests`, `AmlToPnmlAndBackLeavesTheDocumentUnchanged`); `AMLPetriNet: docs/roundtrip-validation.md` (sections "AML to PNML to AML" and "4. Formatting"); `AMLPetriNet: dotnet/PtMapper.Conversion/PtRoundTripCheck.cs` (`Compare`, aspect "everything else" built by `Outside`).
- **Status:** in force.

### D-83 Golden snapshots before a breaking change

- **Context:** A planned rework of the FPD JSON and internal model needed a net that catches any structural change.
- **Options:** no snapshot; snapshots with GUIDs canonicalised by first occurrence, a determinism self-check, regeneration only behind `UPDATE_GOLDEN=1`.
- **Decision:** The latter.
- **Evidence:** `fpb-aml-mapper: dotnet/FpbMapper.Tests/GoldenTests.cs` (class comment on `GoldenTests`, `UpdateGolden`, `CanonicalizeGuids`).
- **Status:** in force.

### D-84 Test the bridge and whole sessions in a real browser

- **Context:** The arc name loss (D-47) passed every C# test, because those went PNML to model to PNML in C# only; the host-facing surface of the page was untested.
- **Options:** C# round trips only; a stand-in `window.chrome.webview` installed before page scripts to drive the real protocol, and an editing session through the real modeler followed by the C# comparison.
- **Decision:** Both browser tests in CI.
- **Evidence:** `AMLPetriNet: web/tools/verify-bridge.mjs` (the `addInitScript` stand-in for `window.chrome.webview`); `AMLPetriNet: web/tools/verify-paper-session.mjs` (header comment).
- **Status:** in force.

### D-85 Validate written PNML against the official grammar

- **Context:** Two converters written in one project can agree on something that is not PNML.
- **Options:** trust mutual agreement; validate with Jing against the pnml.org RELAX NG grammar, cross-checked with libxml2, plus nineteen foreign files. The grammar declares `id` with conflicting ID types, so ID checking is switched off (`-i`) and uniqueness is left to validator rule PT01.
- **Decision:** Grammar validation in CI.
- **Evidence:** `AMLPetriNet: web/tools/validate-pnml.mjs` (header comment on the grammar, and the Jing call with `-i`); `AMLPetriNet: docs/pnml-conformance.md` (section "Against the grammar").
- **Status:** in force.

### D-86 Fixtures are committed and must be present; showcase documents are built in the test

- **Context:** Sweep tests over documents on one machine failed on a clean checkout, because a plain `[Theory]` turns a skip into a failure. FPD showcase round-trip tests depended on files from the plugin repository.
- **Options:** commit every file; skippable facts and theories for machine-local files; generate showcase documents inside the test.
- **Decision:** At first AMLPetriNet used `[SkippableTheory]`/`[SkippableFact]` for files that lived only on one machine or in a paper repository (from the project history). Those files were then committed, and a skip kept hiding a broken lookup instead of a missing file. Now every fixture the tests read is committed and found through a marker walk-up, and the tests that read them (`PaperRoundTripTests`, `PaperExampleTests`, `ForeignDocumentSweepTests`) are plain `[Fact]`/`[Theory]`: a committed fixture that is missing fails the run. Showcase documents are built in the test.
- **Consequences:** A clean clone must run the whole suite green without anything outside the repository. A file that is genuinely optional does not belong in the unit test suite; put it behind a separate script that says when it is absent (see [07](07-testing-and-ci.md) §12).
- **Evidence:** `AMLPetriNet: dotnet/PtMapper.Tests/TestFiles.cs` (`TestFiles`, `Paper`, `Library`); `AMLPetriNet: dotnet/PtMapper.Tests/ForeignDocumentSweepTests.cs` (`Documents`, files inside the repository); `AMLPetriNet: dotnet/PtMapper.Tests/PaperRoundTripTests.cs` (`Document`, `ShippedPnml`, `ShippedAml`); `fpb-aml-mapper: dotnet/FpbMapper.Tests/Showcases/ShowcaseRoundTripTests.cs` (`GeneratedDir`, which builds the showcase documents in the test).
- **Status:** in force; the skippable variant is superseded.

### D-89 Example files written with a fixed timestamp and checked in CI

- **Context:** Example AML files are derived from the JSON example by the mapper. Written with the current time they differ on every run, so nothing could tell a stale example from a fresh one, and a changed mapper could ship with old examples.
- **Options:** normalise the timestamp in a golden test; write the examples with a fixed time and let CI write them again and compare with `git diff`.
- **Decision:** `EflToCaex.Convert` takes an optional `writtenAt`, `eflmap to-aml` passes it from `--timestamp`, the examples are written with `2026-01-01T00:00:00Z`, and CI fails when writing them again changes a byte. `*.aml` is marked `-text` so git does not change line endings. The tool prints what it wrote for every command that writes, and a summary for `validate`, because a silent success cannot be told from a run that wrote nothing.
- **Consequences:** After a change to the mapper or the libraries, write the examples again in the same commit. The same holds for a renamed copy, whose ids change with the prefix.
- **Evidence:** `starter/dotnet/Efl.Conversion/EflToCaex.cs` (`Convert`, parameter `writtenAt`); `starter/dotnet/Efl.Tool/Program.cs` (usage comment at the top; `Run`, commands `to-aml`, `validate` and `library`; `Timestamp`); `starter/.github/workflows/ci.yml` (step "Check that the example files are current"); `starter/.gitattributes` (`*.aml -text`); `.github/workflows/playbook.yml` (step "Check that the example files are current").
- **Status:** in force (starter).

---

## Checklist

- [ ] Before removing code that looks redundant, search this file for its keyword and read the evidence.
- [ ] Where the projects differ, take the variant marked "recommended" unless your language gives a reason not to, and write that reason down.
- [ ] For a new language, record your own Phase 1 decisions (encoding per connection type, stored or computed layout, exchange format, references, rule list) in the same shape: context, options, decision, consequences.
- [ ] When you reverse a decision, mark the old entry "superseded by" the new one instead of deleting it.
- [ ] Keep evidence citable: a file with a symbol (class, method, test name, function, config key) or a named section; mark anything else as notes only.

## Where to look

| Area | Files | Symbols |
|---|---|---|
| Update in place, preconditions, ownership | `AMLPetriNet: dotnet/PtMapper.Conversion/PtNetUpdater.cs`, `PtWrite.cs` | `UpdateInPlace`, `Update`, `ReadWiring`; `IsLanguageOwned`, `SetDisplayName`, `WriteArc` |
| Ids | `AMLPetriNet: dotnet/PtMapper.Conversion/PtIds.cs`, `PtIdText.cs`, `PtCaex.cs` | `For`, `CaexIdSpace`; `ToXmlName`, `MakeIdsDistinct`; `Ids`, `PartnerId` |
| Libraries in documents | `AMLPetriNet: dotnet/PtMapper.Conversion/PtLibraries.cs`, `PtLibraryLoan.cs`, `PtDiPaths.cs` | `EnsureLibraries`, `EmbedSharedLibraries`; `PtLibraryLoan`, `AliasInUse`; `PtDiPaths` |
| Reference typing, boundary states | `fpb-aml-mapper: dotnet/FpbMapper.Conversion/MapperOptions.cs`, `FpbJsonToCaex.cs`, `CaexToFpbJson.cs` | `UseObjectReferencesLibrary`; `DeriveBoundaryStateId`, `RemapSubProcessBoundaryStateIds`, `StripBraces`; `ParseProcess` |
| Layout | `AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs` | `ArrangeMissing`, `AssignColumns`, `BackEdges`, `Defects`, `RouteBackEdges` |
| Plugin lifecycle and echo detection | `AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs`; `AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs`, `Views/IhView.xaml.cs` | `PetriNetPlugin` (constructor), `OnDiagramChanged`, `DocumentLoaded`, `IsSameDocument`; `_rebuildGeneration`, `IsAlreadyRebuilt`; `_echoBaseline`, `LiveSyncTick` |
| Modeler patches and bridge | `AMLPetriNet: web/src/index.js`, `web/src/bridge.js`, `web/build.mjs` | `postExecuteScopedToArcs`, `nameTransitionsBelow`; `connectBridge`, `scheduleChange`, `runImport`; `minify: false`, `alias` |
| Packaging | `AMLPetriNet: Aml.Editor.Plugin.PetriNet/Aml.Editor.Plugin.PetriNet.csproj`; `starter/plugin/Aml.Editor.Plugin.Efl.Tests/PackageTests.cs` | `BaseOutputPath`, `StageWebView2Loader`, `VerifyPtnJsDist`; `ThePackageDoesNotShipTheContractAssembly`, `TheDisplayNameIsUsableAsAnXmlAndWpfName` |
| Starter-only choices | `starter/README.md`, `starter/dotnet/Efl.Conversion/EflLibraries.cs`, `starter/dotnet/Efl.Conversion/EflDiagramInterchange.cs`, `starter/web/src/modeling/ElementFactory.js`, `tools/new-language.mjs` | `EnsureLibraries`, `CreateArtefact`; `EnsureIn`, `PathIn`; `ElementFactory`, `newId`; `renameText` |
