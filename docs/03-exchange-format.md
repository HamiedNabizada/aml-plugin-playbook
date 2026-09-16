# 03 The exchange format between modeler and mapper

## In short

- The modeler speaks only the exchange format; the mapper parses it into a plain model free of CAEX ids and format field names (see §1).
- Use a standard format if one covers semantics and graphics; otherwise design a JSON format with an object root, one writer module, id-string references and a version field. Never ship `JSON.stringify` of live diagram objects (see §2, §10).
- Every element carries id, name and type; attributes keep their structure; bounds, routing, label positions and hierarchy are in the format (see §3).
- The format id is the join key. CAEX ids are derived deterministically from it; interface and link ids from the owner's CAEX id; no random id in a read path (see §4).
- Readers throw only when nothing can be read; everything else becomes a warning. Keep an `Unresolved` set so the updater never deletes what the reader skipped. The starter keeps one too (see §5).
- Parse numbers with the invariant culture and check finiteness; default missing sizes identically on both sides; fallbacks must be deterministic (see §5).
- Writers never emit non-finite numbers or half connections, and omit defaults only when nothing else hangs on them (see §6).
- JS and C# implementations are tested against each other in a real browser, plus a byte-identical browser round trip and a full editing session (see §7).
- Validate against the grammar or a JSON Schema in CI, with negative controls and foreign files (see §8).
- Decide per graphical value what is stored and what is recomputed, name every reference point, round and spell numbers the same on both sides (see §9).
- Before a breaking change, lock behaviour with golden snapshots (see §10); the table in §11 lists the losses actually hit.

Read fully when: you define a new exchange format or write either converter.
Skim when: you keep the starter's JSON format and only add attributes; read §3, §5 and §9.

---

This chapter covers the file or message format that travels between the browser modeler
(diagram-js) and the .NET mapper that reads and writes AML. It covers the choice between a
standard format and your own JSON, what the format has to contain, how ids stay stable,
how readers and writers should behave, how to prove conformance against a grammar, how to
treat graphics and number spelling, versioning, and the data losses the source projects
actually hit. Read it before you write the first line of either converter, and again before
you change the format of a language that already has users. The AML side of the mapping is
in [04 AML mapping](04-aml-mapping.md), the bridge that carries the payload in
[05 Editor plugin](05-editor-plugin.md), and the test harness in
[07 Testing and CI](07-testing-and-ci.md).

The two source lines take opposite routes. AMLPetriNet exchanges **PNML** (ISO/IEC 15909-2),
a standard with a published RELAX NG grammar. AMLFPB.js with fpb-aml-mapper exchanges the
**FPB.JS JSON** export, a custom format that grew out of the modeler's internal objects. Most
of the lessons below come from the friction of the second route and the discipline of the
first.

---

## 1. Put a neutral format between modeler and AML

```
 diagram-js modeler  --(exchange format: PNML / JSON)-->  mapper (.NET)  -->  CAEX / AML
                     <--                              --                  <--
```

**Do:** let the modeler speak only the exchange format, and let the mapper convert that
format into a plain in-memory model before it touches CAEX. Never let the web side produce
CAEX, and never let CAEX ids or CAEX attribute names leak into the plain model.

**Why:**

- *Decoupling.* The modeler can be replaced, upgraded or tested without Aml.Engine, and the
  mapper can be tested without a browser. AMLPetriNet swapped the upstream PNML converter
  of the modeler for its own implementation without touching the mapper; the build aliases
  the package name onto the local module (`AMLPetriNet: web/src/pnml/index.js:18-19`).
- *Testability.* Every direction becomes a pure function you can call from xUnit or from a
  command line tool. AMLPetriNet ships `ptmap to-aml`, `to-pnml`, `roundtrip`, `compare`,
  `fixtures` (`AMLPetriNet: dotnet/PtMapper.Tool/Program.cs:5-15`), which is what CI and
  the paper artefacts run on.
- *A second consumer for free.* When the format is a standard, the same bytes open in other
  tools. AMLPetriNet checked 19 foreign PNML files through both converters
  (`AMLPetriNet: docs/pnml-conformance.md:57-89`).

The plain model is the contract between the two converters. The starter states the rule
explicitly:

```csharp
/// Keep this model free of anything that belongs to one of the two formats.
/// The moment a CAEX id or a JSON field name appears here, the two converters
/// stop being independent and a change on one side breaks the other.
```
(`starter/dotnet/Efl.Conversion/Models.cs:7-9`)

AMLPetriNet keeps one convention explicit at that boundary: the plain model stores bounds
top-left anchored, like DC::Bounds and diagram-js, and the PNML centre convention is
converted only inside the PNML reader and writer
(`AMLPetriNet: dotnet/PtMapper.Conversion/Models/PtModels.cs:6-14`).

---

## 2. Choose: standard format or custom JSON

**Use the standard format when one exists** and covers your language's semantics plus
graphics. PNML covers P/T nets, node positions, dimensions, label offsets and arc bend
points, and offers `<toolspecific>` for the rest. That gave AMLPetriNet a grammar to
validate against, foreign files to test against, and interoperability with other tools.

**Use your own JSON when no standard exists** (FPB/VDI 3682 has none that a modeler can
emit), but design it deliberately, not as a dump of the modeler's objects. The starter
documents the decision in its JSON class:

```csharp
/// EFL uses a format of its own because it has no standard one. A language that
/// has a standard format should use it instead, as AMLPetriNet does with PNML:
/// the same bytes then travel to any other tool. The rules below hold either
/// way.
```
(`starter/dotnet/Efl.Conversion/EflJson.cs:10-13`)

**Do not accept an off-the-shelf converter without checking what it drops.** AMLPetriNet
replaced the upstream `pnml-moddle-converter` because it wrote no `<dimension>` and no arc
`<graphics>`, so every bend point was lost, and because it needed Node builtins that do not
bundle for a browser (`AMLPetriNet: web/src/pnml/index.js:4-16`).

**Anti-pattern observed in FPB.JS: the format is whatever `JSON.stringify` makes of live
objects.** FPB.JS has no JSON writer. The export is `JSON.stringify` over the live
business-object graph with a replacer that turns known reference properties into ids and
drops a denylist of diagram-js internals
(`FPB.JS: app/fpb/layer-panel/components/DownloadModal.js:225-283`). The facade's
`toJSON()` returns the live objects without that replacer
(`FPB.JS: src/index.js:167-170`), and those objects are cyclic, so stringifying them
directly fails. The plugin therefore carries its own copy of the replacer
(`AMLFPB.js: Aml.Editor.Plugin.FPB/fpbjs-assets/index.html:182-228`). Consequences:

- Two copies of the writer that must be kept in sync by hand.
- Any new reference property that is not in the replacer's list is serialized as a nested
  object, or throws on a cycle. The mapper had to learn to accept both forms for `parent`
  and `isDecomposedProcessOperator`, because treating the object form as missing "silently
  flattened every decomposition hierarchy that came out of the running editor"
  (`fpb-aml-mapper: dotnet/FpbMapper.Conversion/Models/FpbModels.cs:381-404`).
- The root is a heterogeneous array `[Project, Process, Process, ...]`, which a typed
  deserializer cannot bind; the mapper parses it by hand
  (`fpb-aml-mapper: dotnet/FpbMapper.Conversion/Models/FpbModels.cs:6-10`, `136-165`).

If you design a JSON format: a single object at the root, an explicit writer function on
the modeler side (one place, unit tested), references always as id strings, typed
sub-objects with an explicit type tag only where the reader needs it, and a version field.

---

## 3. Required content

Whatever the syntax, the format must carry everything the AML side stores and everything the
canvas needs to look the same after a round trip.

| Content | Why it is required | Source evidence |
|---|---|---|
| **Id on every element** (nodes, connections, the diagram/net itself, sub-diagrams) | Without it an update cannot match elements and turns into delete plus create, which breaks every reference a user added in AML | `AMLPetriNet: dotnet/PtMapper.Conversion/PtIds.cs:7-16`; `starter/dotnet/Efl.Conversion/EflJson.cs:30-31` |
| **Name** on every element, connections included | The PNML importer of the modeler once dropped arc names; one Update renamed every arc of the paper example to its GUID | `AMLPetriNet: web/src/pnml/index.js:696-704`; `AMLPetriNet: docs/roundtrip-validation.md:135-141` |
| **Type** of every element, as a closed set | The reader must decide what to build and must report what it does not know | `starter/dotnet/Efl.Conversion/EflJson.cs:96-101` |
| **Typed attributes** with their real structure (numbers as numbers, value plus unit as an object, lists as arrays) | FPB characteristics were modelled as strings and silently lost (section 11) | `fpb-aml-mapper: dotnet/FpbMapper.Conversion/Models/FpbModels.cs:81-104` |
| **Connection endpoints** as ids | Resolution must be possible without geometry | `AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:156-195` |
| **Node bounds** (position and size) | Missing size must be defaulted, not zero (section 5) | `AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:265-282` |
| **Connection routing** (bend points, and decide about endpoints, section 9) | Lost bend points are the classic silent layout loss | `AMLPetriNet: web/src/pnml/index.js:9-12` |
| **Label positions** with a defined reference point | Otherwise labels drift every session (section 9) | `AMLPetriNet: dotnet/PtMapper.Conversion/Models/PtModels.cs:55-64` |
| **Hierarchy / layers** (which sub-diagram belongs to which parent element) with explicit references in both directions or a documented derivation | FPB decompositions read flat when only one direction was present | `fpb-aml-mapper: dotnet/FpbMapper.Conversion/CaexToFpbJson.cs:83-100`; `FPB.JS: app/fpb/importer/JSONImporter.js:109-119` |
| **Tool-specific extras** that the standard lacks, in the extension mechanism | PNML has no silent transition; it travels in `<toolspecific>` that foreign readers ignore | `AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:334-340`; `AMLPetriNet: dotnet/PtMapper.Conversion/Models/PtModels.cs:34-39` |
| **Multiple diagrams per file**, if the format allows it | Reading only the first `<net>` drops the rest without a word | `AMLPetriNet: dotnet/PtMapper.Tests/HostileInputTests.cs:343-362` |

For hierarchies, decide early whether an element that appears on two layers (FPB boundary
states, a sub-process and its parent operator) shares one id or has its own id plus a
reference. FPB.JS shares ids across layers; the golden tests of the mapper have to
canonicalize GUIDs partly because "the JSON process id aliases its parent PO id", a v1 quirk
(`fpb-aml-mapper: dotnet/FpbMapper.Tests/GoldenTests.cs:34-38`). The planned JSON v2 moves
to two-level identity: an `id` per representation and a stable object identifier (the
`uniqueIdent`) per real-world object, with a typed reference between representations. If
you start fresh, start with that.

---

## 4. Stable ids across round trips

**Rules:**

1. **The format id is the join key.** The mapper matches incoming elements against existing
   CAEX elements by the id stored in the element (for example `Identification/id`), never by
   name and never by position.
2. **Derive CAEX ids deterministically** from the format id when you create elements, so the
   same input yields the same document. AMLPetriNet hashes a namespace, a kind discriminator
   and the parts with SHA-256 and stamps UUID version 5 bits:

   ```csharp
   public static string For(string kind, params string[] parts)
   {
       var seed = Namespace + Separator + kind + Separator + string.Join(Separator, parts);
       var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
       var bytes = new byte[16];
       Array.Copy(hash, bytes, 16);
       bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
       bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
       return new Guid(bytes).ToString("B");
   }
   ```
   (`AMLPetriNet: dotnet/PtMapper.Conversion/PtIds.cs:30-47`; the starter does the same at
   `starter/dotnet/Efl.Conversion/EflIds.cs:30-46`, tested by
   `TheSameModelAlwaysProducesTheSameIds` at `starter/dotnet/Efl.Tests/RoundTripTests.cs:217`).
   Derive interface and link ids from the owning element's **CAEX** id, not from the format id:
   seeding them from the format id alone gave duplicate xs:IDs when the same file was imported
   twice, and deleting an arc in one copy removed the other copy's links
   (`AMLPetriNet: dotnet/PtMapper.Conversion/PtIds.cs:51-62`; starter
   `starter/dotnet/Efl.Conversion/EflIds.cs:52-61`).
3. **Never mint a random id in a read path.** fpb-aml-mapper falls back to
   `Guid.NewGuid()` when a CAEX InternalLink has no ID
   (`fpb-aml-mapper: dotnet/FpbMapper.Conversion/CaexToFpbJson.cs:951-957`), and its
   `Convert` mints fresh GUIDs for process elements and flow interfaces, which is why its
   golden tests must fold GUIDs to placeholders
   (`fpb-aml-mapper: dotnet/FpbMapper.Tests/GoldenTests.cs:34-39`, `63-78`). It works in
   the plugin only because the plugin uses update in place, which keeps existing ids. Two
   reads of the same unchanged document should produce byte-identical exchange payloads.
