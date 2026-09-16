# Testing and continuous integration

**In short**

- Keep conversion, update, validation and the round-trip check in a UI-free library; almost the whole test pyramid then runs without the AML Editor (see §1, §10).
- Compare complete models and serialised XML, never counts; a sync without edits must change no line, and a second update must be a no-op (see §2).
- Update-in-place tests assert what must *not* change: foreign attributes, interfaces, links, elements and hierarchies survive; refused updates leave the XML identical (see §3).
- Test id determinism on the rendered UUID string, across copies, re-creation and repeated updates (see §4).
- Golden files: `CallerFilePath`, regenerate only behind `UPDATE_GOLDEN=1`, fail the run that writes, normalise only what is not yours (see §5).
- One broken model per validator rule, with the rule list taken from the validator itself; every "nothing happened" assertion needs a guard that something could have happened (see §6).
- Browser tests run against the built bundle and drive diagram-js through its services; the WebView2 bridge is tested with a stand-in host (see §7).
- Browser tests do not see a misplaced label or overprinted flows: look at the starter's screenshots of the stored and the arranged example, and test layout on the lines (see §1).
- Validate written files against the standard grammar and keep negative controls (see §8).
- `[assembly: CollectionBehavior(DisableTestParallelization = true)]` in every test project that touches Aml.Engine (see §11).
- Test data is found by walking up to a repository marker, never via the current directory or a sibling repository; skips hide failures (see §12).
- CI on `windows-latest` for a plugin: bundle, .NET build, tests, browser checks, grammar check, package, release on tag; the starter also writes its example files again with a fixed timestamp and fails on `git diff` (see §13).

Read fully when: you set up the test projects and CI of a new plugin, or before the first converter. Skim when: you only add a rule or a fixture (read §6 or §12) or debug a flaky test (read §11).

This chapter describes how to test a modeler, an exchange format, an AML mapper and an AML Editor plugin so that a user's document never loses content during a sync, and how to run all of it on GitHub Actions. Read it before writing the first converter: the round-trip and update-in-place tests described here are the specification of the mapper, and it is much cheaper to write them first than to retrofit them after the first corrupted file. The evidence comes from AMLPetriNet (xunit plus Playwright scripts plus a grammar check in CI), fpb-aml-mapper and AMLFPB.js (golden files, echo-cycle tests, validator parity), and FPB.JS (Vitest and Playwright end-to-end tests of a diagram-js modeler). Background on the mapping itself is in [AML mapping](04-aml-mapping.md), on the plugin in [Editor plugin](05-editor-plugin.md), and the recurring traps are collected in [Pitfalls](09-pitfalls.md).

## 1. The test pyramid for a modeler plus mapper plus plugin

Organise the tests by what they can see. Each layer catches defects the layer below cannot.

| Layer | Runs in | What it proves | Source example |
|---|---|---|---|
| Format reader and writer | xunit / Vitest | the exchange format (PNML, JSON) reads and writes stably; defaults are restored | AMLPetriNet: dotnet/PtMapper.Tests/RoundTripTests.cs:73 |
| Mapper round trip | xunit, in memory | model to CAEX and back is the same model, in every connection encoding | starter/dotnet/Efl.Tests/RoundTripTests.cs:50 |
| Identity | xunit | CAEX ids are deterministic, well-formed and collision-free | AMLPetriNet: dotnet/PtMapper.Tests/IdentityTests.cs:54 |
| Update in place | xunit | foreign content survives, deletions are local, preconditions refuse before writing | AMLPetriNet: dotnet/PtMapper.Tests/UpdateInPlaceTests.cs:41 |
| Library golden | xunit | the classes the mapper creates in code equal the published library artefact | AMLPetriNet: dotnet/PtMapper.Tests/PtLibraryGoldenTests.cs:62 |
| Validator rules | xunit | each rule fires on a violation and stays silent on a clean model | AMLPetriNet: dotnet/PtMapper.Tests/ValidatorTests.cs:147 |
| Real documents | xunit | documents written by other tools read, sync without change, and settle | AMLPetriNet: dotnet/PtMapper.Tests/ForeignDocumentSweepTests.cs:57 |
| Hostile input and scale | xunit | entity expansion, malformed ids, 2000 elements | AMLPetriNet: dotnet/PtMapper.Tests/HostileInputTests.cs:89, dotnet/PtMapper.Tests/ScaleTests.cs:53 |
| Modeler in a browser | Playwright | the bundled modeler's own importer and exporter keep everything; the host bridge protocol behaves | AMLPetriNet: web/tools/roundtrip.mjs:22, web/tools/verify-bridge.mjs:32 |
| Cross-implementation interop | Playwright | files the C# mapper wrote come back unchanged from the JS modeler | AMLPetriNet: web/tools/verify-interop.mjs:63 |
| Standard conformance | Jing (Java) | the files both converters write are valid against the published grammar | AMLPetriNet: web/tools/validate-pnml.mjs:70 |
| Editing session | Playwright plus CLI | a real session through the real modeler changes only what the user changed | AMLPetriNet: web/tools/verify-paper-session.mjs:84 |
| Modeler UI end to end | Playwright test runner | palette, context pad, layers, confirmation dialogs | FPB.JS: tests/e2e/pages/ModelerPage.js:360 |

Two lessons shaped this pyramid:

- A C#-only round trip is not enough. In AMLPetriNet the modeler's own PNML importer dropped every arc name, and a single "Update InstanceHierarchy" renamed every arc of the paper document to its GUID. No .NET test could see it because the net never passed through JavaScript (AMLPetriNet: docs/roundtrip-validation.md:135). The browser layer exists for that reason.
- Two converters written by the same team, tested only against each other, can agree on something that is not the standard (AMLPetriNet: docs/pnml-conformance.md:3). The grammar layer exists for that reason.

The pure logic lives in a library project without UI (`PtMapper.Conversion`, `FpbMapper.Conversion`, `starter/dotnet/Efl.Conversion/`), so almost the whole pyramid runs without the AML Editor. Keep it that way; see section 10.

The starter already carries a small version of this pyramid; copy it and extend it rather than starting empty:

| Starter test | Layer | What it checks |
|---|---|---|
| `starter/dotnet/Efl.Tests/RoundTripTests.cs` | round trip, update in place, identity | both connection encodings, no-op sync, foreign content, refused updates, stable ids, a flow element with unresolved links kept by an update together with the port on the node that still exists (`AnUpdateKeepsAFlowTheCanvasCouldNotShow`), link sides written as `ElementId:InterfaceId` read too (`LinksWrittenAsElementAndInterfaceIdAreReadToo`), a flow from a node to itself with an `Out_` and an `In_` port (`AFlowFromANodeToItselfGetsTwoPortsAndSurvivesTheRoundTrip`) |
| `starter/dotnet/Efl.Tests/ValidatorTests.cs` | validator rules | one broken model per rule id EFL01 to EFL07, the sample breaks none |
| `starter/dotnet/Efl.Tests/ExampleAndGeometryTests.cs` | shared fixture, docking geometry | the example JSON the browser tests load equals the sample built in code; a file with a newer `formatVersion` is read with a warning, a file without the field is read as version 1; flows end on the Store ellipse and the Step box; a node without size docks at its centre instead of NaN |
| `starter/dotnet/Efl.Tests/DiagramInterchangeTests.cs` | shared layout library in documents | a created document carries OMG_DD and every layout type path resolves, in both encodings; a created document is valid against the CAEX schema (`CAEXDocument.Validate`), with a negative control that moves the library to a place the schema forbids; the embedded library equals `starter/libraries/OMG_DD_AttributeTypeLib_v0.1.aml`; the library artefact references the file instead of carrying it; an update of a document that references the file under its own alias (`DD`) writes `DD@...` paths and embeds nothing. None of this shows in a round trip test, because the reader goes by attribute names (starter/dotnet/Efl.Tests/DiagramInterchangeTests.cs:12-15) |
| `starter/dotnet/Efl.Tests/LayoutTests.cs` | layout | the arranged picture checked on the lines, not only on the nodes: no flow through a node and no two flows on one segment for a cycle, a back flow between two nodes, several back flows and a flow that skips a column; a self loop gets three bend points; flows at nodes that already had positions keep their waypoints; arranging twice gives the same file |
| `starter/web/tools/verify-modeler.mjs` | modeler in a browser | 12 checks: boot, import/export identity, docking, a newer format version shown with a warning, tolerant import, node and flow drawn with the palette and the mouse, rules, command stack and undo, delete, move, SVG |
| `starter/web/tools/verify-bridge.mjs` | host bridge | 6 checks: ready, import baseline without change events, debounced edit, back-to-back imports, broken import (only `error`, no `imported`), export/SVG/select requests |
| `starter/web/tools/verify-webapp.mjs` | web app end to end | 7 checks against the built `Efl.Web.dll` (not in `npm test`) |
| `starter/plugin/Aml.Editor.Plugin.Efl.Tests/PackageTests.cs` | plugin package | 12 tests: no `Aml.Editor.Plugin.Contract.dll`, no `Aml.Engine.dll`, eight runtime files present, versions agree, valid display name |

`Efl.Tests` holds 45 tests (15 round trip, 9 validator, 6 example and geometry, 7 layout, 8 layout library).

Tests do not see what a person sees. In the trial run of the playbook every browser check passed on a diagram whose arranged form drew a back flow on top of a forward flow and a flow through a node, and on a label far from its line; the first screenshot showed both. `npm run screenshot` in `web/` writes a PNG and the SVG export of the example into `web/screenshots/` (ignored by git), and `npm run screenshot -- --arranged` first strips all layout and lets the mapper arrange it through `eflmap arrange` (starter/web/tools/screenshot.mjs:1-20, starter/web/package.json:12). Look at both after every change to the renderer, the layouter or the layout code; `LayoutTests.cs` then pins what the picture showed.

## 2. Round-trip tests

