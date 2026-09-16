# 09 Pitfalls catalogue

**In short**

- This is a lookup catalogue: search for the symptom you see, or read the section of the area you are about to work on (see "How to read an entry").
- Aml.Engine: append with `Insert(x, false)`, compare wrappers by `ID` or `Node`, disable test parallelisation, load and save through text or streams, check `CAEXFile` for null after loading (see §1).
- Ids: derive every CAEX id deterministically, salt child ids with the owner's CAEX id, one canonical id form per side, one identity function for reader and updater (see §2).
- Update in place is separate from import, reuses existing wiring and names, cleans links document-wide and never deletes what the reader skipped (see §3).
- Exchange format: typed structures end to end, awaitable import on a copy that replaces the previous model, hardened XML reading, invariant and finite numbers (see §4).
- diagram-js: handle `connection.reconnect`, return explicit `false` from rules, add a `connection.start` rule for the connect tool, declare `$inject`, implement `revert`, no `textRenderer` in plain diagram-js, set an explicit root (see §5).
- Bridge: buffer until `ready`, reset on navigation and renderer crash, detect echoes by content, queue imports, let a buffered push lose against the ready handler, no "imported" after a failed import (see §6).
- Plugin: never raise `IsDocumentLoaded` from `DocumentLoaded`, no teardown in `Unloaded`, generation token for rebuilds, display name of letters, digits and `_`, no contract DLL in the package (see §7, §8).
- Layout, testing, build, CI and deployment failures with their fixes follow in §9 to §13; the Checklist at the end condenses all entries.

Read fully when: never needed in one go. Skim when: before starting an area (read that section) or when a symptom appears (search).

This chapter lists every concrete problem the source projects ran into while building an
AutomationML Editor plugin, a mapper and a diagram-js modeler for a graphical language, with
the root cause and what fixed or prevents it. Search it by symptom ("plugin does not show up",
"edits vanish", "empty canvas", "ids change on every save") or read the area you are about to
work on before you start. The chapters explain how to do things right; this catalogue is the
evidence of what goes wrong otherwise. The main lines of advice are in
[04 AML mapping](04-aml-mapping.md), [02 Modeler](02-modeler-diagram-js.md),
[05 Editor plugin](05-editor-plugin.md), [07 Testing and CI](07-testing-and-ci.md) and
[08 Layout](08-layout.md).

---

## How to read an entry

- **ID** `PF-<area>-<nn>`. Areas: `AML` Aml.Engine and CAEX, `ID` ids, `UPD` update in place,
  `FMT` exchange format, `DJS` diagram-js modeler, `WV` WebView2 and bridge, `PLG` plugin
  lifecycle and packaging, `CCH` editor caches and installed state, `LAY` layout, `TST`
  testing, `BLD` build and bundling, `CI`, `DEP` deployment.
- **Symptom** is what a user or developer sees. **Cause** is the verified root cause.
  **Fix** is what the source project did, or what prevents it in a new project.
  **Evidence** cites code (project, repo-relative path, line) or a commit (project, sha).
- **Status** appears only when a problem is not fixed in the cited project.
- Citations into FPB.JS point at the working branch `fix/reconnect-command`, which carries
  the import and layer-consistency fixes of September 2026 ahead of `master`. Commit shas
  are given for those fixes so they can be found on any branch. A few cited FPB.JS commits
  are on other unmerged branches only; those entries say so.
- Evidence from `starter/...` means the starter already contains the fix or the check; copy
  it rather than rebuilding it.

---

## 1. Aml.Engine and CAEX

#### PF-AML-01 Elements come out in reverse order
- **Symptom:** after a write, places, transitions or process elements appear in the AML tree
  in the reverse of the order they were created; diffs against the previous file are huge.
- **Cause:** `Insert(child)` on Aml.Engine collections has `asFirst = true` as its default and
  prepends.
- **Fix:** wrap every insert in one helper that calls `Insert(child, false)`.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Conversion/PtWrite.cs:98-104; the starter does the
  same in `starter/dotnet/Efl.Conversion/EflWrite.cs:61-63`.

#### PF-AML-02 Identity checks on Aml.Engine objects never match
- **Symptom:** "is this link inside my net" is always false; a cleanup routine reports far too
  many notes or removes too much; `Assert.Same` fails on the same element.
- **Cause:** Aml.Engine creates a new CLR wrapper around the same `XElement` on every property
  access, so `ReferenceEquals` on wrappers is always false.
- **Fix:** compare by CAEX `ID`, or compare the underlying `Node` (`XElement`), which is
  reference-stable.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Conversion/PtNetUpdater.cs:522-525;
  AMLPetriNet: dotnet/PtMapper.Tests/PaperExampleTests.cs:114;
  `starter/dotnet/Efl.Conversion/EflUpdater.cs:288-289` (compares by `ID`).

#### PF-AML-03 Tests fail intermittently with "Collection was modified"
- **Symptom:** random test failures inside Aml.Engine internals, sometimes with ids that come
  back reassigned; rerunning makes them pass.
- **Cause:** Aml.Engine keeps process-wide static caches (`XDocumentWrapper.CheckReferences`
  enumerates a shared dictionary) that race when several `CAEXDocument` instances are built in
  parallel.
- **Fix:** disable xUnit parallelisation for every assembly that touches Aml.Engine.
- **Evidence:** fpb-aml-mapper: dotnet/FpbMapper.Tests/TestCollection.cs:3-7 (introduced with
  commit 05384f7); AMLPetriNet: dotnet/PtMapper.Tests/TestCollection.cs:3-7;
  `starter/dotnet/Efl.Tests/AssemblyInfo.cs:6`.

#### PF-AML-04 Two runs of the same code produce different files
- **Symptom:** a byte-exact golden test flips between green and red without code changes; an
  XSD file appears next to saved documents.
- **Cause:** when saving, Aml.Engine adds `xsi:schemaLocation` (and writes the CAEX XSD beside
  the file) depending on state outside the document; `LastWritingDateTime` changes on every
  write.
- **Fix:** normalise both attributes away before comparing; ignore `CAEX_ClassModel_V.3.0.xsd`
  in version control. `CAEXDocument.LoadFromFile` and `SaveToFile` both drop that schema file
  next to the document; reading the file as text and saving through a stream avoids it.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Tests/PaperRoundTripTests.cs:457-470;
  `starter/dotnet/Efl.Conversion/EflDocuments.cs:38-58`.

#### PF-AML-05 `CreateClassInstance` does not compile
- **Symptom:** `CreateClassInstance(string)` is not found on a SystemUnitClass.
- **Cause:** it is an extension method in `Aml.Engine.CAEX.Extensions`.
- **Fix:** add `using Aml.Engine.CAEX.Extensions;` and instantiate from the class rather than
  building elements by hand, so the instance carries the class's attributes and interfaces.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Conversion/PtWrite.cs:2-3 and :84;
  fpb-aml-mapper commit 5f6e629 ("CAEX-conformant instantiation via CreateClassInstance").

#### PF-AML-06 Hand-built InternalLink partner strings do not resolve
- **Symptom:** links written as `"<ElementId>:<InterfaceId>"` strings are not resolved by CAEX
  path resolution.
- **Cause:** the partner side strings were composed manually instead of letting Aml.Engine set
  them from the interface objects.
- **Fix:** keep the `ExternalInterfaceType` objects and assign `link.AInterface` /
  `link.BInterface`.
- **Evidence:** fpb-aml-mapper commit 2426e61;
  `starter/dotnet/Efl.Conversion/EflWrite.cs:191-205`.

#### PF-AML-07 Link endpoints read from foreign files are not found
- **Symptom:** a file written by another tool loses its connections on import.
- **Cause:** `RefPartnerSideA/B` appears both as `InternalElementID:ExternalInterfaceID` and as
  a bare interface id.
- **Fix:** accept both on read (split at the last colon); GUIDs never contain a colon.
- **Evidence:** fpb-aml-mapper: dotnet/FpbMapper.Conversion/CaexToFpbJson.cs:923-934;
  `starter/dotnet/Efl.Conversion/CaexToEfl.cs:167-179` (used by reader and updater);
  `starter/dotnet/Efl.Tests/RoundTripTests.cs:137` (both forms read, and an update neither
  duplicates nor drops such links).

#### PF-AML-08 Documents that reference the library by alias are not recognised
- **Symptom:** a valid AML file that pulls the domain library in through an
  `ExternalReference` opens as "no model found".
- **Cause:** class paths are alias-qualified (`Alias@Lib/Class`) per IEC 62714, while the
  mapping tables stored the document-internal form.
- **Fix:** strip the alias in every read-side path comparison.
- **Evidence:** fpb-aml-mapper: dotnet/FpbMapper.Conversion/FpbMappings.cs:32-45 (commit
  157d1c9, with `AliasToleranceTests.cs`).

#### PF-AML-09 A new document opens in the AML Editor with unresolved geometry and references
- **Symptom:** a net created by the plugin and saved elsewhere opens with its layout attributes
  and `refObj` types unresolved.
- **Cause:** the AML Editor does not follow file references to external libraries.
- **Fix:** embed the small shared libraries (diagram interchange, object references) in new
  documents; leave documents that already name them, inline or by reference, as they are.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Conversion/PtLibraries.cs:58-72;
  `starter/dotnet/Efl.Conversion/EflDiagramInterchange.cs:45-60` (embeds OMG_DD unless the document
  already carries or references it); `starter/dotnet/Efl.Tests/DiagramInterchangeTests.cs:41-57`.

#### PF-AML-10 Dangling alias on every layout attribute after a sync
- **Symptom:** after an update, `RefAttributeType` values read `OMG_DD@OMG_DD_AttributeTypeLib/...`
  in a document that carries that library inline; the round-trip check refuses the result.
- **Cause:** the writer used one fixed spelling of the attribute type path, while documents
  use three forms (inline, aliased reference, none yet).
- **Fix:** resolve the path form once per document before writing and follow the document's
  own convention.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Conversion/PtDiPaths.cs:7-18 and :54-76;
  `starter/dotnet/Efl.Conversion/EflDiagramInterchange.cs:76-99`;
  `starter/dotnet/Efl.Tests/DiagramInterchangeTests.cs:109-128` (a document that references the
  library as `DD` gets `DD@...` paths and no embedded copy).

#### PF-AML-11 A declared default value reads as zero
- **Symptom:** an attribute whose class declares `DefaultValue` 5 is read as 0.
- **Cause:** the reader looked only at `Value`.
- **Fix:** fall back to `DefaultValue` when `Value` is empty.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Conversion/CaexToPtNet.cs:306-318.

#### PF-AML-12 Update throws halfway on a hand-edited library
- **Symptom:** "An item with the same key has already been added" during an update, leaving a
  half-written document.
- **Cause:** a user-edited SystemUnitClassLib contained a class name twice and `ToDictionary`
  threw.
- **Fix:** build lookups with `TryAdd` (first wins) wherever the input comes from a document.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Conversion/PtWrite.cs:70-76; the same pattern in
  AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs:111-115.

#### PF-AML-13 Credentials written into every generated AML file
- **Symptom:** every exported file contains a URL with basic-auth credentials for the base
  library.
- **Cause:** the `ExternalReference` path constant embedded the credentials.
- **Fix:** reference libraries by relative file name; never put secrets into constants that
  end up in output documents.
- **Evidence:** fpb-aml-mapper commit e866fad; fpb-aml-mapper:
  dotnet/FpbMapper.Conversion/FpbMappings.cs (class `AmlBase`);
  `starter/dotnet/Efl.Conversion/EflLibraries.cs:28-34` (the base library by file name).

#### PF-AML-14 A broken upload fails later with a NullReferenceException
- **Symptom:** a web API answers 500 for a file that is simply not AML; the stack trace points
  at code far from the loading.
- **Cause:** `CAEXDocument.LoadFromString` does not reject every non-AML text; for some inputs
  it returns a document whose `CAEXFile` is null.
- **Fix:** load through one helper that turns both an exception and a null `CAEXFile` into a
  `FormatException` with a message, and map that to a client error.
- **Evidence:** `starter/dotnet/Efl.Conversion/EflDocuments.cs:10-36`.

