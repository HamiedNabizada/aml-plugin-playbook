# Recipe: a plugin for a new language

This is the working order for building a mapper, a modeler, a web app and an AutomationML Editor plugin for a graphical language that is not EFL, FPD or P/T nets. Follow it step by step. Every step names what to change, what the result must be, and the command that proves it. The chapters 01 to 09 explain the reasons; this file tells you when to read which part.

## In short

- Start from a renamed copy of the starter, never from an empty folder (§0). Everything must be green before you touch the language.
- Do the analysis (Phase 1) on paper first and have the person who owns the language confirm it (§1).
- Ask all stop point questions in one round, right after the analysis draft (table below). Guessing any of them costs a migration of documents later.
- The connection encoding follows one question: does a connection have an identity and attributes of its own? No: InternalLink. Yes: reified element. Never both for the same connection type (§3, P6).
- Stored layout is a separate decision from the encoding (§3, P8).
- Work in this order: exchange format, model, libraries, mapper both directions, update in place, validator, layout, modeler, web app, plugin, CI (§4). Each step ends with tests that fail without it.
- Keep every name of the language in one place per side (`XxxNames.cs`, `types.js`).
- The AML document is the master. An update changes what the language owns and nothing else (§4.5).
- Test in the real editor with the protocol in §5 before calling the plugin done.

Read fully when: you start a new language. Skim when: you only change one layer of an existing one.

### Stop points

Collect these into one message to the person after the Phase 1 draft, each with options, consequences and your recommendation. Do not continue past §1 without the answers.

| # | Question | Default recommendation | Explained in |
|---|---|---|---|
| S1 | Prefix for CAEX names and code, and the plugin's display name (final after the first install) | Short, and specific to the organisation or domain if files leave the house | §0, §4.9 |
| S2 | Repository, license, author | The person's choice | §0 |
| S3 | Confirmation of the A1 to A4 worksheet, including rule severities | | §1 |
| S4 | Connection encoding per connection type | From A2: identity and attributes means reified | §1, §3 P6 |
| S5 | Stored or computed layout; stored label positions? | Store, and fill in missing layout, unless the layout carries meaning | §3 P8 |
| S6 | Exchange format | A standard format if one covers semantics and graphics, otherwise own JSON | §1, [03](03-exchange-format.md) |
| S7 | Decomposition, sub-diagrams, elements shown in several diagrams | None unless the language has them | §2 P3, §3 P5 |
| S8 | Embed shared libraries in created documents, or only reference them | Embed OMG_DD always (the starter already does); ObjectReferences only if the language uses references (P3), from the published file | §3 "Shared libraries in documents" |

---

## §0 Set up

**Choose the prefix first (S1).** The script needs it, and every name it writes carries it. If the person has not decided yet, run the script with a provisional prefix only to check the environment, and run it again into a fresh folder once the prefix is decided, before any language change.

