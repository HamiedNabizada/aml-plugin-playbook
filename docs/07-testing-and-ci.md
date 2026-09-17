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
- Test data is found by walking up to a repository marker, never via the current directory or a sibling repository; a committed fixture that is missing fails the test instead of skipping it (see §12).
- CI on `windows-latest` for a plugin: bundle, .NET build, tests, browser checks, grammar check, package, release on tag; the starter also writes its example files again with a fixed timestamp and fails on `git diff` (see §13).

Read fully when: you set up the test projects and CI of a new plugin, or before the first converter. Skim when: you only add a rule or a fixture (read §6 or §12) or debug a flaky test (read §11).

This chapter describes how to test a modeler, an exchange format, an AML mapper and an AML Editor plugin so that a user's document never loses content during a sync, and how to run all of it on GitHub Actions. Read it before writing the first converter: the round-trip and update-in-place tests described here are the specification of the mapper, and it is much cheaper to write them first than to retrofit them after the first corrupted file. The evidence comes from AMLPetriNet (xunit plus Playwright scripts plus a grammar check in CI), fpb-aml-mapper and AMLFPB.js (golden files, echo-cycle tests, validator parity), and FPB.JS (Vitest and Playwright end-to-end tests of a diagram-js modeler). Background on the mapping itself is in [AML mapping](04-aml-mapping.md), on the plugin in [Editor plugin](05-editor-plugin.md), and the recurring traps are collected in [Pitfalls](09-pitfalls.md).

## 1. The test pyramid for a modeler plus mapper plus plugin

Organise the tests by what they can see. Each layer catches defects the layer below cannot.

| Layer | Runs in | What it proves | Source example | Symbol |
|---|---|---|---|---|
| Format reader and writer | xunit / Vitest | the exchange format (PNML, JSON) reads and writes stably; defaults are restored | AMLPetriNet: `dotnet/PtMapper.Tests/RoundTripTests.cs` | `PnmlWriteReadIsStable` |
| Mapper round trip | xunit, in memory | model to CAEX and back is the same model, in every connection encoding | `starter/dotnet/Efl.Tests/RoundTripTests.cs` | `AModelSurvivesTheWayIntoAmlAndBack` |
| Identity | xunit | CAEX ids are deterministic, well-formed and collision-free | AMLPetriNet: `dotnet/PtMapper.Tests/IdentityTests.cs` | `DerivedIdsAreWellFormedUuids` |
| Update in place | xunit | foreign content survives, deletions are local, preconditions refuse before writing | AMLPetriNet: `dotnet/PtMapper.Tests/UpdateInPlaceTests.cs` | `UpdatingWithoutChangesTouchesNothing` |
| Library golden | xunit | the classes the mapper creates in code equal the published library artefact | AMLPetriNet: `dotnet/PtMapper.Tests/PtLibraryGoldenTests.cs` | `SystemUnitClassIdsAreStable` |
| Validator rules | xunit | each rule fires on a violation and stays silent on a clean model | AMLPetriNet: `dotnet/PtMapper.Tests/ValidatorTests.cs` | `FindingsCarryTheIdTheModelerUses` |
| Real documents | xunit | documents written by other tools read, sync without change, and settle | AMLPetriNet: `dotnet/PtMapper.Tests/ForeignDocumentSweepTests.cs` | `SyncingItChangesNothingItShouldNot` |
| Hostile input and scale | xunit | entity expansion, malformed ids, 2000 elements | AMLPetriNet: `dotnet/PtMapper.Tests/HostileInputTests.cs`, `dotnet/PtMapper.Tests/ScaleTests.cs` | `AnEntityExpansionAttackIsRefused`, `ARealisticNetGoesThroughTheWholeChainQuickly` |
| Modeler in a browser | Playwright | the bundled modeler's own importer and exporter keep everything; the host bridge protocol behaves | AMLPetriNet: `web/tools/roundtrip.mjs`, `web/tools/verify-bridge.mjs` | `result` (the `page.evaluate` that builds the net), `__hostSend` |
| Cross-implementation interop | Playwright | files the C# mapper wrote come back unchanged from the JS modeler | AMLPetriNet: `web/tools/verify-interop.mjs` | `canonical` |
| Standard conformance | Jing (Java) | the files both converters write are valid against the published grammar | AMLPetriNet: `web/tools/validate-pnml.mjs` | `execFileSync` call with `classpath` |
| Editing session | Playwright plus CLI | a real session through the real modeler changes only what the user changed | AMLPetriNet: `web/tools/verify-paper-session.mjs` | `moveShape` |
| Modeler UI end to end | Playwright test runner | palette, context pad, layers, confirmation dialogs | FPB.JS: `tests/e2e/pages/ModelerPage.js` | `ModelerPage`, `clickByType` |

Two lessons shaped this pyramid:

- A C#-only round trip is not enough. In AMLPetriNet the modeler's own PNML importer dropped every arc name, and a single "Update InstanceHierarchy" renamed every arc of the paper document to its GUID. No .NET test could see it because the net never passed through JavaScript (AMLPetriNet: `docs/roundtrip-validation.md`, section "5. Opening the net in the editor and syncing stores the generated layout"). The browser layer exists for that reason.
- Two converters written by the same team, tested only against each other, can agree on something that is not the standard (AMLPetriNet: `docs/pnml-conformance.md`, introduction). The grammar layer exists for that reason.

The pure logic lives in a library project without UI (`PtMapper.Conversion`, `FpbMapper.Conversion`, `starter/dotnet/Efl.Conversion/`), so almost the whole pyramid runs without the AML Editor. Keep it that way; see section 10.

The starter already carries a small version of this pyramid; copy it and extend it rather than starting empty:

| Starter test | Layer | What it checks |
|---|---|---|
| `starter/dotnet/Efl.Tests/RoundTripTests.cs` | round trip, update in place, identity | both connection encodings, no-op sync, foreign content, refused updates, stable ids, a flow element with unresolved links kept by an update together with the port on the node that still exists (`AnUpdateKeepsAFlowTheCanvasCouldNotShow`), link sides written as `ElementId:InterfaceId` read too (`LinksWrittenAsElementAndInterfaceIdAreReadToo`), a flow from a node to itself with an `Out_` and an `In_` port (`AFlowFromANodeToItselfGetsTwoPortsAndSurvivesTheRoundTrip`) |
| `starter/dotnet/Efl.Tests/ValidatorTests.cs` | validator rules | one broken model per rule id EFL01 to EFL07, the sample breaks none |
| `starter/dotnet/Efl.Tests/ExampleAndGeometryTests.cs` | shared fixture, docking geometry | the example JSON the browser tests load equals the sample built in code; a file with a newer `formatVersion` is read with a warning, a file without the field is read as version 1; flows end on the Store ellipse and the Step box; a node without size docks at its centre instead of NaN |
| `starter/dotnet/Efl.Tests/DiagramInterchangeTests.cs` | shared layout library in documents | a created document carries OMG_DD and every layout type path resolves, in both encodings; a created document is valid against the CAEX schema (`CAEXDocument.Validate`), with a negative control that moves the library to a place the schema forbids; the embedded library equals `starter/libraries/OMG_DD_AttributeTypeLib_v0.1.aml`; the library artefact references the file instead of carrying it; an update of a document that references the file under its own alias (`DD`) writes `DD@...` paths and embeds nothing. None of this shows in a round trip test, because the reader goes by attribute names (`starter/dotnet/Efl.Tests/DiagramInterchangeTests.cs`, summary of `DiagramInterchangeTests`) |
| `starter/dotnet/Efl.Tests/LayoutTests.cs` | layout | the arranged picture checked on the lines, not only on the nodes: no flow through a node and no two flows on one segment for a cycle, a back flow between two nodes, several back flows and a flow that skips a column; a self loop gets three bend points; flows at nodes that already had positions keep their waypoints; arranging twice gives the same file |
| `starter/web/tools/verify-modeler.mjs` | modeler in a browser | 12 checks: boot, import/export identity, docking, a newer format version shown with a warning, tolerant import, node and flow drawn with the palette and the mouse, rules, command stack and undo, delete, move, SVG |
| `starter/web/tools/verify-bridge.mjs` | host bridge | 6 checks: ready, import baseline without change events, debounced edit, back-to-back imports, broken import (only `error`, no `imported`), export/SVG/select requests |
| `starter/web/tools/verify-webapp.mjs` | web app end to end | 7 checks against the built `Efl.Web.dll` (not in `npm test`) |
| `starter/plugin/Aml.Editor.Plugin.Efl.Tests/PackageTests.cs` | plugin package | 13 tests: no `Aml.Editor.Plugin.Contract.dll`, no `Aml.Engine.dll`, eight runtime files present, `THIRD-PARTY-NOTICES.md` packed, versions agree, valid display name |

`Efl.Tests` holds 46 tests (15 round trip, 9 validator, 7 example, geometry and writing time, 7 layout, 8 layout library).