#### PF-AML-15 A connection from a node to itself gets one port for both ends
- **Symptom:** a self loop written into AML has one `Out_`/`In_` interface on the node, and both
  links of the connection attach to it.
- **Cause:** the writer kept node ports in a map keyed by node and connection. Both ends of a self
  loop are on the same node, so the second end found the first end's port.
- **Fix:** one port per connection end, keyed by node, connection and direction; enumerate the
  ends of a node so that a self loop yields two. Use the same helpers in the writer and the
  updater, and test the round trip and a no-op update of a self loop in every encoding, even when
  the language's validator forbids self loops.
- **Evidence:** `starter/dotnet/Efl.Conversion/EflToCaex.cs:187-206`;
  `starter/dotnet/Efl.Conversion/EflUpdater.cs:97-107`;
  `starter/dotnet/Efl.Tests/RoundTripTests.cs:190-214`.

#### PF-AML-16 A shared library rebuilt from its description instead of taken from the published file
- **Symptom:** a document holds two AttributeTypeLibs with the same name (for example two
  `OMG_DD_AttributeTypeLib`) that do not agree on ids and attribute layout. Nothing fails in the
  mapper's own round trip tests, because the reader goes by attribute names
  (`starter/dotnet/Efl.Tests/DiagramInterchangeTests.cs:12-15`).
- **Cause:** the layout library was generated in code from a prose description of its types
  instead of embedding the published `.aml` file. Its ids and its attribute layout were guessed, so
  as soon as the real library came into the same document (from another tool, or added by hand)
  there were two incompatible libraries of one name. Found in the playbook's own trial run.
- **Fix:** ship the published file itself (`starter/libraries/OMG_DD_AttributeTypeLib_v0.1.aml`),
  embed it as an assembly resource, insert it unchanged, and test that the embedded copy equals the
  file. Apply the same rule to ObjectReferences: obtain the published file through the library
  manager of the AutomationML Editor, never rebuild its types from prose.
- **Evidence:** `starter/dotnet/Efl.Conversion/Efl.Conversion.csproj:13-18`;
  `starter/dotnet/Efl.Conversion/EflDiagramInterchange.cs:7-17`, `:42-60`, `:101-112`;
  `starter/dotnet/Efl.Tests/DiagramInterchangeTests.cs:86-95`.

---

## 2. Ids

#### PF-ID-01 One object on two layers needs one id in the modeler but two in CAEX
- **Symptom:** "duplicated states are not saved"; positions of the sub-layer copy are lost on
  every update.
- **Cause:** the modeler represents a boundary state on parent and child layer with one shared
  id. CAEX requires unique ids, so the second copy got a random id on write, and the reader
  never mapped it back to the shared id. The update then matched only the parent copy and
  removed the child copy as orphan.
- **Fix:** derive the child copy's CAEX id deterministically from (shared id, sub-process id),
  link it to the original with `refObj`, and reunify on read by surfacing the `refObj` value as
  the element id.
- **Evidence:** fpb-aml-mapper: dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs:544-565;
  fpb-aml-mapper: dotnet/FpbMapper.Conversion/CaexToFpbJson.cs:328-339 (commit 157d1c9).

#### PF-ID-02 Ids change on every conversion
- **Symptom:** converting the same model twice yields different CAEX ids; golden files cannot
  be compared; links from other hierarchies break after a re-export.
- **Cause:** `Guid.NewGuid()` for process elements and interfaces in the green-field path.
- **Fix:** derive ids with a name-based hash (UUID v5 style) from stable inputs; in tests,
  canonicalise remaining GUIDs to placeholders in order of first appearance.
- **Evidence:** fpb-aml-mapper: dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs:522-542;
  fpb-aml-mapper: dotnet/FpbMapper.Tests/GoldenTests.cs:63-72; starter:
  `starter/dotnet/Efl.Conversion/EflIds.cs`.

#### PF-ID-03 Name-based UUIDs do not show version 5
- **Symptom:** deterministic ids render with a random digit where the UUID version should be.
- **Cause:** `new Guid(byte[])` reads the first three fields little-endian, so the version
  nibble of the rendered third group comes from `bytes[7]`, not `bytes[6]`.
- **Fix:** stamp the version into `bytes[7]` and the variant into `bytes[8]`; test the rendered
  string, not the bytes.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Conversion/PtIds.cs:38-46.
- **Status:** fpb-aml-mapper still stamps `bytes[6]`
  (dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs:538 and :562). The ids stay deterministic and
  unique; only the version digit is wrong. Changing it now would change every derived id.

#### PF-ID-04 Braced and bare GUIDs do not match each other
- **Symptom:** connections silently not updated; jump-to-element from a finding does nothing;
  lookups fail for files whose links had no explicit id.
- **Cause:** CAEX writes ids as `{xxxxxxxx-...}` (B format), the modeler stores them bare, and
  a fallback that minted a braced id leaked into the JSON.
- **Fix:** pick one canonical form per side, convert at the boundary, and compare
  brace-insensitively everywhere else.
- **Evidence:** fpb-aml-mapper: dotnet/FpbMapper.Conversion/CaexToFpbJson.cs:938-957;
  AMLFPB.js: Aml.Editor.Plugin.FPB/Views/FindingRow.cs:41-44; fpb-aml-mapper commit e6c5b67.

#### PF-ID-05 `refObj` never resolves in validation
- **Symptom:** the rule "refObj must resolve to a uniqueIdent" fails for every boundary state
  and sub-process the mapper wrote.
- **Cause:** `refObj` was written in braced CAEX form while `uniqueIdent` values are bare.
- **Fix:** write every rule-visible reference in the form the rule compares against.
- **Evidence:** fpb-aml-mapper commits 99efcda and 7e8cc4b.

#### PF-ID-06 Importing the same net twice links the copies together
- **Symptom:** duplicate `xs:ID` values after importing a file twice; deleting an arc in one
  copy removes the links of the other.
- **Cause:** interface ids were derived from the language-level id alone, which is identical in
  both copies.
- **Fix:** salt derived child ids with the owner's CAEX id, which is already unique per
  document.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Conversion/PtIds.cs:51-59.

#### PF-ID-07 A valid AML file is refused by the modeler
- **Symptom:** "This net cannot be shown: 18 element id(s) are not valid XML names"; or a whole
  import fails over one umlaut.
- **Cause:** documents put the braced CAEX GUID into `Identification/id`, which is not an
  `xs:ID`; the modeler's parser also accepts only ASCII names (`[a-z_][\w-.]*`), stricter than
  XML.
- **Fix:** normalise ids deterministically for the diagram (`{7AE6...}` becomes `_7AE6...`,
  umlauts transliterated), keep the document's own ids, and match through the normalised form.
  Report invalid ids as a validation finding instead of blocking display.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Conversion/PtIdText.cs:5-45;
  AMLPetriNet: dotnet/PtMapper.Conversion/PtValidator.cs:79-83.

#### PF-ID-08 A sync doubles every element of an older document
- **Symptom:** after the first update, each element exists twice.
- **Cause:** documents written against an older library version carry no `Identification`
  attribute. The reader fell back to the CAEX name, the updater did not, so it recognised
  nothing and created everything again.
- **Fix:** use the same identity function in reader and updater, including the name fallback;
  add the missing `Identification` on the first sync.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Conversion/CaexToPtNet.cs:205-214.

#### PF-ID-09 Reader and updater pair up the wrong elements
- **Symptom:** after an update, attributes of one element land on another whose name reduces
  to the same normalised id.
- **Cause:** normalised ids are made distinct by numbering, and the order of claiming differed
  between reader and updater.
- **Fix:** claim names in the same order in both (net first, then children in document order,
  same element kinds).
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Conversion/PtNetUpdater.cs:165-187.

#### PF-ID-10 Copying an element in the AML Editor duplicates its language id
- **Symptom:** two elements with the same `Identification/id`; the canvas shows one.
- **Cause:** the editor's copy keeps attribute values.
- **Fix:** number the copy on read, give it its own id on write, and report the change in the
  update notes.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Conversion/PtNetUpdater.cs:159-163 and :296-302.

#### PF-ID-11 Renaming a hierarchy in the editor creates a second one on the next sync
- **Symptom:** a new, identically structured InstanceHierarchy appears after the user renamed
  the old one in the tree.
- **Cause:** the hierarchy was found by name only.
- **Fix:** stamp a deterministic id on the hierarchy and find it by id.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Conversion/PtNetToCaex.cs:78-86.

#### PF-ID-12 Id allocation is slow or collides with ids outside the model
- **Symptom:** writing a 2000-element net takes twice as long as needed; a new id collides
  with a class id or an element in a foreign hierarchy.
- **Cause:** per-element lookup through the engine index, which also does not know every id.
- **Fix:** read every `ID` attribute of the document once into a set and claim from it.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Conversion/PtIds.cs:114-141.

---

## 3. Update in place

#### PF-UPD-01 Every sync appends another InstanceHierarchy
- **Symptom:** the AML tree fills with copies of the model after each edit.
- **Cause:** the import entry point `ImportInto` appends a fresh hierarchy; it is right for a
  first import and wrong for edit synchronisation.
- **Fix:** a separate `UpdateInPlace` that finds the existing hierarchy, matches elements by id,
  and adds, updates and removes only what changed.
- **Evidence:** fpb-aml-mapper: dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs:90-116 (commit
  05384f7).

#### PF-UPD-02 A sync without edits rewrites existing interfaces
- **Symptom:** diffing a file before and after a no-op sync shows renamed interfaces with new
  ids (`NodeArcEnd_1` became `Out_<arcId>`); links from other tools break.
- **Cause:** the updater recreated wiring in its own naming instead of reusing what the document
  had.
- **Fix:** read the existing wiring (role to interface, node and arc to interface, interface pair
  to link) first and create only what is missing.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Conversion/PtNetUpdater.cs:192-196 and :304-330.

#### PF-UPD-03 Editing one node embeds four libraries into the file
- **Symptom:** a document that deliberately references its domain library externally grows by
  the full library after one edit, and new elements use different class paths than their
  neighbours.
- **Cause:** creating an instance needs the class definitions, so they were added and left.
- **Fix:** borrow the libraries only while instantiating, remove them afterwards and rewrite the
  new paths to the document's alias.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Conversion/PtLibraryLoan.cs:6-19;
  AMLPetriNet: dotnet/PtMapper.Tests/ForeignConventionTests.cs:9-16.

#### PF-UPD-04 A sync renames elements another tool had named
- **Symptom:** `Arc_Idle_to_Start` becomes `a1` after an update in which nobody renamed anything.
- **Cause:** the updater wrote the CAEX name from the language name on every update.
- **Fix:** write the CAEX name only when the language name actually changed, and always for a
  freshly created instance (which is named after its class).
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Conversion/PtWrite.cs:118-143.

#### PF-UPD-05 Deleting an element leaves or silently removes links elsewhere
- **Symptom:** a link from a plant element in another hierarchy points at a deleted element; or
  it disappears without notice.
- **Cause:** link cleanup looked only inside the model's own element.
- **Fix:** scan the whole document for links to removed interfaces, remove them, report each
  removal outside the model, and leave ambiguous ids alone. Do not report the language's own
  links inside the diagram, or every ordinary delete produces a note.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Conversion/PtNetUpdater.cs:479-542;
  `starter/dotnet/Efl.Conversion/EflUpdater.cs:275-294` (notes only links whose owner is not
  the diagram).

#### PF-UPD-06 Elements the diagram could not show are deleted on update
- **Symptom:** an arc with a missing link or an unnamed endpoint disappears from the document
  after the next update.
- **Cause:** "absent from the incoming model" was treated as "deleted by the user", although the
  reader had skipped it.
- **Fix:** record what the reader left out and keep those elements (with a note). Read that set
  before any removal. Keep the node ports of such a connection as well; removing them cuts the half
  of it that still exists.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Conversion/PtNetUpdater.cs:198-203;
  AMLPetriNet: dotnet/PtMapper.Conversion/CaexToPtNet.cs:191-196;
  `starter/dotnet/Efl.Conversion/CaexToEfl.cs:106-116` (marks unresolved flows),
  `starter/dotnet/Efl.Conversion/EflUpdater.cs:67-69` and `:157-164` (keeps them with a note),
  `:109-121` and `:174-175` (keeps their ports),
  `starter/dotnet/Efl.Tests/RoundTripTests.cs:165`.