### 2.1 Model to AML and back equals

Compare the complete model, not a count. Describe each element as one sortable line (id, kind, name, values, bounds, endpoints, waypoints) and assert the two lists are equal. When it fails, the diff names the property.

The starter does this for both connection encodings with one `[Theory]` (starter/dotnet/Efl.Tests/RoundTripTests.cs:50). AMLPetriNet compares tuples per element kind, including bounds (AMLPetriNet: dotnet/PtMapper.Tests/RoundTripTests.cs:82), and separately checks bend points (AMLPetriNet: dotnet/PtMapper.Tests/RoundTripTests.cs:103).

Count-based assertions are a weak first step. AMLFPB.js started with `Roundtrip_PreservesElementCounts` (AMLFPB.js: Aml.Editor.Plugin.FPB.Tests/CaexToFpbJsonTests.cs:32); the mapper later lost characteristic values and the name of a sub-process SystemLimit while counts stayed equal, which needed dedicated tests (fpb-aml-mapper: dotnet/FpbMapper.Tests/ConversionTests.cs:196, dotnet/FpbMapper.Tests/SystemLimitNameTests.cs:68). The SystemLimit defect was invisible on the top-level process because its process name happens to be taken from its SystemLimit (fpb-aml-mapper: dotnet/FpbMapper.Tests/SystemLimitNameTests.cs:6). Pick fixtures where every name is distinct from every other name, or such coincidences hide bugs.

### 2.2 Exchange format through AML and back is byte-identical

When the exchange format has a canonical writer, the strongest assertion is text equality of the format written before and after the detour through CAEX:

```csharp
[Fact]
public void PnmlThroughCaexAndBackIsIdentical()
{
    var before = Pnml.Write(Sample());
    var after = Pnml.Write(CaexToPtNet.ReadAll(PtNetToCaex.Convert(Pnml.Read(before))).Single());

    Assert.Equal(before, after);
}
```
(AMLPetriNet: dotnet/PtMapper.Tests/RoundTripTests.cs:113)

Take the sample from the real modeler's export, not from hand-written XML. AMLPetriNet's `SampledPnml` constant was captured from `web/tools/roundtrip.mjs`, so the C# side and the browser side are checked against the same bytes (AMLPetriNet: dotnet/PtMapper.Tests/RoundTripTests.cs:14).

### 2.3 Writing the same model twice changes no line

This is the property a plugin lives on: opening a document and syncing without editing must leave the file alone. Compare the serialised CAEX tree as text:

```csharp
var document = EflToCaex.Convert(Sample(), "Line", style);
var hierarchy = document.CAEXFile.InstanceHierarchy["Line"]!;
var before = document.CAEXFile.Node.ToString();

var read = CaexToEfl.Read(hierarchy).Single();
EflUpdater.UpdateInPlace(document, hierarchy, read);

Assert.Equal(before, document.CAEXFile.Node.ToString());
```
(starter/dotnet/Efl.Tests/RoundTripTests.cs:64)

Add a convergence test as well: the second update after a real change must be a no-op (AMLPetriNet: dotnet/PtMapper.Tests/UpdateInPlaceTests.cs:268). fpb-aml-mapper pins the exact cycle the plugin runs during live sync, CAEX to JSON, update in place, CAEX to JSON again, per InstanceHierarchy, and a second variant that compares the saved XML after one and after two cycles (fpb-aml-mapper: dotnet/FpbMapper.Tests/Showcases/ShowcaseRoundTripTests.cs:40, dotnet/FpbMapper.Tests/Showcases/ShowcaseRoundTripTests.cs:74). The comment there names the failure class it prevents: every live sync tick washing corruption deeper into the file (jumping states, vanished boundary states, cleared references).

### 2.4 JSON and AML carry the same model

If the modeler talks JSON and the mapper also accepts another format, test that each path yields the same model. The starter writes the model to JSON, reads it back, converts to CAEX, reads back, and compares with the original description (starter/dotnet/Efl.Tests/RoundTripTests.cs:82). fpb-aml-mapper locks the whole JSON to AML to JSON result against a golden file (section 5).

### 2.5 Compare against the same file loaded twice

Aml.Engine reformats on load and save. Never diff a round-trip result against the file on disk. Load the document twice, run the round trip on one copy, and compare the two trees:

```csharp
var before = CAEXDocument.LoadFromFile(Document).CAEXFile.Node.ToString();

var doc = CAEXDocument.LoadFromFile(Document);
RoundTrip(doc);

Assert.Equal(before, doc.CAEXFile.Node.ToString());
```
(AMLPetriNet: dotnet/PtMapper.Tests/PaperRoundTripTests.cs:37)

Follow the whole-document assertion with property-by-property tests (ids, identification, markings, links into other hierarchies, external references) so that a failure says what broke (AMLPetriNet: dotnet/PtMapper.Tests/PaperRoundTripTests.cs:22, docs/roundtrip-validation.md:40).

### 2.6 Put the round-trip check into the product, too

A unit test on a fixture proves nothing about a file somebody edited in the AML Editor and saved. AMLPetriNet exposes the same checks as a library class that the test suite and the command line both use (AMLPetriNet: dotnet/PtMapper.Conversion/PtRoundTripCheck.cs:20), so `ptmap roundtrip <file.aml>` and `ptmap compare <before.aml> <after.aml>` report `[ok]` or `[FAIL]` per aspect (AMLPetriNet: dotnet/PtMapper.Tool/Program.cs:104, dotnet/PtMapper.Tool/Program.cs:118). The test `TheCheckBehindThePapersClaimPasses` asserts that the check itself passes on the example and runs all eleven aspects (AMLPetriNet: dotnet/PtMapper.Tests/PaperRoundTripTests.cs:294). Test the checker's ability to fail as well: `TheRoundTripCheckNoticesARetargetedArc` and `ComparingNoticesAChangeOutsideTheNet` (AMLPetriNet: dotnet/PtMapper.Tests/RegressionTests.cs:626, dotnet/PtMapper.Tests/IdentityAndSessionTests.cs:303).

## 3. Update-in-place tests

Most of these tests assert what must *not* change (AMLPetriNet: dotnet/PtMapper.Tests/UpdateInPlaceTests.cs:8). Write each one against a seeded document and a snapshot of its XML text.

### 3.1 Foreign content survives

- A user's own attribute on a mapped element survives an update that changes a mapped attribute on the same element (AMLPetriNet: dotnet/PtMapper.Tests/UpdateInPlaceTests.cs:54; fpb-aml-mapper: dotnet/FpbMapper.Tests/ConversionTests.cs:419).
- A user's own interface and an InternalLink between two such interfaces survive; they are not the language's interface class, so the updater must not sweep them up (AMLPetriNet: dotnet/PtMapper.Tests/UpdateInPlaceTests.cs:70).
- A foreign InternalElement inside the managed hierarchy is left alone (AMLPetriNet: dotnet/PtMapper.Tests/UpdateInPlaceTests.cs:99; starter/dotnet/Efl.Tests/RoundTripTests.cs:95).
- Links from another hierarchy into the net and reference attributes survive a sync (AMLPetriNet: dotnet/PtMapper.Tests/ReferenceSurvivalTests.cs:32, dotnet/PtMapper.Tests/ReferenceSurvivalTests.cs:73).
- Other InstanceHierarchies are not touched at all (AMLPetriNet: dotnet/PtMapper.Tests/ForeignDocumentSweepTests.cs:57).
- Attributes that belong to the language but do not fit the element's type (a marking on a transition left by a hand edit) may be cleaned up, while a user attribute next to it stays (AMLPetriNet: dotnet/PtMapper.Tests/UpdateInPlaceTests.cs:292).

### 3.2 Deleting a node removes only its connections

- Removing an arc removes its links and the endpoint interfaces on both nodes (AMLPetriNet: dotnet/PtMapper.Tests/UpdateInPlaceTests.cs:138).
- No link is left pointing at an interface that no longer exists, checked over every link (AMLPetriNet: dotnet/PtMapper.Tests/UpdateInPlaceTests.cs:154).
- Deleting a node that a link from *another* hierarchy points into must not leave that link dangling (AMLPetriNet: dotnet/PtMapper.Tests/ReferenceSurvivalTests.cs:106).
- With two copies of the same net in one document, removing an arc in one copy leaves the other copy wired (AMLPetriNet: dotnet/PtMapper.Tests/RegressionTests.cs:45). This caught interface and link ids that were derived from the model id only and therefore collided across copies; they are now salted with the owner's CAEX id (starter/dotnet/Efl.Conversion/EflIds.cs:58 shows the same signature).
- In the starter: removing a node removes its flows and nothing else, and the result reads back without warnings (starter/dotnet/Efl.Tests/RoundTripTests.cs:116).
- Absence from the incoming model is not always a deletion. A flow element whose links somebody broke in the AML Editor never reached the canvas, so the update must keep it; the starter's test removes one of its links, reads the diagram, updates with that model and asserts the element is still there, and so is the port of the flow's surviving half on the node (starter/dotnet/Efl.Tests/RoundTripTests.cs:165; AMLPetriNet: dotnet/PtMapper.Tests/RegressionTests.cs:66).
- Shrinking lists must not keep their tail: a polyline shortened from three to one waypoint has exactly one waypoint attribute afterwards, a label moved back loses its offset, a flag that became false disappears (AMLPetriNet: dotnet/PtMapper.Tests/UpdateInPlaceTests.cs:188, dotnet/PtMapper.Tests/UpdateInPlaceTests.cs:207, dotnet/PtMapper.Tests/UpdateInPlaceTests.cs:220).

### 3.3 Preconditions refuse before writing

An update that fails halfway leaves a document that is neither the old nor the new model. Validate everything first, then write, and test that a refused update leaves the XML identical:

```csharp
[Fact]
public void AnArcToANodeTheNetDoesNotHaveIsRefusedBeforeWriting()
{
    var doc = CAEXDocument.New_CAEXDocument();
    var ih = PtNetToCaex.AppendInto(doc, SmallNet(), "Net");
    var before = XmlOf(doc);

    var broken = SmallNet();
    broken.Places[0].Name = "Renamed";
    broken.Arcs.Add(new PtArc { Id = "a3", SourceId = "t1", TargetId = "ghost" });

    var error = Assert.Throws<ArgumentException>(() => PtNetUpdater.UpdateInPlace(doc, ih, broken));
    Assert.Contains("ghost", error.Message, StringComparison.Ordinal);
    Assert.Equal(before, XmlOf(doc));
}
```
(AMLPetriNet: dotnet/PtMapper.Tests/IdentityAndSessionTests.cs:215)

Note the rename placed *before* the broken arc: without it, an implementation that writes names first and then throws would still pass. The sibling test removes a class from the document's library so that the update cannot instantiate new elements, and asserts nothing was written (AMLPetriNet: dotnet/PtMapper.Tests/IdentityAndSessionTests.cs:198). The starter carries the same test (starter/dotnet/Efl.Tests/RoundTripTests.cs:231).

### 3.4 The end-to-end property

After any update, the hierarchy must read back as exactly the model that went in (AMLPetriNet: dotnet/PtMapper.Tests/UpdateInPlaceTests.cs:247). Combine a rename, a new node, a changed weight and a new bend point in one target model so the test exercises add, change and keep in one pass.

## 4. Determinism and identity tests

Update in place only works if the same model always yields the same CAEX ids. Otherwise every sync replaces every element and every reference a user added dangles (AMLPetriNet: dotnet/PtMapper.Conversion/PtIds.cs:7).

Test all of these:

1. Converting the same model twice yields the same set of `ID` attributes, over the whole document, not only InternalElements (starter/dotnet/Efl.Tests/RoundTripTests.cs:217; AMLPetriNet: dotnet/PtMapper.Tests/RoundTripTests.cs:199).
2. The ids in the document are the ones the id function derived. Aml.Engine reassigns ids on insert under some conditions, so assert it instead of assuming it (AMLPetriNet: dotnet/PtMapper.Tests/IdentityTests.cs:8, dotnet/PtMapper.Tests/IdentityTests.cs:26).
3. The derived ids are well-formed UUIDs, asserted on the rendered string:

```csharp
// .NET reads the first three fields of a byte[] Guid little-endian, so a
// version nibble stamped at the wrong byte comes out somewhere else
// entirely. Assert on the rendered form, which is what lands in the file.
var text = PtIds.Element("p1");

Assert.StartsWith("{", text);
var guid = Guid.Parse(text);
var fields = guid.ToString("D").Split('-');

Assert.Equal('5', fields[2][0]);                    // version 5
Assert.Contains(fields[3][0], new[] { '8', '9', 'a', 'b' }); // RFC 4122 variant
```
(AMLPetriNet: dotnet/PtMapper.Tests/IdentityTests.cs:54)

4. Different kinds of id derived from the same model id do not collide (element, net, interfaces, links) (AMLPetriNet: dotnet/PtMapper.Tests/IdentityTests.cs:70).
5. Dropping and re-creating the hierarchy yields the same ids (AMLPetriNet: dotnet/PtMapper.Tests/IdentityTests.cs:85).
6. Importing the same model twice into one document leaves no duplicate `ID` anywhere, compared case-insensitively (AMLPetriNet: dotnet/PtMapper.Tests/RegressionTests.cs:32).
7. Ids stay stable across repeated updates, including after a decompose cycle in a layered language (fpb-aml-mapper: dotnet/FpbMapper.Tests/ConversionTests.cs:894, dotnet/FpbMapper.Tests/ConversionTests.cs:839).
8. Hostile ids: characters that are not XML names, duplicates in the input, a copied element that arrives with an id already in use (AMLPetriNet: dotnet/PtMapper.Tests/HostileInputTests.cs:237, dotnet/PtMapper.Tests/IdentityAndSessionTests.cs:80, dotnet/PtMapper.Tests/IdentityAndSessionTests.cs:108).
9. The layout algorithm is deterministic too, or every open of a layout-free document produces a different diff (AMLPetriNet: dotnet/PtMapper.Tests/LayoutTests.cs:107; starter/dotnet/Efl.Tests/LayoutTests.cs:184). See [Layout](08-layout.md).