4. **Normalize ids the format cannot hold, deterministically and reversibly in effect.** CAEX
   ids are braced GUIDs; PNML ids are XML names, and the modeler's parser accepts only an
   ASCII name pattern and refuses the whole file over one umlaut
   (`AMLPetriNet: dotnet/PtMapper.Conversion/PtIdText.cs:24-41`). AMLPetriNet rewrites
   such ids for the canvas, leaves the document's ids untouched, and matches them back with
   `SameAfterNormalisation` (`AMLPetriNet: dotnet/PtMapper.Conversion/PtIdText.cs:99-105`;
   `AMLPetriNet: docs/roundtrip-validation.md:73-91`). fpb-aml-mapper strips braces for the
   JSON and must never let a braced id leak, or every later update lookup misses
   (`fpb-aml-mapper: dotnet/FpbMapper.Conversion/CaexToFpbJson.cs:944-950`;
   test `fpb-aml-mapper: dotnet/FpbMapper.Tests/ConversionTests.cs:348-363`).
5. **Make duplicate ids distinct before writing anything, and say so.** A jbpt file carried
   one arc id twice; both went into AML with the same `Identification/id` and the next round
   trip renamed one of them. The fix numbers later duplicates and adds a warning
   (`AMLPetriNet: dotnet/PtMapper.Conversion/PtIdText.cs:161-212`, called at
   `AMLPetriNet: dotnet/PtMapper.Conversion/PtNetToCaex.cs:90-91`).
6. **Validate ids as a rule, not only in the parser.** Rule PT01 checks uniqueness (the
   grammar cannot, section 8) and PT11 reports ids the modeler would refuse
   (`AMLPetriNet: dotnet/PtMapper.Conversion/PtValidator.cs:64`, `79`;
   tests `AMLPetriNet: dotnet/PtMapper.Tests/HostileInputTests.cs:230-301`).

Test: element ids of the input are all present after format to AML to format
(`fpb-aml-mapper: dotnet/FpbMapper.Tests/ConversionTests.cs:328-346`).

---

## 5. Tolerant readers: collect warnings, do not throw

A reader of the exchange format sees files from other tools, from older versions, from hand
edits and from a modeler that just crashed half way. **Throw only when there is nothing to
read** (not well-formed, wrong root, empty input). For everything else, repair or skip, and
record a warning in words a user can act on.

The model carries the warnings, and a set of ids the reader saw but could not represent:

```csharp
/// What a reader had to leave out, in words. Empty for a clean file. A
/// reader that drops something without saying so leaves the next writer
/// to delete it from the document, which is how an arc with one missing
/// link once vanished from a file on the next Update.
public List<string> Warnings { get; } = new();

/// Ids of elements the reader saw but could not represent, in the id form
/// the net uses. An updater must not treat these as "no longer in the
/// diagram": they never reached the diagram in the first place.
public HashSet<string> Unresolved { get; } = new(StringComparer.Ordinal);
```
(`AMLPetriNet: dotnet/PtMapper.Conversion/Models/PtModels.cs:79-92`)

The **`Unresolved` set is the important half.** The updater reads it before removing anything
(`AMLPetriNet: dotnet/PtMapper.Conversion/PtNetUpdater.cs:198-203`), so an arc whose link
was deleted in the AML Editor is reported, not shown, and still survives the next sync
(`AMLPetriNet: dotnet/PtMapper.Tests/RegressionTests.cs:66-87`). A tolerant reader without
this set turns every skipped element into a deletion.

The starter follows the same pattern. Its model carries `Warnings` and `Unresolved`
(`starter/dotnet/Efl.Conversion/Models.cs:22-34`). `CaexToEfl` leaves out a reified flow element
whose links do not resolve, says so and adds its id to the set
(`starter/dotnet/Efl.Conversion/CaexToEfl.cs:106-116`). The updater reads that set from the
document before its first write (`starter/dotnet/Efl.Conversion/EflUpdater.cs:67-69`,
through `CaexToEfl.UnresolvedIn` at `starter/dotnet/Efl.Conversion/CaexToEfl.cs:35`), because the
model coming back from the canvas cannot know what the canvas never showed. The removal loop keeps
such an element and adds a note asking the user to repair or delete it, and the port loop keeps the
node ports of such a flow, so the half of it that still exists stays attached
(`starter/dotnet/Efl.Conversion/EflUpdater.cs:157-164`, `:111-117`, `:174-175`; test
`AnUpdateKeepsAFlowTheCanvasCouldNotShow` at `starter/dotnet/Efl.Tests/RoundTripTests.cs:165`).
Keep this when you add element types that can fail to resolve.

Cases every reader must handle, with the source behaviour:

| Input | Behaviour | Where |
|---|---|---|
| Non-finite numbers (`NaN`, `Infinity`, overflow) | Parse, check `double.IsFinite` / `Number.isFinite`, fall back to 0. NaN "would travel into the AML document and into the diagram, where they render as nothing at all" | `AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:297-307`; `AMLPetriNet: web/src/pnml/index.js:98-101`; the starter treats a non-finite value as missing on both sides (`starter/dotnet/Efl.Conversion/EflJson.cs:217-224`, `starter/web/src/io/json.js:149-153`) |
| Culture | Always parse with the invariant culture. `fpb-aml-mapper: dotnet/FpbMapper.Conversion/Models/FpbModels.cs:411` parses a string number with `double.TryParse(string, out d)`, which uses the current culture; on a German Windows "1.5" does not mean one and a half | as cited |
| Missing or non-positive size | Fall back to the modeler's default size for that type, and do it identically in both readers or the same file opens with different sizes | `AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:271-277`; `AMLPetriNet: web/src/pnml/index.js:487-499`; `starter/dotnet/Efl.Conversion/EflJson.cs:234-237` and `starter/web/src/io/json.js:68-69` with `starter/web/src/types.js:16` (both 100 x 60) |
| Missing position | Leave bounds empty and let a layout step place the element, reported to the user | `AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:863-876` (see [08 Layout](08-layout.md)) |
| Non-numeric or out-of-range attribute values | Default and clamp (`lots` as marking gives 0, `-5` gives 0, weight `heavy` gives 1) | `AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:133-135`, `165-167`; `AMLPetriNet: dotnet/PtMapper.Tests/HostileInputTests.cs:52-73` |
| Unknown element type | Skip with a warning naming id and type | `starter/dotnet/Efl.Conversion/EflJson.cs:96-101`; `starter/web/src/io/json.js:49-52` |
| Duplicate id | Keep the first, skip later ones with a warning (reader); make distinct with a warning (writer, see §4) | `starter/dotnet/Efl.Conversion/EflJson.cs:90-94`; `starter/web/src/io/json.js:48`, `82` |
| Missing id | Skip with a warning | `AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:178-182` |
| Dangling connection end | Skip the connection, warn, add to `Unresolved` | `AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:184-192`; `AMLPetriNet: dotnet/PtMapper.Conversion/CaexToPtNet.cs:125-133` |
| Indirection (PNML reference nodes, multi-page nets) | Resolve chains to the concrete node with a visited set against cycles; flatten pages iteratively so deep nesting cannot overflow the stack | `AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:200-239` |
| A reference that arrives as a string or as an embedded object | Accept both, take the `id` | `fpb-aml-mapper: dotnet/FpbMapper.Conversion/Models/FpbModels.cs:381-404` |
| A scalar that arrives as string or number | Read either as text | `fpb-aml-mapper: dotnet/FpbMapper.Conversion/Models/FpbModels.cs:356-369` |
| Container lists naming elements that have no data | Strip and warn once with a count; test that a clean file produces no such warning | `fpb-aml-mapper: dotnet/FpbMapper.Conversion/CaexToFpbJson.cs:565-593`; `fpb-aml-mapper: dotnet/FpbMapper.Tests/ConversionTests.cs:1239-1263` |
| Hierarchy reference present in one direction only | Derive the other direction, warn | `fpb-aml-mapper: dotnet/FpbMapper.Conversion/CaexToFpbJson.cs:83-100`; `FPB.JS: app/fpb/importer/JSONImporter.js:109-119` |
| XML with a DTD | Refuse DTDs outright (`DtdProcessing.Prohibit`, `XmlResolver = null`): closes entity expansion and external entities | `AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:74-85`; tests `AMLPetriNet: dotnet/PtMapper.Tests/HostileInputTests.cs:88-118` |
| Bytes from disk or network | Read from the stream so the XML declaration's encoding is honoured | `AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:52-64` |
| A file that is the wrong format | Say what was found: root element and namespace | `AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:92-100`; for JSON, `starter/dotnet/Efl.Conversion/EflJson.cs:55-66` |
| AML text that is not AML | `CAEXDocument.LoadFromString` can return a document with a null `CAEXFile`; check and throw a `FormatException` | `starter/dotnet/Efl.Conversion/EflDocuments.cs:10-36` |

**The modeler's importer needs the same tolerance.** FPB.JS's importer used to abort the whole
import on one id without data; it now skips it with a warning
(`FPB.JS: app/fpb/importer/JSONImporter.js:300-305`), drops connections with an unresolved
end and undoes the half-wired side (`FPB.JS: app/fpb/importer/JSONImporter.js:470-514`),
draws a straight line for connections without waypoints
(`FPB.JS: app/fpb/importer/JSONImporter.js:516-527`), and degrades a dangling
`decomposedView` to a non-decomposed operator instead of leaving a string that crashes later
consumers (`FPB.JS: app/fpb/importer/JSONImporter.js:714-723`). It also works on a deep copy,
because it consumes the input arrays while building
(`FPB.JS: app/fpb/importer/JSONImporter.js:180-193`), and every import replaces the model
rather than appending to it (`FPB.JS: app/fpb/importer/JSONImporter.js:40-43`).

Parts of that importer are still strict, and the mapper has to compensate:

- `validateProcessData` rejects a process without `elementVisualInformation`
  (`FPB.JS: app/fpb/importer/ImportUtils.js:235-257`), and the importer dereferenced the
  visual entry of every connection. Layout-free AML therefore produced an empty canvas until
  the mapper was changed to always emit a visual entry with at least two waypoints
  (`fpb-aml-mapper: dotnet/FpbMapper.Conversion/CaexToFpbJson.cs:492-501`;
  `fpb-aml-mapper: dotnet/FpbMapper.Tests/WaypointFallbackTests.cs:7-14`).
- `buildCharacteristics` dereferences `ch.category.$type`, `ch.descriptiveElement.setpointValue.$type`
  and `ch.relationalElement.$type` without guards
  (`FPB.JS: app/fpb/importer/JSONImporter.js:584-611`). The mapper must emit every one of
  those sub-objects with its type tag, even when empty
  (`fpb-aml-mapper: dotnet/FpbMapper.Conversion/CaexToFpbJson.cs:682-686`, `735-743`).

Lesson: a strict reader on one side forces the writer on the other side to encode the
reader's crash conditions. Fix the reader instead, and keep a test on the writer side that
pins the contract.

