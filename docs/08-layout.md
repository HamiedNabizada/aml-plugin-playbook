# 08 Layout and graphical information

**In short**

- Geometry exists three times (canvas, exchange format, AML) with different anchors; convert at exactly one place per boundary and write the convention down (see §1, §9).
- Store node bounds, bend points and moved label offsets; fix and document what `PortCoordinate` means (cropped point or uncropped reference) (see §2).
- Write DD attribute type paths in the form the document already uses (inline, aliased, none) (see §1).
- A model without layout must never reach the canvas or the document unarranged: lay it out in the mapper, deterministically, and log how many nodes were placed (see §3, §4).
- Route what a grid draws badly: back edges in lanes of their own, forward edges that would cross a node, self loops; check the result on the lines and look at a screenshot (see §4.5).
- Place only what is missing; never re-layout nodes or connections that have geometry (see §5).
- Crop connection ends to the drawn outline with `CroppingConnectionDocking`, and compute the same docking points in the importer and in C# (see §6).
- `Waypoint_1..n`: read in numeric order, replace wholesale on write, bend points only (see §7).
- Long names below small shapes; label offsets from the modeler's own anchor, rebuilt label boxes of default size centred on the point (see §8).
- Invariant culture, no exponent, no NaN, identical number spelling in C# and JavaScript; never `NumberStyles.Any` for coordinates (see §10).
- Fit the viewport after importing a different model, not after an echo; export SVG with a margin and without editor furniture (see §11, §12).

Read fully when: you write the first importer or writer of geometry, or implement auto-layout. Skim when: a picture looks wrong (go to the bug list in §13 first).

This chapter covers everything about coordinates: where a diagram's geometry lives (canvas, exchange format, AML document), which parts to store and which to recompute, how to lay out a model that arrives without any geometry, how connections meet shape outlines, how bend points and label positions are stored, which coordinate conventions differ between the layers, how numbers must be formatted, and how to fit the viewport and export an SVG picture. Read it before you write the first `ViewInformation` attribute or the first importer that reads bounds, and again when a picture looks wrong: nearly every visual bug in the source projects came from two layers disagreeing about a convention described here. The mapping of the attributes themselves is in [04 AML mapping](04-aml-mapping.md); the modeler side (renderer, layouter, docking module) is in [02 Modeler on diagram-js](02-modeler-diagram-js.md).

## 1. Know the three places geometry lives

A diagram's geometry exists three times, and each copy has its own conventions:

| Layer | Node geometry | Connection geometry | Label geometry |
|---|---|---|---|
| Canvas (diagram-js DI) | `Bounds` x, y, width, height, **top-left** anchored | full polyline including both docking points | label element bounds, top-left, fixed size box |
| Exchange format (PNML in AMLPetriNet, FPB JSON in AMLFPB.js) | PNML: `<position>` is the **centre**, `<dimension>` the size | PNML: bend points only; FPB JSON: full polyline with `original` on the end points | PNML: `<offset>` relative to the node centre (or to the polyline middle for an arc) |
| AML document | `ViewInformation` (DD_Bounds with `position` x/y, width, height), top-left | `PortCoordinate` (DD_Point) on each end interface, `Waypoint_1..n` (DD_Waypoint) for bend points | `LabelOffset` (DD_Point) instance attribute (AMLPetriNet only) |

The AML side uses the language-neutral OMG_DD attribute type library (`DD_Bounds`, `DD_Point`, `DD_Waypoint`), shared by both domain libraries (`AMLPetriNet: dotnet/PtMapper.Conversion/DiagramInterchangeLibrary.cs:25`). Reuse it rather than defining your own point type per language. The starter uses it too: the published file lies in `starter/libraries/`, the mapper carries it as an assembly resource and embeds it into every document it creates, and every layout attribute takes its type path from `EflDiagramInterchange` (`starter/dotnet/Efl.Conversion/EflDiagramInterchange.cs:29-38`, used in `starter/dotnet/Efl.Conversion/EflWrite.cs:139`, `:142`, `:163-164`, `:185`).

Where the bend points hang depends on the connection encoding ([04 AML mapping](04-aml-mapping.md)):

- Connection as InternalLink (FPD, `EflConnectionStyle.Link`): a link has no attributes, so the bend points go on the **source side interface** and each end interface carries its `PortCoordinate` (`starter/dotnet/Efl.Conversion/EflToCaex.cs:133-140`, `fpb-aml-mapper: dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs:1008`).
- Connection as reified element (PT arcs, `EflConnectionStyle.Element`): the bend points go on the connection element itself, the docking points on its `Source`/`Target` interfaces (`AMLPetriNet: dotnet/PtMapper.Conversion/PtNetToCaex.cs:141`, `starter/dotnet/Efl.Conversion/EflToCaex.cs:163`). The `ViewInformation` rectangle the arc class inherits stays unused (`AMLPetriNet: dotnet/PtMapper.Conversion/PtNames.cs:78`).