#### PF-UPD-07 Sub-layer copies from other id schemes are orphaned on every update
- **Symptom:** boundary copies in files written by a showcase generator or by hand are removed
  and recreated on each update, losing their positions.
- **Cause:** the updater re-derived the expected id, which only matches files written by the
  same derivation.
- **Fix:** locate existing copies primarily through their `refObj` back-link under the
  sub-process element; derive ids only for new copies.
- **Evidence:** fpb-aml-mapper: dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs:567-625.

#### PF-UPD-08 Leftover attributes after a type change; attributes jump to the end
- **Symptom:** a transition still carries `InitialMarking`; an arc's label attribute moves to
  the end of the element on every update.
- **Cause:** attributes the language owns but that do not belong to the new kind were never
  removed; a too-narrow ownership rule removed and re-appended the arc label position.
- **Fix:** remove only language-owned attributes that do not belong on the element kind;
  never touch user attributes.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Conversion/PtWrite.cs:40-61.

#### PF-UPD-09 An external tree edit is overwritten while a confirm dialog is open
- **Symptom:** a change the user made in the AML tree is lost after confirming a large update.
- **Cause:** `MessageBox.Show` pumps the dispatcher, so the live-sync timer ran while the
  dialog was open, dropped the pending snapshot, and the update then applied a local copy.
- **Fix:** suppress live sync for the whole update including dialogs, and clear the flag in
  `finally`.
- **Evidence:** AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs:261-269 (commit
  238391d).

#### PF-UPD-10 Unsynced diagram edits are dropped without recovery
- **Symptom:** "Tree changed externally, pending viewer edits dropped", and the work is gone.
- **Cause:** a conflict between diagram edits and a tree change was resolved by discarding
  the diagram side.
- **Fix:** write the pending snapshot to a backup file first and show its path.
- **Evidence:** AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs:685-703 (commit 7188bd7).

#### PF-UPD-11 An undone edit is written into the document anyway
- **Symptom:** the user undoes a change in the diagram, clicks Update, and the reverted change
  appears in the AML.
- **Cause:** the pending snapshot from before the undo survived.
- **Fix:** when a change event equals the post-import baseline, clear the pending snapshot,
  but only if that baseline is the document's state; after unsaved edits were restored into a
  reloaded page, the baseline is those edits and they stay pending.