A prefix is part of every class path in every document. With a standards body behind the language, qualify the library names with it (`ISO_PT_DomainLibrary`, `VDI_FPD_...`) and keep the class prefix short (`PT_`, `FPD_`). Without one, avoid a prefix other libraries are likely to use (`SM_`, `BP_`): add an organisation or domain qualifier when documents leave the organisation. The script uses one prefix for both; rename the library constants in `<Prefix>Names.cs` by hand if they should differ. See [04 §2.1](04-aml-mapping.md#21-four-libraries-one-prefix-one-names-class).

```bash
node tools/new-language.mjs ../<folder> <Prefix> "<Long name>"
# example
node tools/new-language.mjs ../sm-aml-plugin Sm "State Machine"
```

The script copies `starter/` and renames the language prefix everywhere it lives: CAEX library and class names (`EFL_` to `SM_`), .NET namespaces, projects and types (`Efl` to `Sm`), and the JavaScript side, file names, the plugin asset folder, the WebView2 host name, the settings and log folders, the id namespace (`efl` to `sm`). It does not rename Step, Store and Flow: those are what you replace.

Then, in the new folder, run exactly the commands the script prints. They are:

```bash
cd web && npm install && npm run build && npx playwright install chromium && npm test
cd ../dotnet && dotnet test <Prefix>.sln
dotnet run --project <Prefix>.Tool -- to-aml ../examples/bottling-line.json ../examples/bottling-line.links.aml --timestamp 2026-01-01T00:00:00Z
dotnet run --project <Prefix>.Tool -- to-aml ../examples/bottling-line.json ../examples/bottling-line.elements.aml --style element --timestamp 2026-01-01T00:00:00Z
dotnet run --project <Prefix>.Tool -- library ../examples/<PREFIX>_DomainLibrary_v0.1.0.aml --timestamp 2026-01-01T00:00:00Z
cd ../web && npm run test:webapp
cd ../plugin && dotnet test
```

All must pass, and the .NET build must show no warnings (the starter treats warnings as errors). If something fails here, the environment is the problem (SDK, Node, Playwright browsers, WebView2 packages), not your language. Fix that first; otherwise every later failure is ambiguous.

---

## §1 Phase 1: analyse the language

Fill in this worksheet before writing code. If the language has a formal information model or metamodel, take the answers from it; otherwise derive them from its symbols and rules. Show the filled worksheet to the user and get it confirmed. Most rework in the source projects came from answers that were guessed.

### A1 Element types

| Element type | Abstract? | Specialises | Drawn as | Contains others? |
|---|---|---|---|---|
| | | | | |

Examples: EFL has `Step` and `Store`, disjoint. FPD has product, energy and information specialising a common state, plus process operator, technical resource and system limit. P/T nets have place and transition (the arc is a connection, A2).

### A2 Connection types

Answer per connection type:

| Connection type | Directed? | Own identity and attributes? | Own graphics (label, bends)? | Arity | Legal ends |
|---|---|---|---|---|---|
| | | | | 2 | |

The column "own identity and attributes" is the one that decides the encoding (P6). Ask: does the language's information model give the connection an id, a name, a weight, an inscription, anything that is not just "these two are connected"? Waypoints alone do not count; they are graphics.

Examples: FPD flows have no identity of their own, so they become InternalLinks. P/T arcs carry a weight and an id, so they become reified elements. An n-ary connection can only be reified, because an InternalLink joins exactly two interfaces.

### A3 Structural rules

List every rule a drawing tool could break without noticing. For each: an id (`XX01`), the severity (error: breaks the language; warning: legal but almost certainly unintended), and whether the modeler should prevent it (rules) or only report it (validator).

| Id | Rule | Severity | Enforced in modeler? |
|---|---|---|---|
| | | | |

Examples: P/T nets are bipartite (an arc never joins two places). FPD flows join a state and a process operator, never two states. EFL: a flow joins two different nodes (EFL04). Do not add modelling advice as rules: a rule that fires on a good model teaches people to ignore the findings.

### A4 Information model

| Element type | Attribute | Type | Unit / range | Required? |
|---|---|---|---|---|
| | | | | |

Include identification (id, name) once for all elements; it becomes a shared base attribute (P1). If a formal information model exists (VDI/VDE 3682 Part 2 for the FPD), adopt it rather than inventing names.

Three checks before you show the worksheet:

- **Elements without a name.** Markers and pseudo elements (an initial state dot, a junction) have an identity, because connections attach to them, but often no name. Make the name optional in the shared identification rather than inventing names.
- **Attributes against every legal connection end.** Go through each connection attribute for each combination of ends from A2. "Every transition has a trigger" is usually not true for the transition leaving an initial marker; a rule written from the unchecked sentence fires on correct diagrams.
- **Connections from an element to itself.** Say explicitly whether they are allowed. The starter forbids them (rule EFL04, `canConnect` in `web/src/rules/Rules.js` and its browser test). If your language allows them, all three change, and the layouter needs default bend points for a loop, because a straight line from a node to itself has no length.

### Decisions that come out of Phase 1

These are stop points S3 to S8 from the table at the top. Ask them together with S1 and S2 in one round.

- **STOP: connection encoding per connection type** (from A2, see P6).
- **STOP: stored layout or computed layout** (see P8).
- **STOP: exchange format.** If the language has a standard interchange format that covers semantics and graphics (PNML for Petri nets), use it; otherwise a JSON of your own shaped like the starter's. A standard that covers only the semantics (SCXML for state charts) loses the drawing; offer it as an extra import or export, not as the exchange format. See [03-exchange-format.md](03-exchange-format.md).
- **STOP: containment and cross-diagram references.** Does the language decompose elements into sub-diagrams, or show the same element in several diagrams? Then references use the ObjectReferences attribute types (P3, P5).
- **STOP: rules list** (A3), with severities.

---

## §2 Phase 2: semantic domain model (P1 to P3)

The analysis becomes CAEX class libraries. In the starter they are built in code, in `dotnet/<Prefix>.Conversion/<Prefix>Libraries.cs`, with all names in `<Prefix>Names.cs`.

**P1: role hierarchy with shared base attributes.** Every element type from A1 becomes a RoleClass. Specialisation in the language becomes inheritance between role classes. Attributes every element has (identification) are typed once in the AttributeTypeLib, attached to an abstract base role (`<PREFIX>_Element` in the starter) and inherited. Do not repeat them on each role.

**P2: typed interface pairs for directed connections.** InternalLinks are undirected. Direction comes from typed interface classes: an `Out` and an `In` class per directed connection type (`<PREFIX>_FlowOut`, `<PREFIX>_FlowIn`). An undirected connection type needs one interface class. All derive from one language base interface class. Do not share one In/Out pair between different connection types: the difference would then need an extra attribute to recover.

**P3: typed cross-diagram references.** Decomposition, boundary elements shown in several diagrams, grouping: use the `refObj` family (`refBaseObj`, `refAspectObj`, `refDetailObj`, `refAbstractObj`) from the ObjectReferences AttributeTypeLib, not CAEX nesting. Nesting would state containment the language does not mean, and it merges coordinate spaces that must stay separate. The FPD mapper is the worked example: `fpb-aml-mapper` types decomposition with `refDetailObj`/`refAbstractObj` and boundary states with `refBaseObj`. Use the published library, `AutomationML_ObjectReferences_AttributeTypeLib` 1.1.1-beta, alias `ObjectReferences`. The playbook does not ship it: obtain the file through the library manager of the AutomationML Editor (AMLPetriNet carries the same file as `libraries/AutomationML_ObjectReferences_AttributeTypeLib_AMLEd2_1.1.1-beta.aml`). Never rebuild its types from a description and never redefine them under your own library name: guessed ids and attribute layout give two incompatible libraries of one name in the same document ([09-pitfalls.md](09-pitfalls.md), PF-AML-16). How the library gets into a document is §3, "Shared libraries in documents".

Done when: `dotnet run --project <Prefix>.Tool -- library ../examples/<PREFIX>_DomainLibrary_v0.1.0.aml` writes a library that opens in the AutomationML Editor without errors, and every class path you wrote resolves (the editor shows no red class references).

---

## §3 Phase 3: instantiation conventions (P4 to P8)

**P4: SystemUnitClasses as decoupled templates.** For every instantiable element type, a SystemUnitClass with a SupportedRoleClass pointing at its role. Instances reference the SystemUnitClass. Other templates (a vendor catalogue) can then claim the same role.

**P5: flat diagram structure.** One InternalElement per diagram, its own coordinate space; all language elements are direct children, even when the notation draws containment. Cross-diagram relations use P3 references.

**P6: connection encoding.** Pick per connection type from A2:

| A2 answer | Encoding | Where connection data lives | Starter code |
|---|---|---|---|
| No identity of its own | InternalLink between the `Out` interface of the source and the `In` interface of the target | Waypoints on the source's outgoing interface (`Waypoint_n`); docking points as `PortCoordinate` on both interfaces; the connection id as the link ID | `<Prefix>ConnectionStyle.Link` |
| Identity and attributes | InternalElement per connection with a source and a target interface, two InternalLinks to the node interfaces | Identification, attributes and waypoints on the connection element | `<Prefix>ConnectionStyle.Element` |

The starter implements both only to teach them and to let you test both. **In your language, delete the branch you do not use**, per connection type: in `<Prefix>ToCaex.cs`, `CaexTo<Prefix>.cs`, `<Prefix>Updater.cs`, the `ConnectionStyle` enum, the plugin setting "New diagrams write flows as elements" (`plugin/.../<Prefix>Plugin.xaml(.cs)`, `PluginSettings.cs`) and the `?style=` parameter of the web app. A language whose connection types answer A2 differently uses both encodings side by side, one per type; then the style is a property of the connection type, not a setting.

**Design principle behind P6 to P8.** The AML representation follows the semantics of the language, not its drawing. Graphics are stored where the chosen encoding can hold them; they keep the diagram as drawn but are never needed to reconstruct the model.

**P7: DD-based diagram interchange types.** Layout uses `DD_Bounds`, `DD_Point` and `DD_Waypoint` from the shared `OMG_DD_AttributeTypeLib` v0.1 (alias `OMG_DD`), so two languages in one document describe layout the same way. **The starter already does this; there is nothing to switch.** The published file lies in `libraries/OMG_DD_AttributeTypeLib_v0.1.aml` of the copy, is linked into `<Prefix>.Conversion` as an embedded resource, and `<Prefix>DiagramInterchange` embeds it into every document the mapper creates (`EnsureIn`), makes the library artefact reference it by file name (`ReferenceFrom`), and gives every layout attribute its type path in the form the document uses (`PathOf`, `PathIn`: inline `OMG_DD_AttributeTypeLib/DD_Point`, or `<alias>@OMG_DD_AttributeTypeLib/DD_Point` when the document references the file under any alias). The `library` command of `<Prefix>.Tool` writes the OMG_DD file beside the domain library artefact. Keep `DiagramInterchangeTests`; do not declare layout types in `<Prefix>Libraries.cs` and do not edit the library file. See [04-aml-mapping.md](04-aml-mapping.md) §2.8 and §4 and [08-layout.md](08-layout.md).

**Shared libraries in documents.** A reference alone is not enough for documents people open in the AutomationML Editor: the editor does not follow file references, so a document saved anywhere other than next to the library files shows its layout and reference attributes unresolved. AMLPetriNet therefore embeds the two small shared libraries (OMG_DD and ObjectReferences, a few kilobytes) into every document it creates. Embed OMG_DD always: the starter already does (P7). Embed ObjectReferences only if your language uses references (P3), and only from the published file obtained through the AutomationML Editor's library manager, the same way `<Prefix>DiagramInterchange` handles OMG_DD: add the file as an `EmbeddedResource`, insert it unchanged with an `EnsureIn` that leaves a document alone when it already carries or references the library, write every `refObj` type path in the form the document uses, and add a test that the embedded copy equals the file. Never rebuild its types from a description. AMLPetriNet keeps the AML base libraries as a reference by file name (never a URL with a share token or credentials in it: the path is copied into every document you write), and leaves a document that already chose one form alone (`AMLPetriNet: dotnet/PtMapper.Conversion/PtLibraries.cs` (`EnsureLibraries`, `EmbedSharedLibraries`)). Do the same, and let the class paths follow whichever form the document uses. **STOP: confirm with the user** if documents of your language are only ever processed by tools that resolve references.

**P8: explicit or implicit layout.** Can a deterministic layout algorithm produce an acceptable diagram from the structure alone (layered drawing for a directed graph, tree drawing for a tree)? Then stored layout is optional: keep it when present, compute it when missing (`<Prefix>Layout.ArrangeMissing`). If the arrangement carries meaning the language defines no convention for (FPD states placed on a system limit frame), layout must be stored and a missing layout is a finding. This is independent of P6.

---

## §4 Engineering steps

Each step lists files, the result, and the proof. **4.1 to 4.6 are one compile unit:** replacing the element types in `Models.cs` breaks the writer, reader, updater, validator, layout, tool, web app and plugin at once, so do these steps together and run all their proofs at the end. Their order is a reading order. The same holds for 4.7 inside `web/src`, where `types.js` is used by every module.

### 4.1 Exchange format

Files: `dotnet/<Prefix>.Conversion/<Prefix>Json.cs`, `Models.cs`, `web/src/io/json.js`, `examples/*.json`.

- Replace the node kinds and attributes with A1 and A4. Keep: every element carries its id; layout is in the format; the reader is tolerant and collects warnings; the writer is strict and deterministic, and both sides write keys in the same order.
- If you use a standard format, replace `<Prefix>Json` with a reader/writer for it and validate its output against the standard's grammar in a test, with deliberately broken files that must fail ([03-exchange-format.md](03-exchange-format.md), [07-testing-and-ci.md](07-testing-and-ci.md)).
- Rewrite `examples/bottling-line.json` as a small example of your language (a fork, a join, one connection with bend points, and one instance of every special case the language allows: a connection from an element to itself, parallel connections, an element without a name) and rename it. Update `RoundTripTests.Sample()` to build the same model: `ExampleAndGeometryTests` checks that both are equal, and the browser tests load the file.

Proof: `dotnet test <Prefix>.Tests` (the JSON tests and the example test).

### 4.2 Names and libraries

Files: `<Prefix>Names.cs`, `<Prefix>Libraries.cs`.

- One constant per library, class, interface class and attribute. No string literal for a CAEX name anywhere else.
- Keep `StampClassIds`: class ids derived from the class path. Without it every library write gets new random ids and every document diff is noise.
- Bump `LibraryVersion` on every change of a class path or attribute type.
- Keep `<Prefix>DiagramInterchange` and `libraries/OMG_DD_AttributeTypeLib_v0.1.aml` unchanged. Layout attributes take their type path from `PathIn`/`PathOf`, never from a literal.

Proof: write the library with the tool and open it in the editor.

### 4.3 Model to AML and back

Files: `<Prefix>ToCaex.cs`, `CaexTo<Prefix>.cs`, `<Prefix>Write.cs`, `<Prefix>Geometry.cs`, `<Prefix>Ids.cs`.

- Ids: keep `<Prefix>Ids.For` (SHA-256 based, version and variant bits set on the right bytes) and derive interface and link ids from the owner's CAEX id, not from the model id.
- Always create through `CreateClassInstance` and then set the deterministic ID; insert with `Insert(child, false)`; compare Aml.Engine objects by ID, never by reference ([04-aml-mapping.md](04-aml-mapping.md), trap table).
- Docking points: `<Prefix>Geometry.DockingPoint` must crop against the same outline the web renderer draws (`getShapePath` in `web/src/draw/Renderer.js`). A new shape needs both changed together.
- Reader: tolerant. A connection whose end is missing becomes a warning, not an exception, and its id goes into `<Prefix>Model.Unresolved` so the updater keeps the element instead of taking its absence from the canvas for a deletion.
- Link sides: set them through `AInterface`/`BInterface` when writing, and read both forms, bare `InterfaceId` and `ElementId:InterfaceId` (`CaexTo<Prefix>.InterfaceIdOf`).

Proof: `RoundTripTests` in both directions and for each encoding you keep: model survives AML and back; writing the same model twice changes no line of the document; same model, same ids; links written as `ElementId:InterfaceId` are read too.

### 4.4 Validator

File: `<Prefix>Validator.cs`, `ValidatorTests.cs`.

- One rule id per A3 rule, listed in `RuleIds`. Every finding carries the element id, so the plugin can select the element.
- `ValidatorTests` fails for a rule id without a broken model in its `Broken` table. Add one per rule; each broken model must trip its own rule and no other.

Proof: `dotnet test <Prefix>.Tests`.

### 4.5 Update in place

File: `<Prefix>Updater.cs`.

- Match existing elements by the language id stored in `Identification`, never by name or position.
- Write only attributes the language owns (`<Prefix>Write.IsLanguageOwned`); keep every other attribute, interface and child someone else added.
- Remove interfaces and links of connections that are gone, and only those.
- Check everything that can fail before the first write (`CheckFlowEnds`): an update that fails halfway leaves a document that is part old and part new, and the next save stores it.
- Read the encoding from the document, never from a setting: a hierarchy written as links stays links.

Proof: `AnUpdateKeepsWhatSomebodyElseAddedToTheDocument`, `RemovingANodeRemovesItsFlowsAndNothingElse`, `AnUpdateKeepsAFlowTheCanvasCouldNotShow`, `AFlowToANodeThatIsNotThereIsRefusedBeforeAnythingIsWritten`, adapted to your language. Add one test per kind of foreign content your users are likely to add (attributes on elements, links into another hierarchy, references to your elements).

### 4.6 Layout

File: `<Prefix>Layout.cs`.

- P8 implicit: keep `ArrangeMissing`; tune column and row spacing to your shape sizes; make sure it never moves a node that has a position.
- P8 explicit: keep `ArrangeMissing` for foreign documents, and add a validator warning for missing layout.

Proof: tests in `LayoutTests.cs` adapted to your language: no overlapping nodes, no connection running through an element it does not connect, no two connections on the same straight line, a loop with length. Then look at `npm run screenshot -- --arranged` in `web/`: it removes the example's layout, lets the mapper arrange it, and writes a PNG. A diagram can pass all these tests and still be unusable.

### 4.7 Modeler

Files in `web/src/`: `types.js`, `draw/Renderer.js`, `rules/Rules.js`, `palette/Palette.js`, `context-pad/ContextPad.js`, `label/LabelEditing.js`, `io/json.js`, `icons.js`.

- `types.js`: every type string of the canvas and its mapping to the exchange format, in one place.
- `Renderer.js`: `drawShape`, `drawConnection`, and a `getShapePath` that matches the drawing (cropping uses it). Long names go below the shape, not inside a narrow box.
- `Rules.js`: every A3 rule marked "enforced in modeler". A rule returns `false` to forbid; `undefined` counts as no answer. With no answer diagram-js allows a command (`shape.create`, `elements.move`) but refuses a non-command action: keep the `connection.start` rule, or the palette's connection tool does nothing.
- `ContextPad.js`: one entry per attribute is fine for one or two attributes; more attributes need a properties panel. Every change goes through a command (`UPDATE_PROPERTIES`), never by assigning to the business object.
- Keep the module list in `<Prefix>Modeler.js` unless you know what a module does; a missing module fails silently ([02-modeler-diagram-js.md](02-modeler-diagram-js.md)).
- Do not change `bridge.js` except the payload name; the host side depends on its timing rules.

Proof: `npm run build && npm test` in `web/`. Adapt `tools/verify-modeler.mjs` to your types (the import/export check, the docking check, drawing a connection with the palette tool and the mouse, the rules check). Then look at `npm run screenshot` (the example as stored) and `npm run screenshot -- --arranged`: tests do not see a label drawn in the wrong place.

### 4.8 Web app

Files: `dotnet/<Prefix>.Web/Program.cs`, `wwwroot/index.html`.

- Usually only the connection style parameter and texts change.
- Keep `<Prefix>Documents.Load` for every AML input: it turns non-AML text into a 400 instead of a NullReferenceException.

Proof: `npm run test:webapp` in `web/`.

### 4.9 Plugin

Files: `plugin/Aml.Editor.Plugin.<Prefix>/`.

- `Metadata.xml` and the csproj: description, authors, version. Both versions equal (a test checks).
- `DisplayName` letters, digits and underscore only (a test checks).
- Texts in `<Prefix>Plugin.xaml` (placeholder, menu, tooltips).
- The rest (bridge, update flow, echo baseline, settings, log) is language neutral. Change it only with the reason from [05-editor-plugin.md](05-editor-plugin.md) in hand.

- `THIRD-PARTY-NOTICES.md`: the package redistributes the bundled modeler code and WebView2, whose licenses require their notices. After changing npm dependencies run `npm run notices` in `web/`; `npm test` fails while a runtime dependency is missing from the file, and a package test checks that the file is packed.

Proof: `dotnet test` in `plugin/` (the package tests), then §5.

### 4.10 CI

The copy already has `.github/workflows/ci.yml`, renamed with the rest. It runs on Windows: builds the bundle, runs the .NET tests, the browser tests, the web app test and the plugin package tests, uploads the package, and publishes it as a release asset for a `v*` tag. It also checks that the example files are current: the tool writes them with a fixed `--timestamp`, and a changed byte fails the build. Adjust the branch names if yours differ, and add a step for every new kind of check (a grammar validation for a standard exchange format, for example). Without a remote, run the workflow's commands locally, in Release, as the proof of this step.

---

## §5 Test in the editor

Automated tests cannot load the plugin into the AutomationML Editor. Do this by hand, or ask the user to, before calling the plugin done. Write down the editor version.

1. Build the package: `dotnet build -c Release` in `plugin/`. Install `build/.../Release/Aml.Editor.Plugin.<Prefix>.<version>.nupkg` through the editor's plugin manager. Restart the editor.
2. The plugin appears in the plugin list and its view opens. If not: see "plugin lifecycle and packaging" in [09-pitfalls.md](09-pitfalls.md) (Contract assembly shipped, display name, a same-version package cached).
3. Open a document without a diagram: the placeholder text shows. Diagram menu, New Diagram: a hierarchy appears in the tree, the canvas is empty and editable.
4. Draw two nodes and a connection, rename a node, press Update: the status line reports added elements; the tree shows them; Ctrl+S saves (or the save runs automatically).
5. Close and reopen the file: the diagram comes back unchanged, positions included.
6. In the editor tree, add an attribute to one of your elements by hand. Move a node on the canvas and Update. The hand-added attribute is still there.
7. Delete a node on the canvas, Update: its connections disappear from the document, nothing else does.
8. Import the example JSON: a second hierarchy appears; the diagram picker lists both; switching asks before discarding unsaved edits.
9. Export SVG: the file opens in a browser and shows labels and arrow heads, without black boxes.
10. Break a rule on the canvas: the findings button shows the count; double click selects the element.
11. Switch the editor's theme; undock and redock the plugin view: the canvas keeps its unsaved edits.
12. Open the log folder from the Diagnostics tab: the log has no errors.

---

## §6 Definition of done

- [ ] Phase 1 worksheet filled in and confirmed by the user.
- [ ] README rewritten from the outline the copy script left.
- [ ] No `Example Flow Language` concept left: Step, Store, Flow, Duration, Capacity are gone from code, tests, examples and texts (`grep -ri "step\|store\|duration\|capacity"` finds only intended uses).
- [ ] Only the connection encodings the language needs remain.
- [ ] Layout types still taken from `OMG_DD_AttributeTypeLib` as the starter does, embedded in created documents; `DiagramInterchangeTests` green; no layout types of your own.
- [ ] Cross-diagram references typed with the ObjectReferences family, if the language has any, embedded from the published file (not rebuilt), with a test that the embedded copy equals the file.
- [ ] `dotnet test` in `dotnet/` and `plugin/`, `npm test` and `npm run test:webapp` in `web/` all green, and the same in CI.
- [ ] Every validator rule has a broken model test.
- [ ] Example files written by the tool from the example JSON with `--timestamp`, for each encoding kept, plus the library file and the OMG_DD file beside it; writing them again changes nothing.
- [ ] Editor protocol §5 passed, editor version noted in the README.
- [ ] A LICENSE file and the author in the plugin project and `Metadata.xml`; `THIRD-PARTY-NOTICES.md` current.
- [ ] README of the new repository says what the language is, which decisions were taken in Phase 1 and why, and cites the method.

## §7 Where to look

| Question | Chapter |
|---|---|
| How the parts fit together | [01-architecture.md](01-architecture.md) |
| A diagram-js module, rule, renderer, label or import problem | [02-modeler-diagram-js.md](02-modeler-diagram-js.md) |
| Standard format or own JSON, tolerant reading, conformance | [03-exchange-format.md](03-exchange-format.md) |
| Library design, encodings, ids, update in place, Aml.Engine traps | [04-aml-mapping.md](04-aml-mapping.md) |
| Plugin contract, WebView2, update flow, packaging | [05-editor-plugin.md](05-editor-plugin.md) |
| The mapper as a web app | [06-web-app.md](06-web-app.md) |
| Tests and CI | [07-testing-and-ci.md](07-testing-and-ci.md) |
| Layout, docking, SVG | [08-layout.md](08-layout.md) |
| A symptom you are seeing right now | [09-pitfalls.md](09-pitfalls.md) |
| Why something is the way it is | [decisions.md](decisions.md) |
| A term | [glossary.md](glossary.md) |