Write the attribute type path in the form the document already uses. A document can carry OMG_DD inline (path `OMG_DD_AttributeTypeLib/DD_Point`), reference it under some alias (`OMG_DD@OMG_DD_AttributeTypeLib/DD_Point`, or the author's own alias), or neither. Writing the aliased form into a self-contained document left a dangling alias on every attribute a sync touched, and the round-trip check refused the result. `PtDiPaths` resolves the form once per document (`AMLPetriNet: dotnet/PtMapper.Conversion/PtDiPaths.cs:11`, resolution order at `:54`). The starter's `EflDiagramInterchange.PathOf` looks at the document of the attribute's owner on each write and recognises a reference by the library's file name, whatever alias the document chose (`starter/dotnet/Efl.Conversion/EflDiagramInterchange.cs:76-99`).

## 2. Decide per piece what is stored and what is recomputed

Make the decision explicitly and write it down in the library description; both projects had to.

**Node bounds: store.** Nobody can recompute where a user dragged a shape.

**Connection end points: recompute in the modeler, but also write them to AML.** The end points follow from node geometry and the connection's first/last bend point. PNML therefore carries only bend points: writing the ends "would be redundant and would show up as stray bends in other tools" (`AMLPetriNet: web/src/pnml/index.js:27`). The AML side nevertheless writes `PortCoordinate` so that an AML tool without a layouter shows the same picture as the canvas (`starter/dotnet/Efl.Conversion/EflGeometry.cs:6`). This means the mapper must compute docking points exactly like the modeler (section 6).

**What `PortCoordinate` means differs between the two projects, and you must pick one meaning.** AMLPetriNet stores the cropped point on the outline. The FPD mapper stores the `original` of the modeler's cropped waypoint when present, which is the uncropped reference point diagram-js keeps (`fpb-aml-mapper: dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs:898`; diagram-js `lib/layout/CroppingConnectionDocking.js` assigns `original: docking.point.original || docking.point`). Both work inside their own tool chain; a third tool reading both documents would draw them differently. Document the choice in the connection class description.

**Bend points: store.** Replace them wholesale on every write (section 7).

**Label positions: store only when moved.** AMLPetriNet writes `LabelOffset` only when the label has a position of its own, and removes the attribute when the label is reset: "A label moved back to its default position must not leave the old offset behind, or the next import would move it away again" (`AMLPetriNet: dotnet/PtMapper.Conversion/PtWrite.cs:240`). The FPD mapper stores no label geometry at all (a search for "label" in `FpbMapper.Conversion` finds nothing), so dragged state labels in FPB.JS are lost across AML.

**Whole layout for simple languages: optional.** The three-phase method (Nabizada, Drath, Fay; at Automatisierungstechnik, 2026, forthcoming) argues that basic P/T nets do not need stored layout because a layered algorithm can arrange them from topology alone, which makes the DI library optional for that language. AMLPetriNet follows that and quotes it in `AMLPetriNet: docs/roundtrip-validation.md:60`. The consequence is documented as well: opening such a net in the editor arranges it, and the next sync writes the generated layout into the document (41 geometry attributes on the paper example: 11 `ViewInformation`, 24 `PortCoordinate`, 6 `Waypoint_n`; `AMLPetriNet: docs/roundtrip-validation.md:127`). Treat that as intended and make your round-trip check report it rather than fail on it.

## 3. Never let a model without layout reach the canvas unarranged

Foreign files often carry no geometry: PNML from other tools, hand-authored AML, documents generated by scripts. Every failure mode below actually happened:

| Symptom | Cause | Fix | Evidence |
|---|---|---|---|
| Whole net drawn as one pile at the origin | nodes without `<graphics>` got (0,0) | layered layout on import and before display | `AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs:8` |
| Empty canvas, import aborted | FPB.JS importer dereferenced the connection's visual entry, which a layout-free AML did not produce | mapper always emits a connection visual with at least two waypoints, centre to centre | `fpb-aml-mapper: dotnet/FpbMapper.Conversion/CaexToFpbJson.cs:492`, tests `dotnet/FpbMapper.Tests/WaypointFallbackTests.cs:7` |
| SystemLimit invisible | no `ViewInformation`, rendered as a 0x0 box | writer applies a default of (100, 100, 600, 400) | `fpb-aml-mapper: dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs:79` |
| Shapes scattered differently on every open | importer fallback uses `Math.random()` for missing positions | none yet; do not copy this | `FPB.JS: app/fpb/importer/ImportUtils.js:150`, constants `app/fpb/importer/ImportConstants.js:40` |

Rules derived from that:

1. **Lay out in the mapper, in C#, not only in the browser.** The same code then serves plugin, web app and CLI (`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:872`, `dotnet/PtMapper.Web/Program.cs:134`, `dotnet/PtMapper.Tool/Program.cs:360`).
2. **Arrange before writing, not just before displaying.** Otherwise a freshly imported file sits in the document without geometry until the user happens to press Update (`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:952`).
3. **Apply the same preparation wherever the document is read while the canvas is untouched.** An export that skips it would differ from the diagram it claims to be (`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:857`).

The starter follows rules 1 to 3 with `EflLayout.ArrangeMissing`: before display in the plugin (`starter/plugin/Aml.Editor.Plugin.Efl/EflPlugin.xaml.cs:343`, with the count in the status line), before writing an imported JSON file into the document (`:542`), on JSON export of a stored diagram (`:577`), in the web API (`starter/dotnet/Efl.Web/Program.cs:47`, `:67`) and in the CLI (`starter/dotnet/Efl.Tool/Program.cs:44`, `:59`). The CLI's `arrange` command runs it on its own and writes the placed model as JSON, which is how the screenshot tool shows the arranged form of a file (`starter/dotnet/Efl.Tool/Program.cs:87-96`, `starter/web/tools/screenshot.mjs:37-54`).
4. **Return a count and log it.** `ArrangeMissing` returns how many nodes it placed so the user learns that the file had no layout (`AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs:38`).
5. **Be deterministic.** No randomness, ties broken by ordinal id comparison, so the same file opens the same way every time and golden tests are possible (`AMLPetriNet: dotnet/PtMapper.Tests/LayoutTests.cs:106`).
6. **Defend on both sides of a bridge.** The mapper guarantees connection visuals, and FPB.JS additionally synthesizes a centre line when a visual is still missing (`FPB.JS: app/fpb/importer/JSONImporter.js:516`). Note that `buildSystemLimit` in the same importer still dereferences `vI.type` without a guard (`FPB.JS: app/fpb/importer/JSONImporter.js:328`), which is why the mapper default in the table above matters.
7. **For container shapes, keep the fallback inside the reader's null rule.** The FPD reader treats an all-zero `ViewInformation` (x, y, width and height 0) as absent (`fpb-aml-mapper: dotnet/FpbMapper.Conversion/CaexToFpbJson.cs:794`), because class templates carry empty attributes that parse as zero.

## 4. Use a layered layout, in this order

AMLPetriNet's `PtLayout` is the reference implementation (650 lines, heavily commented); the starter's `EflLayout` is the reduced version you can copy first: cycle breaking, longest-path columns, two barycentre sweeps, a grid, and routing of the flows the grid would draw badly (`starter/dotnet/Efl.Conversion/EflLayout.cs:3-16`). The phases follow the classic layered (Sugiyama style) method. Each refinement below was added because a real net looked wrong without it.

### 4.1 Break cycles with a depth-first search

An arc to a node that is still on the DFS stack closes a cycle; set it aside as a back edge. Do this first: "relaxing it over a cycle once laid a ten-node loop out seventeen thousand pixels wide" (`AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs:74`). That is the "element drawn far away from the rest" failure; its regression test asserts the width stays within node count times column spacing (`AMLPetriNet: dotnet/PtMapper.Tests/RegressionTests.cs:140`).

Where the DFS starts decides which arc of a cycle counts as the back edge, and that decides how the whole diagram reads. Start at the language's natural entry points, then at nodes nothing feeds, then at everything else, each group in id order:

```csharp
var roots = net.Nodes
    .OrderBy(n => n is PtPlace { InitialMarking: > 0 } ? 0 : hasIncoming.Contains(n.Id) ? 2 : 1)
    .ThenBy(n => n.Id, StringComparer.Ordinal)
    .Select(n => n.Id);
```
(`AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs:184`)

For Petri nets the entry point is a marked place. Starting at a merely unfed input place (`Result_NOK`) laid the paper's cyclic net out beginning in the middle, all eleven nodes in one row, with two arcs straight through four nodes each (`AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs:158`, test `dotnet/PtMapper.Tests/RegressionTests.cs:222`). For your language, decide what "where a reader starts" means (a start event, an input state, a node with a flag) and put it first.

Write the DFS iteratively with an explicit stack; a long chain would otherwise be a deep recursion (`AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs:179`). The starter's recursive `Visit` is fine for toy sizes only (`starter/dotnet/Efl.Conversion/EflLayout.cs:82`).

### 4.2 Assign columns by longest path, then pull sources right

Process the acyclic remainder in topological order (Kahn queue, ready nodes sorted by id) and set `column[next] = max(column[next], column[id] + 1)` (`AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs:111`). Build dictionaries with `TryAdd`, not `ToDictionary`: a model with duplicate ids is invalid, the validator reports it, but laying it out must not throw on the way to that message (`AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs:112`).

Then pull every node that nothing feeds to just before the earliest node it feeds, otherwise an input that supplies only a late step sits at the far left with an arc across the whole diagram. Afterwards shift all columns so the leftmost is zero again (`AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs:137`).

The notes record that a sprint once replaced longest path with BFS layering; the current code is longest path again, combined with the source pull. Do not trade one for the other without the defect measurement from 4.3.

### 4.3 Order within columns by barycentre, and measure

Sweep columns forwards and backwards; each node moves to the average row of its neighbours in the adjacent column, nodes without neighbours keep their row rather than jumping to the top (`AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs:430`). Back edges take no part, since they are routed separately and would pull their ends to the far side.

Three things make this robust:

- **Several starting orders** (id order, reversed, busiest node first), best result kept (`AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs:284`). "Measuring rather than trusting the last pass is what makes the outcome independent of the ids."
- **Neighbour swapping (transpose)** while it removes defects, skipped above 80 nodes because the defect count is quadratic in arcs (`AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs:446`, limit at `:33`). A 1000-node chain must lay out within 5 s (`AMLPetriNet: dotnet/PtMapper.Tests/LayoutTests.cs:132`).
- **Count the right defects.** Counting only crossings "reported the paper's extended net as perfect while two arcs ran through four nodes each". Count an arc passing within half a node of a node that is not one of its ends as well, weighted double (`AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs:490`, weight at `:551`).

Result on the paper net: 4 crossings down to 0 (`AMLPetriNet: dotnet/PtMapper.Tests/RegressionTests.cs:160`).

### 4.4 Straighten the main path

The ordering fills each column from the top, so a column with a single node put it in row 0 even when everything it connects to sat lower, which bent the main path into a diagonal. Keep the tallest column fixed, then shift each other column as a whole (most connected first) by the average offset to its settled neighbours, in half rows so a column of two sits symmetrically between three nodes. Keep the result only if it has no more defects than before:

```csharp
var offset = Math.Round(wishes.Average() * 2, MidpointRounding.AwayFromZero) / 2;
foreach (var id in members[next.Column]) shifted[id] = row[id] + offset;
...
return Defects(net, column, shifted, back) <= Defects(net, column, row, back) ? shifted : row;
```
(`AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs:411`, `:422`; test `dotnet/PtMapper.Tests/RegressionTests.cs:185` asserts `Idle`, `Start`, `Checking` share one y and the two branches sit symmetrically above and below.)

### 4.5 Route back edges around the drawing

A straight return arc from the end of a loop to its start runs through everything between. Give each back edge its own lane above or below the drawing (above when nothing sits above either end in its column), leaving the source vertically and entering the target vertically (`AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs:560`). When the lane is below and the end node carries its name underneath, enter and leave **from the side** instead, or the route runs through the name (`AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs:615`; test asserts the lower route's last waypoint has the target's centre y, `dotnet/PtMapper.Tests/RegressionTests.cs:263`).

Route only arcs between nodes this call placed and only arcs without bend points of their own: "a route someone drew is theirs" (`AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs:599`).

Know the modeler behaviour that undoes routing: diagram-js redraws the connections of a dragged node **straight**. Dragging `Idle`, where both loops end, turns both routed loops into lines across the picture, and the session then stores 35 instead of 41 geometry attributes (`AMLPetriNet: docs/roundtrip-validation.md:171`). Warn users before a live demo, and pick a node that touches no loop when measuring.

What is still missing in `PtLayout` compared with the literature: no routing phase for long forward edges (they stay straight lines). Say so if your language has many long edges.

The starter's `EflLayout.RouteFlows` is the small version of this phase, and it also routes forward flows (`starter/dotnet/Efl.Conversion/EflLayout.cs:175-289`). It exists because placing nodes was not enough: in the trial run of the playbook the arranged example drew a back flow exactly on top of the forward flow between the same two nodes, so the reader saw one line where there were two, and a flow that skipped a column ran straight through the node in between. Every browser test passed; the first screenshot showed both. Most graphical languages are cyclic, so expect this with the first real file.

- **Back edges** (flows the DFS set aside, and any flow whose target column is not to the right of its source) leave the source to the right, run along a lane **below** the drawing and come up into the target from the left.
- **Forward edges whose straight line would enter a node** they do not connect take the same shape **above** the drawing. The check is a Liang and Barsky clip against the open box of every other node, run for every forward flow, not only for flows that skip a column, because a steep line to a far row of the next column clips a neighbour of its own source as well (`:335-372`).
- **Self loops** get three bend points: out of the right side, up past the top edge, down into the top; a second loop on the same node reaches higher (`:256-268`). Without bend points a flow from a node to itself has no length and disappears.
- Every routed flow gets **its own lane** (30 px apart, the first 30 px outside the bounding box of all positioned nodes) and its own vertical channel in the gaps between columns; the slots are spread evenly over a gap, so a busy gap packs its legs closer instead of pushing into the next column (`:27-31`, `:304-333`). Routes meet a node a quarter of its height off the centre line, because a straight flow to the neighbouring column runs along that centre line.
- Candidates are sorted by where the flow sits in the drawing (columns and rows of both ends, then id), not by list position, so the lanes do not change when the same flows arrive in another order (`:208-217`).
- Only flows **between two nodes this call placed** and **without waypoints of their own** are routed: a route somebody drew is theirs, and around a node that already had a position the gaps are not known to be free (`:195-197`, `:210-211`).

`starter/dotnet/Efl.Tests/LayoutTests.cs` checks the lines, not only the nodes; a test that only asked whether nodes overlap passed on exactly the pictures described above. Its helpers sample every segment against every node box that is not one of the flow's ends, and look for two collinear segments that overlap over some length; the sampling is deliberately different arithmetic from the clip in the code under test (`starter/dotnet/Efl.Tests/LayoutTests.cs:24-83`). The seven tests cover a three-node cycle, two flows in opposite directions between the same nodes, several back flows, a flow that skips a column (with a guard asserting that the unrouted line would have crossed), a self loop with three bend points and a positive length, flows at nodes that already had positions left untouched, and arranging the same model twice giving the same JSON (`:85-197`).

The starter does not enter named nodes from the side as described above: its names are drawn inside the shapes. Add that if your labels sit under the shape.

### 4.6 Grid and constants

Positions are `origin + column * spacing`, `origin + row * spacing`, with origin (80, 80) in both implementations (`AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs:35`, `starter/dotnet/Efl.Conversion/EflLayout.cs:24-25`). Choose spacings from your largest default shape plus the width of a label under it: PtLayout uses 50 px nodes at 140 by 110 px, the starter 100 by 60 px nodes at 180 by 120 px. Language-specific shapes keep their own size (a silent transition is 20 px wide, `AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs:54`).

## 5. Place only what is missing, keep everything else

`ArrangeMissing` sets bounds only on nodes with `Bounds == null` and returns 0 on a second call (`AMLPetriNet: dotnet/PtMapper.Tests/LayoutTests.cs:56`, `:68`). Never re-layout nodes that have positions, not even "to tidy up".

The same rule applies to connections inside the modeler. FPB.JS once called `layoutConnection` on every connection after each layer switch, decomposition and composition, which destroyed user bend points each time (commit `8f1fc08` in FPB.JS). Before that, a workaround nudged shapes by +3/-3 px to force waypoint recalculation, which polluted the command stack (commit `7f96aaa`). The current code re-lays out only connections attached to newly created or repositioned boundary states (`FPB.JS: app/fpb/modeling/cmd/DecomposeProcessOperator.js:283`).

Known limitation to handle if your files arrive with **partial** layout: `PtLayout` computes columns over all nodes but ignores existing positions when placing the missing ones, so a new node can land on top of an existing one. If partial layout is common for your language, offset the generated grid to the right of or below the bounding box of the placed nodes.

## 6. Crop connections to the real outline, identically on both sides

### In the modeler

Register `CroppingConnectionDocking` as `connectionDocking` and crop in your layouter or after `connection.layout`/`connection.create` (`starter/web/src/modeling/index.js:22`, `starter/web/src/modeling/Layouter.js:37`; `FPB.JS: app/fpb/modeling/index.js:54`, `app/fpb/modeling/updater/DiUpdater.js:41`). diagram-js's `BaseLayouter` returns a centre-to-centre line and leaves cropping to the caller; without it the arrow head is hidden in the middle of the target (`starter/web/src/modeling/Layouter.js:7`).

Cropping uses the renderer's `getShapePath`, so the outline must match the drawing: an ellipse drawn as an ellipse needs an elliptical path, or arrows end inside or short of the shape (`starter/web/src/draw/Renderer.js:22`, path at `:89`). The starter's layouter keeps the user's bend points and recomputes only the two ends (`starter/web/src/modeling/Layouter.js:12`), and `verify-modeler.mjs` checks that flow ends sit on the outlines, not in the centres (`starter/web/tools/verify-modeler.mjs:25`).

Language-specific docking belongs in the layouter. FPB.JS docks flows bottom-centre to top-centre and usages left/right depending on which side the resource sits, and does not crop usages at all (`FPB.JS: app/fpb/modeling/FpbLayouter.js:38`, `app/fpb/modeling/updater/DiUpdater.js:47`).

### On import

The modeler does not run its docking layout on import. If the file supplies only bend points, compute the end points before handing the DI to the canvas:

```js
// Endpoints are not carried in PNML. The modeler does not run its docking
// layout on import either, so anchoring at the node centres would draw the
// arrow head inside the shape. Crop them here instead.
const start = dockingPoint(fromBox, bends[0] ?? centreOf(toBox ?? ORIGIN_BOX));
const end = dockingPoint(toBox, bends[bends.length - 1] ?? centreOf(fromBox ?? ORIGIN_BOX));
```
(`AMLPetriNet: web/src/pnml/index.js:722`)

The direction for each end is the nearest bend point, or the other node's centre when there is none.

### In the mapper

The C# side computes the same points for `PortCoordinate`. Keep one geometry per shape kind and mirror the JavaScript exactly; the file says so: "This mirrors dockingPoint() in web/src/pnml/index.js; keep the two in step" (`AMLPetriNet: dotnet/PtMapper.Conversion/PtGeometry.cs:9`).

```csharp
if (a <= 0 || b <= 0) return centre;           // zero-size node: 0/0 would give NaN

double scale;
if (node is PtPlace)
{
    // Ellipse: solve (dx*s/a)^2 + (dy*s/b)^2 = 1 for s.
    scale = 1 / Math.Sqrt(Math.Pow(dx / a, 2) + Math.Pow(dy / b, 2));
}
else
{
    // Rectangle: shrink to whichever edge is hit first.
    var byX = dx == 0 ? double.PositiveInfinity : a / Math.Abs(dx);
    var byY = dy == 0 ? double.PositiveInfinity : b / Math.Abs(dy);
    scale = Math.Min(byX, byY);
}
return new PtPoint(Round(centre.X + dx * scale), Round(centre.Y + dy * scale));
```
(`AMLPetriNet: dotnet/PtMapper.Conversion/PtGeometry.cs:36`)

The zero-size guard exists because foreign PNML with `<dimension x="0">` produced NaN geometry. The starter's `EflGeometry` mirrors its renderer the same way: a Step is cropped against its box, a Store against the ellipse inside its box (`starter/dotnet/Efl.Conversion/EflGeometry.cs:54-66`). An earlier starter version cropped the Store against its box, which puts every stored end point next to the circle except on the axes; the comment explains that failure (`starter/dotnet/Efl.Conversion/EflGeometry.cs:32`) and two tests pin both shapes (`starter/dotnet/Efl.Tests/ExampleAndGeometryTests.cs:56`, `:75`). The starter guards a zero direction (`starter/dotnet/Efl.Conversion/EflGeometry.cs:45`) and, like `PtGeometry`, a zero size: its CAEX reader accepts a stored width or height of 0 (`starter/dotnet/Efl.Conversion/CaexToEfl.cs:248`), so a node without size docks at its centre instead of writing NaN (`starter/dotnet/Efl.Conversion/EflGeometry.cs:50-52`, test `starter/dotnet/Efl.Tests/ExampleAndGeometryTests.cs:68`).

## 7. Store bend points as an ordered, wholesale-replaced set

- **Name them `Waypoint_1..n`, typed DD_Waypoint with a `position` child** (`AMLPetriNet: dotnet/PtMapper.Conversion/PtWrite.cs:279`, `fpb-aml-mapper: dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs:2087`).
- **Sort by the parsed numeric suffix on read**, never by attribute name or document order (`Waypoint_10` sorts before `Waypoint_2` as a string) (`AMLPetriNet: dotnet/PtMapper.Conversion/CaexToPtNet.cs:282`, `fpb-aml-mapper: dotnet/FpbMapper.Conversion/CaexToFpbJson.cs:815`, `starter/dotnet/Efl.Conversion/CaexToEfl.cs:253`).
- **Remove all old `Waypoint_` attributes before writing the new set**, otherwise a shortened polyline keeps its tail (`AMLPetriNet: dotnet/PtMapper.Conversion/PtWrite.cs:265`, `fpb-aml-mapper: dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs:924`). This is safe because the waypoint attributes belong entirely to the diagram; never apply wholesale replacement to attributes a user may annotate.
- **Store bend points only**; the two ends go to `PortCoordinate` (`AMLPetriNet: web/src/pnml/index.js:388`, `fpb-aml-mapper: dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs:902`).
- **Keep the in-memory shape identical on first read and on echo.** The FPD fallback waypoints must carry the same `original` substructure as real port points, because the write side persists them as `PortCoordinate` and the next read must reproduce them one to one; without it the edit echo cycle drifted, which the showcase round-trip tests exposed (`fpb-aml-mapper: dotnet/FpbMapper.Conversion/CaexToFpbJson.cs:867`).
- **Mirror imported waypoints into the DI objects.** In FPB.JS, `di.waypoint` was only written on layout, move or waypoint update, so a connection imported and never touched had a DI edge without waypoints. A ParallelFlow partner read its partner's DI waypoints and got nothing. Fixed by mirroring on import and by reading the partner's live shape first (`FPB.JS: app/fpb/importer/JSONImporter.js:529`, `app/fpb/modeling/FpbLayouter.js:117`).
- **Count geometry per value in your round-trip check**, one per bounds, one per `PortCoordinate`, one per waypoint. Counting only node bounds reported nine values where twenty-five attributes had been written, and that wrong number went into a paper's document (`AMLPetriNet: dotnet/PtMapper.Tests/RegressionTests.cs:421`).

## 8. Place labels where long names fit

### Long names go below the shape

A 50 px transition box broke "Approve" into "Appr" over "ove". AMLPetriNet now draws transition names underneath, in the same 90 px width and external style the modeler uses for place names; only the drawing changes, the model and PNML stay the same:

```js
renderer.drawTransition = function drawTransitionNamedBelow(parentGfx, element) {
  this.renderEmbeddedLabel = () => undefined;       // keep the box, drop the name inside it
  try { drawTransition.call(this, parentGfx, element); }
  finally { delete this.renderEmbeddedLabel; }

  const name = element.businessObject?.name;
  if (!element.businessObject?.isSilent && name) {
    const text = this.renderLabel(parentGfx, name, {
      box: { width: 90, height: 30 },
      align: 'center-top',
      padding: 0,
      style: { ...textRenderer.getExternalStyle(), fill: 'black' },
    });
    text.setAttribute('transform', `translate(${element.width / 2 - 45}, ${element.height + 2})`);
  }
  return parentGfx;
};
```
(`AMLPetriNet: web/src/index.js:73`, abridged)

Text layout uses only the box size, never its position, which is why the text is moved with a transform. FPB.JS hit the same bug on state labels and widened the external label box from 90 to 150 px "to prevent mid-word breaks", wrapping at spaces and hyphens with at most 3 lines, and truncating embedded labels to one line with an ellipsis and a hover tooltip (commit `31dc89c`; `FPB.JS: app/fpb/core/FpbConstants.js:46`, `:75`). The starter still draws names inside the shape and carries the warning (`starter/web/src/draw/Renderer.js:25`). The excerpt above uses the Petri net package's `textRenderer` service; plain diagram-js has none, so the starter's renderer creates its own `Text` from `diagram-js/lib/util/Text` with a style written onto every text element (`starter/web/src/draw/Renderer.js:34`, style at `:15`). Use that object's `createText` with a box and a transform for names below a shape. Decide label placement per shape type before the first user test; it is the first complaint. Line breaking of long transition names under the box was still an open decision in AMLPetriNet at the time of writing.

Consequences of labels below shapes:
- The layout must leave vertical room (row spacing) and back-edge routes must avoid the area under named nodes (section 4.5).
- A silent or unnamed element has no label, so routes may enter it from below (`AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs:623`).

### Label offsets: one anchor, one fixed box

Store a label position as an offset from an anchor both sides compute identically:
- node label: offset of the label box centre from the node centre (`AMLPetriNet: web/src/pnml/index.js:316`);
- connection label: offset from the middle of the polyline's middle segment, using the modeler's own `getWaypointsMid` formula, "because the two have to agree" (`AMLPetriNet: web/src/pnml/index.js:177`). Do not use the flow-label indent; that is only the default placement.

When rebuilding the DI label from an offset, create a box of the modeler's default label size, **centred** on the point. The modeler treats bounds x/y as the top-left corner, so a 0x0 box "would put the intended centre at the top edge, and every session that touched the node would sink the label another half height" (`AMLPetriNet: web/src/pnml/index.js:660`). That drift bug is invisible in a single round trip and obvious after five; test several consecutive sessions.

Find out which label the modeler actually renders before storing one. In the Petri net modeler an arc's rendered label is its **inscription** (weight), not its name, so "arc label position" and "weight position" are the same thing (`AMLPetriNet: web/src/pnml/index.js:372`). Other editors write the default weight `1` with an offset; dropping the default inscription lost that position on AML to PNML to AML, so it is written whenever an offset exists (`AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:352`).

FPB.JS still has an unresolved default: `getExternalLabelMid` returns `x: element.x - element.width / 2` with a TODO "Find the correct label position" (`FPB.JS: app/fpb/help/utils.js:148`). Do not copy it; derive the default label position from the shape centre.

## 9. Keep coordinate conventions straight

| Convention | Value | Where it bit |
|---|---|---|
| DC Bounds, diagram-js shapes, AML `ViewInformation` | top-left | `AMLPetriNet: dotnet/PtMapper.Conversion/Models/PtModels.cs:7` |
| PNML `<position>` of a node | centre; bounds = centre minus dimension/2 | `AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:279` |
| Label offset | relative to node centre or polyline middle | section 8 |
| diagram-js label element bounds | top-left of a fixed size box | section 8 |
| FPB JSON port points | cropped point plus `original` | section 2 |

Rules:

1. **Convert at exactly one place per boundary** and name the convention in a comment at the top of the converter, as `AMLPetriNet: web/src/pnml/index.js:21` does.
2. **Compute a centre from the size you actually write.** When the size is rounded for the file, derive the written centre from the rounded size, so reading back puts the top-left corner where it was and only the size moves (`AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:419`, test `dotnet/PtMapper.Tests/IdentityAndSessionTests.cs:358`).
3. **Check geometric predicates for the anchor they assume.** FPB.JS decided whether a state lies on the system border by comparing the state's `y` with the border; an audit flagged that it mixed top-left and centre. The current code compares the state centre with a 30 px tolerance (`FPB.JS: app/fpb/help/helpUtils.js:41`).
4. **Keep generated content near the origin.** The layout starts at (80, 80), shifts columns so none is negative and moves the top row to zero (`AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs:145`, `:418`). FPB.JS resets the viewbox to (0, 0) at zoom 1 on every layer switch (`FPB.JS: app/fpb/modeling/cmd/SwitchProcess.js:66`), so content placed thousands of pixels away is simply off screen.
5. **Fall back to the modeler's default size when a file gives none or a non-positive one**, identically in C# and JavaScript, or the same file opens with different node sizes in mapper and modeler; a negative height reached the SVG and the renderer dropped the shape (`AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:272`, `web/src/pnml/index.js:487`).

## 10. Format and parse numbers the same way everywhere

On a machine with a German locale, `12.5.ToString()` gives `12,5`, and both source projects were developed on such machines. Every coordinate that crosses a boundary must be written and read with the invariant culture.

**Writing.** Use one formatter per side, integers without `.0`, at most two decimals, no exponent, nothing non-finite:

```csharp
private static string Format(double value)
{
    if (!double.IsFinite(value)) value = 0;
    return value == Math.Floor(value) && Math.Abs(value) < 1e15
        ? ((long)value).ToString(CultureInfo.InvariantCulture)
        : value.ToString("0.##", CultureInfo.InvariantCulture);
}
```
(`AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:454`, same in `starter/dotnet/Efl.Conversion/EflWrite.cs:236`)

The JavaScript side must spell numbers identically, or "a diagram would appear to have changed every time it crossed between them" (`AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:446`). Two traps on that side:

- `String(n)` prints float remainders such as `5.551115123125783e-17` in exponent notation, which is not an `xs:decimal`, so the PNML became invalid. Round explicitly, away from zero like .NET, and turn `-0` into `0` (`AMLPetriNet: web/src/pnml/index.js:200`; test `dotnet/PtMapper.Tests/IdentityAndSessionTests.cs:350`).
- Schema limits apply to some values: PNML `<dimension>` allows at most four digits with one decimal, so `50.25` and `12000` were invalid. Clamp and round sizes separately from positions, in both implementations (`AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:433`, `web/src/pnml/index.js:216`).

Verify cross-language agreement with a browser test that feeds the same files through both converters (`AMLPetriNet: web/tools/verify-interop.mjs`).

**Reading.** Parse with `NumberStyles.Float` and `CultureInfo.InvariantCulture`, then reject non-finite values: `"NaN"` and overlarge numbers parse successfully and "render as nothing at all" (`AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:298`, `starter/dotnet/Efl.Conversion/CaexToEfl.cs:287`). Apply the check on **every** reader: AMLPetriNet's CAEX reader parses without the finiteness check (`AMLPetriNet: dotnet/PtMapper.Conversion/CaexToPtNet.cs:325`), and `PtWrite.SetChild` formats with `"0.##"` without a guard (`dotnet/PtMapper.Conversion/PtWrite.cs:355`), so a `NaN` typed into the AML Editor would travel through.

Do not use `NumberStyles.Any` for coordinates. It includes `AllowThousands`, so under the invariant culture `"1,5"` (a value typed by a German user) parses as `15` instead of failing. The FPD reader uses `NumberStyles.Any` (`fpb-aml-mapper: dotnet/FpbMapper.Conversion/CaexToFpbJson.cs:783`, `:807`).

Round computed coordinates (docking points) to two decimals before storing; it keeps files short without visibly moving anything (`AMLPetriNet: dotnet/PtMapper.Conversion/PtGeometry.cs:75`, `web/src/pnml/index.js:549`).

Treat empty placeholder attributes as absent. The FPD writer appends an empty `PortCoordinate` with typed but valueless `x`/`y` when no layout exists (`fpb-aml-mapper: dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs:2076`); its reader returns null when parsing fails (`dotnet/FpbMapper.Conversion/CaexToFpbJson.cs:802`), which is what triggers the fallback in section 3.

## 11. Fit the viewport after an import

diagram-js does not move the viewport on import. A model laid out at (80, 80) is visible by accident; a foreign file with coordinates in the thousands shows an empty canvas.

- Call `canvas.zoom('fit-viewport', 'auto')` after each successful import of a different model. Do not call it after an echo re-import of the model the user is editing, or the view jumps on every sync.
- FPB.JS calls `zoom('fit-viewport')` once at startup on the empty canvas (`FPB.JS: app/app.js:42`), which fits nothing. Neither AMLPetriNet's bridge nor the FPD plugin source calls it after import (a search outside `node_modules` finds no call); AMLPetriNet only scrolls to an element when a finding is double-clicked (`AMLPetriNet: web/src/bridge.js:225`). Treat this as an open improvement in both projects rather than a pattern to follow.
- The starter fits the viewport at the end of every `importModel` (`starter/web/src/EflModeler.js:106`), echo imports included. That is correct as long as the host pushes a model only when the document changed from outside or a view is (re)built, never as the answer to the page's own `changed` message. If your host re-imports after each sync, move the fit into the host's decision (for example an option on the import message) instead of dropping it.
- When switching between views of the same model (layers), decide whether to restore the previous viewbox or reset it, and make sure the reset target contains the content (section 9, rule 4).

## 12. Export SVG with a margin, inline styles and fonts

Export the modeler's own drawing rather than redrawing the model elsewhere; a figure should show exactly what the canvas shows (`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:986`).

The raw `saveSVG` of diagram-js sizes the picture to the geometry's bounding box, which cuts the outer half of edge strokes and leaves labels flush with the border. Add a margin by rewriting width, height and viewBox together:

```js
export async function exportSvg(modeler, margin = 10) {
  const result = await modeler.saveSVG();
  const svg = typeof result === 'string' ? result : result.svg;

  return svg.replace(
    /width="([\d.-]+)"\s+height="([\d.-]+)"\s+viewBox="([\d.-]+) ([\d.-]+) ([\d.-]+) ([\d.-]+)"/,
    (match, width, height, x, y, boxWidth, boxHeight) =>
      `width="${Number(width) + 2 * margin}" height="${Number(height) + 2 * margin}" `
      + `viewBox="${Number(x) - margin} ${Number(y) - margin} `
      + `${Number(boxWidth) + 2 * margin} ${Number(boxHeight) + 2 * margin}"`);
}
```
(`AMLPetriNet: web/src/svg.js:10`)

The regular expression accepts negative numbers because the viewBox origin is the content's top-left, not (0, 0).

Check the result in a real browser, not by eye: size and viewBox agree, margin is exactly 20/20 larger than the raw export, every rendered label is present, a `font-family` is set, and there is no empty label element (`AMLPetriNet: web/tools/verify-svg.mjs:50`). Only check labels the canvas actually renders; an arc name is never drawn (`AMLPetriNet: web/tools/verify-svg.mjs:38`).

The starter writes its own `saveSVG` instead of rewriting the diagram-js output: it takes the bounding box of the active layer, adds the margin, clones the layer and removes the editor furniture (`.djs-hit`, `.djs-outline`, bend point handles, segment draggers), which outside the page's CSS would show up as black boxes, and copies the canvas `defs` so the arrow marker travels with the file (`starter/web/src/EflModeler.js:125`, furniture at `:141`). Its renderer writes the label style onto every text element for the same reason (`starter/web/src/draw/Renderer.js:15`). The browser check asserts the picture has the drawing and none of the furniture (`starter/web/tools/verify-modeler.mjs:170`).

FPB.JS writes its own `saveSVG` and shows three further traps (`FPB.JS: app/fpb/FpbModeler.js:360`):
- paths without an explicit fill rendered black outside the page's CSS, so `fill: none` is added to path styles that have none (`:380`);
- the grid pattern from the canvas `defs` must be removed (`:390`);
- it takes the content group's `getBBox()` without margin (`:403`), so the stroke clipping above applies there too.

Fonts: the canvas stylesheet in AMLPetriNet bundles `bpmn-font` and the icon font as data URLs so the WebView host needs one stylesheet and no virtual host mappings (`AMLPetriNet: web/build.mjs:62`). Those icon fonts are not part of the exported SVG; labels in the SVG rely on the `font-family` written into their style, resolved by whatever renders the file. Keep label fonts to common system families so the exported picture wraps text the same way the canvas did.

## 13. Visual bugs that happened, in one list

| # | Symptom | Root cause | Fix | Evidence |
|---|---|---|---|---|
| 1 | Foreign net drawn as one pile at the origin | no layout in file | `ArrangeMissing` on import, display and export | `AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs:8` |
| 2 | Ten-node loop laid out 17 000 px wide, elements far from the rest | longest path relaxed over a cycle | DFS back edges removed before layering | `AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs:74`, `dotnet/PtMapper.Tests/RegressionTests.cs:140` |
| 3 | Cyclic net in a single row, arcs through four nodes, reported as "0 crossings" | DFS started at an arbitrary unfed node; defect count ignored arcs through nodes | start at marked places; count through-node hits double | `AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs:158`, `:497` |
| 4 | Four crossing arcs on the paper net | column order by id | barycentre sweeps, several starts, transpose | `AMLPetriNet: dotnet/PtMapper.Tests/RegressionTests.cs:160` |
| 5 | Main path bent diagonally | every column filled from the top | column straightening in half rows | `AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs:350` |
| 6 | Return arcs straight through the drawing | back edges drawn straight | lanes above/below, side entry under names | `AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs:560` |
| 7 | "Approve" rendered as "Appr" / "ove" | name inside a 50 px box | name drawn below the transition | `AMLPetriNet: web/src/index.js:58` |
| 8 | State names broken mid-word | 90 px external label box | 150 px box, wrap at spaces/hyphens, 3 lines max, ellipsis plus tooltip | `FPB.JS: app/fpb/core/FpbConstants.js:46` (commit `31dc89c`) |
| 9 | Label sinks half its height with every session | label box rebuilt as 0x0 at the offset point | fixed default size box centred on the point | `AMLPetriNet: web/src/pnml/index.js:660` |
| 10 | Arrow head hidden inside the target after import | modeler does not dock on import | crop end points in the importer | `AMLPetriNet: web/src/pnml/index.js:722` |
| 11 | NaN geometry, shape not rendered | zero-size node, 0/0 in ellipse formula; negative dimension | centre fallback, non-positive size falls back to default | `AMLPetriNet: dotnet/PtMapper.Conversion/PtGeometry.cs:33`, `web/src/pnml/index.js:487` |
| 12 | All bend points lost through PNML | upstream converter wrote no arc graphics and no dimension | own DOM-based converter aliased in the build | `AMLPetriNet: web/src/pnml/index.js:9`, `web/build.mjs:57` |
| 13 | PNML invalid | exponent notation, `<dimension>` with two decimals | explicit rounding and size clamping | `AMLPetriNet: web/src/pnml/index.js:200`, `:216` |
| 14 | Empty canvas for layout-free AML | missing connection visual crashed the importer | mapper fallback waypoints, importer fallback line | `fpb-aml-mapper: dotnet/FpbMapper.Conversion/CaexToFpbJson.cs:499`, `FPB.JS: app/fpb/importer/JSONImporter.js:520` |
| 15 | Echo cycle drifted after the fallback | fallback points lacked `original` | same point structure as stored port points | `fpb-aml-mapper: dotnet/FpbMapper.Conversion/CaexToFpbJson.cs:867` |
| 16 | Invisible SystemLimit | no `ViewInformation` | default bounds on write | `fpb-aml-mapper: dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs:79` |
| 17 | User bend points destroyed on layer switch | blanket `layoutConnection` on every switch | re-layout only connections of repositioned boundary states | `FPB.JS: app/fpb/modeling/cmd/DecomposeProcessOperator.js:283` (commit `8f1fc08`) |
| 18 | ParallelFlow partner alignment ignored after import | DI waypoints never written for untouched imported connections | mirror waypoints into DI, read partner from live shape | `FPB.JS: app/fpb/importer/JSONImporter.js:529`, `app/fpb/modeling/FpbLayouter.js:126` |
| 19 | Routed loops became straight lines in a demo | diagram-js re-lays out connections of a dragged node | documented; measurement tool supports `--move`/`--no-move` | `AMLPetriNet: docs/roundtrip-validation.md:171` |
| 20 | Dangling attribute type alias after sync | aliased DD path written into a document with inline library | resolve DD path form per document | `AMLPetriNet: dotnet/PtMapper.Conversion/PtDiPaths.cs:15` |
| 21 | Exported SVG strokes cut at the edge | `saveSVG` bounding box without margin | margin added to width, height and viewBox | `AMLPetriNet: web/src/svg.js:4` |
| 22 | Arranged starter example: a back flow drawn on top of the forward flow, a flow through a node, all browser tests green | nodes placed, flows left straight; tests looked at nodes only | lanes and channels for back flows, crossing forward flows and self loops; layout tests on segments; screenshots | `starter/dotnet/Efl.Conversion/EflLayout.cs:175-289`, `starter/dotnet/Efl.Tests/LayoutTests.cs:85-139`, `starter/web/tools/screenshot.mjs:1-20` |

## Checklist

- [ ] Geometry conventions per layer (anchor, units, what is stored) are written as a comment at the top of each converter.
- [ ] Node bounds, bend points and moved label offsets are stored; the meaning of `PortCoordinate` (cropped or original) is fixed and documented in the library.
- [ ] DD attribute type paths follow the document's own form (inline, aliased, or new external reference).
- [ ] A deterministic layered layout runs in the mapper on import, before display and before any write, and returns a count that is logged.
- [ ] Cycle breaking starts at the language's natural entry points and is iterative.
- [ ] Columns by longest path on the acyclic graph, unfed nodes pulled right, columns normalised to start at 0.
- [ ] Row ordering measures crossings plus arcs through nodes, tries several starts, and has a size limit for the quadratic step.
- [ ] Main path straightened; back edges routed in separate lanes, entering named nodes from the side; forward edges that would cross a node routed; self loops get bend points.
- [ ] Layout tests assert that no flow enters a node it does not connect and no two flows share a segment; a screenshot of the arranged form was looked at.
- [ ] Only nodes without bounds and connections without bend points are touched; partial-layout files are tested for overlaps.
- [ ] Modeler uses `CroppingConnectionDocking`; `getShapePath` matches the drawn outline for every shape type.
- [ ] Importer computes docking points when the file carries only bend points.
- [ ] C# docking geometry mirrors the JavaScript, including ellipses and the zero-size guard.
- [ ] `Waypoint_n` read in numeric order and replaced wholesale on write.
- [ ] Imported waypoints are mirrored into DI objects if any code reads DI instead of shapes.
- [ ] Long names are drawn below shapes or in a box wide enough not to break words; checked with the longest real names.
- [ ] Label offsets use the modeler's own anchor (`getWaypointsMid` for connections); rebuilt label boxes have the default size and are centred; several consecutive sessions leave labels in place.
- [ ] Label offset attribute removed when a label returns to its default position.
- [ ] All number writing uses invariant culture, no exponent, no `-0`, no NaN; C# and JS spell numbers identically (interop test).
- [ ] All number reading uses `NumberStyles.Float`, invariant culture and a finiteness check; empty placeholders count as absent.
- [ ] Schema limits on specific values (sizes, decimals) are enforced in both implementations.
- [ ] Viewport fitted after importing a different model, not after echo imports.
- [ ] SVG export adds a margin, keeps styles inline, removes grid patterns, and is checked in a browser test.
- [ ] Round-trip check counts geometry per value and reports generated layout as an expected difference.

## Where to look

| Topic | File |
|---|---|
| Full layered layout | `AMLPetriNet: dotnet/PtMapper.Conversion/PtLayout.cs` |
| Reduced layered layout with flow routing | `starter/dotnet/Efl.Conversion/EflLayout.cs` |
| Starter layout tests on lines, screenshots of stored and arranged layout | `starter/dotnet/Efl.Tests/LayoutTests.cs`, `starter/web/tools/screenshot.mjs` |
| Layout tests and regressions | `AMLPetriNet: dotnet/PtMapper.Tests/LayoutTests.cs`, `dotnet/PtMapper.Tests/RegressionTests.cs:138` |
| Docking geometry (C#) | `AMLPetriNet: dotnet/PtMapper.Conversion/PtGeometry.cs`, `starter/dotnet/Efl.Conversion/EflGeometry.cs` |
| Docking, label anchor, number spelling (JS) | `AMLPetriNet: web/src/pnml/index.js` |
| Writing ViewInformation, LabelOffset, PortCoordinate, Waypoint_n | `AMLPetriNet: dotnet/PtMapper.Conversion/PtWrite.cs:224` |
| Reading geometry from CAEX | `AMLPetriNet: dotnet/PtMapper.Conversion/CaexToPtNet.cs:255` |
| PNML graphics read/write, size rounding | `AMLPetriNet: dotnet/PtMapper.Conversion/Pnml.cs:265`, `:415` |
| DD type path resolution | `AMLPetriNet: dotnet/PtMapper.Conversion/PtDiPaths.cs` |
| Transition names below the box | `AMLPetriNet: web/src/index.js:58` |
| SVG export and its browser check | `AMLPetriNet: web/src/svg.js`, `web/tools/verify-svg.mjs` |
| Where layout runs in plugin and web app | `AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:863`, `dotnet/PtMapper.Web/Program.cs:134` |
| Generated layout in the round-trip report | `AMLPetriNet: docs/roundtrip-validation.md:127` |
| Waypoint fallback for layout-free AML | `fpb-aml-mapper: dotnet/FpbMapper.Conversion/CaexToFpbJson.cs:837`, `dotnet/FpbMapper.Tests/WaypointFallbackTests.cs` |
| FPD waypoint and port writing | `fpb-aml-mapper: dotnet/FpbMapper.Conversion/FpbJsonToCaex.cs:885`, `:2067` |
| Modeler cropping and FPB docking | `FPB.JS: app/fpb/modeling/updater/DiUpdater.js:41`, `app/fpb/modeling/FpbLayouter.js` |
| Import fallbacks (connections, shapes) | `FPB.JS: app/fpb/importer/JSONImporter.js:516`, `app/fpb/importer/ImportUtils.js:150` |
| Label sizes and limits | `FPB.JS: app/fpb/core/FpbConstants.js:44` |
| Custom SVG export | `FPB.JS: app/fpb/FpbModeler.js:360` |
| Starter modeler docking and outline | `starter/web/src/modeling/Layouter.js`, `starter/web/src/modeling/index.js:22`, `starter/web/src/draw/Renderer.js:89` |
| Starter SVG export and viewport fit | `starter/web/src/EflModeler.js:106`, `:125` |
| Starter geometry tests | `starter/dotnet/Efl.Tests/ExampleAndGeometryTests.cs`, `starter/web/tools/verify-modeler.mjs` |