- **Evidence:** AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs:611-631;
  `starter/plugin/Aml.Editor.Plugin.Efl/EflPlugin.xaml.cs:148-158` (baseline marked as the
  document's unless restoring) and `:160-173` (clears the pending model).

#### PF-UPD-12 External tree changes are never picked up
- **Symptom:** edits in the AML tree no longer refresh the diagram.
- **Cause:** a failing hash function returned an empty string, and `"" == ""` looked like "no
  change" forever.
- **Fix:** log hash failures; do not let an error value compare equal to the last value.
- **Evidence:** AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs:742-757.

#### PF-UPD-13 The editor freezes during update and while syncing
- **Symptom:** the AML Editor stops responding during Update (up to 23 s observed); short
  stutters with several large hierarchies open.
- **Cause:** mapping, validation and the editor's save run synchronously on the UI thread;
  live sync hashes the complete hierarchy XML every 2 s per tab on the UI thread. Aml.Engine
  is not thread-safe, so moving the write off the UI thread is not trivial.
- **Fix:** time each phase and log it; move read-only work (validation) off the UI thread
  first; poll less or hash less.
- **Evidence:** AMLFPB.js: Aml.Editor.Plugin.FPB/Bridge/EditorSaver.cs:76-85;
  AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs:48 and :742-748.
- **Status:** open in AMLFPB.js.

---

## 4. Exchange format

#### PF-FMT-01 Structured values vanish between modeler and AML
- **Symptom:** a setpoint "20 °C" arrives in AML as an empty string.
- **Cause:** the .NET model typed setpoint, validity limits and actual values as `string`; the
  JSON reader returns `""` for objects and arrays.
- **Fix:** model the structure end to end (value with unit, lists), and add a regression test
  per value kind.
- **Evidence:** fpb-aml-mapper: dotnet/FpbMapper.Conversion/Models/FpbModels.cs:81-91 (commit
  f5b508e).

#### PF-FMT-02 Values written back are skipped or crash the modeler import
- **Symptom:** characteristics disappear after AML to modeler; or the import throws on
  `setpointValue.$type`.
- **Cause:** the reverse path emitted untyped flat strings; the modeler importer requires
  `$type` on each object.
- **Fix:** write exactly the typed shape the importer reads; test the full round trip, not one
  direction.
- **Evidence:** fpb-aml-mapper commit f5b508e.

#### PF-FMT-03 A many-valued field edited as a single object
- **Symptom:** the properties panel shows the entered actual value, the export contains 0; the
  loss becomes visible only after reloading.
- **Cause:** the schema declares `actualValues` as a list and the panel creates a list, but the
  input fields read and write `.value` on the list itself.
- **Fix:** bind UI editors to the element of a collection, and add an export check for every
  editable field.
- **Evidence:** FPB.JS: app/fpb/properties-panel/PropertiesView.js:246 and :540-547.
- **Status:** open in FPB.JS.

#### PF-FMT-04 A stored relation that can be derived drifts out of sync
- **Symptom:** tandem flow groups become one-sided or keep ids of deleted flows; the XML round
  trip loses them completely.
- **Cause:** `inTandemWith` is fully derivable from flow type and source, but it is stored and
  has to be maintained on every edit; an importer loop also replaced entries while iterating.
- **Fix:** derive such relations on read (the mapper groups by `sourceRef`) instead of storing
  them, or at least resolve over a copy and drop dangling ids.
- **Evidence:** fpb-aml-mapper: dotnet/FpbMapper.Conversion/CaexToFpbJson.cs:543-560; FPB.JS
  commit d269a18.
- **Status:** XML loss open in FPB.JS.

#### PF-FMT-05 `JSON.stringify` throws on the model export
- **Symptom:** "Converting circular structure to JSON" when the host asks for the model.
- **Cause:** the library's `toJSON()` returns the live business-object graph with back
  references.
- **Fix:** export through a serializer that replaces known relationship properties by ids and
  drops `di`, `children` and `labels`.
- **Evidence:** AMLFPB.js: Aml.Editor.Plugin.FPB/fpbjs-assets/index.html:182-233; FPB.JS commit
  54540a7 (serializer extracted on a feature branch).

#### PF-FMT-06 Export right after import returns an empty model
- **Symptom:** a script or test imports and exports immediately and gets zero processes.
- **Cause:** `importJSON` returns nothing and builds the processes in a `setTimeout` of
  2000 ms.
- **Fix:** make import return a promise that resolves when the model is complete, or fire an
  explicit "import done" event.
- **Evidence:** FPB.JS: app/fpb/importer/ImportConstants.js:36;
  FPB.JS: app/fpb/importer/JSONImporter.js:135-156.
- **Status:** open in FPB.JS.

#### PF-FMT-07 Importing the same object a second time fails
- **Symptom:** a second import of the same data throws a cryptic error.
- **Cause:** the importer consumed the caller's arrays and replaced ids by objects in place.
- **Fix:** deep-copy import data before working on it.
- **Evidence:** FPB.JS: app/fpb/importer/JSONImporter.js:180-193 (commit bef2db5).

#### PF-FMT-08 Importing a second file mixes both models
- **Symptom:** duplicate layers in the layer panel; the export contains three project
  definitions, the first pointing at the old file.
- **Cause:** import appended project definition and processes; `clear()` did not reset the
  custom collections.
- **Fix:** reset model state and panels before registering the new model, and only after the new
  one resolved; make `clear()` reset everything it owns.
- **Evidence:** FPB.JS: app/fpb/importer/JSONImporter.js:205-214 (commit 3270712);
  FPB.JS: app/fpb/FpbModeler.js:223-229 (commit 2cc0c85); the host workaround in
  AMLFPB.js: Aml.Editor.Plugin.FPB/fpbjs-assets/index.html:253-270.

#### PF-FMT-09 One broken reference leaves an empty canvas
- **Symptom:** import aborts with a bare `TypeError`; nothing is drawn.
- **Cause:** a flow without visual information, an unknown id in a container, a state without
  incoming/outgoing lists or a dangling flow reference each threw.
- **Fix:** skip and warn per element, draw connections without stored geometry, drop flows with
  a missing end.
- **Evidence:** FPB.JS commits d93f87d and 1966b36.

#### PF-FMT-10 Importing an XML file ignores its content
- **Symptom:** after `toXML()` and editing the file, `importXML()` restores the old model.
- **Cause:** the library facade keeps one mapper instance; after an export it returns the stored
  JSON and only updates geometry.
- **Fix:** create a mapper per call, or never shortcut a parser with cached state.
- **Evidence:** FPB.JS: src/index.js:121.
- **Status:** open in FPB.JS (the web app and the AML plugin are not affected).

#### PF-FMT-11 The upstream PNML converter cannot be used
- **Symptom:** the bundle does not build for the browser; after a round trip every bendpoint and
  every node size is gone.
- **Cause:** the package's converter depends on `xmlbuilder2` (Node built-ins `url`, `events`)
  and writes neither `<dimension>` nor arc `<graphics>`.
- **Fix:** a DOM-based converter aliased onto the package name at build time, so the modeler's
  own import/export functions use it unchanged.
- **Evidence:** AMLPetriNet: web/src/pnml/index.js:1-19; AMLPetriNet: web/build.mjs:53-59.

#### PF-FMT-12 Only the first model of a file is imported
- **Symptom:** a PNML file with several `<net>` elements loses all but the first without a
  message.
- **Cause:** the reader took the first net.
- **Fix:** read all nets and write them into one hierarchy; offer a selector in the UI.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Conversion/PtNetToCaex.cs:58-63.

#### PF-FMT-13 Non-ASCII names are mangled
- **Symptom:** umlauts in names break when the file declares an encoding other than UTF-8.
- **Cause:** the caller decoded bytes to a string before the XML parser could honour the
  declaration.
- **Fix:** pass streams or bytes to the XML reader.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:52-64.

#### PF-FMT-14 Opening a file expands entities or reads local files
- **Symptom:** none until someone opens a crafted file (billion laughs, external entity).
- **Cause:** the XML parser expanded DTD entities.
- **Fix:** `DtdProcessing.Prohibit` and `XmlResolver = null` for every file that comes from
  outside; test both attacks.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:74-83;
  AMLPetriNet: dotnet/PtMapper.Tests/HostileInputTests.cs:88-118.

#### PF-FMT-15 Switching layers hangs the modeler
- **Symptom:** the layer panel loops endlessly after importing a decomposed model.
- **Cause:** the sub-process `parent` pointed at the parent operator or at itself instead of the
  containing process.
- **Fix:** resolve `parent` through a reverse lookup (operator to containing process); on a
  dangling reference fall back to the entry process and never emit a self-parent. The modeler
  importer also derives a missing `parent` now.
- **Evidence:** fpb-aml-mapper: dotnet/FpbMapper.Conversion/CaexToFpbJson.cs:166-190; FPB.JS
  commit 564f194.

#### PF-FMT-16 Usage connections break the importer
- **Symptom:** the modeler import crashes on models with technical resources.
- **Cause:** usage connections were listed in the system limit's container although they
  connect to elements outside it, so the importer consumed them twice.
- **Fix:** add only non-usage flows to the system limit container.
- **Evidence:** fpb-aml-mapper: dotnet/FpbMapper.Conversion/CaexToFpbJson.cs:538-540.

#### PF-FMT-17 A nested container gets its parent's name
- **Symptom:** `SL_Manufacture_Cylinder` comes back as `Manufacture_Cylinder`.
- **Cause:** the writer named the sub-process system limit after the process name, which for a
  sub-process is the decomposed operator's name.
- **Fix:** the element's own name first.
- **Evidence:** fpb-aml-mapper commit 5074b71 (with `SystemLimitNameTests.cs`).

#### PF-FMT-18 Exported coordinates make the file invalid
- **Symptom:** the grammar validator rejects a PNML file the tool wrote.
- **Cause:** `String(5.551115123125783e-17)` prints exponent notation, which is not an
  `xs:decimal`; node sizes like 50.25 or 12000 violate the dimension type.
- **Fix:** format numbers explicitly (at most two decimals, rounded the same way on both sides)
  and clamp sizes to what the grammar allows.
- **Evidence:** AMLPetriNet: web/src/pnml/index.js:200-225.

#### PF-FMT-19 NaN and Infinity travel into the document
- **Symptom:** shapes render as nothing; files contain `NaN`.
- **Cause:** `double.TryParse` accepts `"NaN"` and overflows to infinity.
- **Fix:** require `double.IsFinite` on read and write.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:297-307 and :454-458.

---

## 5. diagram-js modeler

#### PF-DJS-01 Dragging a connection end changes the drawing but not the model
- **Symptom:** the line follows the new element, the export still names the old one; ends can
  be dropped on elements the rules forbid.
- **Cause:** diagram-js executes `connection.reconnect`; updaters and rules listened only to
  `connection.reconnectStart` and `connection.reconnectEnd`, which no version in use emits.
- **Fix:** register updaters and rules for `connection.reconnect`.
- **Evidence:** FPB.JS: app/fpb/modeling/updater/ConnectionUpdater.js:66-85;
  FPB.JS: app/fpb/rules/FpbRuleProvider.js:131-139 (commit bc09580).

#### PF-DJS-02 Rules allow what they should forbid
- **Symptom:** a connection can be reconnected to an invalid target.
- **Cause:** a rule that returns `undefined` means "no opinion". For an action that is a
  command (`connection.reconnect`, `shape.create`, `elements.move`) diagram-js then allows it,
  as long as a handler exists.
- **Fix:** return an explicit `false` (`canConnect(...) || false`).
- **Evidence:** FPB.JS: app/fpb/rules/FpbRuleProvider.js:138 (commit 6c9ae73);
  `starter/web/src/rules/Rules.js:5-20`.

#### PF-DJS-19 The palette's connect tool does nothing
- **Symptom:** clicking the flow tool in the palette and dragging from a node draws no
  connection; creating one through the API works.
- **Cause:** `connection.start`, asked by the global connect tool, is not a command. For such
  actions diagram-js refuses when no rule answers (the opposite of PF-DJS-02), and a rule set
  written only for `connection.create` never answers it.
- **Fix:** add a `connection.start` rule that allows valid sources; test drawing with the
  palette tool and the mouse, not only through the API.
- **Evidence:** `starter/web/src/rules/Rules.js:37`;
  `starter/web/tools/verify-modeler.mjs:69` (fails without the rule).

#### PF-DJS-03 A minified bundle dies at boot
- **Symptom:** `No provider for "l"!` or the command stack fails while registering handlers.
- **Cause:** didi falls back to constructor parameter names when a component has no `$inject`;
  a minifier renames them. One upstream handler lacked `$inject`; a second failure in a
  minified build was never explained.
- **Fix:** keep minification off for bundles loaded from disk; patch missing `$inject` on
  upstream components; keep a browser test that boots the bundle.
- **Evidence:** AMLPetriNet: web/build.mjs:24-43; AMLPetriNet: web/src/index.js:9-18.

#### PF-DJS-04 Clearing a name sets it to "1"
- **Symptom:** a place whose name was deleted is called "1" in the document.
- **Cause:** the upstream label handler sets the default arc weight text on every element.
- **Fix:** wrap `postExecute` so the default applies to arcs only.
- **Evidence:** AMLPetriNet: web/src/index.js:20-45.

#### PF-DJS-05 Undo breaks across layers
- **Symptom:** Ctrl+Z reverts changes in a layer that is not shown and throws; decomposition
  cannot be undone; delete plus undo does not restore the child-layer state.
- **Cause:** custom commands for decompose, compose and switch have no `revert`.
- **Fix:** implement `revert` for every custom command from the start, or clear the command
  stack when switching context.
- **Evidence:** FPB.JS: app/fpb/modeling/cmd/DecomposeProcessOperator.js, ComposeProcess.js and
  SwitchProcess.js define no `revert`; in that folder only
  FPB.JS: app/fpb/modeling/cmd/UpdatePropertiesHandler.js:108 does.
- **Status:** open in FPB.JS.

#### PF-DJS-06 Undo restores only part of a rename
- **Symptom:** after rename and undo, the synchronised name on the other layer stays.
- **Cause:** `postExecute` wrote side effects directly into the model, outside the command's
  recorded context.
- **Fix:** record side writes on the command context and revert/reapply them.
- **Evidence:** FPB.JS commit c4c1f07.

#### PF-DJS-07 Layer switch bypasses the command stack
- **Symptom:** listeners that rely on `diagram.clear` do not run on layer switch.
- **Cause:** the switch command calls the private `canvas._clear()`.
- **Fix:** avoid private canvas API; if unavoidable, fire the events consumers rely on.
- **Evidence:** FPB.JS: app/fpb/modeling/cmd/SwitchProcess.js:63-65.
- **Status:** open in FPB.JS.

#### PF-DJS-08 A deleted connection blocks redrawing it
- **Symptom:** "cannot draw flows", but only between the two elements whose connection was
  deleted on another layer.
- **Cause:** cross-layer delete removed the connection from containers and business objects but
  not from the shapes' `incoming`/`outgoing`, so `areAlreadyConnected` still saw it.
- **Fix:** clean all three representations; check invariants (container, business object, shape)
  after each operation.
- **Evidence:** FPB.JS commit edcc321.

#### PF-DJS-09 Library use throws `ReferenceError: fpbjs is not defined`
- **Symptom:** modeling works in the standalone app, fails when embedded.
- **Cause:** module code referenced a global modeler handle set only by the standalone entry.
- **Fix:** inject the service through DI (`$inject`); the plugin host also sets the global as a
  workaround for remaining internal uses.
- **Evidence:** FPB.JS commit 3a7df27; AMLFPB.js: Aml.Editor.Plugin.FPB/fpbjs-assets/index.html:174-176.

#### PF-DJS-10 Token simulation saves a mid-simulation state
- **Symptom:** after playing the simulation and clicking Update, the AML holds a changed marking.
- **Cause:** the simulator writes `initialMarking` through the command stack, which looks like
  user edits.
- **Fix:** suppress change reports while the simulation mode is active and export once when it
  ends; refuse explicit exports during simulation.
- **Evidence:** AMLPetriNet: web/src/bridge.js:130-137, :175-186 and :201-211.

#### PF-DJS-11 Runtime crash from a missing semicolon
- **Symptom:** `... is not a function` in compose.
- **Cause:** automatic semicolon insertion joined a `forEach(...)` with a following line starting
  with `(`.
- **Fix:** consistent semicolons plus a linter rule.
- **Evidence:** FPB.JS commit 7ca90e0.

#### PF-DJS-12 Duplicate entries in model collections
- **Symptom:** a connection listed twice in `outgoing`; removal removes only one.
- **Cause:** `.push()` on moddle collections.
- **Fix:** `collectionAdd` / `collectionRemove` from diagram-js utilities.
- **Evidence:** FPB.JS commit 348d8b8.

#### PF-DJS-13 Panels vanish during development
- **Symptom:** React side panels disappear after layer switches while developing; console empty.
- **Cause (probable):** failed hot updates left a half-applied module graph ("Cannot apply
  update. Need to do a full reload!").
- **Fix:** configure the dev server to reload fully; watch panel containers for emptied content.
- **Evidence:** FPB.JS commit 4987625.
- **Status:** the change is on the unmerged FPB.JS branch `chore/dev-diagnostics` only.

#### PF-DJS-14 React panel crashes after an update
- **Symptom:** hooks error in the layer tree.
- **Cause:** `useState`/`useMemo` called after a conditional return.
- **Fix:** hooks before any early return; lint with the hooks rule.
- **Evidence:** FPB.JS commit 67510bc.

#### PF-DJS-15 Multi-selection draws a black rectangle; SVG export draws black shapes
- **Symptom:** a black box on multi-select; exported SVG with filled black paths.
- **Cause:** missing `fill: none` on `.djs-selection-outline`; export used the default layer and
  unstyled paths.
- **Fix:** include the selection outline style (also for dark mode); export from the root
  element's graphics with explicit fills.
- **Evidence:** FPB.JS commits 22538e1 and 045821d.

#### PF-DJS-16 Names inside small shapes are broken mid-word
- **Symptom:** "Approve" renders as "Appr" over "ove".
- **Cause:** the upstream renderer lays out the name inside a 50 px box.
- **Fix:** draw the name below the shape by wrapping the renderer; model and file unchanged.
- **Evidence:** AMLPetriNet: web/src/index.js:58-96.

#### PF-DJS-17 `No provider for "textRenderer"` in a modeler without bpmn-js
- **Symptom:** the renderer fails at boot, or labels are not drawn, when code copied from a
  bpmn-js based modeler asks for `textRenderer`.
- **Cause:** `textRenderer` is a service of bpmn-js and the packages built on it; plain
  diagram-js has no such service.
- **Fix:** create a `Text` from `diagram-js/lib/util/Text` in the renderer and write the label
  style onto each text element, so an exported SVG looks the same without the page's CSS.
- **Evidence:** `starter/web/src/draw/Renderer.js:15` and `:34-36`.

#### PF-DJS-18 The diagram's own id and name are lost on export
- **Symptom:** the exported model has no diagram id or name, or code reading
  `canvas.getRootElement().businessObject` throws.
- **Cause:** the root element diagram-js creates on demand has no business object.
- **Fix:** on import, create an explicit root with `elementFactory.createRoot` carrying the
  diagram's business object and set it with `canvas.setRootElement`; read id and name from it
  on export.
- **Evidence:** `starter/web/src/io/json.js:34-39` and `:112-115`.

---

## 6. WebView2 and bridge

#### PF-WV-01 The first model push never arrives
- **Symptom:** the viewer stays empty after opening a document.
- **Cause:** `PostWebMessage` before the page has attached its listener is dropped.
- **Fix:** buffer pushes until the page posts `ready`, then flush.
- **Evidence:** AMLFPB.js: Aml.Editor.Plugin.FPB/Bridge/FpbWebView.cs:147-153 and :228-233;
  `starter/plugin/Aml.Editor.Plugin.Efl/Bridge/ModelerView.cs:144-155`.

#### PF-WV-02 Pushes vanish after F5 or a reload
- **Symptom:** after reloading from the context menu, updates from the host are ignored.
- **Cause:** a new navigation removes the page listener while the host still thinks it is ready.
- **Fix:** set `ready = false` in `NavigationStarting`.
- **Evidence:** AMLFPB.js: Aml.Editor.Plugin.FPB/Bridge/FpbWebView.cs:101-105;
  AMLPetriNet: Aml.Editor.Plugin.PetriNet/Bridge/PtWebView.cs:131-134;
  `starter/plugin/Aml.Editor.Plugin.Efl/Bridge/ModelerView.cs:111`.

#### PF-WV-03 A blank white tab after a renderer crash
- **Symptom:** the viewer turns white and nothing reacts.
- **Cause:** no `ProcessFailed` handler; `ready` stayed true.
- **Fix:** handle `ProcessFailed`, drop out of ready, show a banner; reload for renderer exits.
- **Evidence:** AMLFPB.js: Aml.Editor.Plugin.FPB/Bridge/FpbWebView.cs:106-113;
  AMLPetriNet: Aml.Editor.Plugin.PetriNet/Bridge/PtWebView.cs:135-148;
  `starter/plugin/Aml.Editor.Plugin.Efl/Bridge/ModelerView.cs:118-129`.

#### PF-WV-04 `ObjectDisposedException` during startup
- **Symptom:** exception from `CoreWebView2.Settings` when a tab is closed quickly.
- **Cause:** `EnsureCoreWebView2Async` completed after the view was disposed.
- **Fix:** check a disposed flag after every await.
- **Evidence:** AMLFPB.js: Aml.Editor.Plugin.FPB/Bridge/FpbWebView.cs:56-58.

#### PF-WV-05 The editor slows down over a long session
- **Symptom:** many `msedgewebview2` processes after switching documents repeatedly.
- **Cause:** disposing the bridge only detached handlers; the WebView2 control lived until
  finalisation.
- **Fix:** dispose the control explicitly when a view is torn down.
- **Evidence:** AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs:864-871 (commit 238391d).

#### PF-WV-06 Model payloads corrupt the message envelope
- **Symptom:** the page receives invalid JSON for some models.
- **Cause:** the envelope was built by string concatenation.
- **Fix:** parse the payload (`JsonDocument.Parse`) and serialise the envelope with a JSON
  serializer; report invalid mapper output as such.
- **Evidence:** AMLFPB.js: Aml.Editor.Plugin.FPB/Bridge/FpbWebView.cs:155-171.

#### PF-WV-07 Import fallout is reported as user edits, or real edits are swallowed
- **Symptom:** the tab shows unsaved changes right after loading; or a genuine edit shortly
  after a push is lost.
- **Cause:** import fires many change events; a timed suppression window was armed even when
  the push had only been buffered, and swallowed real edits within 3 s.
- **Fix:** suppress changes during import in the page; send the page's own export with the
  "imported" acknowledgement and treat only content different from that baseline as an edit;
  arm any fallback window only when the push was really posted.
- **Evidence:** AMLFPB.js: Aml.Editor.Plugin.FPB/fpbjs-assets/index.html:235-243 and :286-297;
  AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs:204-222 and :611-639;
  AMLPetriNet: web/src/bridge.js:124-127.

#### PF-WV-08 All edits are ignored after an import in a hidden tab
- **Symptom:** in a background tab or collapsed panel, edits never register after a refresh.
- **Cause:** the "importing" flag was reset in `requestAnimationFrame`, which WebView2 pauses
  while content is hidden.
- **Fix:** add a `setTimeout` fallback with a settled guard.
- **Evidence:** AMLFPB.js: Aml.Editor.Plugin.FPB/fpbjs-assets/index.html:275-299 (commit 238391d).

#### PF-WV-09 Two quick imports report the second as user edits
- **Symptom:** a document switch right after a refresh marks the diagram as changed.
- **Cause:** the imports interleaved at an `await`; the first `finally` cleared the flag while the
  second was running.
- **Fix:** serialise imports in a promise queue.
- **Evidence:** AMLPetriNet: web/src/bridge.js:159-173 and :193-200;
  `starter/web/src/bridge.js:105-135`, checked by `starter/web/tools/verify-bridge.mjs:53`.

#### PF-WV-10 A stale buffered model overwrites a fresh one on boot
- **Symptom:** after the page boots, the diagram shows an older state than the document.
- **Cause:** the host's `Ready` handler pushes the current model, then `FlushPending` posts the
  older buffered push.
- **Fix:** in the ready handler, push only when nothing is buffered (and restore unsaved edits
  first); or let the handler always push (unsaved edits, else the document read now) and have
  the bridge drop the older buffered push whenever the handler sent something.
- **Evidence:** AMLFPB.js: Aml.Editor.Plugin.FPB/Bridge/FpbWebView.cs:228-233 and :271-275;
  AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:202-225;
  `starter/plugin/Aml.Editor.Plugin.Efl/EflPlugin.xaml.cs:130-146` (the handler always
  pushes) and `starter/plugin/Aml.Editor.Plugin.Efl/Bridge/ModelerView.cs:217-238` (the bridge
  takes the buffer before the handler runs and flushes it only if the handler posted no import).
- **Status:** open in AMLFPB.js.

#### PF-WV-11 Host and page disagree about the message shape
- **Symptom:** echo detection falls back to the timed window after an update.
- **Cause:** older cached page assets send acknowledgements without the baseline payload.
- **Fix:** version the bridge messages and tolerate missing fields on the host.
- **Evidence:** AMLFPB.js: Aml.Editor.Plugin.FPB/Bridge/FpbWebView.cs:250-258.

#### PF-WV-12 A failed import throws away unsaved edits
- **Symptom:** the page could not show a pushed model, and afterwards the host no longer
  reports unsaved changes; the next Update or document switch loses them.
- **Cause:** the host treats the "imported" acknowledgement as "the canvas now shows what I
  sent" and clears its pending edits. A page that sends it after a failed import says
  something untrue.
- **Fix:** after a failed import, post only an error, no "imported"; test that no
  acknowledgement follows a broken import and that the page still accepts the next one.
- **Evidence:** `starter/web/src/bridge.js:111-127`;
  `starter/web/tools/verify-bridge.mjs:71`.

---

## 7. Plugin lifecycle and packaging

#### PF-PLG-01 `DocumentLoaded` is called 40 times per second
- **Symptom:** CPU load and endless rebuilds after opening a file.
- **Cause:** the plugin raised `IsDocumentLoaded` inside `DocumentLoaded`; the editor answers that
  event by calling `DocumentLoaded` again.
- **Fix:** never raise `IsDocumentLoaded` from `DocumentLoaded`.
- **Evidence:** AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs:289-293;
  AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:288-296.

#### PF-PLG-02 Switching editor tabs throws away unsaved diagram edits
- **Symptom:** the diagram reloads and loses edits when the user switches tabs or resizes panels.
- **Cause:** WPF fires `Loaded`/`Unloaded` on every reparent; teardown in `Unloaded` disposed the
  views and started a rebuild cycle.
- **Fix:** no teardown in `Unloaded`; end the view's life in `DocumentUnLoaded` and
  `ApplicationClose`.
- **Evidence:** AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs:169-180;
  AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:153-158.

#### PF-PLG-03 The first document is dropped silently
- **Symptom:** empty viewer after opening a file; log claims "buffered".
- **Cause:** `DocumentLoaded` can arrive before the view's `Loaded`, when the bridge was still
  null.
- **Fix:** create the bridge object in the constructor; boot WebView2 later.
- **Evidence:** AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:124-127.

#### PF-PLG-04 The plugin shows "no document" although one is open
- **Symptom:** after undock/redock, editor restart with session restore, or a late install, the
  plugin never shows the open document.
- **Cause:** the editor fires `DocumentLoaded` once; views created afterwards never get it.
- **Fix:** on `Loaded`, look up the active document by reflection; log and fail soft.
- **Evidence:** AMLPetriNet: Aml.Editor.Plugin.PetriNet/Bridge/DocumentDiscoverer.cs:1-15;
  AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs:161-167.

#### PF-PLG-05 Ghost tabs for a closed or wrong document
- **Symptom:** tabs of the previous document appear after a fast document switch.
- **Cause:** tab rebuilds await WebView2 initialisation per hierarchy; a second rebuild interleaved
  and the stale continuation added tabs afterwards.
- **Fix:** a monotonic generation token checked after every await; `DocumentUnLoaded` bumps it.
  Do not use a semaphore (a hung init would block forever).
- **Evidence:** AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs:40-56 and :405-468 (commit 238391d).

#### PF-PLG-06 Closing a background document clears the active one
- **Symptom:** views and cached edits of the visible document disappear when another document is
  closed.
- **Cause:** `DocumentUnLoaded` has no parameter; the plugin assumes the current document.
- **Fix:** none yet; needs knowledge of the editor's callback order with several documents.
- **Evidence:** AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs:296-317.
- **Status:** open.

#### PF-PLG-07 Plugin commands appear in the wrong plugin's toolbar
- **Symptom:** Petri net commands show up in the FPD tab, or commands appear twice.
- **Cause:** the AML Editor shows the toolbar commands of one plugin at a time.
- **Fix:** put commands into the plugin's own panel menu, not the editor toolbar.
- **Evidence:** AMLFPB.js commit a96fc36; AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:117-122.

#### PF-PLG-08 Changes are not saved by the editor's Save button
- **Symptom:** the plugin changed the document, but the editor does not consider it dirty.
- **Cause:** the plugin contract (4.3.0) offers no way to mark a document modified or to save.
- **Fix:** invoke the editor's save command by reflection as a best effort; try all candidates
  (`CanExecute == false` on one must not stop the search); log that the command was invoked,
  not that the file was written.
- **Evidence:** AMLFPB.js: Aml.Editor.Plugin.FPB/Bridge/EditorSaver.cs:1-11, :53-74 and :87-92
  (commit 238391d).

#### PF-PLG-09 An editor update breaks features without an error
- **Symptom:** auto-save, theme sync or document discovery silently stop working.
- **Cause:** reflection paths and contract members changed with a new package version; no compile
  error.
- **Fix:** pin editor packages exactly (`[4.3.0]`), add contract snapshot tests, and run a startup
  compatibility check that logs each path.
- **Evidence:** AMLFPB.js: Aml.Editor.Plugin.FPB/Aml.Editor.Plugin.FPB.csproj:24-38;
  AMLFPB.js: Aml.Editor.Plugin.FPB.Tests/ApiContractTests.cs:1-35;
  AMLFPB.js: Aml.Editor.Plugin.FPB/Diagnostics/ApiCompatCheck.cs:1-38.

#### PF-PLG-10 One transient failure disables validation for the whole session
- **Symptom:** after one error at startup, the rule check never runs again until the editor
  restarts.
- **Cause:** `Lazy<T>` in the default mode caches the exception.
- **Fix:** `LazyThreadSafetyMode.PublicationOnly`; isolate rule loading per block.
- **Evidence:** AMLFPB.js: Aml.Editor.Plugin.FPB/Validation/Vdi3682OclRuleSet.cs:75-84 (commit 365641d).

#### PF-PLG-11 Foreign AML files are flagged with errors
- **Symptom:** "project needs at least one process" on any AML file without the language.
- **Cause:** validation ran on every document.
- **Fix:** skip validation when the document contains no element of the language.
- **Evidence:** AMLFPB.js commit 365641d.

#### PF-PLG-12 Plugin fails on activation and on editor close
- **Symptom:** `ArgumentException` when activating the plugin and when closing the editor.
- **Cause:** the display name is used as a WPF element name and as an XML element name in the
  editor configuration; characters such as `.` or `/` are invalid there.
- **Fix:** display names with letters, digits and `_` only.
- **Evidence:** AMLFPB.js: Aml.Editor.Plugin.FPB/Metadata.xml:5 and
  Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs:76 (`AMLFPBjs`); the starter checks the rule and
  that `Metadata.xml` and the plugin class agree:
  `starter/plugin/Aml.Editor.Plugin.Efl.Tests/PackageTests.cs:87`.

#### PF-PLG-13 Install fails with "Unable to resolve dependency"
- **Symptom:** the PlugIn Manager refuses the package.
- **Cause:** a `ProjectReference` (the mapper) became a NuGet dependency in the nuspec.
- **Fix:** `PrivateAssets=all` on the reference plus an explicit `<None ... Pack="true">` for its
  DLL.
- **Evidence:** AMLFPB.js: Aml.Editor.Plugin.FPB/Aml.Editor.Plugin.FPB.csproj:50-71;
  AMLPetriNet: Aml.Editor.Plugin.PetriNet/Aml.Editor.Plugin.PetriNet.csproj:53-62.

#### PF-PLG-14 WebView2 fails to start in the installed plugin
- **Symptom:** "WebView2 runtime missing or failed to start" only after installing the package.
- **Cause:** `WebView2Loader.dll` from `runtimes/win-x64/native` is dropped by NuGet pack.
- **Fix:** stage the loader under `obj/bundled/` before reference resolution and pack it with an
  explicit file name in `PackagePath`; pack the managed WebView2 DLLs from
  `$(PkgMicrosoft_Web_WebView2)` (needs `GeneratePathProperty="true"`).
- **Evidence:** AMLFPB.js: Aml.Editor.Plugin.FPB/Aml.Editor.Plugin.FPB.csproj:42-47, :74-92 and
  :142-148.

#### PF-PLG-15 Content files from outside the project are not copied
- **Symptom:** modeler assets missing or incomplete in `bin/`.
- **Cause:** MSBuild cannot derive an output path for items included from `..`.
- **Fix:** set `Link` metadata on every item outside the project directory.
- **Evidence:** AMLFPB.js: Aml.Editor.Plugin.FPB/Aml.Editor.Plugin.FPB.csproj:95-108;
  AMLPetriNet: Aml.Editor.Plugin.PetriNet/Aml.Editor.Plugin.PetriNet.csproj:68-69 and :90-97.

#### PF-PLG-16 Every bridge message fails if the host lacks a dependency
- **Symptom:** (would be) no communication with the page at all.
- **Cause:** `System.Text.Json` is not packed; both plugins rely on the editor process providing it.
- **Fix:** check such assumptions once at startup and log the result.
- **Evidence:** AMLPetriNet: Aml.Editor.Plugin.PetriNet/Diagnostics/StartupCheck.cs:32-52.

#### PF-PLG-17 The package lands in a second build tree
- **Symptom:** a `build/` folder inside the project directory after `dotnet build` of the
  project alone.
- **Cause:** `BaseOutputPath` used `$(SolutionDir)`, which is empty without a solution.
- **Fix:** anchor at `$(MSBuildThisFileDirectory)`.
- **Evidence:** AMLPetriNet: Aml.Editor.Plugin.PetriNet/Aml.Editor.Plugin.PetriNet.csproj:16-19.

#### PF-PLG-18 The released package carries the old version
- **Symptom:** a release tagged 0.7.0 ships a 0.6.20 package; the editor offers no update.
- **Cause:** the version lives in the csproj and in `Metadata.xml`; the release commit took a
  stale staged bump.
- **Fix:** bump both files together and check the package file name before tagging; a test
  that compares the two versions makes a forgotten bump fail the build.
- **Evidence:** AMLFPB.js commits c891a1a and ebb79b6;
  `starter/plugin/Aml.Editor.Plugin.Efl.Tests/PackageTests.cs:80`.

#### PF-PLG-19 A splitter does not resize the findings pane
- **Symptom:** dragging the `GridSplitter` does nothing.
- **Cause:** a `GridSplitter` cannot resize a row with `Height="Auto"`.
- **Fix:** give the row a pixel height (0 while hidden).
- **Evidence:** AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml:25-28.

#### PF-PLG-20 The status log stops updating after a tab switch
- **Symptom:** the diagnostics panel freezes; the log file still grows.
- **Cause:** the log subscription was attached once and detached on `Unloaded`.
- **Fix:** subscribe in `Loaded` (remove first to avoid duplicates) and unsubscribe in `Unloaded`.
- **Evidence:** AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs:156-159 and :179.

#### PF-PLG-21 The plugin builds without its modeler bundle
- **Symptom:** the installed plugin shows "index.html missing" or a script 404.
- **Cause:** `dotnet build` ran before `npm run build`.
- **Fix:** an MSBuild target that fails the build with a clear message when the bundle is missing;
  the page host also checks for the files at startup.
- **Evidence:** AMLFPB.js: Aml.Editor.Plugin.FPB/Aml.Editor.Plugin.FPB.csproj:121-124;
  AMLPetriNet: Aml.Editor.Plugin.PetriNet/Bridge/PtWebView.cs:103-117.

---

## 8. Editor caches and installed state

#### PF-CCH-01 The plugin disappears from the editor without an error
- **Symptom:** after copying a new build into the installed plugin folder, the plugin is no longer
  listed.
- **Cause:** a second copy of `Aml.Editor.Plugin.Contract.dll` in the plugin folder is loaded
  separately, so the plugin's interface types no longer match the editor's.
- **Fix:** copy only the files the package contains; never the whole build output. Prefer
  uninstall and install through the PlugIn Manager. Keep the contract assembly out of the
  package itself and test that.
- **Evidence:** AMLPetriNet: README.md:81-83;
  `starter/plugin/Aml.Editor.Plugin.Efl.Tests/PackageTests.cs:48` (and `:56` for
  `Aml.Engine.dll`, which the editor also loads itself).

#### PF-CCH-02 The PlugIn Manager forgets or ignores an installed plugin
- **Symptom:** hours spent on install state that "does not stick".
- **Cause:** installed state is spread over several files (the manager's DataContract XML, the
  editor's activation configuration, the plugin folder, the editor error log). The manager
  removes entries whose `IsInstalled` deserialises as false; hand edits break the DataContract
  format.
- **Fix:** list every persistence location before forming a hypothesis; change install state
  only through the PlugIn Manager UI. If you must script it, round-trip through the same
  DataContract serializer and simulate the manager's filter.
- **Evidence:** AMLFPB.js: GeneratePluginXml/Program.cs:4-9 and :101-110.

#### PF-CCH-03 Unsaved diagram edits vanish although the same file is still open
- **Symptom:** pending edits are gone after an unrelated editor action.
- **Cause:** the editor sometimes hands out a new `CAEXDocument` wrapper for the same file; caches
  keyed by object reference miss, and a reattach reloads the canvas.
- **Fix:** key caches and identity by document content identity, not by reference; on a new
  wrapper for the same file, keep the diagram.
- **Evidence:** AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs:58-64;
  AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:305-320.

#### PF-CCH-04 Pending edits of two files collide
- **Symptom:** with two documents open, one shows the other's unsaved edits.
- **Cause:** `OriginID` names the authoring tool and is identical in every editor-saved file (and
  empty in legacy files), so it alone does not identify a document.
- **Fix:** combine `OriginID` with the file name; treat two brand new documents as distinct.
- **Evidence:** AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs:66-71;
  AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:322-344;
  `starter/plugin/Aml.Editor.Plugin.Efl/EflPlugin.xaml.cs:220-237`.

---

## 9. Layout

#### PF-LAY-01 Files without layout produce an empty canvas
- **Symptom:** an AML file from another tool imports as a blank diagram.
- **Cause:** connections without visual information made the modeler importer dereference
  `undefined` and abort the whole import.
- **Fix:** the mapper always emits at least two waypoints (port coordinates, else shape centres,
  else fixed points); the importer draws connections without stored geometry.
- **Evidence:** fpb-aml-mapper: dotnet/FpbMapper.Conversion/CaexToFpbJson.cs:837-878 (commit
  2c80523); FPB.JS commit d93f87d.

#### PF-LAY-02 Fallback geometry changes on every sync
- **Symptom:** a no-op sync keeps reporting changed waypoints.
- **Cause:** fallback points lacked the `original` substructure that port-derived points carry, so
  the write side stored them differently than the read side produced them.
- **Fix:** give generated points exactly the shape of read points; run echo round trips in tests.
- **Evidence:** fpb-aml-mapper: dotnet/FpbMapper.Conversion/CaexToFpbJson.cs:864-875.

#### PF-LAY-03 Every node sits at the origin
- **Symptom:** a foreign PNML file opens as one pile of shapes.
- **Cause:** many producers write no graphics.
- **Fix:** automatic layered layout for nodes without bounds, on import and before display; never
  move existing positions.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs:5-19.

#### PF-LAY-04 A ten-node loop is laid out 17,000 px wide
- **Symptom:** absurd diagram widths for cyclic models.
- **Cause:** longest-path column assignment relaxed over a cycle.
- **Fix:** break cycles first (depth-first), then assign columns.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs:70-85.

#### PF-LAY-05 A cyclic model is drawn as one long row starting in the middle
- **Symptom:** the loop starts at an arbitrary place and two arcs cross the whole drawing.
- **Cause:** cycle breaking started at nodes nothing feeds, not where the process starts.
- **Fix:** start at marked places (language semantics), then sources, then the rest, each group in
  id order; route back arcs around the drawing.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs:154-167.

#### PF-LAY-06 The layout metric reports 0 defects for an unreadable drawing
- **Symptom:** "0 crossings" while arcs run through four nodes each.
- **Cause:** only crossings were counted.
- **Fix:** count arcs through non-incident nodes as well (weighted double).
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs:490-499.

#### PF-LAY-07 Moving one node straightens carefully routed arcs
- **Symptom:** after dragging a node in a demo, routed loops become straight lines across the
  drawing; round-trip counts depend on which node was moved.
- **Cause:** diagram-js recomputes the connections of a moved shape.
- **Fix:** expect it; choose test moves deliberately and document the effect.
- **Evidence:** AMLPetriNet: web/tools/verify-paper-session.mjs:12-17.

#### PF-LAY-08 User bendpoints are lost on every layer switch
- **Symptom:** bendpoints reset after switching layers or decomposing.
- **Cause:** commands called `layoutConnection` on all connections; before that, a "nudge" moved
  shapes by 3 px to force recalculation because `canvas.addConnection()` does not recompute stored
  waypoints.
- **Fix:** re-layout only connections attached to shapes that actually moved or were created.
- **Evidence:** FPB.JS commits 7f96aaa, 8f1fc08 and 3470667.

#### PF-LAY-09 An XML round trip flattens all connections
- **Symptom:** 66 waypoints before, 40 after, all straight lines.
- **Cause:** the writer stored waypoints; the reader ignored them and used a two-point default.
- **Fix:** read what the writer writes; measure waypoint counts in a round-trip test.
- **Evidence:** FPB.JS commit 478861a.
- **Status:** the fix is on the unmerged FPB.JS branch `fix/xml-waypoints` only.

#### PF-LAY-10 Parallel branches align to missing data
- **Symptom:** after import, the layouter finds no waypoints for the partners of a parallel flow
  and cannot align its bends to them.
- **Cause:** imported waypoints were not mirrored into the DI objects, and the layouter read
  partner waypoints from DI.
- **Fix:** mirror waypoints into DI on import; read partner waypoints from the shape.
- **Evidence:** FPB.JS commits 2fd4d76 and d1be437.

#### PF-LAY-11 A zero-size node produces NaN docking points
- **Symptom:** connections to a node vanish.
- **Cause:** 0/0 in the ellipse intersection formula.
- **Fix:** return the centre when width or height is not positive.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Conversion/PtGeometry.cs:33-36;
  `starter/dotnet/Efl.Conversion/EflGeometry.cs:50-52` (checked by
  `ANodeWithoutSizeDocksAtItsCentreInsteadOfNaN` in
  `starter/dotnet/Efl.Tests/ExampleAndGeometryTests.cs:68`).

#### PF-LAY-12 Stray bends and shifted shapes in other tools
- **Symptom:** another PNML tool shows extra bends at arc ends; nodes are offset by half their size.
- **Cause:** writing docking points as waypoints; mixing top-left and centre positions.
- **Fix:** write only intermediate bendpoints; position = centre plus `<dimension>`, converted on
  read.
- **Evidence:** AMLPetriNet: web/src/pnml/index.js:21-30.

#### PF-LAY-13 Moved labels come back somewhere else
- **Symptom:** an arc label dragged by the user reappears at a different spot after reimport.
- **Cause:** the offset was measured from a different reference point than the modeler uses.
- **Fix:** measure from the modeler's own reference (`getWaypointsMid`).
- **Evidence:** AMLPetriNet: web/src/pnml/index.js:177-185 and :372-374.

#### PF-LAY-14 A new element is placed exactly on an existing one
- **Symptom:** two boundary states at the same coordinates on the child layer.
- **Cause:** placement by "count of existing times spacing" assumed left-packed elements.
- **Fix:** place right of the outermost existing element.
- **Evidence:** FPB.JS commit edcc321.

#### PF-LAY-15 The arranged diagram is unreadable although every test passes
- **Symptom:** after automatic layout a back flow lies exactly on top of the forward flow between
  the same two nodes, a flow runs straight through a node, a self loop is invisible; the browser
  tests are green.
- **Cause:** the layout placed nodes and left every flow straight, and the tests checked only that
  nodes do not overlap.
- **Fix:** route back flows in lanes below the drawing, forward flows that would enter a node in
  lanes above, and give self loops bend points, only for flows between nodes the call placed and
  without waypoints of their own. Test the lines: no segment inside a node that is not one of its
  ends, no two flows on a shared segment. Look at a screenshot of the arranged form.
- **Evidence:** `starter/dotnet/Efl.Conversion/EflLayout.cs:175-289`;
  `starter/dotnet/Efl.Tests/LayoutTests.cs:6-13`, `:85-197`; `starter/web/tools/screenshot.mjs:1-20`.

---

## 10. Testing

#### PF-TST-01 Green tests, broken models
- **Symptom:** "16 tests green" while the importer crashes on real files.
- **Cause:** tests asserted element counts, not structure or references.
- **Fix:** golden snapshots of the full structure, with GUIDs canonicalised, a determinism
  self-check and an explicit regeneration switch.
- **Evidence:** fpb-aml-mapper: dotnet/FpbMapper.Tests/GoldenTests.cs:9-29 (commit 209bde8).

#### PF-TST-02 A golden file cements a bug
- **Symptom:** a wrong name was "expected" for months.
- **Cause:** goldens were regenerated without reading the diff.
- **Fix:** review every regenerated golden like code; add a targeted test for each value found
  wrong.
- **Evidence:** fpb-aml-mapper commit 5074b71 (the Temperieren goldens recorded the wrong name).

#### PF-TST-03 A round-trip test measures the engine's reformatting
- **Symptom:** round-trip diffs full of whitespace and attribute order changes.
- **Cause:** comparing against the file on disk instead of the same file loaded and serialised by
  the same engine.
- **Fix:** compare `Load(file)` serialised against `Load(file) + round trip` serialised.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Tests/PaperRoundTripTests.cs:41-50.

#### PF-TST-04 Fixtures prove nothing about a file the user edited
- **Symptom:** unit tests pass; a file saved from the editor still changes on sync.
- **Cause:** the fixtures differ from real editor output.
- **Fix:** one shared round-trip/compare check used by tests and by a CLI command run against
  editor-saved files.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Conversion/PtRoundTripCheck.cs:20-30.

#### PF-TST-05 Tests depend on a sibling checkout or local files
- **Symptom:** CI fails in a repository that does not contain the files; a fresh clone fails.
- **Cause:** showcase tests read files produced in the plugin repository; paper tests read files
  from a local folder.
- **Fix:** generate inputs inside the test or commit them into the repository, find them by
  walking up to a repository marker, and write outputs to a configurable temp directory. A skip
  on a missing file is acceptable only while the file is genuinely optional; once it is
  committed, a skip means a broken lookup, so convert the test to a plain fact or fail CI on
  skips (see [07 Testing and CI](07-testing-and-ci.md), §12).
- **Evidence:** fpb-aml-mapper commit 0e1b7a5 and fpb-aml-mapper: .github/workflows/test.yml:23-26;
  AMLPetriNet: dotnet/PtMapper.Tests/PaperRoundTripTests.cs:27 (example now in
  `examples/paper/`, looked up through `TestFiles`) and :36-39 (the leftover skip).

#### PF-TST-06 Headless browser reports downloads as canceled
- **Symptom:** a browser test of the web app fails on "download canceled".
- **Cause:** blob downloads in headless Chromium report themselves as canceled.
- **Fix:** wrap `URL.createObjectURL`, record entries synchronously and assert the blob content.
- **Evidence:** AMLPetriNet: web/tools/verify-webapp.mjs:38-60.

#### PF-TST-07 E2E selectors break after a diagram-js update
- **Symptom:** palette tests fail after updating diagram-js.
- **Cause:** palette entries carry `aria-label` instead of `title` in newer versions.
- **Fix:** select by role or test id, update selectors together with the dependency.
- **Evidence:** FPB.JS commit c97a091.

#### PF-TST-08 Clicking on the canvas does not open the context pad in tests
- **Symptom:** position-based clicks miss elements; context pad tests are flaky.
- **Cause:** canvas offsets and generated element ids.
- **Fix:** select through the selection service and fire `element.click` in the page.
- **Evidence:** FPB.JS: tests/e2e/pages/ModelerPage.js:381-387.

#### PF-TST-09 `toBe(false)` fails for a false result
- **Symptom:** a type check expected false returns `undefined`.
- **Cause:** `is()` returns the result of a short-circuit expression; plain objects without
  `$instanceOf` yield `undefined`.
- **Fix:** `toBeFalsy()` or make helpers return booleans.
- **Evidence:** FPB.JS: app/fpb/help/utils.js:12-15.

#### PF-TST-10 Committed example files no longer match the mapper
- **Symptom:** the example AML files in the repository show classes or attributes the mapper no
  longer writes; or every run of the example writer changes the files although nothing changed.
- **Cause:** examples are derived artefacts written by hand when someone remembers, and every
  write stamps the current time into `LastWritingDateTime`, so a stale file and a fresh one cannot
  be told apart by a diff.
- **Fix:** write examples with a fixed timestamp, write them again in CI and fail on
  `git diff --exit-code`; mark `*.aml` as `-text` so line endings do not produce a diff.
- **Evidence:** `starter/dotnet/Efl.Conversion/EflToCaex.cs:32-50`;
  `starter/dotnet/Efl.Tool/Program.cs:46`, `:116-120`; `starter/.github/workflows/ci.yml:45-55`;
  `starter/.gitattributes:1-2`.

---

## 11. Build and bundling

#### PF-BLD-01 The library thinks it runs outside a browser
- **Symptom:** browser-only features never initialise in the library build.
- **Cause:** `DefinePlugin` replaced `typeof window` at compile time.
- **Fix:** detect the environment at runtime.
- **Evidence:** FPB.JS commit 16a527e; FPB.JS: src/index.js:4-12.

#### PF-BLD-02 Consumers of the library fail to import chunks
- **Symptom:** import errors in projects that bundle the library.
- **Cause:** code-split chunks with a chunk-loading runtime.
- **Fix:** `asyncChunks: false` and `LimitChunkCountPlugin({ maxChunks: 1 })`.
- **Evidence:** FPB.JS: webpack.lib.config.js:18 and :73 (commit 7642ad2).

#### PF-BLD-03 Schema JSON arrives as a string
- **Symptom:** moddle fails to read its schema in the library build.
- **Cause:** a JSON `asset/source` rule turned schema files into strings.
- **Fix:** let webpack 5 parse JSON natively.
- **Evidence:** FPB.JS commit 16a527e.

#### PF-BLD-04 Icons are blank when the modeler is used as a library
- **Symptom:** panels in the plugin show empty icon slots.
- **Cause:** only the standalone app entry registered the icon library.
- **Fix:** register icons in the library entry.
- **Evidence:** FPB.JS: src/index.js:7-12 (commit 5adfe8c).

#### PF-BLD-05 The production build is a development build
- **Symptom:** the "production" build output is an unoptimised development build.
- **Cause:** build scripts lacked `--mode production`.
- **Fix:** set the mode explicitly in every build script.
- **Evidence:** FPB.JS commit 67510bc.

#### PF-BLD-06 The ESM library cannot resolve React
- **Symptom:** `Failed to resolve module specifier "react"` in the WebView2 page.
- **Cause:** the library keeps React as a peer dependency with bare specifiers.
- **Fix:** an import map to vendored React files of exactly the version the library expects
  (18.3.1 here, not 17).
- **Evidence:** AMLFPB.js: Aml.Editor.Plugin.FPB/fpbjs-assets/index.html:90-98.

#### PF-BLD-07 Vendored react-dom 404s under the virtual host
- **Symptom:** the page fails loading `/react@18.3.1/es2022/react.mjs`.
- **Cause:** the downloaded ESM build imports React by an absolute CDN path.
- **Fix:** rewrite it to the bare specifier at build time (idempotent MSBuild target).
- **Evidence:** AMLFPB.js: Aml.Editor.Plugin.FPB/Aml.Editor.Plugin.FPB.csproj:126-140.

#### PF-BLD-08 The npm package ships without its stylesheet
- **Symptom:** unstyled modeler for npm consumers.
- **Cause:** CSS was not part of the library build output.
- **Fix:** a library entry and config that emit the CSS.
- **Evidence:** FPB.JS commit eed4ea4.

#### PF-BLD-09 A commit builds locally and not from a clean checkout
- **Symptom:** the next checkout does not compile.
- **Cause:** staging specific files left out new files that the staged ones depended on.
- **Fix:** build from a clean clone (or in CI) before pushing.
- **Evidence:** AMLFPB.js commit 2a523a4 ("include validation pipeline files").

---

## 12. CI

#### PF-CI-01 CI was silently broken for months
- **Symptom:** a release build fails although local builds pass.
- **Cause:** the plugin references sibling repositories by relative path; the workflow checked out
  a flat layout and omitted one sibling added later.
- **Fix:** replicate the local directory layout exactly in the workflow and check out every
  referenced repository.
- **Evidence:** AMLFPB.js: .github/workflows/build.yml:17-48 (commit d93f319).

#### PF-CI-02 Nothing ran on push
- **Symptom:** broken tests discovered only at release time.
- **Cause:** the only workflow built and published on tags.
- **Fix:** unit, e2e and build jobs on every push and pull request.
- **Evidence:** FPB.JS commit 4642f78 (adds `.github/workflows/test.yml`).
- **Status:** on the unmerged FPB.JS branch `chore/ci-tests` only; the cited branch still has
  just the tag-triggered `release.yml`.

#### PF-CI-03 CI installs are slow and e2e times out
- **Symptom:** every job builds the whole library during `npm ci`; the Playwright web server
  times out.
- **Cause:** the `prepare` script builds on install; CI runners are slower than local machines.
- **Fix:** `npm ci --ignore-scripts` for jobs that do not need the build; raise the web server
  timeout.
- **Evidence:** FPB.JS commit 4642f78 (branch `chore/ci-tests`, see PF-CI-02).

#### PF-CI-04 Glob arguments are passed literally on Windows runners
- **Symptom:** a validation tool receives `*.pnml` as a file name.
- **Cause:** PowerShell does not expand `*`.
- **Fix:** `shell: bash` for such steps.
- **Evidence:** AMLPetriNet: .github/workflows/ci.yml:59-63.

#### PF-CI-05 The .NET build fails in CI without the web bundle
- **Symptom:** "The modeler bundle is missing" in CI.
- **Cause:** the plugin and web app ship the bundle; the npm build must run first.
- **Fix:** order the steps: `npm ci && npm run build`, then `dotnet build`.
- **Evidence:** AMLPetriNet: .github/workflows/ci.yml:31-42.

#### PF-CI-06 npm publishing from CI needs several attempts
- **Symptom:** the release workflow needed four consecutive fix commits before publishing through
  npm trusted publishing worked.
- **Cause:** trusted publishing (OIDC) needs `id-token: write` and a matching setup-node
  configuration; the `registry-url` setting was removed and later restored.
- **Fix:** follow the npm trusted publishing documentation exactly and test with a pre-release tag.
- **Evidence:** FPB.JS: .github/workflows/release.yml:8-25; FPB.JS commits 1158205, 42af9e8,
  59650ca, 4db374c.

---

## 13. Deployment

#### PF-DEP-01 The Node proxy does not start under Phusion Passenger
- **Symptom:** the app fails to boot on the shared host.
- **Cause:** Passenger loaded the app as CommonJS; the code was ESM.
- **Fix:** CommonJS entry (after a wrapper attempt).
- **Evidence:** fpb-aml-mapper commits 433e8ad, 37b321e, 40d88b9, 8929cb5.

#### PF-DEP-02 Runtime APIs missing on the hosting Node version
- **Symptom:** `crypto.randomUUID is not a function`; `fetch is not defined`.
- **Cause:** the host ran Node 16.
- **Fix:** polyfill or import explicitly (`node-fetch`), or pin a newer runtime.
- **Evidence:** fpb-aml-mapper commits 4482c8a and 46e187f; fpb-aml-mapper: server.js:1.

#### PF-DEP-03 Fifteen page loads lock everyone out
- **Symptom:** HTTP 429 for all users after little traffic.
- **Cause:** behind the cloud front end every request had the front end's address, so the rate
  limiter used one bucket; static files also consumed permits.
- **Fix:** `UseForwardedHeaders` with cleared known proxies, and rate-limit only API endpoints;
  return a body and `Retry-After`.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Web/Program.cs:29-64 and :68.

#### PF-DEP-04 Example files return 404
- **Symptom:** the demo `.aml` file is not served.
- **Cause:** `.aml` and `.pnml` are not in the default MIME table, and unknown extensions are not
  served.
- **Fix:** register the extensions in a `FileExtensionContentTypeProvider`.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Web/Program.cs:87-91.

#### PF-DEP-05 `dotnet run` serves the page without the modeler
- **Symptom:** locally the page loads but the canvas is missing; the published app works.
- **Cause:** files included with `Link` land in the publish output, not in the content root that
  `dotnet run` serves.
- **Fix:** copy the bundle into `wwwroot/` in a `BeforeBuild` target and ignore the copies in git.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Web/PtMapper.Web.csproj:21-38.

#### PF-DEP-07 A deleted or renamed example is still served
- **Symptom:** the web app offers an example file that no longer exists in the repository.
- **Cause:** the build copies examples into `wwwroot/`; a copy adds and overwrites but never
  removes what the source no longer has.
- **Fix:** remove the staged folder before copying into it.
- **Evidence:** `starter/dotnet/Efl.Web/Efl.Web.csproj:31-37`.

#### PF-DEP-06 A developer's browser is pinned to https for localhost
- **Symptom:** after running the app once, localhost only works over https.
- **Cause:** HSTS sent in development.
- **Fix:** `UseHsts()` only outside development.
- **Evidence:** AMLPetriNet: dotnet/PtMapper.Web/Program.cs:70-73.

---

## Checklist

- [ ] Every Aml.Engine insert goes through a helper with `Insert(x, false)` (PF-AML-01).
- [ ] No `ReferenceEquals` on Aml.Engine wrappers; compare ids or `Node` (PF-AML-02).
- [ ] Every test assembly that touches Aml.Engine disables parallelisation (PF-AML-03).
- [ ] Golden comparisons strip `LastWritingDateTime` and `xsi:schemaLocation`; documents are loaded
      and saved through text or streams, not `LoadFromFile`/`SaveToFile` (PF-AML-04).
- [ ] AML text is loaded through one helper that rejects a null `CAEXFile` (PF-AML-14).
- [ ] InternalLinks set through `AInterface`/`BInterface`; readers accept both partner formats
      (PF-AML-06, PF-AML-07).
- [ ] Class path comparisons strip aliases; shared libraries are embedded in new documents; DD
      paths follow the document's form (PF-AML-08 to PF-AML-10).
- [ ] Shared libraries (OMG_DD, ObjectReferences) are embedded from the published files, never
      rebuilt in code; a test compares the embedded copy with the file (PF-AML-16).
- [ ] Numbers parsed and written with `InvariantCulture` and `double.IsFinite` (PF-FMT-19).
- [ ] The exchange format carries a version; newer versions are read with a warning (see [03](03-exchange-format.md) §10).
- [ ] One node port per connection end, keyed with the direction; self loops tested (PF-AML-15).
- [ ] No credentials or local paths in constants that reach output files; the base library is
      referenced by file name, never by a URL with a token (PF-AML-13).
- [ ] All CAEX ids derived deterministically; UUID version nibble in `bytes[7]` (PF-ID-02, PF-ID-03).
- [ ] One canonical id form per side, converted at the boundary; rule-visible references in the
      compared form (PF-ID-04, PF-ID-05).
- [ ] Objects shown on several views get distinct CAEX ids plus a back reference, reunified on read
      (PF-ID-01).
- [ ] Child ids salted with the owner's CAEX id; hierarchy found by stamped id (PF-ID-06, PF-ID-11).
- [ ] Diagram ids normalised to what the modeler accepts; invalid ids reported, not blocking (PF-ID-07).
- [ ] Reader and updater share one identity function and one claiming order (PF-ID-08, PF-ID-09).
- [ ] Update in place exists separately from import; a no-op sync on a foreign file produces an
      empty diff (PF-UPD-01 to PF-UPD-04).
- [ ] Deleting elements cleans links document-wide and reports it; skipped elements are not deleted
      (PF-UPD-05, PF-UPD-06).
- [ ] Live sync suppressed during update dialogs; pending edits backed up before dropping
      (PF-UPD-09, PF-UPD-10).
- [ ] Every structured value has a typed model and a full round-trip test (PF-FMT-01, PF-FMT-02).
- [ ] Derivable relations are derived, not stored (PF-FMT-04).
- [ ] Model export is cycle-free; import is awaitable, works on a copy, replaces the previous model
      and skips bad elements with warnings (PF-FMT-05 to PF-FMT-09).
- [ ] XML input parsed with DTDs prohibited, from bytes (PF-FMT-13, PF-FMT-14).
- [ ] Rules and updaters registered for `connection.reconnect`; rules return explicit `false`;
      a `connection.start` rule lets the palette's connect tool work (PF-DJS-01, PF-DJS-02,
      PF-DJS-19).
- [ ] Every component declares `$inject`; minification off or proven by a boot test (PF-DJS-03).
- [ ] Custom commands implement `revert`; side writes recorded on the context (PF-DJS-05, PF-DJS-06).
- [ ] Simulation or other non-edit command traffic is not reported as a change (PF-DJS-10).
- [ ] Renderer uses `diagram-js/lib/util/Text`, not `textRenderer`; import sets an explicit root
      element with the diagram's business object (PF-DJS-17, PF-DJS-18).
- [ ] Bridge buffers until `ready`, resets on `NavigationStarting`, handles `ProcessFailed`, checks
      disposal after awaits, disposes the WebView2 control (PF-WV-01 to PF-WV-05).
- [ ] Envelope built by a JSON serializer; import echo detected by content baseline; imports queued;
      suppression reset does not rely on `requestAnimationFrame` (PF-WV-06 to PF-WV-09).
- [ ] A buffered push never overwrites what the ready handler sent; a failed import posts an error
      and no acknowledgement (PF-WV-10, PF-WV-12).
- [ ] A change equal to the import baseline clears pending edits when the baseline is the
      document's state (PF-UPD-11).
- [ ] `IsDocumentLoaded` never raised from `DocumentLoaded`; no teardown in `Unloaded` (PF-PLG-01, PF-PLG-02).
- [ ] Bridge created in the constructor; active document discovered on `Loaded`; rebuilds guarded by a
      generation token (PF-PLG-03 to PF-PLG-05).
- [ ] Commands in the plugin panel, not the editor toolbar (PF-PLG-07).
- [ ] Editor packages pinned exactly; contract snapshot tests and startup compatibility log (PF-PLG-09).
- [ ] `Lazy<T>` for fallible initialisation uses `PublicationOnly` (PF-PLG-10).
- [ ] Display name uses letters, digits and `_` only (PF-PLG-12).
- [ ] Package: `PrivateAssets=all` plus explicit packing of project DLLs, WebView2 DLLs and a staged
      `WebView2Loader.dll`; `Link` on outside items; version bumped in csproj and `Metadata.xml`
      (PF-PLG-13 to PF-PLG-18).
- [ ] Manual deploys never copy `Aml.Editor.Plugin.Contract.dll`; package tests assert it is not in the
      package, and that `Metadata.xml` and csproj versions agree (PF-CCH-01, PF-PLG-18).
- [ ] Document identity = `OriginID` plus file name, never object reference (PF-CCH-03, PF-CCH-04).
- [ ] Mapper always emits connection geometry, shaped exactly like read geometry (PF-LAY-01, PF-LAY-02).
- [ ] Auto-layout for nodes without bounds; cycles broken first; defects include arcs through nodes
      (PF-LAY-03 to PF-LAY-06).
- [ ] Layer switches keep user bendpoints (PF-LAY-08).
- [ ] Generated layout routes back flows, crossing forward flows and self loops; layout tests check
      segments against nodes and each other; screenshots looked at (PF-LAY-15).
- [ ] Golden structure tests with GUID canonicalisation; regenerated goldens reviewed (PF-TST-01, PF-TST-02).
- [ ] Round-trip comparisons against the reloaded document; shared check usable from the command line
      (PF-TST-03, PF-TST-04).
- [ ] Tests generate their own inputs; local-file tests are skippable (PF-TST-05).
- [ ] Example files written with a fixed timestamp and checked by CI with `git diff` (PF-TST-10).
- [ ] CI replicates sibling layout, runs on every push, builds the web bundle before .NET (PF-CI-01,
      PF-CI-02, PF-CI-05).
- [ ] Web app: forwarded headers, API-only rate limit, MIME types for your file extensions, assets
      copied into a cleaned folder in `wwwroot`, HSTS outside development only (PF-DEP-03 to PF-DEP-07).

---

## Where to look

| Topic | File |
|---|---|
| Aml.Engine insert order, class instantiation, names, foreign attributes | AMLPetriNet: dotnet/PtMapper.Conversion/PtWrite.cs |
| Update in place with wiring reuse, link cleanup, renumbering | AMLPetriNet: dotnet/PtMapper.Conversion/PtNetUpdater.cs |
| Deterministic ids, id space | AMLPetriNet: dotnet/PtMapper.Conversion/PtIds.cs |
| Id normalisation for the modeler | AMLPetriNet: dotnet/PtMapper.Conversion/PtIdText.cs |
| Library borrowing and DD path forms | AMLPetriNet: dotnet/PtMapper.Conversion/PtLibraryLoan.cs, PtDiPaths.cs; `starter/dotnet/Efl.Conversion/EflDiagramInterchange.cs` |
| Hardened XML reading, number formatting | AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs |
| Layered layout | AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs |
| Page-side bridge (import queue, simulation suppression) | AMLPetriNet: web/src/bridge.js |
| Upstream modeler patches, minification note | AMLPetriNet: web/src/index.js, web/build.mjs |
| Lossless PNML converter | AMLPetriNet: web/src/pnml/index.js |
| Plugin lifecycle (constructor bridge, hot swap, no Unloaded teardown) | AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs |
| WebView2 host with crash and reload handling | AMLPetriNet: Aml.Editor.Plugin.PetriNet/Bridge/PtWebView.cs |
| Packaging | AMLPetriNet: Aml.Editor.Plugin.PetriNet/Aml.Editor.Plugin.PetriNet.csproj |
| Web app hardening | AMLPetriNet: dotnet/PtMapper.Web/Program.cs |
| Rebuild race token, document callbacks, cache keys | AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs |
| Echo baseline, live sync, update dialogs, backups | AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs |
| Cycle-breaking export, hidden-tab import flag | AMLFPB.js: Aml.Editor.Plugin.FPB/fpbjs-assets/index.html |
| Reflection save and its limits | AMLFPB.js: Aml.Editor.Plugin.FPB/Bridge/EditorSaver.cs |
| API drift detection | AMLFPB.js: Aml.Editor.Plugin.FPB.Tests/ApiContractTests.cs, Aml.Editor.Plugin.FPB/Diagnostics/ApiCompatCheck.cs |
| Sibling-layout CI | AMLFPB.js: .github/workflows/build.yml |
| Boundary state ids, sub-process ids, update in place | fpb-aml-mapper: dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs |
| Reunification, parent resolution, waypoint fallback, id normalisation | fpb-aml-mapper: dotnet/FpbMapper.Conversion/CaexToFpbJson.cs |
| Golden snapshot tests | fpb-aml-mapper: dotnet/FpbMapper.Tests/GoldenTests.cs |
| Reconnect handling in updaters and rules | FPB.JS: app/fpb/modeling/updater/ConnectionUpdater.js, app/fpb/rules/FpbRuleProvider.js |
| Import robustness | FPB.JS: app/fpb/importer/JSONImporter.js |
| Library build configuration | FPB.JS: webpack.lib.config.js, src/index.js |
| Loading AML safely, schema file side effect | `starter/dotnet/Efl.Conversion/EflDocuments.cs` |
| Ready handshake with buffered push, crash and navigation handling | `starter/plugin/Aml.Editor.Plugin.Efl/Bridge/ModelerView.cs` |
| Ready handler, echo baseline, undone edits, document identity | `starter/plugin/Aml.Editor.Plugin.Efl/EflPlugin.xaml.cs` |
| Rule semantics for commands and `connection.start` | `starter/web/src/rules/Rules.js` |
| Package checks (contract DLL, versions, display name) | `starter/plugin/Aml.Editor.Plugin.Efl.Tests/PackageTests.cs` |
| Ports per connection end, flow routing in the layout, format version | `starter/dotnet/Efl.Conversion/EflToCaex.cs`, `starter/dotnet/Efl.Conversion/EflLayout.cs`, `starter/dotnet/Efl.Conversion/EflJson.cs` |
| Text rendering and explicit root in plain diagram-js | `starter/web/src/draw/Renderer.js`, `starter/web/src/io/json.js` |