Tests do not see what a person sees. In the trial run of the playbook every browser check passed on a diagram whose arranged form drew a back flow on top of a forward flow and a flow through a node, and on a label far from its line; the first screenshot showed both. `npm run screenshot` in `web/` writes a PNG and the SVG export of the example into `web/screenshots/` (ignored by git), and `npm run screenshot -- --arranged` first strips all layout and lets the mapper arrange it through `eflmap arrange` (`starter/web/tools/screenshot.mjs`, header comment; `starter/web/package.json`, script `screenshot`). Look at both after every change to the renderer, the layouter or the layout code; `LayoutTests.cs` then pins what the picture showed.

## 2. Round-trip tests

### 2.1 Model to AML and back equals

Compare the complete model, not a count. Describe each element as one sortable line (id, kind, name, values, bounds, endpoints, waypoints) and assert the two lists are equal. When it fails, the diff names the property.

The starter does this for both connection encodings with one `[Theory]` (`starter/dotnet/Efl.Tests/RoundTripTests.cs`, `AModelSurvivesTheWayIntoAmlAndBack`). AMLPetriNet compares tuples per element kind, including bounds, and separately checks bend points (AMLPetriNet: `dotnet/PtMapper.Tests/RoundTripTests.cs`, `CaexRoundTripPreservesTheNet`, `CaexRoundTripPreservesBendpoints`).

Count-based assertions are a weak first step. AMLFPB.js started with `Roundtrip_PreservesElementCounts` (AMLFPB.js: `Aml.Editor.Plugin.FPB.Tests/CaexToFpbJsonTests.cs`); the mapper later lost characteristic values while counts stayed equal, which needed a dedicated test (fpb-aml-mapper: `dotnet/FpbMapper.Tests/ConversionTests.cs`, `Roundtrip_PreservesCharacteristicSetpointAndType`). Counts also cannot see names: fpb-aml-mapper's `Convert` writes the name of a sub-process SystemLimit from the process name, which for a sub-process is the name of the decomposed operator (fpb-aml-mapper: `dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs`, `BuildProcess`, the `SetIdentification` call for the SystemLimit), and its round-trip golden records `Erhitzen` for a SystemLimit that the input names `SL_Erhitzen` (`dotnet/FpbMapper.Tests/TestData/Temperieren.roundtrip.golden.json`; `dotnet/FpbMapper.Tests/TestData/Temperieren.json`), so the golden pins the defect instead of catching it. The defect is invisible on the top-level process, because its process name happens to be taken from its SystemLimit. Pick fixtures where every name is distinct from every other name, or such coincidences hide bugs.

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
(AMLPetriNet: `dotnet/PtMapper.Tests/RoundTripTests.cs`, `PnmlThroughCaexAndBackIsIdentical`)

Take the sample from the real modeler's export, not from hand-written XML. AMLPetriNet's `SampledPnml` constant was captured from `web/tools/roundtrip.mjs`, so the C# side and the browser side are checked against the same bytes (AMLPetriNet: `dotnet/PtMapper.Tests/RoundTripTests.cs`, `SampledPnml`).

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
(`starter/dotnet/Efl.Tests/RoundTripTests.cs`, `WritingTheSameModelBackChangesNoLineOfTheDocument`)

Add a convergence test as well: the second update after a real change must be a no-op (AMLPetriNet: `dotnet/PtMapper.Tests/UpdateInPlaceTests.cs`, `RepeatedUpdatesConverge`). fpb-aml-mapper pins the exact cycle the plugin runs during live sync, CAEX to JSON, update in place, CAEX to JSON again, per InstanceHierarchy, and a second variant that compares the saved XML after one and after two cycles (fpb-aml-mapper: `dotnet/FpbMapper.Tests/Showcases/ShowcaseRoundTripTests.cs`, `Showcase_EchoCycle_IsIdempotentPerIh`, `Showcase_EchoCycle_TwiceProducesStableXml`). The comment there names the failure class it prevents: every live sync tick washing corruption deeper into the file (jumping states, vanished boundary states, cleared references).

### 2.4 JSON and AML carry the same model

If the modeler talks JSON and the mapper also accepts another format, test that each path yields the same model. The starter writes the model to JSON, reads it back, converts to CAEX, reads back, and compares with the original description (`starter/dotnet/Efl.Tests/RoundTripTests.cs`, `TheJsonFormatCarriesTheSameModel`). fpb-aml-mapper locks the whole JSON to AML to JSON result against a golden file (section 5).

### 2.5 Compare against the same file loaded twice

Aml.Engine reformats on load and save. Never diff a round-trip result against the file on disk. Load the document twice, run the round trip on one copy, and compare the two trees:

```csharp
var before = CAEXDocument.LoadFromFile(Document).CAEXFile.Node.ToString();

var doc = CAEXDocument.LoadFromFile(Document);
RoundTrip(doc);

Assert.Equal(before, doc.CAEXFile.Node.ToString());
```
(AMLPetriNet: `dotnet/PtMapper.Tests/PaperRoundTripTests.cs`, `AmlToPnmlAndBackLeavesTheDocumentUnchanged`)

Follow the whole-document assertion with property-by-property tests (ids, identification, markings, links into other hierarchies, external references) so that a failure says what broke (AMLPetriNet: `dotnet/PtMapper.Tests/PaperRoundTripTests.cs`, summary of `PaperRoundTripTests`, `TheNetItselfComesBackElementForElement`, `TheCaexIdentityIsTheOneTheDocumentBroughtAlong`, `TheLinksIntoThePlantHierarchySurvive`; `docs/roundtrip-validation.md`, section "AML to PNML to AML").

### 2.6 Put the round-trip check into the product, too

A unit test on a fixture proves nothing about a file somebody edited in the AML Editor and saved. AMLPetriNet exposes the same checks as a library class that the test suite and the command line both use (AMLPetriNet: `dotnet/PtMapper.Conversion/PtRoundTripCheck.cs`, `PtRoundTripCheck`), so `ptmap roundtrip <file.aml>` and `ptmap compare <before.aml> <after.aml>` report `[ok]` or `[FAIL]` per aspect (AMLPetriNet: `dotnet/PtMapper.Tool/Program.cs`, commands `roundtrip` and `compare`, which call `PtRoundTripCheck.Run` and `PtRoundTripCheck.Compare`). The test `TheRoundTripCheckPasses` asserts that the check itself passes on the example and runs all eleven aspects (AMLPetriNet: `dotnet/PtMapper.Tests/PaperRoundTripTests.cs`). Test the checker's ability to fail as well: `TheRoundTripCheckNoticesARetargetedArc` and `ComparingNoticesAChangeOutsideTheNet` (AMLPetriNet: `dotnet/PtMapper.Tests/RegressionTests.cs`, `dotnet/PtMapper.Tests/IdentityAndSessionTests.cs`).

## 3. Update-in-place tests

Most of these tests assert what must *not* change (AMLPetriNet: `dotnet/PtMapper.Tests/UpdateInPlaceTests.cs`, summary of `UpdateInPlaceTests`). Write each one against a seeded document and a snapshot of its XML text.

### 3.1 Foreign content survives

- A user's own attribute on a mapped element survives an update that changes a mapped attribute on the same element (AMLPetriNet: `dotnet/PtMapper.Tests/UpdateInPlaceTests.cs`, `AUsersOwnAttributeSurvives`; fpb-aml-mapper: `dotnet/FpbMapper.Tests/ConversionTests.cs`, `UpdateInPlace_PreservesCustomAttributeOnExistingElement`).
- A user's own interface and an InternalLink between two such interfaces survive; they are not the language's interface class, so the updater must not sweep them up (AMLPetriNet: `dotnet/PtMapper.Tests/UpdateInPlaceTests.cs`, `AUsersOwnInterfaceAndLinkSurvive`).
- A foreign InternalElement inside the managed hierarchy is left alone (AMLPetriNet: `dotnet/PtMapper.Tests/UpdateInPlaceTests.cs`, `AForeignElementInTheHierarchyIsLeftAlone`; `starter/dotnet/Efl.Tests/RoundTripTests.cs`, `AnUpdateKeepsWhatSomebodyElseAddedToTheDocument`).
- Links from another hierarchy into the net and reference attributes survive a sync (AMLPetriNet: `dotnet/PtMapper.Tests/ReferenceSurvivalTests.cs`, `RefObjOnTheNetSurvivesASync`, `ALinkFromAnotherHierarchyIntoTheNetSurvivesASync`).
- Other InstanceHierarchies are not touched at all (AMLPetriNet: `dotnet/PtMapper.Tests/ForeignDocumentSweepTests.cs`, `SyncingItChangesNothingItShouldNot`).
- Attributes that belong to the language but do not fit the element's type (a marking on a transition left by a hand edit) may be cleaned up, while a user attribute next to it stays (AMLPetriNet: `dotnet/PtMapper.Tests/UpdateInPlaceTests.cs`, `AttributesOfTheLanguageThatDoNotFitTheTypeAreCleanedUp`).

### 3.2 Deleting a node removes only its connections