**Fallbacks must be deterministic.** FPB.JS positions an element without visual data at a
random offset (`FPB.JS: app/fpb/importer/ImportUtils.js:150-159`). A random fallback makes
two imports of the same file differ, which defeats echo detection
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:233-239`) and golden tests.

**Surface warnings.** The plugin logs the reader's warnings and feeds them to the validation
view (`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:490-493`); the FPB
plugin logs `ConversionResult.Warnings` per hierarchy
(`AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs:223`). For conversions that return
a value, carry the warnings with it (`fpb-aml-mapper: dotnet/FpbMapper.Conversion/ConversionResult.cs:3-19`).

---

## 6. Strict writers

A writer only writes what the format allows and what the reader on the other side will read
back identically.

- **Never write a non-finite number.** Clamp at the last moment
  (`AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:454-463`;
  `AMLPetriNet: web/src/pnml/index.js:209-214`).
- **Never write an empty or half connection.** The web exporter refuses an arc without both
  ends and logs it; writing `source=""` would make the .NET reader drop it and the next
  update delete the element (`AMLPetriNet: web/src/pnml/index.js:354-364`).
- **Respect the grammar's value types**, not only its structure (section 9 for sizes).
- **Leave defaults implicit only if nothing else hangs on them.** PNML writers omit marking 0
  and weight 1. But the weight annotation also carries the arc label's offset; dropping it
  for weight 1 lost the position of labels that other editors had placed
  (`AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:352-357`;
  `AMLPetriNet: docs/pnml-conformance.md:97-102`).
- **Keep machine payloads unindented when the format has mixed content.** PNML annotations
  are mixed content, so the bridge payload is written without indentation; indentation is
  only for files a person reads (`AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:311-316`).
- **Keep the converter's own DI ids out of the user's id space.** When the web converter builds the
  modeler's own XML, it picks a prefix for diagram-interchange ids that no id in the file
  starts with (`AMLPetriNet: web/src/pnml/index.js:587-595`).
- **Put everything non-standard in the extension point**, tagged with tool name and version
  (`AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:26-27`, `334-340`).

---

## 7. Two implementations must agree: test them against each other

In this architecture the format has two implementations, JavaScript in the modeler and C# in
the mapper. Each file carries a comment naming its counterpart
(`AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:10-11`), and a cross test loads the
mapper's output into the real modeler in Chromium, exports it again and compares:

```js
const [actual, normalizedExpected] = await page.evaluate(async (pnml) => {
  const canonical = (xml) =>
    new XMLSerializer().serializeToString(new DOMParser().parseFromString(xml, 'application/xml'));

  await window.ptn.importPNML(pnml);
  return [canonical(await window.ptn.savePNML()), canonical(pnml)];
}, expected);
```
(`AMLPetriNet: web/tools/verify-interop.mjs:62-68`)

Both sides are canonicalized by the same XML engine because the two serializers disagree on
whitespace before `/>`; that is formatting, not content
(`AMLPetriNet: web/tools/verify-interop.mjs:51-56`). The fixture set (empty net, net without
layout, silent transition, markup characters in ids and names, moved labels with bend
points) comes from `ptmap fixtures`
(`AMLPetriNet: dotnet/PtMapper.Tool/Program.cs:150-157`). A browser-only round trip checks
that export, import, export is byte identical (`AMLPetriNet: web/tools/roundtrip.mjs:1-6`).

Also run a whole editing session through the real modeler, not only the converters. The arc
name loss in the modeler's PNML importer was invisible to every C# test and found only by
`verify-paper-session.mjs` (`AMLPetriNet: docs/roundtrip-validation.md:135-141`).

For a custom JSON format, the equivalent is a golden snapshot of the round trip with a
determinism self-check (convert twice, assert identical), sorted keys, and an explicit
opt-in to update the golden (`fpb-aml-mapper: dotnet/FpbMapper.Tests/GoldenTests.cs:9-17`,
`82-95`, `97-105`).

---

## 8. Conformance against the grammar, with negative controls and foreign files

Two converters written in one project can agree on something that is not the standard.
Check them against the published grammar.

**Setup in AMLPetriNet** (`AMLPetriNet: web/tools/validate-pnml.mjs`):

- The RELAX NG grammar files for P/T nets from pnml.org and Jing (the reference validator)
  plus Xerces are downloaded once into a cache (`validate-pnml.mjs:29-34`, `56-62`).
- The grammar's absolute include URLs are rewritten to the local copies
  (`validate-pnml.mjs:57-58`).
- The grammar declares `id` with conflicting ID types, which Jing refuses as a schema error;
  `-i` switches ID checking off, and uniqueness is left to validator rule PT01
  (`validate-pnml.mjs:15-21`, `70-71`).
- Results were cross-checked with libxml2, which agreed on every file
  (`AMLPetriNet: docs/pnml-conformance.md:13-17`).
- CI runs the check on the fixtures, the example and the paper net on every push
  (`AMLPetriNet: .github/workflows/ci.yml:59-63`).

**Negative controls.** A validator that says "valid" to everything proves nothing. Break
copies of a valid file on purpose and confirm each is rejected: marking `-1`, marking `viel`,
inscription `0`, a wrong net type URI, an unknown element on the page
(`AMLPetriNet: docs/pnml-conformance.md:44-55`). These were run by hand; put them in CI as a
test that expects the validator to fail.

**Foreign files.** Nineteen PNML files from public repositories of other tools, found by code
search, went through both converters in both directions
(`AMLPetriNet: docs/pnml-conformance.md:57-89`). Do not copy them into your repository when
their licences differ; record the source path instead. Define "semantically equal" before you
compare (same nodes with names and markings, same arcs with weights, references resolved).
Findings from that sweep:

1. Weight-1 inscriptions carrying a label offset were dropped (section 6).
2. A duplicate id went into AML twice (section 4).
3. Every PNML the mapper wrote from these files, invalid inputs included, is valid.
4. Three pre-standard files without the PNML namespace are refused, and the refusal message
   names the namespace (`AMLPetriNet: docs/pnml-conformance.md:119-124`).

**Know what the grammar does not check** and cover it with semantic rules: id uniqueness,
bipartite connections, and anything your language requires beyond structure
(`AMLPetriNet: docs/pnml-conformance.md:131-139`). Also record documented losses there
(colours, fonts, line styles are read past and not written back): "the grammar allows
writing less".

**Some findings come from reading the grammar, not from files.** The `<dimension>` value
constraint in section 9 was found that way (`AMLPetriNet: docs/pnml-conformance.md:108-117`).

For a custom JSON format, write a JSON Schema for it anyway and run the same three checks:
your own output validates, deliberately broken copies fail, and hand-written or older files
are either valid or rejected with a useful message.

---

## 9. Graphics semantics: store or recompute, reference points, rounding

Decide and document, for each graphical value, **what it means and whether it is stored**.
Both implementations must implement the same decision. AMLPetriNet writes the conventions at
the top of both converters (`AMLPetriNet: web/src/pnml/index.js:21-33`;
`AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:13-18`):

- Node `<position>` is the **centre**; `<dimension>` is width and height. The plain model and
  AML use top-left bounds; convert at the format boundary.
- An arc stores **only the intermediate bend points**. The two docking endpoints are
  recomputed from node geometry, because writing them "would show up as stray bends in other
  tools" (`AMLPetriNet: web/src/pnml/index.js:388-400`).
- Label `<offset>` on a node is relative to the node **centre**; label width and height are
  not stored, the renderer derives them from the text.

**Recomputed values must be recomputed on import, for the right shape.** The modeler does not
run its docking layout on import, so arcs anchored at node centres would draw the arrow head
inside the shape. The importer crops endpoints to the circle or rectangle itself
(`AMLPetriNet: web/src/pnml/index.js:521-546`, `722-728`). The FPB side stores the complete
waypoint list including endpoints and an `original` point
(`fpb-aml-mapper: dotnet/FpbMapper.Conversion/Models/FpbModels.cs:113-129`), and the mapper
synthesizes centre-to-centre points when AML has none; those are written back as port
coordinates so the next read reproduces them and echo cycles stay idempotent
(`fpb-aml-mapper: dotnet/FpbMapper.Conversion/CaexToFpbJson.cs:837-878`). The FPB.JS importer
also has to mirror imported waypoints into the DI object, or an untouched connection had a DI
edge without waypoints (`FPB.JS: app/fpb/importer/JSONImporter.js:529-542`).

The starter follows the PNML decision for its JSON: a flow stores bend points only
(`starter/dotnet/Efl.Conversion/Models.cs:69-70`, `starter/web/src/io/json.js:131-138`); the
modeler recomputes the ends through its layouter on import (`starter/web/src/io/json.js:91-100`),
and the mapper computes the same ends when it writes port coordinates into AML, cropping a Store
against its ellipse and a Step against its box, because the outline has to be the one the
renderer's `getShapePath` uses (`starter/dotnet/Efl.Conversion/EflGeometry.cs:29-69`; test
`AFlowEndsOnTheCircleOfAStoreNotOnItsBox` at `starter/dotnet/Efl.Tests/ExampleAndGeometryTests.cs:56`).
Bounds are top-left anchored on both sides, so there is no centre conversion.

**Every relative position needs a named reference point, the same one on both sides.** An
arc label is measured from the middle of the polyline's middle segment, copied from the
modeler's own `getWaypointsMid`, because that is where the modeler puts a label when a file
brings none (`AMLPetriNet: web/src/pnml/index.js:177-198`). PNML does not fix this point for
arcs, so another tool may measure elsewhere; write it into your library description
(`AMLPetriNet: docs/roundtrip-validation.md:178-188`).

**Mind the anchor of the box you create.** diagram-js treats label bounds x/y as the top-left
corner. Writing a 0x0 box at the intended centre put the centre on the top edge, and every
session sank the label by another half height. The fix creates a box of the modeler's default
label size centred on the point (`AMLPetriNet: web/src/pnml/index.js:660-677`).

**Rounding and number spelling:**

- Coordinates: at most two decimals, rounded away from zero on both sides, no exponent.
  JavaScript `String()` prints float remainders such as `5.551115123125783e-17` in exponent
  notation, which is not an `xs:decimal` (`AMLPetriNet: web/src/pnml/index.js:200-214`).
- Sizes: the PNML grammar types `<dimension>` as a positive decimal with at most four digits
  and one after the point, so `50.25` and `12000` are both invalid
  (`AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:433-444`;
  `AMLPetriNet: web/src/pnml/index.js:216-227`).
- **The centre follows the rounded size**, so reading back restores the top-left corner and
  only an off-grid size moves (`AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:415-431`).
- Integers without a trailing `.0`, identically in C# and JavaScript, or a diagram appears
  changed every time it crosses between them
  (`AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:446-463`).
- Test it in the real browser with a deliberately odd resize and a float-remainder move
  (`AMLPetriNet: web/tools/verify-numbers.mjs:20-54`).
- The starter is looser here. The modeler rounds to two decimals with `Math.round`
  (`starter/web/src/io/json.js:161`), the mapper's geometry uses `Math.Round(value, 2)`, which
  rounds halves to even (`starter/dotnet/Efl.Conversion/EflGeometry.cs:72`), and `EflJson.Write`
  writes numbers unrounded (`starter/dotnet/Efl.Conversion/EflJson.cs:165-174`). The plugin is not
  affected, because its echo baseline is the page's own export, but a byte comparison between a
  mapper-written file and a modeler-written file can differ in the last digit. Align both sides
  if your tests or users compare such files.

Why spelling matters beyond validity: the plugin compares the modeler's next change against
the modeler's own export right after import (the echo baseline) and ignores an identical
payload as import fallout (`AMLPetriNet: Aml.Editor.Plugin.PetriNet/Bridge/PtWebView.cs:43-48`;
`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:233-239`). Any
nondeterminism in spelling or fallback positions turns into a phantom unsaved edit.

**Layout that is not stored.** If the format carries no layout (a hand-written AML, a PNML
without graphics), arrange on import and tell the user. Once the user syncs, the arranged
layout is written into the document; that is expected and should be documented as the one
place where an editor round trip differs from a mapper round trip
(`AMLPetriNet: docs/roundtrip-validation.md:127-176`). See [08 Layout](08-layout.md).

---

## 10. Versioning the format

- **Standard formats version themselves by URI.** PNML carries the grammar version in its
  namespace and the net type URI (`AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:22-24`).
  Your extensions carry tool name and tool version in `<toolspecific>`
  (`AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:26-27`). Readers should check the
  namespace and report a mismatch in words (`Pnml.cs:92-100`).
- **A custom JSON needs a version field from the first release.** The FPB.JS JSON has none:
  the importer checks only for a project definition with `name` and `targetNamespace`
  (`FPB.JS: app/fpb/importer/ImportUtils.js:212-230`). A planned breaking v2 (object root,
  flat element lists, structured characteristics, own ids for boundary states and
  sub-processes) therefore has to tell v1 from v2 by shape, and its v1 to v2 migrator is
  still open.
- **The starter shows the minimal version field.** Both writers put `"formatVersion": 1` first,
  so a file written on either side diffs cleanly against the other
  (`starter/dotnet/Efl.Conversion/EflJson.cs:15-25`, `:198-203`;
  `starter/web/src/io/json.js:15-16`, `:114`). Both readers treat a missing field as version 1,
  because such a file predates the field, and read a newer version as far as they can with a
  warning that anything a newer version added is not read and would be lost on writing
  (`starter/dotnet/Efl.Conversion/EflJson.cs:74-78`, `starter/web/src/io/json.js:27-32`). Tests:
  `AFileFromANewerFormatVersionIsReadWithAWarning` and `AFileWithoutAFormatVersionIsReadAsVersionOne`
  (`starter/dotnet/Efl.Tests/ExampleAndGeometryTests.cs:39`, `:49`), and the browser check
  "a file from a newer format version is shown with a warning"
  (`starter/web/tools/verify-modeler.mjs:37-42`). Raise the number only when an older reader
  would lose something, and keep the reader able to read every older version
  (`starter/dotnet/Efl.Conversion/EflJson.cs:45-49`).
- **Readers accept older versions; writers write the current one.** Keep a migration step in
  the reader, not scattered checks. The mapper tests that a document stamped with an older
  library version still converts
  (`fpb-aml-mapper: dotnet/FpbMapper.Tests/ConversionTests.cs:1786-1808`).
- **Before a breaking change, lock the current behaviour.** fpb-aml-mapper added the golden
  snapshots as a safety net specifically for the v2 rework
  (`fpb-aml-mapper: dotnet/FpbMapper.Tests/GoldenTests.cs:9-17`). Do the same on the modeler
  side: extract the writer into one tested module first, then change it.

---

## 11. Data loss found in practice, and how it was found

| Loss | Cause | How it was found | Fix and guard |
|---|---|---|---|
| FPB characteristics: setpoint value, validity limits and actual values vanished on JSON to AML (a setpoint of 20 °C arrived as an empty string) | The mapper modelled them as `string`; the helper that reads strings returns `""` for objects and arrays (`fpb-aml-mapper: dotnet/FpbMapper.Conversion/Models/FpbModels.cs:349-354`). The reverse path wrote flat untyped values, which the FPB.JS importer skips or crashes on | A structured review of the internal model before the JSON v2 redesign flagged it; it was then confirmed empirically by pushing a model with a known setpoint through the mapper and inspecting the CAEX attribute | Structured types end to end (`FpbModels.cs:81-104`, `281-310`), `$type` tags on the way back (`CaexToFpbJson.cs:682-772`), regression tests `JsonToAml_PreservesCharacteristicValues` and `Roundtrip_PreservesCharacteristicSetpointAndType` (`fpb-aml-mapper: dotnet/FpbMapper.Tests/ConversionTests.cs:166`, `196`); commit `f5b508e` |
| FPB decomposition hierarchy read flat from the running editor | Live export serialized `parent` and `isDecomposedProcessOperator` as objects; the reader expected strings | Files from the editor behaved differently from test fixtures | Accept both forms (`fpb-aml-mapper: dotnet/FpbMapper.Conversion/Models/FpbModels.cs:381-404`). Guard: use real editor exports as fixtures, not only hand-written JSON |
| Empty canvas for layout-free AML | FPB.JS importer requires a visual entry per connection | Opening hand-authored engineering AML | Mapper always emits two waypoints (`WaypointFallbackTests.cs`) |
| Every arc renamed to its GUID after one Update | The modeler's PNML importer dropped arc names | Only a real-browser session test showed it; C# tests passed | `AMLPetriNet: web/src/pnml/index.js:696-704`; `docs/roundtrip-validation.md:135-141` |
| Arc label position lost for weight 1 | Default-omission removed the annotation that held the offset | Foreign PNML (Freiburg process editor) | `AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:352-357`, test `AnArcWithTheDefaultWeightKeepsAPlacedLabel` (`AMLPetriNet: dotnet/PtMapper.Tests/IdentityAndSessionTests.cs:268`) |
| Duplicate element after round trip | Invalid foreign file with a duplicate id | Foreign PNML (jbpt) | `PtIdText.MakeIdsDistinct`, test `APnmlFileThatUsesAnIdTwiceComesBackUnchangedFromAml` (`AMLPetriNet: dotnet/PtMapper.Tests/IdentityAndSessionTests.cs:80`) |
| Invalid PNML after a resize | Sizes with two decimals, exponent notation for float remainders | Reading the grammar | Rounding rules of section 9, test `ANodeSizeOffTheGridIsWrittenAsPnmlAllowsIt` (`AMLPetriNet: dotnet/PtMapper.Tests/IdentityAndSessionTests.cs:344`), `verify-numbers.mjs` |
| An arc vanished from the document on the next Update | Reader skipped an arc with a missing link silently; updater treated absence as deletion | Editing a link in the AML Editor | `Warnings` plus `Unresolved` (section 5), `AMLPetriNet: dotnet/PtMapper.Tests/RegressionTests.cs:66-87` |
| Labels sank with every session | 0x0 label box placed at the intended centre | Visual inspection across sessions | `AMLPetriNet: web/src/pnml/index.js:660-677` |
| Every bend point lost | Upstream PNML converter wrote no arc graphics | Reading the converter before adopting it | Own converter (`AMLPetriNet: web/src/pnml/index.js:4-16`) |
| FPB.JS XML path: tandem relations lost, file content ignored | The XML mapper keeps the JSON of its last export and, when it has one, returns that JSON on import and only updates graphics from the file (`FPB.JS: app/fpb/xml/XMLMapper.js:41-42`, `54-55`, `70-95`); the facade shares one instance for export and import (`FPB.JS: src/index.js:121`, `162-175`) | A measurement through the library facade: an exported XML with renamed elements imported back with the old names; the real parser path dropped all 12 `inTandemWith` relations of the test model | Open. Lesson: a "perfect round trip" that replays cached input hides how little the parser carries. Round-trip tests must use a fresh reader instance and a modified file |

Patterns behind these findings:

1. **Silent defaults hide loss.** `GetStringProp` returning `""` for a wrong kind, a reader
   returning null for an unexpected shape, a skipped element without a warning. Make a kind
   mismatch a warning.
2. **Fixtures written by hand agree with the reader by construction.** Use files the real
   modeler wrote, files other tools wrote, and files with every optional feature present.
3. **Round-trip equality needs a semantic definition per aspect**, reported per aspect so a
   failure names itself. AMLPetriNet checks ids, names, attribute values, connection
   direction, cross-hierarchy links, external references, libraries and "everything else"
   separately, and compares against the same document loaded a second time so the engine's
   own reformatting cancels out (`AMLPetriNet: docs/roundtrip-validation.md:35-53`,
   `148-160`).
4. **Test the direction users take.** A round trip through the converters is not a round trip
   through the editor.

---

## Checklist

- [ ] The modeler only speaks the exchange format; the mapper converts it into a plain model
      that contains no CAEX ids and no format field names.
- [ ] Standard format used if one exists; any off-the-shelf converter was checked for dropped
      graphics before adoption.
- [ ] If custom JSON: object root, one explicit writer module on the modeler side, references
      as id strings, a `version` field, a JSON Schema.
- [ ] Every element (connections and sub-diagrams included) has id, name, type; attributes
      keep their structure and value types.
- [ ] Bounds, routing, label positions and hierarchy are in the format, each with a written
      definition of its reference point and of what is stored versus recomputed.
- [ ] Tool-specific concepts live in the format's extension mechanism with tool name and
      version.
- [ ] CAEX ids are derived deterministically; interface and link ids are seeded from the
      owner's CAEX id; no random id in any read path.
- [ ] Ids the format cannot hold are normalized deterministically for display, and matched
      back without overwriting the document's ids.
- [ ] Duplicate ids are made distinct before writing, with a warning; uniqueness and id syntax
      are validator rules.
- [ ] Readers throw only when nothing can be read; everything else becomes a warning in user
      words.
- [ ] Readers fill an `Unresolved` set, and the updater never deletes what is in it (the starter
      does, see §5).
- [ ] Numbers parsed with the invariant culture and checked for finiteness on both sides.
- [ ] Missing sizes fall back to the same per-type default in both readers.
- [ ] Unknown types, missing ids, dangling references and one-directional hierarchy references
      are handled and reported.
- [ ] XML readers prohibit DTDs and read from streams; wrong-format errors name what was found.
- [ ] The modeler's importer is equally tolerant, works on a copy, replaces instead of
      appending, and uses deterministic fallback positions.
- [ ] Writers never emit non-finite numbers, half connections, or values outside the grammar's
      value types; defaults are omitted only when nothing else hangs on them.
- [ ] Coordinates and sizes are rounded and spelled identically in C# and JavaScript; the
      centre follows the rounded size.
- [ ] Cross-implementation test: mapper output loaded into the real modeler and exported again
      compares equal after canonicalization.
- [ ] Browser round trip (export, import, export) is byte identical.
- [ ] A full editing session runs through the real modeler in CI, not only the converters.
- [ ] Grammar or schema validation runs in CI on fixtures and examples.
- [ ] Negative controls (deliberately broken files) are in CI and expected to fail.
- [ ] Foreign files from other tools were run through both directions; results and refusals
      are documented, with their sources.
- [ ] Golden snapshots with a determinism self-check lock behaviour before any breaking change.
- [ ] Readers accept older format versions; a migration step exists before a breaking change
      ships.
- [ ] Documented losses (what the format or the converter does not carry) are written down.

---

## Where to look

| Topic | File |
|---|---|
| PNML reader and writer (.NET), conventions, tolerant reading, rounding | `AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs` |
| Plain model with `Warnings` and `Unresolved` | `AMLPetriNet: dotnet/PtMapper.Conversion/Models/PtModels.cs` |
| PNML converter in the modeler (JS), docking, label boxes, number spelling | `AMLPetriNet: web/src/pnml/index.js` |
| Deterministic ids | `AMLPetriNet: dotnet/PtMapper.Conversion/PtIds.cs` |
| Id normalization and duplicate handling | `AMLPetriNet: dotnet/PtMapper.Conversion/PtIdText.cs` |
| Updater honouring unresolved elements | `AMLPetriNet: dotnet/PtMapper.Conversion/PtNetUpdater.cs` |
| Grammar validation with Jing | `AMLPetriNet: web/tools/validate-pnml.mjs` |
| Cross-implementation check in Chromium | `AMLPetriNet: web/tools/verify-interop.mjs` |
| Number format check in Chromium | `AMLPetriNet: web/tools/verify-numbers.mjs` |
| Browser round trip | `AMLPetriNet: web/tools/roundtrip.mjs` |
| Conformance record: grammar, negative controls, foreign files | `AMLPetriNet: docs/pnml-conformance.md` |
| Round-trip record: per-aspect checks, remaining differences | `AMLPetriNet: docs/roundtrip-validation.md` |
| Hostile and malformed input tests | `AMLPetriNet: dotnet/PtMapper.Tests/HostileInputTests.cs` |
| Command line front end, fixtures | `AMLPetriNet: dotnet/PtMapper.Tool/Program.cs` |
| CI including grammar check | `AMLPetriNet: .github/workflows/ci.yml` |
| FPB JSON models and parser | `fpb-aml-mapper: dotnet/FpbMapper.Conversion/Models/FpbModels.cs` |
| AML to FPB JSON, fallbacks, `$type` emission, dangling strip | `fpb-aml-mapper: dotnet/FpbMapper.Conversion/CaexToFpbJson.cs` |
| Result with warnings | `fpb-aml-mapper: dotnet/FpbMapper.Conversion/ConversionResult.cs` |
| Golden snapshots with GUID canonicalization | `fpb-aml-mapper: dotnet/FpbMapper.Tests/GoldenTests.cs` |
| Characteristics, id stability, schema migration, validation tests | `fpb-aml-mapper: dotnet/FpbMapper.Tests/ConversionTests.cs` |
| Layout-free AML fallback tests | `fpb-aml-mapper: dotnet/FpbMapper.Tests/WaypointFallbackTests.cs` |
| FPB.JS JSON importer | `FPB.JS: app/fpb/importer/JSONImporter.js` |
| FPB.JS import validation and fallbacks | `FPB.JS: app/fpb/importer/ImportUtils.js` |
| FPB.JS JSON export replacer | `FPB.JS: app/fpb/layer-panel/components/DownloadModal.js` |
| FPB.JS facade import/export API | `FPB.JS: src/index.js` |
| FPB.JS XML mapper (cached round trip) | `FPB.JS: app/fpb/xml/XMLMapper.js` |
| Plugin copy of the export replacer | `AMLFPB.js: Aml.Editor.Plugin.FPB/fpbjs-assets/index.html` |
| Starter JSON format, tolerant reader, strict writer (.NET) | `starter/dotnet/Efl.Conversion/EflJson.cs` |
| Starter JSON import and export (JS), same key order as the C# writer | `starter/web/src/io/json.js` |
| Starter plain model with `Warnings` and `Unresolved` | `starter/dotnet/Efl.Conversion/Models.cs` |
| Starter deterministic ids | `starter/dotnet/Efl.Conversion/EflIds.cs` |
| Starter docking points for stored end points | `starter/dotnet/Efl.Conversion/EflGeometry.cs` |
| Starter AML loading that refuses non-AML text | `starter/dotnet/Efl.Conversion/EflDocuments.cs` |
| Starter example model shared by .NET and browser tests | `starter/examples/bottling-line.json` |