If a converter path is not id-stable by design (fpb-aml-mapper's `Convert` mints fresh GUIDs for process elements and flow interfaces, while the plugin uses `UpdateInPlace`, which preserves ids), say so in the test and canonicalise instead of pretending (fpb-aml-mapper: dotnet/FpbMapper.Tests/GoldenTests.cs:34). Prefer deterministic ids everywhere in a new project; the starter derives all ids from SHA-256 (starter/dotnet/Efl.Conversion/EflIds.cs:30).

## 5. Golden files

Golden files catch every unintended change in output structure. They also create churn, so be deliberate about what goes in them and how they are refreshed.

### 5.1 Three kinds of golden in the source projects

| Kind | What is compared | Source |
|---|---|---|
| Structural dump | a normalised text dump of the CAEX tree (element name, class path, sorted attribute paths, links) | fpb-aml-mapper: dotnet/FpbMapper.Tests/GoldenTests.cs:32 |
| Normalised round-trip output | JSON after JSON to AML to JSON, re-serialised with sorted keys, GUIDs canonicalised | fpb-aml-mapper: dotnet/FpbMapper.Tests/GoldenTests.cs:48 |
| Published artefact | the library the mapper builds in code against the published `.aml` library file, structurally: class names, class ids, inheritance, attribute names, the wording of a description the mapper depends on | AMLPetriNet: dotnet/PtMapper.Tests/PtLibraryGoldenTests.cs:8 |
| Shipped example output | the exported PNML and the AML written from it, which accompany a paper, against what the current code writes | AMLPetriNet: dotnet/PtMapper.Tests/PaperRoundTripTests.cs:308, dotnet/PtMapper.Tests/PaperRoundTripTests.cs:322 |

The library golden compares structure, not bytes: "element order and formatting are Aml.Engine's business, the class model is ours" (AMLPetriNet: dotnet/PtMapper.Tests/PtLibraryGoldenTests.cs:14). Class ids are part of the contract because documents already written reference them (AMLPetriNet: dotnet/PtMapper.Tests/PtLibraryGoldenTests.cs:62). Conventions that exist only as prose in a class description (waypoint attribute naming) get a test on that description, so a silent removal fails (AMLPetriNet: dotnet/PtMapper.Tests/PtLibraryGoldenTests.cs:127).

### 5.2 How to regenerate

Use an environment switch, write to the source tree, and fail the run that wrote the file:

```csharp
private static string TestDataDir([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "") =>
    Path.Combine(Path.GetDirectoryName(thisFile)!, "TestData");

private static readonly bool UpdateGolden =
    Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1";

private static void CompareToGolden(string goldenName, string actual)
{
    var path = Path.Combine(TestDataDir(), goldenName);
    actual = actual.Replace("\r\n", "\n");

    if (UpdateGolden || !File.Exists(path))
    {
        File.WriteAllText(path, actual, new UTF8Encoding(false));
        Assert.Fail(/* message: review the diff, commit, re-run without UPDATE_GOLDEN */);
    }

    var expected = File.ReadAllText(path).Replace("\r\n", "\n");
    Assert.Equal(expected, actual);
}
```
(fpb-aml-mapper: dotnet/FpbMapper.Tests/GoldenTests.cs:22, dotnet/FpbMapper.Tests/GoldenTests.cs:82; the message string is shortened here)

Why each detail matters:

- `CallerFilePath` points at the source folder. Writing into `bin/` would regenerate a copy nobody commits.
- Failing on regeneration prevents a CI run with `UPDATE_GOLDEN=1` accidentally set from passing while silently overwriting.
- A missing golden is created and fails too, so adding a new golden is a two-step review.
- Line endings are normalised on both sides; git may check files out either way (AMLPetriNet: dotnet/PtMapper.Tests/PaperRoundTripTests.cs:452).
- Each golden test first converts twice and asserts equality, so a golden can never be made "stable" over nondeterministic output (fpb-aml-mapper: dotnet/FpbMapper.Tests/GoldenTests.cs:42).

Regeneration procedure:

1. Run the suite normally. Read the failure; decide whether the change is intended.
2. Run with `UPDATE_GOLDEN=1` (PowerShell: `$env:UPDATE_GOLDEN='1'; dotnet test`; bash: `UPDATE_GOLDEN=1 dotnet test`).
3. Review the diff with invariants in mind, not only by eye. When fpb-aml-mapper's boundary-state fix changed the round-trip golden, the review confirmed that line count, number of `$type` entries and number of id fields were unchanged, and only the number of distinct GUID placeholders dropped (boundary states that belong together were reunified). Numbers like these tell a structural fix from a lost element.
4. Unset the variable and run again. Commit golden and code together.

### 5.3 Normalise what is not yours

Strip exactly what differs for reasons outside the model, and document why:

```csharp
private static string WithoutWritingTime(string xml)
{
    var withoutTime = System.Text.RegularExpressions.Regex.Replace(
        xml, "LastWritingDateTime=\"[^\"]*\"", "LastWritingDateTime=\"\"");
    return System.Text.RegularExpressions.Regex.Replace(
        withoutTime, " xsi:schemaLocation=\"[^\"]*\"", "");
}
```
(AMLPetriNet: dotnet/PtMapper.Tests/PaperRoundTripTests.cs:464)

The comment above it records that Aml.Engine adds the schema location, and a copy of the XSD next to the saved file, depending on state that is not the document's (AMLPetriNet: dotnet/PtMapper.Tests/PaperRoundTripTests.cs:457). The AMLPetriNet `.gitignore` excludes `**/CAEX_ClassModel_V.3.0.xsd` for the same reason. Other things to neutralise:

- `SaveToFile` stamps the target file name into the document. Serialise both sides to the same fixed temp name, or replace the temp name before comparing (fpb-aml-mapper: dotnet/FpbMapper.Tests/Showcases/ShowcaseRoundTripTests.cs:103; AMLPetriNet: dotnet/PtMapper.Tests/PaperRoundTripTests.cs:340).
- Only two *saved* documents are comparable, because saving adds attributes an in-memory tree lacks (AMLPetriNet: dotnet/PtMapper.Tests/PaperRoundTripTests.cs:327).
- Random GUIDs: replace each distinct GUID by `#1`, `#2`, ... in order of first appearance. This keeps the reference wiring (which element points at which) while ignoring the values (fpb-aml-mapper: dotnet/FpbMapper.Tests/GoldenTests.cs:65).
- JSON key order: re-serialise with sorted keys (fpb-aml-mapper: dotnet/FpbMapper.Tests/GoldenTests.cs:98).
- XML writer differences between .NET and the browser (space before `/>`): parse and re-serialise both sides in the same engine before comparing (AMLPetriNet: web/tools/verify-interop.mjs:51).

Do not normalise away anything that a user would see as a change.

### 5.4 Ids change when an example is rewritten

Shipped examples are derived artefacts. When their source document is rewritten, every derived file changes, and the diff is total rather than local.

In AMLPetriNet the paper document was rewritten with new CAEX ids, so `MPS500_PT.pnml` and `MPS500_PT_export.aml` had to be written again. Both are pinned by `TheSupplementaryPnmlIsWhatTheMapperProduces` and `TheSupplementaryAmlIsWhatTheMapperProduces`, which fail until that happens, so the shipped artefacts cannot drift from the code (AMLPetriNet: docs/roundtrip-validation.md:210). The regeneration commands are part of the document (AMLPetriNet: docs/roundtrip-validation.md:213). Two effects explain why the diff is total:

- The PNML ids are normalised CAEX GUIDs of the source document, so new CAEX ids mean new PNML ids on every element (AMLPetriNet: docs/roundtrip-validation.md:73).
- An AML document written from PNML alone gets ids derived from the PNML ids (UUID v5), so a changed PNML id changes the derived CAEX id as well (AMLPetriNet: docs/roundtrip-validation.md:97).

Rules:

- Keep the regeneration commands next to the artefacts, in a doc or a script, never only in someone's shell history.
- Review a rewritten example with a structural compare (`ptmap compare`, a structural dump, or a canonicalised golden), not with a text diff that is 100 percent red.
- Regenerate the source document and everything derived from it in one commit, and note that ids changed. Anyone holding references into the old ids (a paper figure, another hierarchy, a bug report) needs to know.
- Make the output location of example writers configurable. fpb-aml-mapper's showcase writers default to a folder in the sibling plugin repository by walking six directories up from the test binary, with `SHOWCASE_OUTPUT_DIR` as override (fpb-aml-mapper: dotnet/FpbMapper.Tests/Showcases/ShowcaseTests.cs:23, dotnet/FpbMapper.Tests/Showcases/ShowcaseTests.cs:33). CI sets that variable to the runner's temp directory (fpb-aml-mapper: .github/workflows/test.yml:26), and the round-trip tests produce the files lazily themselves because "CI runs the mapper repo alone" (fpb-aml-mapper: dotnet/FpbMapper.Tests/Showcases/ShowcaseRoundTripTests.cs:24). A test that writes into a sibling repository by default is a trap on a developer machine; prefer the temp directory as default and an explicit opt-in for writing examples.

## 6. Validator rule tests

For each rule write at least one positive and one negative case, and assert rule id, severity and element id, not only "some finding exists":

```csharp
[Fact]
public void AnArcBetweenTwoPlacesIsAnError()
{
    var net = Valid();
    net.Arcs.Add(new PtArc { Id = "a3", SourceId = "p1", TargetId = "p2" });

    var finding = Assert.Single(Rules(net, "PT03"));
    Assert.Equal(PtSeverity.Error, finding.Severity);
    Assert.Equal("a3", finding.ElementId);
    Assert.Contains("bipartite", finding.Message);
}
```
(AMLPetriNet: dotnet/PtMapper.Tests/ValidatorTests.cs:40)

Further tests the source projects found necessary:

- A well-formed model has no findings at all (AMLPetriNet: dotnet/PtMapper.Tests/ValidatorTests.cs:24).
- Overlapping rules do not double-report: an unconnected transition is reported once as unconnected, not also as source and sink (AMLPetriNet: dotnet/PtMapper.Tests/ValidatorTests.cs:135).
- Findings carry the id the *modeler* uses, because the plugin turns a finding into a select message for the canvas (AMLPetriNet: dotnet/PtMapper.Tests/ValidatorTests.cs:147).
- The example shipped with the repository is itself clean; the test project copies `examples/*.pnml` into the output for this (AMLPetriNet: dotnet/PtMapper.Tests/ValidatorTests.cs:160, dotnet/PtMapper.Tests/PtMapper.Tests.csproj:22).
- A conformance report lists every rule, derived from the validator's rule id list rather than typed into the test. A hand-written list stopped at PT10 when PT11 was added and the coverage claim went stale (AMLPetriNet: dotnet/PtMapper.Tests/ConformanceReportTests.cs:48). The starter does it this way: `EflValidator.RuleIds` lists EFL01 to EFL07 (starter/dotnet/Efl.Conversion/EflValidator.cs:42), and a `[Theory]` over that list fails when a rule has no broken model in the test's dictionary, then asserts the rule fires on its own broken model and, apart from EFL02, alone (starter/dotnet/Efl.Tests/ValidatorTests.cs:71). A new rule therefore cannot be added without a test.
- Rule ids are stable, and disabling a rule removes only that rule's findings (AMLFPB.js: Aml.Editor.Plugin.FPB.Tests/ValidationPipelineTests.cs:19, Aml.Editor.Plugin.FPB.Tests/ValidationPipelineTests.cs:33).
- When two implementations of the same rules exist (hard-coded C# and OCL constraints), prove parity on a model that actually violates them. Mutate a valid model, assert the hard-coded side found something, then assert equality; "an empty-vs-empty comparison proves nothing" (AMLFPB.js: Aml.Editor.Plugin.FPB.Tests/SideBySideValidationTests.cs:66, Aml.Editor.Plugin.FPB.Tests/SideBySideValidationTests.cs:77). Load the constraints from the same embedded artefacts the runtime uses, not from inline copies in the test.

The general principle behind the last point, used again in sections 7 and 8: **every "nothing happened" assertion needs a guard that something could have happened.** In the bridge test, before asserting that firing a transition in simulation mode reports no edit, the script checks that firing really changed the marking, "otherwise the next check proves nothing" (AMLPetriNet: web/tools/verify-bridge.mjs:147).

## 7. Browser tests against the bundled modeler

### 7.1 Test the bundle, not the sources

Run the browser checks against the same build output the plugin and web app ship. AMLPetriNet serves `web/dist` with a tiny static server on a random port, because module scripts need http, not `file://` (AMLPetriNet: web/tools/serve.mjs:1, web/tools/serve.mjs:15). The page sets a readiness flag after the modeler is created, and every script waits for it instead of sleeping:

```js
await page.goto(`http://127.0.0.1:${port}/index.html`);
await page.waitForFunction(() => window.ptnReady === true, null, { timeout: 15000 });
```
(AMLPetriNet: web/tools/roundtrip.mjs:19; the flag is set in web/index.html:78)

Expose the modeler instance on `window` for tests (`window.ptn`, AMLPetriNet: web/index.html:73; `window.fpbjs`, FPB.JS: app/app.js:57). Forward `pageerror` and console errors to the test output (AMLPetriNet: web/tools/roundtrip.mjs:14), otherwise a failing import shows up as a timeout with no reason.

The starter puts serving, page setup and error collection into one shared harness: it serves `dist/` on a random port (starter/web/tools/harness.mjs:23), opens a fresh page per check, collects `pageerror` and console errors and fails the check on any (starter/web/tools/harness.mjs:47), and waits for `window.efl.modeler` instead of a separate flag (starter/web/tools/harness.mjs:58).

AMLPetriNet uses plain Node scripts with the `playwright` library and exit codes; `npm test` chains them and `pretest` installs Chromium (AMLPetriNet: web/package.json:9, web/package.json:10). The starter's `npm test` runs `verify-modeler.mjs` and `verify-bridge.mjs`; Chromium is installed once with `npm run browsers` rather than on every test run (starter/web/package.json:9, starter/web/package.json:11). FPB.JS uses the `@playwright/test` runner with a page object and a `webServer` that starts the dev server (FPB.JS: playwright.config.js:50). Both work. Scripts are simpler to run in CI without a dev server; the runner gives traces, videos and retries.

### 7.2 Drive diagram-js through its services, not through pixels

Create, connect, label and move elements with `elementFactory`, `modeling` and `canvas`. This is deterministic and independent of zoom, scroll, palette layout and animation:

```js
const m = window.ptn;
const factory = m.get('elementFactory');
const modeling = m.get('modeling');
const root = m.get('canvas').getRootElement();

const p1 = factory.createShape({ type: 'ptn:Place' });
const t1 = factory.createShape({ type: 'ptn:Transition' });
modeling.createShape(p1, { x: 150, y: 200 }, root);
modeling.createShape(t1, { x: 320, y: 200 }, root);

modeling.updateLabel(p1, 'Waiting');
modeling.updateProperty(p1, 'initialMarking', 3);
const a1 = modeling.connect(p1, t1, { type: 'ptn:Arc' });
modeling.updateProperty(a1, 'inscription', '2');
```
(AMLPetriNet: web/tools/roundtrip.mjs:23, shortened)

Then export, import the export, export again, and compare the two strings (AMLPetriNet: web/tools/roundtrip.mjs:51). Read state back through `elementRegistry` and business objects rather than counting SVG nodes (AMLPetriNet: web/tools/verify-webapp.mjs:96; FPB.JS: tests/e2e/data-integrity.spec.js:42).

Use real UI interaction only where the UI itself is under test (palette entries, context pad entries, dialogs). FPB.JS clicks palette buttons by accessible name, because diagram-js 15 sets `aria-label` rather than `title` (FPB.JS: tests/e2e/pages/ModelerPage.js:66).

### 7.3 Selection API instead of clicking on an element

Clicking on a shape at a canvas position turned out unreliable for opening the context pad in FPB.JS, and element ids are UUIDs, so selectors like `[data-element-id*="SystemLimit"]` match nothing. The page object finds elements by type in the element tree, selects them through the selection service and fires the click event on the event bus:

```js
const matching = allElements.filter(e => e.type === type);
const element = matching[index];
if (!element) return false;

const selection = window.fpbjs.get('selection');
selection.select(element);

const eventBus = window.fpbjs.get('eventBus');
eventBus.fire('element.click', { element });
```
(FPB.JS: tests/e2e/pages/ModelerPage.js:377)

Then click the context pad entry by its `data-action` attribute (FPB.JS: tests/e2e/pages/ModelerPage.js:144). The layer-consistency spec switched from position-based to type-based deletion for this reason (FPB.JS: tests/e2e/layer-consistency.spec.js:80).

Further observations from FPB.JS:

- Collect elements recursively from the root. Connections are children of the container shape (the SystemLimit), not of the root (FPB.JS: tests/e2e/pages/ModelerPage.js:451).
- Inspect nested model state through business objects in `page.evaluate`, for example the child layer of a decomposed operator (FPB.JS: tests/e2e/layer-consistency.spec.js:96).
- Modeling rules constrain where you may place elements. FPB.JS's rule requires an output to lie more than 50 px below its source, so test positions must follow it (FPB.JS: tests/e2e/full-model.spec.js:33). Keep positions in one constant table per spec.
- Page-object assertion helpers must import `expect`. FPB.JS's `ModelerPage.js` calls `expect` in `expectElementExists` without importing it (FPB.JS: tests/e2e/pages/ModelerPage.js:562), which only stays hidden because no spec calls those helpers.

### 7.4 Coordinate offsets

A Playwright `click({ position })` on `.djs-container` is in container pixels. A diagram-js shape's `x`/`y` is its top-left corner, and creating a shape from the palette centres it on the pointer. In FPB.JS work a click at (350, 150) produced an element at about (325, 125). The container-to-diagram mapping also shifts with zoom and scroll. Therefore:

- Never assert positions derived from click coordinates; read `element.x`, `element.y`, `width`, `height` back from the registry.
- When you need a click on an existing element, compute the centre from the element's bounds, or use the selection API above.
- In the exchange format, document which point a coordinate means (centre or top-left) and test it, as AMLPetriNet does for PNML, whose position is the centre (AMLPetriNet: dotnet/PtMapper.Tests/RoundTripTests.cs:39). See [Exchange format](03-exchange-format.md).

### 7.5 Cross-implementation interop

When the mapper (C#) and the modeler (JS) both read and write the exchange format, load files written by the mapper into the real modeler and compare what comes back. AMLPetriNet's fixture set covers an empty net, a net without layout, a silent transition, markup characters in ids and names, moved labels with bend points, and named arcs; the fixtures are written by a CLI command of the mapper (`ptmap fixtures`) and committed (AMLPetriNet: web/tools/verify-interop.mjs:1, dotnet/PtMapper.Tool/Program.cs:150). The script says which convention mismatches this catches: centre versus top-left, which points an arc carries, how a flag is spelled (AMLPetriNet: web/tools/verify-interop.mjs:4).

### 7.6 The host bridge without the host

`window.chrome.webview` exists only inside WebView2, so ordinary browser tests never touch the bridge code. Install a stand-in before any page script runs and drive the real message protocol:

```js
await page.addInitScript(() => {
  const listeners = [];
  window.__hostMessages = [];
  window.chrome = {
    webview: {
      postMessage: (message) => window.__hostMessages.push(message),
      addEventListener: (_type, handler) => listeners.push(handler),
    },
  };
  // What PostWebMessageAsJson does on the C# side: deliver an object as e.data.
  window.__hostSend = (message) => listeners.forEach((h) => h({ data: message }));
});
```
(AMLPetriNet: web/tools/verify-bridge.mjs:32)

What AMLPetriNet asserts over that protocol (AMLPetriNet: web/tools/verify-bridge.mjs:61 to web/tools/verify-bridge.mjs:181):

- boot posts `ready`;
- an import is answered with an echo baseline and reaches the canvas;
- an import raises no change event of its own, checked after waiting past the 300 ms debounce window;
- a user edit is reported with the new value;
- an unknown id in `selectElement` is not an error;
- two imports back to back do not interleave and report no edits;
- token simulation reports no edits, an export request during simulation is refused with a reason, leaving simulation restores the exact pre-simulation model;
- a malformed import is reported and does not take the modeler down.

Each of these corresponds to a bug class described in [Editor plugin](05-editor-plugin.md): echo loops, phantom pending changes, saving a simulation state.

The starter avoids the stand-in altogether. Its `post` sends every message to `window.chrome?.webview` when present and also dispatches it as an `efl-bridge-out` DOM event (starter/web/src/bridge.js:40), and `connectBridge` returns its `handle` function so a test or a plain web page can deliver host messages directly (starter/web/src/bridge.js:170). The harness records the events in an init script (starter/web/tools/harness.mjs:51), and `verify-bridge.mjs` waits 600 ms, longer than the 300 ms change debounce (starter/web/src/bridge.js:29), before asserting that an import raised no change (starter/web/tools/verify-bridge.mjs:15). Its broken-import check also asserts that a failed import posts `error` and no `imported` (starter/web/tools/verify-bridge.mjs:71-82), because the host takes `imported` as "the canvas shows what I sent" and would drop its unsaved edits (starter/web/src/bridge.js:111-127). Either approach works; what matters is that the real bridge code runs, not a copy of it.

### 7.7 Capturing downloads in headless Chromium

A web page that saves via a blob URL and `<a download>` reports the download as canceled in headless Chromium. Capture the content where the page makes it by wrapping `URL.createObjectURL` in an init script, record the entry synchronously, and wait until `blob.text()` has resolved before reading (AMLPetriNet: web/tools/verify-webapp.mjs:38, web/tools/verify-webapp.mjs:71). Then assert what matters for a web mapper: the saved file is the document that was opened, with the model updated, not a fresh document holding only the model (AMLPetriNet: web/tools/verify-webapp.mjs:132). See [Web app](06-web-app.md).

### 7.8 A measured editing session

`verify-paper-session.mjs` opens a real AML file in the web mapper, moves one named node with `modeling.moveShape`, saves, and writes the result so that `ptmap compare` can report what the session changed (AMLPetriNet: web/tools/verify-paper-session.mjs:1, web/tools/verify-paper-session.mjs:84). The measurement result is recorded in AMLPetriNet: docs/roundtrip-validation.md:148. Which node is moved matters: dragging a node straightens the routed arcs attached to it, so the stored attribute count differs (AMLPetriNet: docs/roundtrip-validation.md:171). Choose the node deliberately and name it in the script invocation.

This script and `verify-webapp.mjs` need a running web mapper and are not part of `npm test` or CI (AMLPetriNet: web/package.json:10). Run them before a release.

The starter's `verify-webapp.mjs` starts the server itself: it builds `Efl.Web` and then launches the built `Efl.Web.dll` directly with the project folder as working directory, because a process started through `dotnet run` survives killing `dotnet run`, keeps the port and locks the build output for the next run (starter/web/tools/verify-webapp.mjs:20, starter/web/tools/verify-webapp.mjs:28). It polls `/api/health` before the first check (starter/web/tools/verify-webapp.mjs:36).

### 7.9 Unit tests of modeler code

FPB.JS runs unit tests with Vitest in happy-dom (FPB.JS: vitest.config.js:8) and a separate browser-mode config for integration tests (FPB.JS: vitest.config.browser.js:11). Lessons from that suite:

- happy-dom does not fully support `getAttributeNS`; XML mapping code must be tested with jsdom or in a real browser.
- The `is()` helper without a moddle instance returns `undefined`, not `false`; assert with `toBeFalsy()`.
- Test helpers, constants, rule predicates and import utilities as pure functions. Modules that need `eventBus`, `modeling` and `canvas` together are better covered by the browser layer than by large mock setups (a mock event bus exists at FPB.JS: tests/setup/mocks/MockEventBus.js:7).

## 8. Conformance against a standard grammar, with negative controls

If your exchange format has a published grammar (RELAX NG, XSD, JSON Schema), validate the files both implementations write against it with a reference validator.

AMLPetriNet validates PNML against the ISO/IEC 15909-2 P/T net grammar from pnml.org with Jing (AMLPetriNet: web/tools/validate-pnml.mjs:1). The tool:

- downloads the five grammar files and two jars once into `web/.cache/pnml` (git-ignored), rewriting the absolute pnml.org include hrefs to local ones (AMLPetriNet: web/tools/validate-pnml.mjs:56);
- runs Jing with `-i`, because the grammar declares the `id` attribute with conflicting ID types and Jing refuses it as a schema error otherwise; id uniqueness is then checked by validator rule PT01 instead (AMLPetriNet: web/tools/validate-pnml.mjs:15, web/tools/validate-pnml.mjs:70);
- exits 1 if any file is invalid.

```js
execFileSync('java', ['-cp', classpath, 'com.thaiopensource.relaxng.util.Driver', '-i',
  join(cache, 'ptnet.pntd'), path], { stdio: 'pipe' });
```
(AMLPetriNet: web/tools/validate-pnml.mjs:70)

**Negative controls.** A validator that says "valid" to everything is indistinguishable from a working one until you feed it something broken. AMLPetriNet ran deliberately broken copies of the example: marking `-1`, marking `viel`, inscription `0`, a wrong net type, an unknown element; each was rejected with the expected reason (AMLPetriNet: docs/pnml-conformance.md:44). The same control applies when a switch like `-i` weakens the check: know exactly what it no longer catches and cover that elsewhere. In AMLPetriNet those controls are recorded in the doc but not part of CI; for a new project, commit the broken files (for example `examples/invalid/*.xml`) and add a CI step that expects each of them to fail.

Further conformance practice from the same document:

- Cross-check the reference validator with a second implementation once (AMLPetriNet used libxml2; AMLPetriNet: docs/pnml-conformance.md:16).
- Run files from other tools through both directions and tabulate grammar result, mapper result and modeler result per file (AMLPetriNet: docs/pnml-conformance.md:57). This found a lost label position for default-weight arcs, a duplicate id written twice into AML, and node sizes with more decimals than the grammar allows; each fix got a regression test named in the table (AMLPetriNet: docs/pnml-conformance.md:95).
- Read the grammar's value types, not only the structure. Sizes as decimals with one fractional digit and coordinates without exponent notation were found by reading, then tested in the browser (AMLPetriNet: docs/pnml-conformance.md:108, web/tools/verify-numbers.mjs:1).
- Write down what the grammar does not check (id uniqueness, bipartite arcs) so that nobody reads "valid" as "correct" (AMLPetriNet: docs/pnml-conformance.md:131).
- Do not copy third-party test files into the repository when their licences differ; list where each one is (AMLPetriNet: docs/pnml-conformance.md:59).

The same idea applies to CAEX: an AML document the mapper writes should validate against the CAEX 3.0 schema and should resolve every class path inside the file when it is meant to be self-contained (AMLPetriNet: dotnet/PtMapper.Tests/SelfContainedTests.cs:115, dotnet/PtMapper.Tests/SelfContainedTests.cs:150).

## 9. Hostile input, real documents and scale

- **XML attacks.** A reader for user-supplied files must refuse entity expansion and must not resolve external entities (AMLPetriNet: dotnet/PtMapper.Tests/HostileInputTests.cs:89, dotnet/PtMapper.Tests/HostileInputTests.cs:108).
- **Malformed values.** Non-numeric markings fall back to defaults, negative values are reported, non-finite coordinates never reach the model or the file, zero-sized nodes do not divide by zero (AMLPetriNet: dotnet/PtMapper.Tests/HostileInputTests.cs:53, dotnet/PtMapper.Tests/RegressionTests.cs:603, dotnet/PtMapper.Tests/HostileInputTests.cs:158; starter/dotnet/Efl.Tests/ExampleAndGeometryTests.cs:68).
- **Encoding.** A file that declares its own encoding is read from bytes, not from a string decoded with a guessed encoding (AMLPetriNet: dotnet/PtMapper.Tests/RegressionTests.cs:645).
- **Documents from other tools.** Load every real document you have, check that it reads, that a sync without edits changes nothing, that syncing twice settles, that the model read after sync equals the model read before, and that files with the vocabulary but no model (libraries, other languages) are not mistaken for one (AMLPetriNet: dotnet/PtMapper.Tests/ForeignDocumentSweepTests.cs:32 to dotnet/PtMapper.Tests/ForeignDocumentSweepTests.cs:126). Alias-qualified class paths must read like unaliased ones (fpb-aml-mapper: dotnet/FpbMapper.Tests/AliasToleranceTests.cs:65). Layout-free input must still produce usable geometry (fpb-aml-mapper: dotnet/FpbMapper.Tests/WaypointFallbackTests.cs:69).
- **Scale.** Run the full chain on 50 and 500 element pairs and an update of a single value on a large model, with generous time limits that only a quadratic path would exceed (AMLPetriNet: dotnet/PtMapper.Tests/ScaleTests.cs:53, dotnet/PtMapper.Tests/ScaleTests.cs:77). Keep the limits generous; CI runners are slower than developer machines.

## 10. Plugin tests without the editor

The AML Editor cannot run in CI. Structure the plugin so that almost nothing needs it:

1. **Keep logic out of the WPF view.** In AMLPetriNet the plugin project contains the view, the WebView2 bridge and diagnostics only (`Aml.Editor.Plugin.PetriNet/Bridge/`, `Diagnostics/`, `PetriNetPlugin.xaml.cs`); conversion, update, validation, round-trip check and compare live in `PtMapper.Conversion` and are tested there. AMLFPB.js tests validator classes that live in the plugin assembly directly, without creating the view (AMLFPB.js: Aml.Editor.Plugin.FPB.Tests/ValidationPipelineTests.cs:2).
2. **Snapshot the editor contract by reflection.** The plugin depends on members of `Aml.Editor.Plugin.Contract` that vary between editor versions. AMLFPB.js asserts their existence so a renamed member fails in CI instead of silently degrading at the user's site (AMLFPB.js: Aml.Editor.Plugin.FPB.Tests/ApiContractTests.cs:1, Aml.Editor.Plugin.FPB.Tests/ApiContractTests.cs:28). The contract package is referenced with `PrivateAssets=all`, so the test locates the loaded assembly after touching a plugin type (AMLFPB.js: Aml.Editor.Plugin.FPB.Tests/ApiContractTests.cs:16).
3. **Test the JS side of the bridge with a stand-in host** (section 7.6).
4. **If you must instantiate WPF objects in a test** (a `UserControl`, a `Dispatcher`-bound helper), run that code on a dedicated thread set to `ApartmentState.STA` and join it, because the xunit worker threads are MTA and WPF throws on them. Neither source project does this today; both avoid it by keeping the view thin, which is the better option.
5. **Target the right framework.** A test project that references the plugin must target the plugin's Windows TFM (AMLFPB.js uses `net8.0-windows7.0`, AMLFPB.js: Aml.Editor.Plugin.FPB.Tests/Aml.Editor.Plugin.FPB.Tests.csproj:4); the mapper test project stays `net8.0` and runs on any OS (fpb-aml-mapper: dotnet/FpbMapper.Tests/FpbMapper.Tests.csproj:4).
6. **Test the package, not only the code.** The failures that cost the most time in the editor were silent packaging mistakes: a second `Aml.Editor.Plugin.Contract.dll` makes the editor skip the plugin, a missing bundle gives an empty panel (see [Pitfalls](09-pitfalls.md), PF-CCH-01, PF-PLG-21). The starter's plugin test project references the plugin with `ReferenceOutputAssembly="false"` only so that the package is built first (starter/plugin/Aml.Editor.Plugin.Efl.Tests/Aml.Editor.Plugin.Efl.Tests.csproj:16), then opens the newest `.nupkg` of the csproj version and asserts: no contract assembly (starter/plugin/Aml.Editor.Plugin.Efl.Tests/PackageTests.cs:48), no `Aml.Engine.dll` (:56), plugin DLL, mapper DLL, WebView2 DLLs, `WebView2Loader.dll` and the bundle files present (:72), `Metadata.xml` version equals the csproj version (:80), and the display name matches `^[A-Za-z_][A-Za-z0-9_]*$` and is the one the plugin class sets (:87).
7. **Manual editor check before a release.** Keep a small set of documents meant for opening in the AML Editor (AMLPetriNet: `examples/editor-test/Bridging.aml`, `examples/editor-test/QualityGate.aml`) and check: open, edit, Update InstanceHierarchy, save, reopen, then `ptmap roundtrip` and `ptmap compare` on the saved file (AMLPetriNet: docs/roundtrip-validation.md:218).

## 11. Disable test parallelisation where Aml.Engine state is shared

Aml.Engine keeps process-wide static caches. When several `CAEXDocument` instances are built in parallel, ids come back reassigned and "Collection was modified" surfaces from engine internals. All three .NET test projects that use Aml.Engine carry the same assembly attribute:

```csharp
using Xunit;

// Aml.Engine keeps static caches (XDocumentWrapper.CheckReferences enumerates a
// shared dictionary) that race when several CAEXDocument instances are built in
// parallel: ids come back reassigned and "Collection was modified" surfaces from
// engine internals. Same measure as in fpb-aml-mapper's test suite.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
```
(AMLPetriNet: dotnet/PtMapper.Tests/TestCollection.cs:1; also fpb-aml-mapper: dotnet/FpbMapper.Tests/TestCollection.cs:7, AMLFPB.js: Aml.Editor.Plugin.FPB.Tests/TestCollection.cs:5)

In AMLFPB.js the symptom was intermittent failures of the element-count round-trip test with mismatched counts (AMLFPB.js: Aml.Editor.Plugin.FPB.Tests/TestCollection.cs:1). Intermittent id or count mismatches that disappear on a rerun are this problem until proven otherwise. Add the attribute when the project is created, as the starter does (starter/dotnet/Efl.Tests/AssemblyInfo.cs:6). The same reasoning applies to the plugin at runtime: do not build documents on several threads.

## 12. Test data that is found in clean clones and in CI

The source projects use four strategies. Know what each depends on.

| Strategy | Depends on | Use for | Source |
|---|---|---|---|
| Walk up from `AppContext.BaseDirectory` until marker folders exist | repository layout | fixtures that live in the repository outside the test project | AMLPetriNet: dotnet/PtMapper.Tests/TestFiles.cs:19 |
| `CallerFilePath` of the test source | building from source | golden files that are rewritten and committed | fpb-aml-mapper: dotnet/FpbMapper.Tests/GoldenTests.cs:22 |
| `CopyToOutputDirectory` plus `AppContext.BaseDirectory` | the csproj item | read-only fixtures | AMLFPB.js: Aml.Editor.Plugin.FPB.Tests/SideBySideValidationTests.cs:19; AMLPetriNet: dotnet/PtMapper.Tests/PtMapper.Tests.csproj:19 |
| `Path.Combine("TestData", name)` relative | current directory being the output folder | avoid | fpb-aml-mapper: dotnet/FpbMapper.Tests/SystemLimitNameTests.cs:18 |

The walk-up needs a marker that only the repository root has. AMLPetriNet copies `libraries/` and `examples/` into the test output as well, so a marker of `libraries` alone would stop in `bin/`; the helper requires `examples/paper` too:

```csharp
private static string FindRoot()
{
    var dir = AppContext.BaseDirectory;
    while (dir != null
           && !(Directory.Exists(Path.Combine(dir, "libraries")) && Directory.Exists(Path.Combine(dir, "examples", "paper"))))
        dir = Path.GetDirectoryName(dir);
    return dir ?? throw new InvalidOperationException("repository root not found");
}
```
(AMLPetriNet: dotnet/PtMapper.Tests/TestFiles.cs:19)

The relative-to-current-directory form works under `dotnet test`, which runs from the output folder, and breaks under any runner or script that does not.

The starter walks up from `AppContext.BaseDirectory` until `examples/<file>` exists (starter/dotnet/Efl.Tests/ExampleAndGeometryTests.cs:16), and the package tests walk up until the plugin's csproj appears (starter/plugin/Aml.Editor.Plugin.Efl.Tests/PackageTests.cs:20). Both look for a file, not only a folder name, which avoids the `bin/` copy problem above.

Opening fixtures has its own trap: `CAEXDocument.LoadFromFile` and `SaveToFile` write `CAEX_ClassModel_V.3.0.xsd` next to the file, so a test that loads a committed example leaves a new file in the repository. Load and save through text or streams instead, as `EflDocuments.LoadFile` and `SaveFile` do (starter/dotnet/Efl.Conversion/EflDocuments.cs:43, starter/dotnet/Efl.Conversion/EflDocuments.cs:58).

Other rules:

- **No absolute or sibling paths to other repositories in tests.** AMLPetriNet's paper tests used to read the example from a separate paper repository and skipped when it was absent. The example was moved into `examples/paper/`, the suite was checked green in a fresh clone, and the lookup went through `TestFiles` (AMLPetriNet: dotnet/PtMapper.Tests/PaperRoundTripTests.cs:27).
- **Skips hide failures.** `Xunit.SkippableFact` with `Skip.IfNot(File.Exists(...))` is useful while a fixture is optional (AMLPetriNet: dotnet/PtMapper.Tests/PaperRoundTripTests.cs:39), but once the file is committed a skip means a broken lookup, not a missing file. Convert those tests to plain facts, or fail CI when the skip count is not zero. A leftover skip message such as "the at-2026 repository is not available here" in a test whose file is now in the repository is a sign of this (AMLPetriNet: dotnet/PtMapper.Tests/PaperExampleTests.cs:26).
- **Browser scripts resolve the repository root from their own location**, not from the working directory (AMLPetriNet: web/tools/verify-interop.mjs:21).
- **Optional fixture sets print a note instead of passing silently.** `verify-interop.mjs` states that `examples/interop` is missing and how to create it (AMLPetriNet: web/tools/verify-interop.mjs:31). In CI, make their absence fatal.
- **Tests never write into the repository by default** (section 5.4), except golden regeneration behind an explicit switch.

## 13. CI on GitHub Actions

### 13.1 Reference workflow

AMLPetriNet's workflow builds the modeler bundle, builds the solution, runs the mapper tests, runs the browser checks, validates the written files against the grammar, uploads the plugin package and publishes a release on a version tag, all on one Windows job (AMLPetriNet: .github/workflows/ci.yml:1):

```yaml
jobs:
  windows:
    # Windows, because the plugin targets net8.0-windows and WPF, and because
    # the browser checks run against the same Chromium a developer has here.
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

      # The .NET build refuses to run without the bundle: the plugin and the
      # web application ship it.
      - name: Build the modeler bundle
        working-directory: web
        run: |
          npm ci
          npm run build

      - name: Build
        run: |
          dotnet restore Aml.Editor.Plugin.PetriNet.sln
          dotnet build Aml.Editor.Plugin.PetriNet.sln -c Release --no-restore

      - name: Test the mapper
        run: dotnet test dotnet/PtMapper.Tests -c Release --no-build

      - name: Test the modeler
        working-directory: web
        run: npm test

      - uses: actions/setup-java@v4
        with:
          distribution: temurin
          java-version: 17

      # bash, because PowerShell passes the * through unexpanded.
      - name: Check the PNML against the ISO/IEC 15909-2 grammar
        working-directory: web
        shell: bash
        run: node tools/validate-pnml.mjs ../examples/interop/*.pnml ../examples/quality-gate.pnml ../examples/paper/MPS500_PT.pnml
```
(AMLPetriNet: .github/workflows/ci.yml:13, shortened)

Points worth copying:

- **`windows-latest`** because the plugin targets `net8.0-windows` with WPF. A pure mapper repository can use `ubuntu-latest`, as fpb-aml-mapper does (fpb-aml-mapper: .github/workflows/test.yml:13).
- **Bundle before .NET build.** The plugin csproj has a target that fails the build with a clear message when the bundle is missing (AMLPetriNet: Aml.Editor.Plugin.PetriNet/Aml.Editor.Plugin.PetriNet.csproj:99). This turns a packaging mistake (a plugin shipped without its modeler) into a build error.
- **`-c Release --no-build`** for tests, so the tested binaries are the built ones.
- **`npm test` installs Chromium in `pretest`** (AMLPetriNet: web/package.json:9), so the workflow needs no separate Playwright action.
- **`shell: bash` for globbing.** On Windows runners the default shell is PowerShell, which passes `*.pnml` to Node unexpanded (AMLPetriNet: .github/workflows/ci.yml:59).
- **`setup-java`** only for the grammar check. The grammar and jars are downloaded from pnml.org and Maven Central at run time; a network failure there fails the build. Cache `web/.cache` with `actions/cache` if that becomes a problem.
- **Artifact upload and release on tag**, with `permissions: contents: write` and `fail_on_unmatched_files: true`, so a tag without a package fails instead of producing an empty release:

```yaml
      - uses: actions/upload-artifact@v4
        with:
          name: Aml.Editor.Plugin.PetriNet-nupkg
          path: build/Plugins/Aml.Editor.Plugin.PetriNet/Release/*.nupkg

      # A version tag (v0.1.12, …) publishes the package as a release asset.
      - name: Publish GitHub Release
        if: startsWith(github.ref, 'refs/tags/v')
        uses: softprops/action-gh-release@v2
        with:
          files: build/Plugins/Aml.Editor.Plugin.PetriNet/Release/*.nupkg
          generate_release_notes: true
          fail_on_unmatched_files: true
```
(AMLPetriNet: .github/workflows/ci.yml:65; permissions at .github/workflows/ci.yml:10)

The starter ships the same shape of workflow, written for the root of a repository made from it (starter/.github/workflows/ci.yml:3). It differs in three places worth knowing: Chromium is installed with `npx playwright install chromium` in the bundle step, because the starter's `npm test` has no `pretest` (starter/.github/workflows/ci.yml:39); the web app end-to-end check does run in CI, since `verify-webapp.mjs` builds and starts the server itself (starter/.github/workflows/ci.yml:61-63); and the plugin job is `dotnet test` on the plugin solution, which builds the Release package and runs the package tests on it before the artifact is uploaded (starter/.github/workflows/ci.yml:66-68). It also has one step AMLPetriNet lacks: it writes both example AML files again with the tool and fails when `git diff --exit-code` finds a changed byte, so examples that no longer match the mapper cannot be merged (starter/.github/workflows/ci.yml:45-55). That only works because the files carry a fixed writing time (`eflmap to-aml ... --timestamp 2026-01-01T00:00:00Z`, passed to the optional `writtenAt` of `EflToCaex.Convert`, starter/dotnet/Efl.Conversion/EflToCaex.cs:32-50) and because `.gitattributes` marks `*.aml` as `-text`, so git does not convert line endings and report a difference on a Windows checkout (starter/.gitattributes:1-2). The playbook repository runs the same steps on the starter and on a copy renamed by `tools/new-language.mjs` (.github/workflows/playbook.yml:40-50, :64-90). All four .NET projects under `starter/dotnet/` build with `TreatWarningsAsErrors`, so a new warning fails CI instead of accumulating (starter/dotnet/Efl.Conversion/Efl.Conversion.csproj:7).

### 13.2 Multi-repository builds

AMLFPB.js depends on three sibling repositories through `ProjectReference` and relative content paths. Its workflow checks each repository out into a `path:` that mirrors the local development tree, builds the FPB.JS bundle, and passes the bundle directory explicitly (AMLFPB.js: .github/workflows/build.yml:17, AMLFPB.js: .github/workflows/build.yml:73). The test project references the mapper and the OCL engine by relative paths into those siblings (AMLFPB.js: Aml.Editor.Plugin.FPB.Tests/Aml.Editor.Plugin.FPB.Tests.csproj:25, Aml.Editor.Plugin.FPB.Tests/Aml.Editor.Plugin.FPB.Tests.csproj:28). This works, but the build is only as reproducible as the default branches of three other repositories at that moment. For a new project prefer one repository (AMLPetriNet's layout: `web/`, `dotnet/`, plugin project, `libraries/`, `examples/` side by side), or pin sibling checkouts with `ref:`. See [Architecture](01-architecture.md).

### 13.3 Gaps to avoid

- **A release workflow without tests.** FPB.JS publishes to npm on a tag after `npm ci` and `npm run build`, with no test step (FPB.JS: .github/workflows/release.yml:23). On the cited branch its Vitest and Playwright suites run only locally; a workflow that runs unit tests, e2e tests and the build on every push exists only on the unmerged branch `chore/ci-tests` (FPB.JS commit 4642f78, see [Pitfalls](09-pitfalls.md), PF-CI-02 and PF-CI-03). Gate publishing on the tests.
- **Path filters that skip dependants.** fpb-aml-mapper runs tests only when `dotnet/**` changes (fpb-aml-mapper: .github/workflows/test.yml:3). That is fine within one repository; the plugin that consumes the mapper does not rebuild when the mapper changes, so run the plugin build on a schedule or on dispatch from the mapper.
- **Browser checks that need a server.** Keep scripts that need a running web app out of `npm test` (AMLPetriNet: web/package.json:18) or start the app inside the job; a CI step waiting on a server that is not there fails with a timeout that says nothing.
- **Grepping localised tool output.** `dotnet test` prints its summary in the machine's UI language (on a German Windows "Bestanden!" instead of "Passed!"). Rely on exit codes, or set `DOTNET_CLI_UI_LANGUAGE=en` in scripts that parse output.
- **Playwright retries masking flakiness.** FPB.JS retries twice on CI and uses one worker there (FPB.JS: playwright.config.js:12, FPB.JS: playwright.config.js:15). Retries are acceptable for UI tests; for the format and bridge checks, a retry that passes is a bug report, so keep those scripts retry-free as AMLPetriNet does.

## Checklist

- [ ] Conversion, update, validation and round-trip check live in a UI-free library with its own xunit project.
- [ ] `[assembly: CollectionBehavior(DisableTestParallelization = true)]` in every test project that touches Aml.Engine.
- [ ] Round-trip test model to AML to model, per connection encoding, comparing full element descriptions.
- [ ] Round-trip test exchange format to AML to exchange format with text equality, sample captured from the real modeler.
- [ ] "Sync without edit changes no line" test on the serialised XML, plus a "second update is a no-op" test.
- [ ] Update-in-place tests: user attribute, user interface and link, foreign element, other hierarchies, links from other hierarchies all survive.
- [ ] Deleting a node or connection removes exactly its links and endpoint interfaces; no dangling link anywhere in the document.
- [ ] Shortened lists (waypoints, offsets, flags) leave no tail.
- [ ] Refused updates (bad endpoint, unusable library) leave the XML identical, with a change placed before the error in the input.
- [ ] Determinism tests: same ids twice, ids equal the derived ones, well-formed UUID version and variant, no collision between id kinds, no duplicate `ID` after importing twice.
- [ ] Golden files resolved via `CallerFilePath`, rewritten only behind `UPDATE_GOLDEN=1`, failing on the run that writes them, with a determinism self-check first.
- [ ] Normalisation limited to writing time, schema location, file name, GUID values and key order, each justified in a comment.
- [ ] Library golden: classes built in code, class ids, inheritance and attributes equal the published library file.
- [ ] Shipped example artefacts pinned by tests or by a CI step that writes them again with a fixed timestamp and fails on `git diff`; `*.aml` marked `-text`; regeneration commands documented next to them; rewritten examples reviewed structurally.
- [ ] Example writers default to a temp directory; CI sets the output directory explicitly.
- [ ] One positive and one negative test per validator rule, asserting rule id, severity and element id; report coverage derived from the rule id list.
- [ ] Every "nothing happened" assertion is preceded by a guard that something could have happened (mutation detected, tokens moved).
- [ ] Browser tests run against the built bundle, wait on a readiness flag, forward page errors.
- [ ] Screenshots of the modeler (stored layout and arranged layout) looked at after every visual change; layout tests check flows against nodes and against each other, not only node overlap.
- [ ] Browser tests use `elementFactory`, `modeling`, `elementRegistry` and the selection API; positions are read back from elements, never assumed from clicks.
- [ ] Interop fixtures written by the mapper are loaded into the modeler and compared after canonical re-serialisation.
- [ ] The host bridge is exercised with a stand-in `window.chrome.webview`: import is not an edit, edits are reported, back-to-back imports do not interleave, malformed input is reported.
- [ ] Files both implementations write are validated against the standard grammar with a reference validator; broken copies are committed and expected to fail.
- [ ] Hostile input tests: entity expansion, external entities, invalid ids, non-finite numbers, declared encodings.
- [ ] Real documents from other tools: read, sync unchanged, settle, not mistaken when they hold no model.
- [ ] Plugin contract members checked by reflection; no WPF object created in tests (or STA thread if unavoidable).
- [ ] Package tests on the built `.nupkg`: no contract assembly, no `Aml.Engine.dll`, runtime files and bundle present, `Metadata.xml` and csproj versions equal, display name valid.
- [ ] Test data found via marker walk-up or copied output, never via current directory or sibling repositories; no skipped tests in CI.
- [ ] CI on `windows-latest` for the plugin: bundle build, .NET build, `dotnet test --no-build`, `npm test`, grammar check with `shell: bash`, artifact upload, release on `v*` tag with `fail_on_unmatched_files`.
- [ ] The bundle's absence fails the .NET build.
- [ ] Publishing (npm, release) is gated on the tests.
- [ ] Manual AML Editor check on the editor test documents before a release, followed by `roundtrip` and `compare` on the saved file.

## Where to look

| Topic | File |
|---|---|
| Round trip, format and CAEX | AMLPetriNet: dotnet/PtMapper.Tests/RoundTripTests.cs |
| Update in place | AMLPetriNet: dotnet/PtMapper.Tests/UpdateInPlaceTests.cs |
| Refuse before writing, id repair | AMLPetriNet: dotnet/PtMapper.Tests/IdentityAndSessionTests.cs |
| Deterministic ids | AMLPetriNet: dotnet/PtMapper.Tests/IdentityTests.cs, dotnet/PtMapper.Conversion/PtIds.cs |
| Regression tests from an audit | AMLPetriNet: dotnet/PtMapper.Tests/RegressionTests.cs |
| Links from other hierarchies | AMLPetriNet: dotnet/PtMapper.Tests/ReferenceSurvivalTests.cs |
| Whole-document round trip on a real example, shipped artefacts | AMLPetriNet: dotnet/PtMapper.Tests/PaperRoundTripTests.cs |
| Round-trip check as product code | AMLPetriNet: dotnet/PtMapper.Conversion/PtRoundTripCheck.cs |
| Library golden | AMLPetriNet: dotnet/PtMapper.Tests/PtLibraryGoldenTests.cs |
| Validator and report | AMLPetriNet: dotnet/PtMapper.Tests/ValidatorTests.cs, dotnet/PtMapper.Tests/ConformanceReportTests.cs |
| Foreign documents, hostile input, scale | AMLPetriNet: dotnet/PtMapper.Tests/ForeignDocumentSweepTests.cs, dotnet/PtMapper.Tests/HostileInputTests.cs, dotnet/PtMapper.Tests/ScaleTests.cs |
| Test data lookup, parallelisation | AMLPetriNet: dotnet/PtMapper.Tests/TestFiles.cs, dotnet/PtMapper.Tests/TestCollection.cs, dotnet/PtMapper.Tests/PtMapper.Tests.csproj |
| Browser round trip, interop, bridge, labels, numbers, SVG | AMLPetriNet: web/tools/roundtrip.mjs, web/tools/verify-interop.mjs, web/tools/verify-bridge.mjs, web/tools/verify-labels.mjs, web/tools/verify-numbers.mjs, web/tools/verify-svg.mjs |
| Web app and editing session in a browser | AMLPetriNet: web/tools/verify-webapp.mjs, web/tools/verify-paper-session.mjs |
| Grammar validation | AMLPetriNet: web/tools/validate-pnml.mjs, docs/pnml-conformance.md |
| Round-trip measurement write-up | AMLPetriNet: docs/roundtrip-validation.md |
| CI workflow | AMLPetriNet: .github/workflows/ci.yml |
| Starter CI workflow | starter/.github/workflows/ci.yml |
| Golden files with regeneration switch | fpb-aml-mapper: dotnet/FpbMapper.Tests/GoldenTests.cs, dotnet/FpbMapper.Tests/TestData/ |
| Echo-cycle idempotence on showcase documents | fpb-aml-mapper: dotnet/FpbMapper.Tests/Showcases/ShowcaseRoundTripTests.cs, dotnet/FpbMapper.Tests/Showcases/ShowcaseTests.cs |
| Update in place for a layered language | fpb-aml-mapper: dotnet/FpbMapper.Tests/ConversionTests.cs, dotnet/FpbMapper.Tests/SystemLimitNameTests.cs |
| Mapper CI | fpb-aml-mapper: .github/workflows/test.yml |
| Editor contract snapshot, validator parity | AMLFPB.js: Aml.Editor.Plugin.FPB.Tests/ApiContractTests.cs, Aml.Editor.Plugin.FPB.Tests/SideBySideValidationTests.cs, Aml.Editor.Plugin.FPB.Tests/ValidationPipelineTests.cs |
| Multi-repository CI with release on tag | AMLFPB.js: .github/workflows/build.yml |
| Playwright page object with selection API | FPB.JS: tests/e2e/pages/ModelerPage.js |
| Model state checks through business objects | FPB.JS: tests/e2e/data-integrity.spec.js, tests/e2e/layer-consistency.spec.js |
| Test runner configuration | FPB.JS: playwright.config.js, vitest.config.js, vitest.config.browser.js |
| Starter mapper tests | starter/dotnet/Efl.Tests/RoundTripTests.cs, starter/dotnet/Efl.Tests/ValidatorTests.cs, starter/dotnet/Efl.Tests/ExampleAndGeometryTests.cs, starter/dotnet/Efl.Tests/AssemblyInfo.cs |
| Starter browser checks and harness | starter/web/tools/harness.mjs, starter/web/tools/verify-modeler.mjs, starter/web/tools/verify-bridge.mjs, starter/web/tools/verify-webapp.mjs |
| Starter package tests | starter/plugin/Aml.Editor.Plugin.Efl.Tests/PackageTests.cs |