- Removing an arc removes its links and the endpoint interfaces on both nodes (AMLPetriNet: `dotnet/PtMapper.Tests/UpdateInPlaceTests.cs`, `RemovingAnArcRemovesItsLinksAndEndpoints`).
- No link is left pointing at an interface that no longer exists, checked over every link (AMLPetriNet: `dotnet/PtMapper.Tests/UpdateInPlaceTests.cs`, `NoDanglingLinkIsLeftBehind`).
- Deleting a node that a link from *another* hierarchy points into must not leave that link dangling (AMLPetriNet: `dotnet/PtMapper.Tests/ReferenceSurvivalTests.cs`, `DeletingANodeDoesNotLeaveALinkFromAnotherHierarchyDangling`).
- With two copies of the same net in one document, removing an arc in one copy leaves the other copy wired (AMLPetriNet: `dotnet/PtMapper.Tests/RegressionTests.cs`, `RemovingAnArcInOneCopyLeavesTheOtherCopyWired`). This caught interface and link ids that were derived from the model id only and therefore collided across copies; they are now salted with the owner's CAEX id (`starter/dotnet/Efl.Conversion/EflIds.cs`, `Interface`, shows the same signature).
- In the starter: removing a node removes its flows and nothing else, and the result reads back without warnings (`starter/dotnet/Efl.Tests/RoundTripTests.cs`, `RemovingANodeRemovesItsFlowsAndNothingElse`).
- Absence from the incoming model is not always a deletion. A flow element whose links somebody broke in the AML Editor never reached the canvas, so the update must keep it; the starter's test removes one of its links, reads the diagram, updates with that model and asserts the element is still there, and so is the port of the flow's surviving half on the node (`starter/dotnet/Efl.Tests/RoundTripTests.cs`, `AnUpdateKeepsAFlowTheCanvasCouldNotShow`; AMLPetriNet: `dotnet/PtMapper.Tests/RegressionTests.cs`, `AnArcWhoseLinkIsMissingIsReportedAndSurvivesTheNextUpdate`).
- Shrinking lists must not keep their tail: a polyline shortened from three to one waypoint has exactly one waypoint attribute afterwards, a label moved back loses its offset, a flag that became false disappears (AMLPetriNet: `dotnet/PtMapper.Tests/UpdateInPlaceTests.cs`, `AShortenedPolylineDoesNotKeepItsTail`, `ALabelMovedBackLosesItsOffset`, `ATransitionThatStopsBeingSilentLosesTheFlag`).

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
(AMLPetriNet: `dotnet/PtMapper.Tests/IdentityAndSessionTests.cs`, `AnArcToANodeTheNetDoesNotHaveIsRefusedBeforeWriting`)

Note the rename placed *before* the broken arc: without it, an implementation that writes names first and then throws would still pass. The sibling test removes a class from the document's library so that the update cannot instantiate new elements, and asserts nothing was written (AMLPetriNet: `dotnet/PtMapper.Tests/IdentityAndSessionTests.cs`, `AnUpdateTheDocumentsLibraryCannotServeWritesNothing`). The starter carries the same test (`starter/dotnet/Efl.Tests/RoundTripTests.cs`, `AFlowToANodeThatIsNotThereIsRefusedBeforeAnythingIsWritten`).

### 3.4 The end-to-end property

After any update, the hierarchy must read back as exactly the model that went in (AMLPetriNet: `dotnet/PtMapper.Tests/UpdateInPlaceTests.cs`, `AnUpdatedHierarchyReadsBackAsTheSameNet`). Combine a rename, a new node, a changed weight and a new bend point in one target model so the test exercises add, change and keep in one pass.

## 4. Determinism and identity tests

Update in place only works if the same model always yields the same CAEX ids. Otherwise every sync replaces every element and every reference a user added dangles (AMLPetriNet: `dotnet/PtMapper.Conversion/PtIds.cs`, summary of `PtIds`).

Test all of these:

1. Converting the same model twice yields the same set of `ID` attributes, over the whole document, not only InternalElements (`starter/dotnet/Efl.Tests/RoundTripTests.cs`, `TheSameModelAlwaysProducesTheSameIds`; AMLPetriNet: `dotnet/PtMapper.Tests/RoundTripTests.cs`, `MapperIdsAreDeterministic`).
2. The ids in the document are the ones the id function derived. Aml.Engine reassigns ids on insert under some conditions, so assert it instead of assuming it (AMLPetriNet: `dotnet/PtMapper.Tests/IdentityTests.cs`, summary of `IdentityTests`, `ElementIdsAreTheOnesTheMapperDerived`).
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
(AMLPetriNet: `dotnet/PtMapper.Tests/IdentityTests.cs`, `DerivedIdsAreWellFormedUuids`)

4. Different kinds of id derived from the same model id do not collide (element, net, interfaces, links) (AMLPetriNet: `dotnet/PtMapper.Tests/IdentityTests.cs`, `DifferentKindsOfIdDoNotCollide`).
5. Dropping and re-creating the hierarchy yields the same ids (AMLPetriNet: `dotnet/PtMapper.Tests/IdentityTests.cs`, `ReplacingTheHierarchyTwiceKeepsTheSameIds`).
6. Importing the same model twice into one document leaves no duplicate `ID` anywhere, compared case-insensitively (AMLPetriNet: `dotnet/PtMapper.Tests/RegressionTests.cs`, `ImportingTheSameNetTwiceLeavesNoDuplicateIdAnywhere`).
7. Ids stay stable across repeated updates, including after a decompose cycle in a layered language (fpb-aml-mapper: `dotnet/FpbMapper.Tests/ConversionTests.cs`, `UpdateInPlace_PreservesIdsAcrossMultipleCalls`, `UpdateInPlace_DecomposeCycle_YieldsStableSubProcessAmlId`).
8. Hostile ids: characters that are not XML names, duplicates in the input, a copied element that arrives with an id already in use (AMLPetriNet: `dotnet/PtMapper.Tests/HostileInputTests.cs`, `AnIdThatIsNotAValidXmlNameIsReported`; `dotnet/PtMapper.Tests/IdentityAndSessionTests.cs`, `APnmlFileThatUsesAnIdTwiceComesBackUnchangedFromAml`, `ACopiedElementGetsAnIdOfItsOwnAndTheUpdateSaysSo`).
9. The layout algorithm is deterministic too, or every open of a layout-free document produces a different diff (AMLPetriNet: `dotnet/PtMapper.Tests/LayoutTests.cs`, `LayoutIsDeterministic`; `starter/dotnet/Efl.Tests/LayoutTests.cs`, `ArrangingTheSameModelTwiceGivesTheSameDocument`). See [Layout](08-layout.md).

If a converter path is not id-stable by design (fpb-aml-mapper's `Convert` mints fresh GUIDs for process elements and flow interfaces, while the plugin uses `UpdateInPlace`, which preserves ids), say so in the test and canonicalise instead of pretending (fpb-aml-mapper: `dotnet/FpbMapper.Tests/GoldenTests.cs`, comment in `JsonToAml_StructuralDump_MatchesGolden`). Prefer deterministic ids everywhere in a new project; the starter derives all ids from SHA-256 (`starter/dotnet/Efl.Conversion/EflIds.cs`, `EflIds.For`).

## 5. Golden files

Golden files catch every unintended change in output structure. They also create churn, so be deliberate about what goes in them and how they are refreshed.

### 5.1 Three kinds of golden in the source projects

| Kind | What is compared | Source | Symbol |
|---|---|---|---|
| Structural dump | a normalised text dump of the CAEX tree (element name, class path, sorted attribute paths, links) | fpb-aml-mapper: `dotnet/FpbMapper.Tests/GoldenTests.cs` | `JsonToAml_StructuralDump_MatchesGolden` |
| Normalised round-trip output | JSON after JSON to AML to JSON, re-serialised with sorted keys, GUIDs canonicalised | fpb-aml-mapper: `dotnet/FpbMapper.Tests/GoldenTests.cs` | `Roundtrip_JsonToAmlToJson_MatchesGolden` |
| Published artefact | the library the mapper builds in code against the published `.aml` library file, structurally: class names, class ids, inheritance, attribute names, the wording of a description the mapper depends on | AMLPetriNet: `dotnet/PtMapper.Tests/PtLibraryGoldenTests.cs` | `PtLibraryGoldenTests` |
| Shipped example output | the exported PNML and the AML written from it, which accompany a paper, against what the current code writes | AMLPetriNet: `dotnet/PtMapper.Tests/PaperRoundTripTests.cs` | `TheShippedPnmlIsWhatTheMapperProduces`, `TheShippedAmlIsWhatTheMapperProduces` |

The library golden compares structure, not bytes: "element order and formatting are Aml.Engine's business, the class model is ours" (AMLPetriNet: `dotnet/PtMapper.Tests/PtLibraryGoldenTests.cs`, summary of `PtLibraryGoldenTests`). Class ids are part of the contract because documents already written reference them (same file, `SystemUnitClassIdsAreStable`). Conventions that exist only as prose in a class description (waypoint attribute naming) get a test on that description, so a silent removal fails (same file, `ArcDescriptionStatesTheInstanceAttributeConventions`).

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
(fpb-aml-mapper: `dotnet/FpbMapper.Tests/GoldenTests.cs`, `TestDataDir`, `UpdateGolden`, `CompareToGolden`; the message string is shortened here)

Why each detail matters:

- `CallerFilePath` points at the source folder. Writing into `bin/` would regenerate a copy nobody commits.
- Failing on regeneration prevents a CI run with `UPDATE_GOLDEN=1` accidentally set from passing while silently overwriting.
- A missing golden is created and fails too, so adding a new golden is a two-step review.
- Line endings are normalised on both sides; git may check files out either way (AMLPetriNet: `dotnet/PtMapper.Tests/PaperRoundTripTests.cs`, `Normalise`).
- Each golden test first converts twice and asserts equality, so a golden can never be made "stable" over nondeterministic output (fpb-aml-mapper: `dotnet/FpbMapper.Tests/GoldenTests.cs`, the determinism self-check in `JsonToAml_StructuralDump_MatchesGolden`).

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
(AMLPetriNet: `dotnet/PtMapper.Tests/PaperRoundTripTests.cs`, `WithoutWritingTime`)

The comment above it records that Aml.Engine adds the schema location, and a copy of the XSD next to the saved file, depending on state that is not the document's (AMLPetriNet: `dotnet/PtMapper.Tests/PaperRoundTripTests.cs`, comment on `WithoutWritingTime`). The AMLPetriNet `.gitignore` excludes `**/CAEX_ClassModel_V.3.0.xsd` for the same reason. Other things to neutralise:

- `SaveToFile` stamps the target file name into the document. Serialise both sides to the same fixed temp name, or replace the temp name before comparing (fpb-aml-mapper: `dotnet/FpbMapper.Tests/Showcases/ShowcaseRoundTripTests.cs`, `Serialize`; AMLPetriNet: `dotnet/PtMapper.Tests/PaperRoundTripTests.cs`, `TheShippedAmlIsWhatTheMapperProduces`).
- Only two *saved* documents are comparable, because saving adds attributes an in-memory tree lacks (AMLPetriNet: `dotnet/PtMapper.Tests/PaperRoundTripTests.cs`, comment in `TheShippedAmlIsWhatTheMapperProduces`).
- Random GUIDs: replace each distinct GUID by `#1`, `#2`, ... in order of first appearance. This keeps the reference wiring (which element points at which) while ignoring the values (fpb-aml-mapper: `dotnet/FpbMapper.Tests/GoldenTests.cs`, `CanonicalizeGuids`).
- JSON key order: re-serialise with sorted keys (fpb-aml-mapper: `dotnet/FpbMapper.Tests/GoldenTests.cs`, `Normalize`).
- XML writer differences between .NET and the browser (space before `/>`): parse and re-serialise both sides in the same engine before comparing (AMLPetriNet: `web/tools/verify-interop.mjs`, `canonical`).

Do not normalise away anything that a user would see as a change.

### 5.4 Ids change when an example is rewritten

Shipped examples are derived artefacts. When their source document is rewritten, every derived file changes, and the diff is total rather than local.

In AMLPetriNet the paper document was rewritten with new CAEX ids, so `MPS500_PT.pnml` and `MPS500_PT_export.aml` had to be written again. Both are pinned by `TheShippedPnmlIsWhatTheMapperProduces` and `TheShippedAmlIsWhatTheMapperProduces`, which fail until that happens, so the shipped artefacts cannot drift from the code (AMLPetriNet: `docs/roundtrip-validation.md`, section "The files shipped with the example"). The regeneration commands are part of that section. Two effects explain why the diff is total:

- The PNML ids are normalised CAEX GUIDs of the source document, so new CAEX ids mean new PNML ids on every element (AMLPetriNet: `docs/roundtrip-validation.md`, section "1. PNML ids are normalised CAEX ids").
- An AML document written from PNML alone gets ids derived from the PNML ids (UUID v5), so a changed PNML id changes the derived CAEX id as well (AMLPetriNet: `docs/roundtrip-validation.md`, section "2. A document built from PNML alone holds the net and nothing else").

Rules:

- Keep the regeneration commands next to the artefacts, in a doc or a script, never only in someone's shell history.
- Review a rewritten example with a structural compare (`ptmap compare`, a structural dump, or a canonicalised golden), not with a text diff that is 100 percent red.
- Regenerate the source document and everything derived from it in one commit, and note that ids changed. Anyone holding references into the old ids (a paper figure, another hierarchy, a bug report) needs to know.
- Make the output location of example writers configurable. fpb-aml-mapper's showcase writers default to a folder in the sibling plugin repository by walking six directories up from the test binary, with `SHOWCASE_OUTPUT_DIR` as override (fpb-aml-mapper: `dotnet/FpbMapper.Tests/Showcases/ShowcaseTests.cs`, `ShowcaseTests.OutputDir`). CI sets that variable to the runner's temp directory (fpb-aml-mapper: `.github/workflows/test.yml`, `env` of step "Test"), and the round-trip tests produce the files lazily themselves because "CI runs the mapper repo alone" (fpb-aml-mapper: `dotnet/FpbMapper.Tests/Showcases/ShowcaseRoundTripTests.cs`, `GeneratedDir`). A test that writes into a sibling repository by default is a trap on a developer machine; prefer the temp directory as default and an explicit opt-in for writing examples.

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
(AMLPetriNet: `dotnet/PtMapper.Tests/ValidatorTests.cs`, `AnArcBetweenTwoPlacesIsAnError`)

Further tests the source projects found necessary:

- A well-formed model has no findings at all (AMLPetriNet: `dotnet/PtMapper.Tests/ValidatorTests.cs`, `AWellFormedNetHasNoFindings`).
- Overlapping rules do not double-report: an unconnected transition is reported once as unconnected, not also as source and sink (AMLPetriNet: `dotnet/PtMapper.Tests/ValidatorTests.cs`, `AnUnconnectedNodeIsNotAlsoReportedAsSourceAndSink`).
- Findings carry the id the *modeler* uses, because the plugin turns a finding into a select message for the canvas (AMLPetriNet: `dotnet/PtMapper.Tests/ValidatorTests.cs`, `FindingsCarryTheIdTheModelerUses`).
- The example shipped with the repository is itself clean; the test project copies `examples/*.pnml` into the output for this (AMLPetriNet: `dotnet/PtMapper.Tests/ValidatorTests.cs`, `TheSampleNetIsClean`; `dotnet/PtMapper.Tests/PtMapper.Tests.csproj`, the `None` item for `examples\*.pnml`).
- A conformance report lists every rule, derived from the validator's rule id list rather than typed into the test. A hand-written list stopped at PT10 when PT11 was added and the coverage claim went stale (AMLPetriNet: `dotnet/PtMapper.Tests/ConformanceReportTests.cs`, `EveryRuleIsAccountedFor`). The starter does it this way: `EflValidator.RuleIds` lists EFL01 to EFL07 (`starter/dotnet/Efl.Conversion/EflValidator.cs`, `RuleIds`), and a `[Theory]` over that list fails when a rule has no broken model in the test's dictionary, then asserts the rule fires on its own broken model and, apart from EFL02, alone (`starter/dotnet/Efl.Tests/ValidatorTests.cs`, `EachRuleFiresOnItsOwnBrokenModelAndOnlyThere`). A new rule therefore cannot be added without a test.
- Rule ids are stable, and disabling a rule removes only that rule's findings (AMLFPB.js: `Aml.Editor.Plugin.FPB.Tests/ValidationPipelineTests.cs`, `DefaultRules_HaveStableIds`, `DisabledRuleId_IsExcludedFromPass`).
- When two implementations of the same rules exist (hard-coded C# and OCL constraints), prove parity on a model that actually violates them. Mutate a valid model, assert the hard-coded side found something, then assert equality; "an empty-vs-empty comparison proves nothing" (AMLFPB.js: `Aml.Editor.Plugin.FPB.Tests/SideBySideValidationTests.cs`, `Violated_model_produces_identical_findings`). Load the constraints from the same embedded artefacts the runtime uses, not from inline copies in the test.

The general principle behind the last point, used again in sections 7 and 8: **every "nothing happened" assertion needs a guard that something could have happened.** In the bridge test, before asserting that firing a transition in simulation mode reports no edit, the script checks that firing really changed the marking, "otherwise the next check proves nothing" (AMLPetriNet: `web/tools/verify-bridge.mjs`, the check comparing `markingDuring` with `markingBefore`).

## 7. Browser tests against the bundled modeler

### 7.1 Test the bundle, not the sources

Run the browser checks against the same build output the plugin and web app ship. AMLPetriNet serves `web/dist` with a tiny static server on a random port, because module scripts need http, not `file://` (AMLPetriNet: `web/tools/serve.mjs`, `serve`). The page sets a readiness flag after the modeler is created, and every script waits for it instead of sleeping:

```js
await page.goto(`http://127.0.0.1:${port}/index.html`);
await page.waitForFunction(() => window.ptnReady === true, null, { timeout: 15000 });
```
(AMLPetriNet: `web/tools/roundtrip.mjs`, the `ptnReady` wait; the flag is set in `web/index.html`, `window.ptnReady`)

Expose the modeler instance on `window` for tests (`window.ptn`, AMLPetriNet: `web/index.html`, the `createPetriNetModeler` call; `window.fpbjs`, FPB.JS: `app/app.js`, `window.fpbjs`). Forward `pageerror` and console errors to the test output (AMLPetriNet: `web/tools/roundtrip.mjs`, the `pageerror` and `console` handlers), otherwise a failing import shows up as a timeout with no reason.

The starter puts serving, page setup and error collection into one shared harness: it serves `dist/` on a random port (`starter/web/tools/harness.mjs`, `serve`), opens a fresh page per check, collects `pageerror` and console errors and fails the check on any (same file, `errors`), and waits for `window.efl.modeler` instead of a separate flag (same file, the `waitForFunction` on `window.efl?.modeler`).

AMLPetriNet uses plain Node scripts with the `playwright` library and exit codes; `npm test` chains them and `pretest` installs Chromium (AMLPetriNet: `web/package.json`, scripts `test` and `pretest`). The starter's `npm test` runs `verify-modeler.mjs` and `verify-bridge.mjs`; Chromium is installed once with `npm run browsers` rather than on every test run (`starter/web/package.json`, scripts `test` and `browsers`). FPB.JS uses the `@playwright/test` runner with a page object and a `webServer` that starts the dev server (FPB.JS: `playwright.config.js`, `webServer`). Both work. Scripts are simpler to run in CI without a dev server; the runner gives traces, videos and retries.

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
(AMLPetriNet: `web/tools/roundtrip.mjs`, `result`, shortened)

Then export, import the export, export again, and compare the two strings (AMLPetriNet: `web/tools/roundtrip.mjs`, `nativeBefore`). Read state back through `elementRegistry` and business objects rather than counting SVG nodes (AMLPetriNet: `web/tools/verify-webapp.mjs`, `shapes`; FPB.JS: `tests/e2e/data-integrity.spec.js`, test "creating elements populates process data correctly").

Use real UI interaction only where the UI itself is under test (palette entries, context pad entries, dialogs). Find palette buttons by accessible name (`getByRole('button', { name })`). FPB.JS finds them by `title` (FPB.JS: `tests/e2e/pages/ModelerPage.js`, `clickPaletteAndPlace`, the `getByTitle` call), which works with the diagram-js 15.3.0 its lock file pins (FPB.JS: `package-lock.json`, entry `node_modules/diagram-js`); later diagram-js 15 releases set `aria-label` and `role="button"` on palette entries instead of `title`, and a lookup by title then finds nothing.

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
(FPB.JS: `tests/e2e/pages/ModelerPage.js`, `clickByType`)

Then click the context pad entry by its `data-action` attribute (FPB.JS: `tests/e2e/pages/ModelerPage.js`, `clickContextPadAction`). The layer-consistency spec switched from position-based to type-based deletion for this reason (FPB.JS: `tests/e2e/layer-consistency.spec.js`, the `deleteByType` call in the first state deletion test).

Further observations from FPB.JS:

- Collect elements recursively from the root. Connections are children of the container shape (the SystemLimit), not of the root (FPB.JS: `tests/e2e/pages/ModelerPage.js`, `clickConnectionByIndex` with its `collectConnections`).
- Inspect nested model state through business objects in `page.evaluate`, for example the child layer of a decomposed operator (FPB.JS: `tests/e2e/layer-consistency.spec.js`, `childBO` in the first state deletion test).
- Modeling rules constrain where you may place elements. FPB.JS's rule requires an output to lie more than 50 px below its source, so test positions must follow it (FPB.JS: `tests/e2e/full-model.spec.js`, comment on `POSITIONS`). Keep positions in one constant table per spec.
- Page-object assertion helpers must import `expect`. FPB.JS's `ModelerPage.js` calls `expect` in `expectElementExists` without importing it (FPB.JS: `tests/e2e/pages/ModelerPage.js`, `expectElementExists`), which only stays hidden because no spec calls those helpers.

### 7.4 Coordinate offsets

A Playwright `click({ position })` on `.djs-container` is in container pixels. A diagram-js shape's `x`/`y` is its top-left corner, and creating a shape from the palette centres it on the pointer. In FPB.JS work a click at (350, 150) produced an element at about (325, 125). The container-to-diagram mapping also shifts with zoom and scroll. Therefore:

- Never assert positions derived from click coordinates; read `element.x`, `element.y`, `width`, `height` back from the registry.
- When you need a click on an existing element, compute the centre from the element's bounds, or use the selection API above.
- In the exchange format, document which point a coordinate means (centre or top-left) and test it, as AMLPetriNet does for PNML, whose position is the centre (AMLPetriNet: `dotnet/PtMapper.Tests/RoundTripTests.cs`, `PnmlPositionIsTheCentreAndBoundsAreTopLeft`). See [Exchange format](03-exchange-format.md).

### 7.5 Cross-implementation interop

When the mapper (C#) and the modeler (JS) both read and write the exchange format, load files written by the mapper into the real modeler and compare what comes back. AMLPetriNet's fixture set covers an empty net, a net without layout, a silent transition, markup characters in ids and names, moved labels with bend points, and named arcs; the fixtures are written by a CLI command of the mapper (`ptmap fixtures`) and committed (AMLPetriNet: `web/tools/verify-interop.mjs`, header comment and `fixtureFiles`; `dotnet/PtMapper.Tool/Program.cs`, command `fixtures`). The script says which convention mismatches this catches: centre versus top-left, which points an arc carries, how a flag is spelled (AMLPetriNet: `web/tools/verify-interop.mjs`, header comment).

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
(AMLPetriNet: `web/tools/verify-bridge.mjs`, `__hostSend`)

What AMLPetriNet asserts over that protocol (AMLPetriNet: `web/tools/verify-bridge.mjs`, the `check` calls from "boot posts ready with the page url" to "a broken import does not take the modeler down"):

- boot posts `ready`;
- an import is answered with an echo baseline and reaches the canvas;
- an import raises no change event of its own, checked after waiting past the 300 ms debounce window;
- a user edit is reported with the new value;
- an unknown id in `selectElement` is not an error;
- two imports back to back do not interleave and report no edits;
- token simulation reports no edits, an export request during simulation is refused with a reason, leaving simulation restores the exact pre-simulation model;
- a malformed import is reported and does not take the modeler down.

Each of these corresponds to a bug class described in [Editor plugin](05-editor-plugin.md): echo loops, phantom pending changes, saving a simulation state.

The starter avoids the stand-in altogether. Its `post` sends every message to `window.chrome?.webview` when present and also dispatches it as an `efl-bridge-out` DOM event (`starter/web/src/bridge.js`, `post`), and `connectBridge` returns its `handle` function so a test or a plain web page can deliver host messages directly (same file, `connectBridge`). The harness records the events in an init script (`starter/web/tools/harness.mjs`, `window.__posted`), and `verify-bridge.mjs` waits 600 ms, longer than the 300 ms change debounce (`starter/web/src/bridge.js`, `CHANGE_DEBOUNCE_MS`), before asserting that an import raised no change (`starter/web/tools/verify-bridge.mjs`, `settle`). Its broken-import check also asserts that a failed import posts `error` and no `imported` (same file, check "a broken import is reported as an error and leaves the page working"), because the host takes `imported` as "the canvas shows what I sent" and would drop its unsaved edits (`starter/web/src/bridge.js`, `runImport`). Either approach works; what matters is that the real bridge code runs, not a copy of it.

### 7.7 Capturing downloads in headless Chromium

A web page that saves via a blob URL and `<a download>` reports the download as canceled in headless Chromium. Capture the content where the page makes it by wrapping `URL.createObjectURL` in an init script, record the entry synchronously, and wait until `blob.text()` has resolved before reading (AMLPetriNet: `web/tools/verify-webapp.mjs`, `window.__downloads`, `saves`). Then assert what matters for a web mapper: the saved file is the document that was opened, with the model updated, not a fresh document holding only the model (same file, check "the saved document is the one that was opened"). See [Web app](06-web-app.md).

### 7.8 A measured editing session

`verify-paper-session.mjs` opens a real AML file in the web mapper, moves one named node with `modeling.moveShape`, saves, and writes the result so that `ptmap compare` can report what the session changed (AMLPetriNet: `web/tools/verify-paper-session.mjs`, header comment and the `moveShape` call). The measurement result is recorded in AMLPetriNet: `docs/roundtrip-validation.md`, section "5. Opening the net in the editor and syncing stores the generated layout". Which node is moved matters: dragging a node straightens the routed arcs attached to it, so the stored attribute count differs (same section). Choose the node deliberately and name it in the script invocation.

This script and `verify-webapp.mjs` need a running web mapper and are not part of `npm test` or CI (AMLPetriNet: `web/package.json`, script `test`). Run them before a release.

The starter's `verify-webapp.mjs` starts the server itself: it builds `Efl.Web` and then launches the built `Efl.Web.dll` directly with the project folder as working directory, because a process started through `dotnet run` survives killing `dotnet run`, keeps the port and locks the build output for the next run (`starter/web/tools/verify-webapp.mjs`, `build`, `server`). It polls `/api/health` before the first check (same file, `waitForServer`).

### 7.9 Unit tests of modeler code

FPB.JS runs unit tests with Vitest in happy-dom (FPB.JS: `vitest.config.js`, `environment`) and a separate browser-mode config for integration tests (FPB.JS: `vitest.config.browser.js`, `browser`). Lessons from that suite:

- happy-dom does not fully support `getAttributeNS`; XML mapping code must be tested with jsdom or in a real browser.
- The `is()` helper without a moddle instance returns `undefined`, not `false`; assert with `toBeFalsy()`.
- Test helpers, constants, rule predicates and import utilities as pure functions. Modules that need `eventBus`, `modeling` and `canvas` together are better covered by the browser layer than by large mock setups (a mock event bus exists at FPB.JS: `tests/setup/mocks/MockEventBus.js`, `MockEventBus`).

## 8. Conformance against a standard grammar, with negative controls

If your exchange format has a published grammar (RELAX NG, XSD, JSON Schema), validate the files both implementations write against it with a reference validator.

AMLPetriNet validates PNML against the ISO/IEC 15909-2 P/T net grammar from pnml.org with Jing (AMLPetriNet: `web/tools/validate-pnml.mjs`, header comment). The tool:

- downloads the five grammar files and two jars once into `web/.cache/pnml` (git-ignored), rewriting the absolute pnml.org include hrefs to local ones (AMLPetriNet: `web/tools/validate-pnml.mjs`, the loop over `GRAMMAR_FILES`);
- runs Jing with `-i`, because the grammar declares the `id` attribute with conflicting ID types and Jing refuses it as a schema error otherwise; id uniqueness is then checked by validator rule PT01 instead (AMLPetriNet: `web/tools/validate-pnml.mjs`, header comment and the `execFileSync` call);
- exits 1 if any file is invalid.

```js
execFileSync('java', ['-cp', classpath, 'com.thaiopensource.relaxng.util.Driver', '-i',
  join(cache, 'ptnet.pntd'), path], { stdio: 'pipe' });
```
(AMLPetriNet: `web/tools/validate-pnml.mjs`, `execFileSync` call)

**Negative controls.** A validator that says "valid" to everything is indistinguishable from a working one until you feed it something broken. AMLPetriNet ran deliberately broken copies of the example: marking `-1`, marking `viel`, inscription `0`, a wrong net type, an unknown element; each was rejected with the expected reason (AMLPetriNet: `docs/pnml-conformance.md`, section "The validator does reject things"). The same control applies when a switch like `-i` weakens the check: know exactly what it no longer catches and cover that elsewhere. In AMLPetriNet those controls are recorded in the doc but not part of CI; for a new project, commit the broken files (for example `examples/invalid/*.xml`) and add a CI step that expects each of them to fail.

Further conformance practice from the same document:

- Cross-check the reference validator with a second implementation once (AMLPetriNet used libxml2; AMLPetriNet: `docs/pnml-conformance.md`, section "Against the grammar").
- Run files from other tools through both directions and tabulate grammar result, mapper result and modeler result per file (AMLPetriNet: `docs/pnml-conformance.md`, section "Against files from other tools"). This found a lost label position for default-weight arcs, a duplicate id written twice into AML, and node sizes with more decimals than the grammar allows; each fix got a regression test named in the table (same document, section "What it found").
- Read the grammar's value types, not only the structure. Sizes as decimals with one fractional digit and coordinates without exponent notation were found by reading, then tested in the browser (AMLPetriNet: `docs/pnml-conformance.md`, section "What it found"; `web/tools/verify-numbers.mjs`, header comment).
- Write down what the grammar does not check (id uniqueness, bipartite arcs) so that nobody reads "valid" as "correct" (AMLPetriNet: `docs/pnml-conformance.md`, section "What this does not show").
- Do not copy third-party test files into the repository when their licenses differ; list where each one is (AMLPetriNet: `docs/pnml-conformance.md`, section "Against files from other tools").

The same idea applies to CAEX: an AML document the mapper writes should validate against the CAEX 3.0 schema and should resolve every class path inside the file when it is meant to be self-contained (AMLPetriNet: `dotnet/PtMapper.Tests/SelfContainedTests.cs`, `EveryClassPathResolvesInsideTheFile`, `TheChildOrderStaysSchemaValid`).

## 9. Hostile input, real documents and scale

- **XML attacks.** A reader for user-supplied files must refuse entity expansion and must not resolve external entities (AMLPetriNet: `dotnet/PtMapper.Tests/HostileInputTests.cs`, `AnEntityExpansionAttackIsRefused`, `AnExternalEntityIsNotResolved`).
- **Malformed values.** Non-numeric markings fall back to defaults, negative values are reported, non-finite coordinates never reach the model or the file, zero-sized nodes do not divide by zero (AMLPetriNet: `dotnet/PtMapper.Tests/HostileInputTests.cs`, `NonNumericMarkingsAndWeightsFallBackToDefaults`, `ZeroSizedNodesDoNotDivideByZero`; `dotnet/PtMapper.Tests/RegressionTests.cs`, `NonFiniteCoordinatesNeverReachTheModelOrTheFile`; `starter/dotnet/Efl.Tests/ExampleAndGeometryTests.cs`, `ANodeWithoutSizeDocksAtItsCentreInsteadOfNaN`).
- **Encoding.** A file that declares its own encoding is read from bytes, not from a string decoded with a guessed encoding (AMLPetriNet: `dotnet/PtMapper.Tests/RegressionTests.cs`, `APnmlFileDeclaringItsOwnEncodingIsReadFromTheBytes`).
- **Documents from other tools.** Load every real document you have, check that it reads, that a sync without edits changes nothing, that syncing twice settles, that the model read after sync equals the model read before, and that files with the vocabulary but no model (libraries, other languages) are not mistaken for one (AMLPetriNet: `dotnet/PtMapper.Tests/ForeignDocumentSweepTests.cs`, from `ItReadsAsANetTheModelerCouldShow` to `AFileWithoutANetIsNotMistakenForOne`). Alias-qualified class paths must read like unaliased ones (fpb-aml-mapper: `dotnet/FpbMapper.Tests/AliasToleranceTests.cs`, `Convert_AliasQualifiedDocument_MatchesUnaliasedResult`). Layout-free input must still convert completely and open in the modeler: FPB.JS's importer dereferences the visual entry of every connection, so fpb-aml-mapper pins that a document without stored layout still yields a visual entry with at least two waypoints per connection (fpb-aml-mapper: `dotnet/FpbMapper.Tests/WaypointFallbackTests.cs`, summary of `WaypointFallbackTests`, `Convert_WithoutConnectionLayout_EveryConnectionGetsFallbackWaypoints`, `Convert_WithoutAnyLayout_StillEmitsTwoWaypointsPerConnection`). Invented geometry is a trade-off: it keeps the modeler from arranging those connections itself (see [Layout](08-layout.md)).
- **Scale.** Run the full chain on 50 and 500 element pairs and an update of a single value on a large model, with generous time limits that only a quadratic path would exceed (AMLPetriNet: `dotnet/PtMapper.Tests/ScaleTests.cs`, `ARealisticNetGoesThroughTheWholeChainQuickly`, `UpdatingALargeNetIsNotQuadraticEnoughToHurt`). Keep the limits generous; CI runners are slower than developer machines.

## 10. Plugin tests without the editor

The AML Editor cannot run in CI. Structure the plugin so that almost nothing needs it:

1. **Keep logic out of the WPF view.** In AMLPetriNet the plugin project contains the view, the WebView2 bridge and diagnostics only (`Aml.Editor.Plugin.PetriNet/Bridge/`, `Diagnostics/`, `PetriNetPlugin.xaml.cs`); conversion, update, validation, round-trip check and compare live in `PtMapper.Conversion` and are tested there. AMLFPB.js tests validator classes that live in the plugin assembly directly, without creating the view (AMLFPB.js: `Aml.Editor.Plugin.FPB.Tests/ValidationPipelineTests.cs`, `ValidationPipelineTests`, which uses `Aml.Editor.Plugin.FPB.Validation`).
2. **Snapshot the editor contract by reflection.** The plugin depends on members of `Aml.Editor.Plugin.Contract` that vary between editor versions. AMLFPB.js asserts their existence so a renamed member fails in CI instead of silently degrading at the user's site (AMLFPB.js: `Aml.Editor.Plugin.FPB.Tests/ApiContractTests.cs`, `ApiContractTests`, for example `INotifyAMLDocumentLoad_HasExpectedMembers`). The contract package is referenced with `PrivateAssets=all`, so the test locates the loaded assembly after touching a plugin type (same file, `ContractAssembly`).
3. **Test the JS side of the bridge with a stand-in host** (section 7.6).
4. **If you must instantiate WPF objects in a test** (a `UserControl`, a `Dispatcher`-bound helper), run that code on a dedicated thread set to `ApartmentState.STA` and join it, because the xunit worker threads are MTA and WPF throws on them. Neither source project does this today; both avoid it by keeping the view thin, which is the better option.
5. **Target the right framework.** A test project that references the plugin must target the plugin's Windows TFM (AMLFPB.js uses `net8.0-windows7.0`, AMLFPB.js: `Aml.Editor.Plugin.FPB.Tests/Aml.Editor.Plugin.FPB.Tests.csproj`, `TargetFramework`); the mapper test project stays `net8.0` and runs on any OS (fpb-aml-mapper: `dotnet/FpbMapper.Tests/FpbMapper.Tests.csproj`, `TargetFramework`).
6. **Test the package, not only the code.** The failures that cost the most time in the editor were silent packaging mistakes: a second `Aml.Editor.Plugin.Contract.dll` makes the editor skip the plugin, a missing bundle gives an empty panel (see [Pitfalls](09-pitfalls.md), PF-CCH-01, PF-PLG-21). The starter's plugin test project references the plugin with `ReferenceOutputAssembly="false"` only so that the package is built first (`starter/plugin/Aml.Editor.Plugin.Efl.Tests/Aml.Editor.Plugin.Efl.Tests.csproj`, the `ProjectReference` to `Aml.Editor.Plugin.Efl.csproj`), then opens the newest `.nupkg` of the csproj version and asserts (`starter/plugin/Aml.Editor.Plugin.Efl.Tests/PackageTests.cs`): no contract assembly (`ThePackageDoesNotShipTheContractAssembly`), no `Aml.Engine.dll` (`ThePackageDoesNotShipWhatTheEditorAlreadyLoads`), plugin DLL, mapper DLL, WebView2 DLLs, `WebView2Loader.dll` and the bundle files present (`ThePackageShipsEverythingThePluginNeedsAtRuntime`), `Metadata.xml` version equals the csproj version (`MetadataAndProjectAgreeOnTheVersion`), and the display name matches `^[A-Za-z_][A-Za-z0-9_]*$` and is the one the plugin class sets (`TheDisplayNameIsUsableAsAnXmlAndWpfName`).
7. **Manual editor check before a release.** Keep a small set of documents meant for opening in the AML Editor (AMLPetriNet: `examples/editor-test/Bridging.aml`, `examples/editor-test/QualityGate.aml`) and check: open, edit, Update InstanceHierarchy, save, reopen, then `ptmap roundtrip` and `ptmap compare` on the saved file (AMLPetriNet: `docs/roundtrip-validation.md`, section "Checking a file the AML Editor saved").

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
(AMLPetriNet: `dotnet/PtMapper.Tests/TestCollection.cs`, `CollectionBehavior`; also fpb-aml-mapper: `dotnet/FpbMapper.Tests/TestCollection.cs`, AMLFPB.js: `Aml.Editor.Plugin.FPB.Tests/TestCollection.cs`, same attribute)

In AMLFPB.js the symptom was intermittent failures of the element-count round-trip test with mismatched counts (AMLFPB.js: `Aml.Editor.Plugin.FPB.Tests/TestCollection.cs`, comment on `CollectionBehavior`). Intermittent id or count mismatches that disappear on a rerun are this problem until proven otherwise. Add the attribute when the project is created, as the starter does (`starter/dotnet/Efl.Tests/AssemblyInfo.cs`, `CollectionBehavior`). The same reasoning applies to the plugin at runtime: do not build documents on several threads.

## 12. Test data that is found in clean clones and in CI

The source projects use four strategies. Know what each depends on.

| Strategy | Depends on | Use for | Source | Symbol |
|---|---|---|---|---|
| Walk up from `AppContext.BaseDirectory` until marker folders exist | repository layout | fixtures that live in the repository outside the test project | AMLPetriNet: `dotnet/PtMapper.Tests/TestFiles.cs` | `FindRoot` |
| `CallerFilePath` of the test source | building from source | golden files that are rewritten and committed | fpb-aml-mapper: `dotnet/FpbMapper.Tests/GoldenTests.cs` | `TestDataDir` |
| `CopyToOutputDirectory` plus `AppContext.BaseDirectory` | the csproj item | read-only fixtures | AMLFPB.js: `Aml.Editor.Plugin.FPB.Tests/SideBySideValidationTests.cs`; AMLPetriNet: `dotnet/PtMapper.Tests/PtMapper.Tests.csproj` | `TestDataPath`; the `None` item for `libraries\**\*.aml` |
| `Path.Combine("TestData", name)` relative | current directory being the output folder | avoid | fpb-aml-mapper: `dotnet/FpbMapper.Tests/ConversionTests.cs` | `LoadTestData` |

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
(AMLPetriNet: `dotnet/PtMapper.Tests/TestFiles.cs`, `FindRoot`)

The relative-to-current-directory form works under `dotnet test`, which runs from the output folder, and breaks under any runner or script that does not.

The starter walks up from `AppContext.BaseDirectory` until `examples/<file>` exists (`starter/dotnet/Efl.Tests/ExampleAndGeometryTests.cs`, `ExampleFile`), and the package tests walk up until the plugin's csproj appears (`starter/plugin/Aml.Editor.Plugin.Efl.Tests/PackageTests.cs`, `PluginFolder`). Both look for a file, not only a folder name, which avoids the `bin/` copy problem above.

Opening fixtures has its own trap: `CAEXDocument.LoadFromFile` and `SaveToFile` write `CAEX_ClassModel_V.3.0.xsd` next to the file, so a test that loads a committed example leaves a new file in the repository. Load and save through text or streams instead, as `EflDocuments.LoadFile` and `SaveFile` do (`starter/dotnet/Efl.Conversion/EflDocuments.cs`, `LoadFile`, `SaveFile`).

Other rules:

- **No absolute or sibling paths to other repositories in tests.** AMLPetriNet's paper tests used to read the example from a separate paper repository and skipped when it was absent. The example was moved into `examples/paper/`, the suite was checked green in a fresh clone, and the lookup went through `TestFiles` (AMLPetriNet: `dotnet/PtMapper.Tests/PaperRoundTripTests.cs`, `Document`, which calls `TestFiles.Paper`).
- **Skips hide failures.** A committed fixture that is missing must fail the test, not skip it. AMLPetriNet's paper and sweep tests once used `Xunit.SkippableFact` with `Skip.IfNot(File.Exists(...))`, which made sense while the files lived outside the repository; after the files were committed, a skip could only mean a broken lookup, and a green run no longer proved that the tests had run (from the project history). Now `PaperRoundTripTests`, `PaperExampleTests` and `ForeignDocumentSweepTests` are plain `[Fact]`/`[Theory]` over files found through `TestFiles` (AMLPetriNet: `dotnet/PtMapper.Tests/PaperRoundTripTests.cs`, `AmlToPnmlAndBackLeavesTheDocumentUnchanged`; `dotnet/PtMapper.Tests/PaperExampleTests.cs`, `ThePaperExampleIsReadable`; `dotnet/PtMapper.Tests/ForeignDocumentSweepTests.cs`, `Documents`). Keep a skip only for a file that is genuinely optional, and then fail CI when the skip count is not zero.
- **Browser scripts resolve the repository root from their own location**, not from the working directory (AMLPetriNet: `web/tools/verify-interop.mjs`, `repoRoot`).
- **Optional fixture sets print a note instead of passing silently.** `verify-interop.mjs` states that `examples/interop` is missing and how to create it (AMLPetriNet: `web/tools/verify-interop.mjs`, `fixtureFiles`). In CI, make their absence fatal.
- **Tests never write into the repository by default** (section 5.4), except golden regeneration behind an explicit switch.

## 13. CI on GitHub Actions

### 13.1 Reference workflow

AMLPetriNet's workflow builds the modeler bundle, builds the solution, runs the mapper tests, runs the browser checks, validates the written files against the grammar, and uploads the plugin package on one Windows job with read-only permissions; a separate job publishes a release on a version tag (AMLPetriNet: `.github/workflows/ci.yml`, jobs `windows` and `release`, see below):

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
(AMLPetriNet: `.github/workflows/ci.yml`, job `windows`, shortened)

Points worth copying:

- **`windows-latest`** because the plugin targets `net8.0-windows` with WPF. A pure mapper repository can use `ubuntu-latest`, as fpb-aml-mapper does (fpb-aml-mapper: `.github/workflows/test.yml`, `runs-on` of job `test`).
- **Bundle before .NET build.** The plugin csproj has a target that fails the build with a clear message when the bundle is missing (AMLPetriNet: `Aml.Editor.Plugin.PetriNet/Aml.Editor.Plugin.PetriNet.csproj`, target `VerifyPtnJsDist`). This turns a packaging mistake (a plugin shipped without its modeler) into a build error.
- **`-c Release --no-build`** for tests, so the tested binaries are the built ones.
- **`npm test` installs Chromium in `pretest`** (AMLPetriNet: `web/package.json`, script `pretest`), so the workflow needs no separate Playwright action.
- **`shell: bash` for globbing.** On Windows runners the default shell is PowerShell, which passes `*.pnml` to Node unexpanded (AMLPetriNet: `.github/workflows/ci.yml`, step "Check the PNML against the ISO/IEC 15909-2 grammar").
- **`setup-java`** only for the grammar check. The grammar and jars are downloaded from pnml.org and Maven Central at run time; a network failure there fails the build. Cache `web/.cache` with `actions/cache` if that becomes a problem.
- **Least privilege: read by default, write only in the release job.** The workflow sets `permissions: contents: read` at the top, so building and testing, which run code from every pull request, cannot write to the repository. The Windows job uploads the package as an artifact; a separate `release` job runs only for a version tag, after the Windows job, and is the only one with `contents: write`.
- **The tag must match the package version.** Before publishing, the release job checks that the artifact contains a package whose version equals the tag without its `v`, so a tag `v0.1.15` cannot publish a 0.1.14 package. `fail_on_unmatched_files: true` additionally makes a tag without a package fail instead of producing an empty release:

```yaml
permissions:
  contents: read

jobs:
  windows:
    # ... build and test steps as above ...
      - uses: actions/upload-artifact@v4
        with:
          name: Aml.Editor.Plugin.PetriNet-nupkg
          path: build/Plugins/Aml.Editor.Plugin.PetriNet/Release/*.nupkg

  release:
    # Only this job may write, and only for a version tag. The tag has to match
    # the package version, or a tag v0.1.15 would publish a 0.1.14 package.
    if: startsWith(github.ref, 'refs/tags/v')
    needs: windows
    runs-on: ubuntu-latest
    permissions:
      contents: write
    steps:
      - uses: actions/download-artifact@v4
        with:
          name: Aml.Editor.Plugin.PetriNet-nupkg
          path: nupkg

      - name: Check that the tag matches the package version
        shell: bash
        run: |
          version="${GITHUB_REF_NAME#v}"
          test -f "nupkg/Aml.Editor.Plugin.PetriNet.${version}.nupkg" || { ls nupkg; echo "no package for version ${version}"; exit 1; }

      - name: Publish GitHub Release
        uses: softprops/action-gh-release@v2
        with:
          files: nupkg/*.nupkg
          generate_release_notes: true
          fail_on_unmatched_files: true
```
(AMLPetriNet: `.github/workflows/ci.yml`, `permissions` at the top of the file, the `actions/upload-artifact` step of job `windows`, and job `release` with steps "Check that the tag matches the package version" and "Publish GitHub Release"; shortened)

The starter ships the same shape of workflow, written for the root of a repository made from it (`starter/.github/workflows/ci.yml`, header comment). It differs in three places worth knowing: Chromium is installed with `npx playwright install chromium` in the bundle step, because the starter's `npm test` has no `pretest` (same file, step "Build the modeler bundle"); the web app end-to-end check does run in CI, since `verify-webapp.mjs` builds and starts the server itself (step "Test the web app end to end"); and the plugin job is `dotnet test` on the plugin solution, which builds the Release package and runs the package tests on it before the artifact is uploaded (step "Build and test the plugin package"). It also has one step AMLPetriNet lacks: it writes both example AML files again with the tool and fails when `git diff --exit-code` finds a changed byte, so examples that no longer match the mapper cannot be merged (step "Check that the example files are current"). That only works because the files carry a fixed writing time (`eflmap to-aml ... --timestamp 2026-01-01T00:00:00Z`, passed to the optional `writtenAt` of `EflToCaex.Convert`, `starter/dotnet/Efl.Conversion/EflToCaex.cs`, `Convert`) and because `.gitattributes` marks `*.aml` as `-text`, so git does not convert line endings and report a difference on a Windows checkout (`starter/.gitattributes`, the `*.aml -text` rule). The playbook repository runs the same steps on the starter and on a copy renamed by `tools/new-language.mjs` (`.github/workflows/playbook.yml`, step "Check that the example files are current" and job `renamed-copy`). All four .NET projects under `starter/dotnet/` build with `TreatWarningsAsErrors`, so a new warning fails CI instead of accumulating (`starter/dotnet/Efl.Conversion/Efl.Conversion.csproj`, `TreatWarningsAsErrors`).

### 13.2 Multi-repository builds

AMLFPB.js depends on three sibling repositories through `ProjectReference` and relative content paths. Its workflow checks each repository out into a `path:` that mirrors the local development tree, builds the FPB.JS bundle, and passes the bundle directory explicitly (AMLFPB.js: `.github/workflows/build.yml`, the checkout steps of job `build` and step "Restore + build plugin" with `FpbJsDistDir`). The test project references the mapper and the OCL engine by relative paths into those siblings (AMLFPB.js: `Aml.Editor.Plugin.FPB.Tests/Aml.Editor.Plugin.FPB.Tests.csproj`, the `ProjectReference` items for `FpbMapper.Conversion.csproj` and `OCL.NET.Core.csproj`). This works, but the build is only as reproducible as the default branches of three other repositories at that moment. For a new project prefer one repository (AMLPetriNet's layout: `web/`, `dotnet/`, plugin project, `libraries/`, `examples/` side by side), or pin sibling checkouts with `ref:`. See [Architecture](01-architecture.md).

### 13.3 Gaps to avoid

- **A release workflow without tests.** FPB.JS publishes to npm on a tag after `npm ci` and `npm run build`, with no test step (FPB.JS: `.github/workflows/release.yml`, job `publish`). `release.yml` is the only workflow in the FPB.JS repository (`https://github.com/HamiedNabizada/FPB.JS`), so its Vitest and Playwright suites run only locally, and nothing runs unit tests, e2e tests and the build on every push (see [Pitfalls](09-pitfalls.md), PF-CI-02 and PF-CI-03). Gate publishing on the tests.
- **Path filters that skip dependants.** fpb-aml-mapper runs tests only when `dotnet/**` changes (fpb-aml-mapper: `.github/workflows/test.yml`, `on.push.paths` and `on.pull_request.paths`). That is fine within one repository; the plugin that consumes the mapper does not rebuild when the mapper changes, so run the plugin build on a schedule or on dispatch from the mapper.
- **Browser checks that need a server.** Keep scripts that need a running web app out of `npm test` (AMLPetriNet: `web/package.json`, script `test:webapp`) or start the app inside the job; a CI step waiting on a server that is not there fails with a timeout that says nothing.
- **Grepping localised tool output.** `dotnet test` prints its summary in the machine's UI language (on a German Windows "Bestanden!" instead of "Passed!"). Rely on exit codes, or set `DOTNET_CLI_UI_LANGUAGE=en` in scripts that parse output.
- **Playwright retries masking flakiness.** FPB.JS retries twice on CI and uses one worker there (FPB.JS: `playwright.config.js`, `retries`, `workers`). Retries are acceptable for UI tests; for the format and bridge checks, a retry that passes is a bug report, so keep those scripts retry-free as AMLPetriNet does.

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
- [ ] Test data found via marker walk-up or copied output, never via current directory or sibling repositories; tests on committed fixtures are plain facts that fail when the file is missing; no skipped tests in CI.
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
| Regression tests | AMLPetriNet: dotnet/PtMapper.Tests/RegressionTests.cs |
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
| Update in place for a layered language | fpb-aml-mapper: dotnet/FpbMapper.Tests/ConversionTests.cs |
| Connection visuals for layout-free documents | fpb-aml-mapper: dotnet/FpbMapper.Tests/WaypointFallbackTests.cs |
| Mapper CI | fpb-aml-mapper: .github/workflows/test.yml |
| Editor contract snapshot, validator parity | AMLFPB.js: Aml.Editor.Plugin.FPB.Tests/ApiContractTests.cs, Aml.Editor.Plugin.FPB.Tests/SideBySideValidationTests.cs, Aml.Editor.Plugin.FPB.Tests/ValidationPipelineTests.cs |
| Multi-repository CI with release on tag | AMLFPB.js: .github/workflows/build.yml |
| Playwright page object with selection API | FPB.JS: tests/e2e/pages/ModelerPage.js |
| Model state checks through business objects | FPB.JS: tests/e2e/data-integrity.spec.js, tests/e2e/layer-consistency.spec.js |
| Test runner configuration | FPB.JS: playwright.config.js, vitest.config.js, vitest.config.browser.js |
| Starter mapper tests | starter/dotnet/Efl.Tests/RoundTripTests.cs, starter/dotnet/Efl.Tests/ValidatorTests.cs, starter/dotnet/Efl.Tests/ExampleAndGeometryTests.cs, starter/dotnet/Efl.Tests/AssemblyInfo.cs |
| Starter browser checks and harness | starter/web/tools/harness.mjs, starter/web/tools/verify-modeler.mjs, starter/web/tools/verify-bridge.mjs, starter/web/tools/verify-webapp.mjs |
| Starter package tests | starter/plugin/Aml.Editor.Plugin.Efl.Tests/PackageTests.cs |
