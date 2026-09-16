# Building the browser modeler with diagram-js

## In short

- Look for an existing diagram-js modeler first; reuse it from outside (patches guarded against double wrapping, bundler aliases), otherwise start from `starter/web/` (see §1).
- Module list must include keyboard and editor actions, or Ctrl+Z and Delete do nothing; `starter/web/src/EflModeler.js:49-72` is a complete minimal list (see §2).
- Every service and `__init__` function carries `$inject` in parameter order; minified builds die without it (see §2, §14).
- Plain business objects unless the modeler reads and writes its own XML; element id equals business object id equals exchange-format id (see §3).
- Replace `elementFactory` so new ids are random, not `<type>_<counter>` (see §4).
- `getShapePath` must match the drawn outline; register `CroppingConnectionDocking`; use a layouter that keeps bend points and crops end points (see §5, §6).
- Rules return `false` to forbid; `undefined` is no answer, which allows a command that has a handler and refuses a non-command action such as `connection.start`. Cover `connection.reconnect` and `connection.start`; keep rules free of side effects (see §7).
- Every edit goes through a command handler with `execute` and `revert`; a cleared name is removed, never `''` (see §9).
- Import: validate outside the canvas, explicit root with a business object, shapes before connections, dock waypoints, await completion (see §10).
- Export with a whitelist serializer, bend points only, rounded numbers, byte-identical export-import-export test (see §11).
- Debounce `commandStack.changed`, suppress it during import and non-edit modes, send an export baseline after each import (see §12).
- One ESM file, one CSS file, assets inlined, no chunks, minification off (see §14); pin diagram-js exactly (see §15).

Read fully when: you write or adapt the modeler, or a canvas symptom from §16 shows up.
Skim when: the starter modeler already runs and you only add element types; read §5, §7, §10 and §11 for those.

---

This chapter covers the graphical editor that runs inside the WebView2 control of the AML Editor
plugin and inside the web page: how to decide between reusing an existing diagram-js modeler and
writing your own, how the modules fit together, how to draw, constrain, label, import and export
elements, how to notice that the user changed something, and how to bundle the result into a single
file the host can load from disk. Read it before you write the first line of JavaScript, and again
when a shape does not render, an arrow ends in the middle of a node, a label comes back as `"1"`,
or the minified bundle dies at boot. The message protocol between page and host is in
[05-editor-plugin.md](05-editor-plugin.md); the file format the modeler speaks is in
[03-exchange-format.md](03-exchange-format.md).

The two source projects cover both paths:

- **AMLPetriNet** reuses an external modeler, `@bptlab/openbpt-modeler-petri-net` 1.0.2 (MIT, built
  on diagram-js 15), and customises it from the outside: two prototype patches, one renderer
  override, one replaced dependency, a bridge and an esbuild script
  (`AMLPetriNet: web/src/index.js`, `web/build.mjs`).
- **FPB.JS** is a modeler written from scratch on diagram-js for VDI 3682 with its own renderer,
  rules, palette, context pad, label editing, JSON importer, command handlers and behaviors
  (`FPB.JS: app/fpb/`). It is larger, older and carries many fixes whose reasons are recorded in
  the code comments.
- The playbook's `starter/web/` is a complete small modeler for the toy language EFL on diagram-js
  15.26.0 without moddle: `src/EflModeler.js` (module list, `importModel`, `exportModel`,
  `saveSVG`), `src/types.js`, `src/draw/Renderer.js`, `src/modeling/` (element factory, layouter,
  properties command), `src/rules/Rules.js`, `src/palette/Palette.js`,
  `src/context-pad/ContextPad.js`, `src/label/LabelEditing.js`, `src/io/json.js`, `src/bridge.js`,
  an esbuild script and Playwright checks in `tools/`. Use it as the skeleton for a new language.

Citations into third-party packages use the package name and version, for example
`diagram-js 15.26.0: lib/layout/BaseLayouter.js:39`.

---

## 1. Reuse or build

### What to do

1. Search npm and GitHub for a diagram-js based modeler of your language (look for `diagram-js` in
   the dependencies, not only in the name). Check the licence (MIT is what both projects rely on),
   the diagram-js major version, whether it builds in a browser without Node polyfills, and
   whether its import/export keeps geometry (node bounds, bendpoints, label positions).
2. **Reuse** when such a modeler exists, is maintained or at least small enough to patch, and
   stores the semantics you need. Customise it from outside (additional modules, prototype
   patches, build aliases); do not fork unless you must.
3. **Build** when nothing exists, when the existing one is tied to a different metamodel, or when
   its structure (containers, layers, decomposition) does not match your language. Start from
   `starter/web/`, and read the reused package or FPB.JS for patterns.
4. In either case, pin the exact versions and record every patch with the reason and a test that
   fails without it.

### Why

Reuse gave AMLPetriNet a working Petri net editor with token replay in days, but it was not free.
Every one of these upstream problems was found only by testing the real bundle in a browser:

| Upstream problem | Consequence | Fix in AMLPetriNet |
|---|---|---|
| `UpdatePropertyHandler` declares no `$inject` | A minified bundle dies while the command stack registers handlers | Annotation added at load time, `AMLPetriNet: web/src/index.js:9-18` |
| `UpdateLabelHandler.postExecute` sets the text `"1"` whenever a label is cleared, for every element type | A place whose name was deleted was called "1", and that name went into the document | Prototype wrapped so the default only applies to arcs, `AMLPetriNet: web/src/index.js:20-45` |
| The PNML converter uses `xmlbuilder2` (Node builtins) and drops node dimensions and all arc waypoints | Not bundleable for a browser; every round trip loses layout | Replaced by a DOM based converter through an esbuild alias, `AMLPetriNet: web/src/pnml/index.js:1-33`, `web/build.mjs:53-59` |
| Transition names are drawn inside the 50 px box and break mid word | "Approve" rendered as "Appr" over "ove" | Renderer method wrapped to draw the name below, `AMLPetriNet: web/src/index.js:58-96` |
| Token simulation writes `initialMarking` through the command stack | A simulation looks like the user editing every marking | Bridge suppresses change reports while simulating, `AMLPetriNet: web/src/bridge.js:130-137`, `175-186` |

The bptlab package is itself written as a template for custom modelers: its rule provider,
renderer and label editing carry `CustomModelerTodo` markers where a language plugs in
(`@bptlab/openbpt-modeler-petri-net 1.0.2: lib/rules/CustomRuleProvider.js:12`,
`lib/draw/CustomRenderer.js:39`, `lib/modeling/CustomLabelEditing.js:85`). Even when you build your
own modeler, it is a compact reference for a complete module set.

### How to patch a reused modeler without forking

Patch once, guard against double wrapping (two bundles on one page), and keep the patch a no-op
when upstream fixes the bug:

```js
// AMLPetriNet: web/src/index.js:16-18
if (!UpdatePropertyHandler.$inject) {
  UpdatePropertyHandler.$inject = ['eventBus'];
}

// AMLPetriNet: web/src/index.js:26-45 (shortened)
if (!UpdateLabelHandler.prototype.postExecute.scopedToArcs) {
  const postExecute = UpdateLabelHandler.prototype.postExecute;
  const scoped = function postExecuteScopedToArcs(context) {
    const target = context.element?.labelTarget || context.element;
    if (target?.businessObject?.$instanceOf?.('ptn:Arc')) {
      return postExecute.call(this, context);
    }
    const setText = this.setText;
    this.setText = (element) => setText.call(this, element, undefined);
    try { return postExecute.call(this, context); } finally { delete this.setText; }
  };
  scoped.scopedToArcs = true;
  UpdateLabelHandler.prototype.postExecute = scoped;
}
```

Replace a whole dependency with a bundler alias instead of editing `node_modules`:

```js
// AMLPetriNet: web/build.mjs:47-60
await esbuild.build({
  ...shared,
  entryPoints: [resolve(here, 'src/index.js')],
  format: 'esm',
  splitting: false,
  outfile: resolve(dist, 'ptnjs.esm.js'),
  alias: {
    'pnml-moddle-converter': resolve(here, 'src/pnml/index.js'),
  },
});
```

Keep a `THIRD-PARTY-NOTICES.md` listing every bundled package with version and licence, including
transitive ones such as didi, preact, htm and bpmn-font (`AMLPetriNet: THIRD-PARTY-NOTICES.md:7-31`).

---

## 2. Module composition

### What to do

A diagram-js editor is a `Diagram` instance plus a list of modules. Each module is an object with
`__depends__` (other modules), `__init__` (services instantiated eagerly) and service definitions
`name: ['type', Constructor]`. A later definition with the same name replaces an earlier one; that
is how you swap `elementFactory`, `layouter`, `connectionDocking` or `modeling`.

Start from this set and remove what you do not need:

| Feature | diagram-js module | Needed for |
|---|---|---|
| Core | (always loaded by `Diagram`) | canvas, elementRegistry, eventBus, graphicsFactory |
| Your renderer | own module with `__init__: ['yourRenderer']` | drawing |
| Modeling | `lib/features/modeling` (pulls command, label-support, attach-support, change-support) | every edit through the command stack |
| Rules | `lib/features/rules` plus your `RuleProvider` | what may be created, moved, connected |
| Palette | `lib/features/palette` plus your provider | creating elements |
| Create | `lib/features/create` | drag from palette or context pad |
| Connect | `lib/features/connect`, optionally `global-connect` | drawing connections |
| Context pad | `lib/features/context-pad` plus your provider | per element actions |
| Move | `lib/features/move` | dragging shapes |
| Selection | `lib/features/selection` | select, needed by most others |
| Lasso, hand, space tool | `lasso-tool`, `hand-tool`, `space-tool` | palette tools |
| Bendpoints | `lib/features/bendpoints` | user routing of connections |
| Resize | `lib/features/resize` | resizable shapes |
| Direct editing | npm `diagram-js-direct-editing` plus your provider | in-place label editing |
| Keyboard and editor actions | `lib/features/keyboard`, `lib/features/editor-actions` | Ctrl+Z, Ctrl+Y, Delete |
| Navigation | `lib/navigation/movecanvas`, `zoomscroll` | pan and zoom |
| Snapping | `lib/features/snapping`, `grid-snapping` | alignment |

The starter's list is the minimal working set in one array, with the language's own services in
one module (`starter/web/src/EflModeler.js:35-72`); its comment names the silent failures of a
missing module (no `rules`: nothing can be created; no `keyboard`: the Delete key does nothing).
The reused Petri net modeler shows a larger list in one place
(`@bptlab/openbpt-modeler-petri-net 1.0.2: lib/CustomModeler.js:54-96`). FPB.JS registers top-level
modules in `FPB.JS: app/fpb/FpbModeler.js:108-131` and pulls the rest through `__depends__` of its
own modules, for example palette, create, space tool, lasso, hand tool and global connect in
`FPB.JS: app/fpb/palette/index.js:11-23`, and direct editing, context pad, connect and create in
`FPB.JS: app/fpb/context-pad/index.js:9-19`.

FPB.JS replaces core services by name in its modeling module:

```js
// FPB.JS: app/fpb/modeling/index.js:46-54
fpbFactory: [ 'type', FpbFactory ],
confirmationHandler: [ 'type', ConfirmationHandler ],
shapeUpdater: [ 'type', ShapeUpdater ],
connectionUpdater: [ 'type', ConnectionUpdater ],
diUpdater: [ 'type', DiUpdater ],
elementFactory: [ 'type', FpbElementFactory ],
modeling: [ 'type', FpbModeling ],
layouter: [ 'type', FpbLayouter ],
connectionDocking: [ 'type', CroppingConnectionDocking ]
```

### Why

- **Missing keyboard modules means no undo shortcut.** FPB.JS has a full command stack, but its
  module list contains neither `keyboard` nor `editor-actions` (`FPB.JS: app/fpb/FpbModeler.js:108-131`),
  so Ctrl+Z does nothing. The Petri net modeler includes both
  (`@bptlab/openbpt-modeler-petri-net 1.0.2: lib/CustomModeler.js:68-69`).
- **A module that is imported but never offered is dead weight.** FPB.JS depends on
  `GlobalConnectModule` (`FPB.JS: app/fpb/palette/index.js:18`) but its palette has no entry for it
  (`FPB.JS: app/fpb/palette/FpbPaletteProvider.js:57-83`). The context pad provider asks the
  injector for `autoPlace` (`FPB.JS: app/fpb/context-pad/FpbContextPadProvider.js:44-46`), a module
  that is never registered, so the lookup always yields `false`.
- **Order matters for modules that listen to each other.** The Petri net modeler notes that its
  simulator module must be registered before the context pads module
  (`@bptlab/openbpt-modeler-petri-net 1.0.2: lib/CustomModeler.js:88-90`).
- **Raw DOM listeners bypass diagram-js focus handling and leak.** FPB.JS's
  `KeyboardAlignService` adds `keydown` listeners to `document` and the container
  (`FPB.JS: app/fpb/services/KeyboardAlignService.js:46-58`); it needed a bound reference and a
  `diagram.destroy` hook to stop leaking (`FPB.JS: app/fpb/services/KeyboardAlignService.js:30-39`,
  `63-68`). Use the `keyboard` service instead of raw listeners.

### Every service needs `$inject`

didi, the injector behind diagram-js, reads constructor parameter names when `$inject` is missing.
That works in development and breaks after minification (section 14). Annotate every class,
including tiny ones and `__init__` functions:

```js
// starter/web/src/modeling/index.js:11-14
function registerHandlers(commandStack) {
  commandStack.registerHandler(UPDATE_PROPERTIES, UpdatePropertiesHandler);
}
registerHandlers.$inject = ['commandStack'];
```

Keep the `$inject` list and the parameter list in the same order and length. FPB.JS's context pad
provider takes eleven parameters but lists twelve names
(`FPB.JS: app/fpb/context-pad/FpbContextPadProvider.js:24-28` against `55-68`); an extra name at
the end is harmless, a swapped pair is not.

---

## 3. Business objects: moddle or plain objects

### What to do

Every diagram element (`shape`, `connection`, `root`, `label`) has a `businessObject` that holds
the semantics. diagram-js does not care what that object is. Choose:

- **Plain objects** when your exchange format is JSON or when a converter outside the modeler owns
  the file format. Store a `type` on the element (`'efl:Step'`) and keep all type strings, default
  sizes and the mapping to the exchange format's type names in one file
  (`starter/web/src/types.js:9-23`). This is the recommended default for a new language with an
  AML mapper.
- **moddle** (`moddle`, `moddle-xml`) when the modeler must read and write its own XML directly,
  with typed properties, inheritance (`$instanceOf`), references resolved by id, and a
  `moddle.ids` registry. bpmn-js and the Petri net modeler work this way: a schema describes model
  and diagram interchange (DI), `importXML` parses into moddle objects and `saveXML` serialises
  them (`@bptlab/openbpt-modeler-petri-net 1.0.2: lib/BaseViewer.js:66-115`, `140-183`).

Whatever you choose, follow these rules:

1. The element id equals the business object id equals the id in the exchange format. FPB.JS sets
   `id: businessObject.id` when it creates an element
   (`FPB.JS: app/fpb/modeling/FpbElementFactory.js:136-139`); AMLPetriNet selects an element on
   request of the host with `elementRegistry.get(msg.id)` using the PNML id
   (`AMLPetriNet: web/src/bridge.js:221-226`).
2. Initialise every array property when the object is created, not when it is first used.
3. Write type checks in one helper that works on both the element and the business object.

```js
// FPB.JS: app/fpb/help/utils.js:12-20
export function is(element, type) {
  const bo = getBusinessObject(element);
  return bo && bo.$instanceOf && (typeof bo.$instanceOf === 'function') && bo.$instanceOf(type);
}

export function getBusinessObject(element) {
  return (element && element.businessObject) || element;
}
```

### Why

- **Mixing both kinds breaks type checks silently.** FPB.JS creates business objects through moddle
  (`FPB.JS: app/fpb/modeling/FpbFactory.js:38-46`) but also accepts plain objects and shims
  `$instanceOf`, `get` and `set` onto them
  (`FPB.JS: app/fpb/modeling/FpbElementFactory.js:141-168`). The shim compares `this.type === type`
  exactly, so a supertype check such as `is(element, 'fpb:BaseElement')` is false for a plain object
  while it is true for the moddle object of the same type. Pick one representation.
- **`is()` returns `undefined`, not `false`, without moddle.** The helper above short-circuits on
  `bo.$instanceOf`; tests that assert `toBe(false)` fail. Assert falsy, or return a boolean.
- **Uninitialised arrays crash later, far from the cause.** FPB.JS's factory creates `incoming`,
  `outgoing`, `isAssignedTo`, `elementsContainer` and friends up front
  (`FPB.JS: app/fpb/modeling/FpbElementFactory.js:94-135`), and its importer still has to call
  `ensureArray` before pushing (`FPB.JS: app/fpb/importer/JSONImporter.js:633`, `647`).
- **The upstream moddle import keeps going when an element fails.** The tree walker catches the
  error, logs it to the console and continues
  (`@bptlab/openbpt-modeler-petri-net 1.0.2: lib/import/CustomTreeWalker.js:61-78`). The import
  "succeeds" with elements missing. Forward console output to the host log
  (`AMLPetriNet: web/src/bridge.js:65-99`) so this is visible.

### Shape versus business object

Keep references typed and document them. FPB.JS's most frequent bug class was confusing the two:
`processOperator.businessObject.decomposedView` holds the child process **shape**, not its business
object, while `childProcess.businessObject.isDecomposedProcessOperator` holds a **business object**
(`FPB.JS: app/fpb/importer/JSONImporter.js:703-713`). A string id where an object is expected is
the same bug one step earlier: the importer clears a `decomposedView` that could not be resolved
because every later consumer would read `.businessObject` off a string
(`FPB.JS: app/fpb/importer/JSONImporter.js:714-723`). Rule of thumb: store ids in the exchange
format, objects in memory, and resolve all references in one pass right after import.

---

## 4. Element factory and ids

### What to do

Replace `elementFactory` with a subclass that fills in the business object, the default size per
type and a collision-free id:

```js
// starter/web/src/modeling/ElementFactory.js:18-35 (blank lines removed; newId at :38-44)
export default class ElementFactory extends BaseElementFactory {
  create(elementType, attrs = {}) {
    const type = attrs.type || (elementType === 'connection' ? FLOW : undefined);
    const withDefaults = { ...attrs };
    if (type) withDefaults.type = type;
    if (!withDefaults.id) withDefaults.id = newId(type);
    if (!withDefaults.businessObject) withDefaults.businessObject = {};
    if (withDefaults.businessObject.id === undefined) withDefaults.businessObject.id = withDefaults.id;
    if (elementType === 'shape' && isNode(withDefaults)) {
      if (withDefaults.width === undefined) withDefaults.width = NODE_SIZE.width;
      if (withDefaults.height === undefined) withDefaults.height = NODE_SIZE.height;
    }
    return super.create(elementType, withDefaults);
  }
}
```

### Why

- **The default ids repeat across sessions.** The base factory names elements
  `<type>_<counter>` with a counter starting at 12 on every page load
  (`diagram-js 15.26.0: lib/core/ElementFactory.js:24`, `106-107`). An imported model can already
  contain such an id, and the element registry throws `element with id ... already added`
  (`diagram-js 15.26.0: lib/core/ElementRegistry.js:270-276`). FPB.JS uses random UUIDs for new
  elements (`FPB.JS: app/fpb/modeling/FpbFactory.js:25-35`).
- **Label shapes share the registry namespace.** Both projects derive label ids from the element id
  (`FPB.JS: app/fpb/label/cmd/UpdateLabelHandler.js:65-68`,
  `@bptlab/openbpt-modeler-petri-net 1.0.2: lib/import/CustomImporter.js:154`). A model id that
  already ends in `_label` collides. Reject or rename such ids in the importer.
- **Helper ids you derive must not collide with model ids.** The PNML converter needs DI ids and
  picks a prefix no model id starts with, instead of appending `_di`, because a foreign file may
  legally use `<id>_di` for something else (`AMLPetriNet: web/src/pnml/index.js:587-595`).
- **Ids that the AML side must recognise are not yours to invent.** Deterministic ids and their
  normalisation are the mapper's job; see [04-aml-mapping.md](04-aml-mapping.md).

---

## 5. The renderer

### What to do

Subclass `BaseRenderer` with a priority above the default renderer (both projects use 2000),
implement `canRender`, `drawShape`, `drawConnection`, `getShapePath` and `getConnectionPath`, and
register it in `__init__` so it is instantiated.

```js
// FPB.JS: app/fpb/core/FpbRenderer.js:27-28, 87, 149-151
export default function FpbRenderer(eventBus, styles, canvas, textRenderer, elementRegistry) {
  BaseRenderer.call(this, eventBus, 2000);
  // ...
}
FpbRenderer.$inject = ['eventBus', 'styles', 'canvas', 'textRenderer', 'elementRegistry'];

FpbRenderer.prototype.canRender = function(element) {
  return is(element, 'fpb:BaseElement');
};
```

Draw in element-local coordinates (the group is already translated to `x, y`), but compute
`getShapePath` in absolute coordinates, and make it match the drawn outline:

```js
// FPB.JS: app/fpb/core/paths/PathCalculator.js:13-27
getProductPath(shape) {
  const cx = shape.x + shape.width / 2;
  const cy = shape.y + shape.height / 2;
  const radius = shape.width / 2;
  const pathComponents = [
    ['M', cx, cy],
    ['m', 0, -radius],
    ['a', radius, radius, 0, 1, 1, 0, 2 * radius],
    ['a', radius, radius, 0, 1, 1, 0, -2 * radius],
    ['z']
  ];
  return componentsToPath(pathComponents);
}
```

Register `CroppingConnectionDocking` as `connectionDocking` (`FPB.JS: app/fpb/modeling/index.js:18`,
`54`; `starter/web/src/modeling/index.js:2`, `22`). It intersects connections with `getShapePath`.

The starter renderer is the compact version of all this: priority 2000, `canRender` limited to the
three EFL types, a rectangle or ellipse drawn in local coordinates, and a matching absolute
`getShapePath` with an ellipse path for a Store (`starter/web/src/draw/Renderer.js:29-42`,
`89-110`). Two details differ from FPB.JS and bpmn-js:

- **Plain diagram-js has no `textRenderer` service.** FPB.JS injects one
  (`FPB.JS: app/fpb/core/FpbRenderer.js:87`) because its core module registers one; diagram-js
  15.26.0 defines no service of that name. In a modeler built on bare diagram-js, create the text layout yourself from
  `diagram-js/lib/util/Text` (`starter/web/src/draw/Renderer.js:4`, `34-36`), or asking the
  injector for `textRenderer` fails at boot.
- **Write fill, stroke and font as attributes.** The starter puts the label style on every text
  element so an exported SVG looks the same without the page's stylesheet
  (`starter/web/src/draw/Renderer.js:11-15`).

Define markers (arrow heads) once in the canvas `<defs>`. With several modelers on one page, give
the marker an id unique per renderer instance (FPB.JS derives it from an `Ids` generator,
`FPB.JS: app/fpb/core/FpbRenderer.js:25`, `31-32`) so they do not share or overwrite markers. The
starter uses the fixed id `efl-arrow` and adds the marker on first use
(`starter/web/src/draw/Renderer.js:9`, `117-142`), which is enough for one modeler per page, as in
the plugin and the web app.

### Why

- **`getShapePath` is what docking crops against.** A rectangle path for a circle, or a missing
  case, lets arrows end inside or short of the shape. FPB.JS draws its technical resource with
  rounded corners but returns a plain rectangle and says so
  (`FPB.JS: app/fpb/core/paths/PathCalculator.js:82-100`); that is acceptable for small radii and
  wrong for circles. FPB.JS logs a warning for unknown types instead of silently returning
  `undefined` (`FPB.JS: app/fpb/core/FpbRenderer.js:287`).
- **A renderer that claims everything hides other renderers.** The Petri net renderer returns
  `true` from `canRender` for every element
  (`@bptlab/openbpt-modeler-petri-net 1.0.2: lib/draw/CustomRenderer.js:35-37`). Restrict it to
  your types so labels and any added element kinds reach the right renderer.
- **Overriding one draw method is enough to change a look.** AMLPetriNet moved transition names
  below the box by wrapping `drawTransition` on the renderer instance and suppressing the embedded
  label for the duration of the call (`AMLPetriNet: web/src/index.js:68-96`). The model and the
  PNML are unchanged; only drawing differs.
- **Labels inside narrow boxes break words.** Measure with the text renderer and truncate or draw
  outside. FPB.JS truncates to a maximum line count with an ellipsis and a hover tooltip
  (`FPB.JS: app/fpb/core/shapes/BaseShapeRenderer.js:95-130`).
- **Re-rendering all connections on every change is expensive.** FPB.JS fires `element.changed`
  for every connection after each command to redraw crossing bridges
  (`FPB.JS: app/fpb/core/FpbRenderer.js:55-57`, `92-104`). Acceptable for small diagrams; scope it
  to affected connections for large ones.

---

## 6. Layout and docking of connections

### What to do

Provide a `layouter` that keeps user bendpoints and crops the end points, or crop in a command
interceptor after `connection.create` and `connection.layout`.

```js
// starter/web/src/modeling/Layouter.js:22-38 (blank lines removed)
layoutConnection(connection, hints = {}) {
  const source = hints.source || connection.source;
  const target = hints.target || connection.target;
  const current = hints.waypoints || connection.waypoints || [];
  const bends = current.length > 2 ? current.slice(1, -1) : [];
  const waypoints = [
    hints.connectionStart || getMid(source),
    ...bends,
    hints.connectionEnd || getMid(target),
  ];
  if (!source || !target) return waypoints;
  return this._connectionDocking.getCroppedWaypoints({ waypoints, source, target }, source, target);
}
```

FPB.JS uses Manhattan routing with preferred directions per connection type and repairs existing
waypoints instead of discarding them (`FPB.JS: app/fpb/modeling/FpbLayouter.js:29-115`), and crops
in a command interceptor (`FPB.JS: app/fpb/modeling/updater/DiUpdater.js:41-62`).

### Why

- **The base layouter draws straight centre to centre lines.** `BaseLayouter.layoutConnection`
  returns only the two mid points (`diagram-js 15.26.0: lib/layout/BaseLayouter.js:39-47`), and
  moving a shape re-lays out all its incoming and outgoing connections
  (`diagram-js 15.26.0: lib/features/modeling/cmd/MoveShapeHandler.js:72-85`). With the base
  layouter, moving a node straightens every bent connection on it. This was observed in the Petri
  net modeler during a live session.
- **Cropped waypoints carry an `original` point.** `CroppingConnectionDocking` returns the docked
  point with an `original` property pointing at the uncropped anchor
  (`diagram-js 15.26.0: lib/layout/CroppingConnectionDocking.js:17-18`). Export only `x` and `y`.
  FPB.JS's XML mapper falls back with `point.x || point.original?.x`
  (`FPB.JS: app/fpb/xml/XMLMapper.js:2051`), which replaces a legitimate coordinate `0` by the
  original anchor. Test coordinates with `!== undefined`, never with `||`.
- **Some connection types must not be cropped.** FPB.JS skips cropping for `fpb:Usage`, which docks
  on the side of the technical resource by layout (`FPB.JS: app/fpb/modeling/updater/DiUpdater.js:47-49`,
  `FPB.JS: app/fpb/modeling/FpbLayouter.js:44-73`).

---


### Connections from an element to itself

Four things differ from ordinary connections, all found in the trial run of this playbook with a state machine:

- The rule: `connection.create` must allow `source === target` (the starter's `canConnect` forbids it for EFL).
- The gesture: the global connect tool starts a connection on `element.out` (`diagram-js 15.26.0: lib/features/global-connect/GlobalConnect.js`), so a loop is drawn by dragging out of the element and back into it. Say so in the palette tooltip, or users will think loops are broken, and drive the same gesture in the mouse test.
- The layout: a line from centre to centre has length zero. The layouter has to supply default bend points beside the element for a loop without them; the mapper's layout does the same for loops in documents without layout (three bend points above the element, `starter/dotnet/Efl.Conversion/EflLayout.cs:256-268`).
- Moving: diagram-js moves the bend points of a connection whose both ends are moved, so a loop follows its element without layouter work.

### Labels on connections that are not stored

When the language draws a connection's text ("trigger [guard]") but nobody drags it, do not create a label element: draw the text in `drawConnection` near the middle of the polyline's length, offset to the side of the segment it sits on (above a horizontal segment, beside a vertical one; placing it the same way for both put it far from vertical lines in the trial run). It is then part of the SVG export without extra work, and there is no label position to store, import or keep in sync.

## 7. Rules

### What to do

Subclass `RuleProvider`, add rules in `init()`, and return exactly one of: `true` (allowed), `false`
(forbidden), `null` (ignore this interaction), or for connections an attribute object such as
`{ type: 'efl:Flow' }` that becomes the new connection's attributes. Return `undefined` only when a
lower-priority rule should decide (`diagram-js 15.26.0: lib/features/rules/RuleProvider.js:31-40`).

Cover these actions at minimum:

| Action | Asked when |
|---|---|
| `shape.create` and `elements.create` | palette or context pad create, paste |
| `elements.move` | drag of one or more shapes |
| `shape.resize` | resize handles |
| `connection.create` | connect tool end, hover feedback |
| `connection.start` | global connect tool start (`diagram-js 15.26.0: lib/features/global-connect/GlobalConnect.js:144`) |
| `connection.reconnect` | an end of an existing connection is dragged to another shape |
| `connection.updateWaypoints` | bendpoint moves |
| `elements.delete` | delete from pad or keyboard |

```js
// @bptlab/openbpt-modeler-petri-net 1.0.2: lib/rules/CustomRuleProvider.js:87-96
function canConnect(source, target) {
  return (
    ((source.type === `${MODELER_PREFIX}:Place` &&
      target.type === `${MODELER_PREFIX}:Transition`) ||
      (source.type === `${MODELER_PREFIX}:Transition` &&
        target.type === `${MODELER_PREFIX}:Place`)) && {
      type: `${MODELER_PREFIX}:Arc`,
    }
  );
}
```

The starter's provider covers every action in the table
(`starter/web/src/rules/Rules.js:26-47`); its `canConnect` returns `false` or `{ type: FLOW }` and
nothing else (`:56-59`). Its palette offers the global connect tool
(`starter/web/src/palette/Palette.js:42-47`), and by the diagram-js code cited below that tool
cannot start without a `connection.start` rule, so the provider has one:
`this.addRule('connection.start', ({ source }) => isNode(source))` (`:37`). The comment at `:5-20`
states the decision logic: a command is allowed when no rule answers and a handler exists, an
action that is not a command is refused when no rule answers, and a rule returning `undefined`
counts as no answer. `starter/web/tools/verify-modeler.mjs:69-97` draws a flow with the palette tool
and the real mouse; that check fails without the rule.

Enforce the structural rules a second time in the mapper's validator: files produced by other tools
never passed through the canvas rules (`starter/web/src/rules/Rules.js:16-19` names
`EflValidator.cs`; the canvas rule against a flow to itself is validator rule EFL04 at
`starter/dotnet/Efl.Conversion/EflValidator.cs:70-72`).

### Why

- **`undefined` is not "no".** In diagram-js, an action backed by a command handler is allowed when
  no rule answers: `Rules.allowed` maps `undefined` to `true`
  (`diagram-js 15.26.0: lib/features/rules/Rules.js:39-50`), and `CommandStack.canExecute` returns
  `false` only when there is no handler at all (`diagram-js 15.26.0: lib/command/CommandStack.js:211-232`).
  FPB.JS's `canConnect` returns `undefined` for a forbidden pair
  (`FPB.JS: app/fpb/rules/FpbRuleProvider.js:284-325`), so the connect tool treats it as allowed
  and calls `modeling.connect` with no attributes
  (`diagram-js 15.26.0: lib/features/connect/Connect.js:102-116`). FPB.JS only avoids a typeless
  connection because its `Modeling.connect` asks the rules again and bails out
  (`FPB.JS: app/fpb/modeling/FpbModeling.js:43-52`). For the reconnect rules it appends `|| false`
  (`FPB.JS: app/fpb/rules/FpbRuleProvider.js:133-155`). Return `false` explicitly.
- **Pseudo-actions without a handler are denied by default.** `connection.start` has no command
  handler, so global connect never starts unless a rule allows it
  (`diagram-js 15.26.0: lib/command/CommandStack.js:221-224`).
- **`connection.reconnect` is the action that actually runs.** FPB.JS notes that diagram-js
  executes `connection.reconnect` when an end is dragged, and the `reconnectStart` and
  `reconnectEnd` rules are never asked about it (`FPB.JS: app/fpb/rules/FpbRuleProvider.js:131-139`).
  The same applies to updaters: without handling `connection.reconnect`, `sourceRef` and
  `targetRef` kept pointing at the old element, so the drawing followed and the export did not
  (`FPB.JS: app/fpb/modeling/updater/ConnectionUpdater.js:66-85`).
- **The connect tool tries the reverse direction.** When the forward check returns `false`, it asks
  again with source and target swapped and connects backwards if that is allowed
  (`diagram-js 15.26.0: lib/features/connect/Connect.js:61-78`). A directed language gets
  connections the user drew in the wrong direction flipped silently; decide whether that is wanted.
- **Rules with side effects are hard to reason about.** FPB.JS fires `illegalMove`,
  `illegalCreate` and a delete confirmation event from inside rules
  (`FPB.JS: app/fpb/rules/FpbRuleProvider.js:105-110`, `171-192`). Rules are evaluated repeatedly
  during hover, so such events fire many times. Keep rules pure and put feedback in a separate
  listener on the end of the interaction.
- **Do not pass state from the UI to rules through the shape.** FPB.JS's context pad stores the
  chosen flow kind as `element.TemporaryFlowHint` on the source shape
  (`FPB.JS: app/fpb/context-pad/FpbContextPadProvider.js:157-165`) and the rule reads it
  (`FPB.JS: app/fpb/rules/FpbRuleProvider.js:298`). The property then leaks into exports and every
  serializer has to strip it (`FPB.JS: app/fpb/layer-panel/components/DownloadModal.js:273-278`).
  Pass the connection type in the connect context or as attributes to `connect.start` instead.

---

## 8. Palette and context pad

### What to do

Both are providers registered on their service; both return an entries object keyed by entry id.
Every create entry needs both `click` and `dragstart`:

```js
// FPB.JS: app/fpb/palette/PaletteUtils.js:17-34
static createElementAction(type, group, className, title, options, create, elementFactory) {
  function createListener(event) {
    const shape = elementFactory.createShape(assign({ type: type }, options));
    create.start(event, shape);
  }
  const shortType = type.replace(/^fpb:/, '');
  return {
    group: group,
    className: className,
    title: title || 'Create ' + shortType,
    action: {
      dragstart: createListener,
      click: createListener
    }
  };
}
```

The context pad provider implements `getContextPadEntries(element)` and typically offers delete,
connect (`connect.start(event, element)`) and append actions. Return an empty object for labels
(`FPB.JS: app/fpb/context-pad/FpbContextPadProvider.js:185-188`).

The starter's palette has both on each create entry (`starter/web/src/palette/Palette.js:49-60`);
its context pad offers connect, a value prompt that goes through the properties command, and delete
only when `rules.allowed('elements.delete', ...)` says so
(`starter/web/src/context-pad/ContextPad.js:22-69`).

Icons: use `className` with an icon font, or `imageUrl` with an inline SVG data URL
(`starter/web/src/icons.js:1-22`, sized in `starter/web/src/efl.css:15-23`). Inline everything;
the host loads the page from disk (section 14).

### Why

- **An entry with only `dragstart` does not work by click, and vice versa.** Users try both.
- **Delete must go through modeling.** The pad calls `modeling.removeElements([element])`
  (`FPB.JS: app/fpb/context-pad/FpbContextPadProvider.js:132-155`), which creates an undoable
  command and a change event. Confirmation dialogs belong before that call, not inside a rule.
- **Imported helpers can shadow injected services.** FPB.JS's provider imports diagram-js's
  default `translate` function at module level (`FPB.JS: app/fpb/context-pad/FpbContextPadProvider.js:5`)
  and uses that in `getContextPadEntries`, so a custom `translate` service registered by the host is
  ignored for pad titles. Use `this._translate`.
- **Context pad entries depend on state.** FPB.JS shows connect entries only when valid targets
  exist (`FPB.JS: app/fpb/context-pad/FpbContextPadProvider.js:202-265`). Keep that logic consistent
  with the rules or users see actions that then fail.

---

## 9. Label editing and command handlers

### The direct editing provider contract

`diagram-js-direct-editing` asks each registered provider `activate(element)`; the first that
returns a context object wins (`diagram-js-direct-editing 3.5.1: lib/DirectEditing.js:194-196`).
The context is `{ text, bounds, style?, options? }`, with bounds in client coordinates scaled by the
current zoom. On completion it calls `update(element, newText, oldText, bounds)`, and it does so
when the text **or** the textbox size changed
(`diagram-js-direct-editing 3.5.1: lib/DirectEditing.js:109-120`).

```js
// FPB.JS: app/fpb/label/LabelProvider.js:101-136 (shortened)
LabelEditingProvider.prototype.activate = function(element) {
  const text = getLabel(element);
  if (text === undefined) {
    return;                         // not editable: let another provider answer
  }
  const { bounds, style } = this._boundsCalculator.calculateEditingBBox(element);
  const context = { text: text, bounds: bounds };
  if (this._hasInternalLabel(element)) {
    context.options = { ...EDITING_OPTIONS.DEFAULT };
  }
  if (style) {
    context.style = style;
  }
  return context;
};

LabelEditingProvider.prototype.update = function(element, newLabel) {
  const processedLabel = isEmptyText(newLabel) ? null : newLabel;
  this._modeling.updateLabel(element, processedLabel);
};
```

Wire the lifecycle like both projects do: activate on `element.dblclick`, complete on
`element.mousedown`, `drag.init`, `canvas.viewbox.changing`, cancel on `commandStack.changed`
(`FPB.JS: app/fpb/label/LabelProvider.js:43-78`,
`@bptlab/openbpt-modeler-petri-net 1.0.2: lib/modeling/CustomLabelEditing.js:20-55`).
The reused modeler also activates editing right after a shape is created
(`@bptlab/openbpt-modeler-petri-net 1.0.2: lib/modeling/CustomLabelEditing.js:57-71`), which saves
the user a double click. The starter's provider is the minimal form: `activate` returns zoomed
client bounds for nodes and `undefined` otherwise, `update` trims and issues the properties command
only when the name changed, and it completes (rather than cancels) editing on
`commandStack.changed`, `drag.init`, `canvas.viewbox.changing` and `autoPlace`
(`starter/web/src/label/LabelEditing.js:19-52`).

### The update goes through a command handler

`update` must never write `businessObject.name` directly. Register a handler (FPB.JS overrides
`getHandlers`, `FPB.JS: app/fpb/modeling/FpbModeling.js:21-29`; the starter registers in an
`__init__` function) and give it `execute` and `revert` that return the changed elements:

```js
// FPB.JS: app/fpb/label/cmd/UpdateLabelHandler.js:75-98
UpdateLabelHandler.prototype.execute = function(ctx) {
  ctx.oldLabel = getLabel(ctx.element);
  (ctx.directWrites || []).forEach((write) => {
    write.target[write.key] = write.newValue;
  });
  return this._setText(ctx.element, ctx.newLabel);
};

UpdateLabelHandler.prototype.revert = function(ctx) {
  const changed = this._setText(ctx.element, ctx.oldLabel);
  const writes = ctx.directWrites || [];
  for (let i = writes.length - 1; i >= 0; i--) {
    writes[i].target[writes[i].key] = writes[i].oldValue;
  }
  return changed;
};
```

Use `preExecute` and `postExecute` only for follow-up **commands** (create or remove the external
label shape, resize it); nested commands are undone together with the parent.

### Why

- **A direct write bypasses undo, redraw and change detection.** The starter's properties handler
  documents the failure: the screen shows the new value, undo does not know it, the shape is not
  redrawn and `commandStack.changed` never fires, so the host never learns about the edit
  (`starter/web/src/modeling/UpdatePropertiesHandler.js:1-10`; the handler itself at `:11-41`).
- **Writes in `postExecute` are not reverted.** `postExecute` is not re-run on redo and its plain
  assignments are not undone. FPB.JS's name synchronisation across layers wrote straight into other
  objects there; undo left renamed states behind. The fix records each write on the context and
  replays or reverts it in `execute` and `revert`
  (`FPB.JS: app/fpb/label/cmd/UpdateLabelHandler.js:25-38`, `75-98`, `217-288`). The upstream Petri
  net handler has the same shape of bug: its default weight `"1"` is set in `postExecute` for every
  element type (`@bptlab/openbpt-modeler-petri-net 1.0.2: lib/modeling/UpdateLabelHandler.js:82-90`).
  AMLPetriNet's browser test clears a place, a transition and an arc, then undoes all three and
  checks that names and weight come back (`AMLPetriNet: web/tools/verify-labels.mjs:20-70`).
- **An empty label is not an empty string.** Store `null` or delete the property when the user
  clears a name (`FPB.JS: app/fpb/label/LabelProvider.js:133-136`,
  `starter/web/src/modeling/UpdatePropertiesHandler.js:43-51`), and treat whitespace as empty
  (`FPB.JS: app/fpb/label/utils/LabelUtils.js:10-12`). FPB.JS rendered whitespace-only names as
  labels until the renderer got a `trim()` guard
  (`FPB.JS: app/fpb/core/shapes/BaseShapeRenderer.js:42-46`). An empty string also reaches the
  export and ends up as a meaningless AML attribute.
- **External labels are separate shapes.** A name drawn beside a shape is a `label` element with
  `labelTarget`, created and removed by commands
  (`FPB.JS: app/fpb/modeling/behavior/LabelBehavior.js:36-77`). Its bounds must be written back to
  the model after every change, not only on move, or label positions are lost
  (`FPB.JS: app/fpb/modeling/updater/DiUpdater.js:90-96`).
- **Property edits need the same treatment.** A properties panel must call
  `modeling.updateProperties` (or your equivalent) and the handler must store old values for
  revert (`FPB.JS: app/fpb/modeling/cmd/UpdatePropertiesHandler.js:40-83`, `108-129`). When the
  name is changed through properties, trigger the label update as a follow-up command
  (`FPB.JS: app/fpb/modeling/behavior/LabelBehavior.js:25-34`).

---

## 10. Importing a model

### What to do

1. Parse and validate the exchange format **outside** the canvas; fail before touching the canvas.
2. Work on a copy of the input.
3. Clear the diagram (`diagram.clear()` also clears the command stack,
   `diagram-js 15.26.0: lib/command/CommandStack.js:159-164`).
4. Create an explicit root with a business object that carries the diagram's id and name, and set
   it with `canvas.setRootElement`; the implicit root the canvas creates on demand has no business
   object, and an export that reads `getRootElement().businessObject.id` then throws
   (`starter/web/src/io/json.js:34-39`, `112`). Then add **all shapes** with `elementFactory.createShape` and `canvas.addShape`,
   parents before children, then **all connections** with `elementFactory.createConnection` and
   `canvas.addConnection`, passing `source`, `target` and `waypoints`.
5. Compute waypoints that dock on the shape outlines, because `canvas.addConnection` does not run
   the layouter or the docking.
6. Resolve all references in one pass; drop connections whose ends are missing, with a warning.
7. Return a promise that resolves when the canvas is populated, and report errors to the caller.

```js
// FPB.JS: app/fpb/FpbModeler.js:274-293
FpbModeler.prototype.addFpbElements = function (fpbElements) {
  if (!isArray(fpbElements)) {
    throw new Error('argument must be an array');
  }
  const shapes = [];
  const connections = [];
  fpbElements.forEach(function (fpbElement) {
    if (isFpbConnection(fpbElement)) {
      connections.push(fpbElement);
    } else {
      shapes.push(fpbElement);
    }
  });
  shapes.forEach(this._addFpbShape, this);
  connections.forEach(this._addFpbConnection, this);
};
```

The moddle based importer defers connections the same way until all nodes are drawn
(`@bptlab/openbpt-modeler-petri-net 1.0.2: lib/import/CustomTreeWalker.js:199-209`) and creates
each connection from source, target and DI waypoints
(`@bptlab/openbpt-modeler-petri-net 1.0.2: lib/import/CustomImporter.js:102-119`).

Docking on import, as AMLPetriNet's converter does it for circles and rectangles:

```js
// AMLPetriNet: web/src/pnml/index.js:527-546
function dockingPoint(box, target) {
  if (!box) return { x: 0, y: 0 };
  const centre = centreOf(box);
  const dx = target.x - centre.x;
  const dy = target.y - centre.y;
  if (dx === 0 && dy === 0) return centre;
  const a = box.width / 2;
  const b = box.height / 2;
  const scale =
    box.shape === 'circle'
      ? 1 / Math.sqrt((dx / a) ** 2 + (dy / b) ** 2)
      : Math.min(dx === 0 ? Infinity : a / Math.abs(dx), dy === 0 ? Infinity : b / Math.abs(dy));
  return { x: round(centre.x + dx * scale), y: round(centre.y + dy * scale) };
}
```

A simpler alternative for a modeler you own: run the imported bend points through your own
layouter before adding the connection, so import and editing crop the same way. The starter does
that, and its `importModel` wraps the whole import so the command stack is cleared afterwards
(`starter/web/src/io/json.js:91-100`, `starter/web/src/EflModeler.js:91-109`):

```js
// starter/web/src/io/json.js:95-101
const connection = elementFactory.createConnection({ id, type: FLOW, source, target, businessObject });
connection.waypoints = layouter.layoutConnection(connection, {
  source,
  target,
  waypoints: [centre(source), ...bends, centre(target)],
});
canvas.addConnection(connection, root);
```

The same starter file shows the tolerant side of step 6: nodes without id, duplicate ids, unknown
types and flows with missing ends are left out with a warning, and missing positions fall back to
the origin with a warning (`starter/web/src/io/json.js:45-57`, `77-86`). A file with a newer
`formatVersion` than the modeler knows is shown as far as possible, with a warning that anything
the newer version added is not shown and would be lost on saving; a file without the field counts as
version 1 (`starter/web/src/io/json.js:27-32`).

### Why

- **Connections before shapes fail.** `canvas.addConnection` needs registered source and target
  elements; the upstream importer throws `notYetDrawn` when an end is missing
  (`@bptlab/openbpt-modeler-petri-net 1.0.2: lib/import/CustomImporter.js:174-192`).
- **Uncropped imported waypoints draw arrow heads inside the shape.** Without stored waypoints the
  upstream importer uses the two centres (`@bptlab/openbpt-modeler-petri-net 1.0.2: lib/import/CustomImporter.js:218-228`),
  and FPB.JS's importer does the same for connections without visual information
  (`FPB.JS: app/fpb/importer/JSONImporter.js:520-527`). The PNML converter computes docking points
  because the modeler does not run its docking layout on import
  (`AMLPetriNet: web/src/pnml/index.js:722-734`).
- **Store only bendpoints when end points can be recomputed.** The PNML export writes only the
  intermediate points; the end points are derived from node geometry, and writing them shows up as
  stray bends in other tools (`AMLPetriNet: web/src/pnml/index.js:27-30`, `388-400`). Your mapper
  must use the same convention; AMLPetriNet checks it with files the C# mapper produced
  (`AMLPetriNet: web/tools/verify-interop.mjs:1-13`).
- **An importer that consumes its input cannot import twice.** FPB.JS's importer removes matched
  entries from the input lists while building, so a second import of the same object found nothing;
  it now clones the data first (`FPB.JS: app/fpb/importer/JSONImporter.js:180-193`, `36-43`).
- **Import must replace, not append.** FPB.JS accumulated projects and layers across imports until
  both the importer and `clear()` reset their collections
  (`FPB.JS: app/fpb/importer/JSONImporter.js:40-43`, `205-214`; `FPB.JS: app/fpb/FpbModeler.js:223-229`).
- **One bad reference must not abort the whole import.** FPB.JS skips ids without data, drops
  connections with unresolved ends and removes the half-resolved back references
  (`FPB.JS: app/fpb/importer/JSONImporter.js:300-305`, `470-514`).
- **Fire-and-forget imports race the host.** FPB.JS's facade `importJSON` only fires an event
  (`FPB.JS: src/index.js:158-160`); the importer then registers processes and switches to the entry
  process after a fixed 2000 ms timer (`FPB.JS: app/fpb/importer/JSONImporter.js:135-156`,
  `FPB.JS: app/fpb/importer/ImportConstants.js:36`). Any "import finished" signal sent earlier is a
  guess. AMLPetriNet awaits `modeler.importPNML` and only then acknowledges
  (`AMLPetriNet: web/src/bridge.js:162-173`), and it serialises imports so two in quick succession
  do not interleave (`AMLPetriNet: web/src/bridge.js:192-200`).
- **Canvas navigation is not a model edit.** FPB.JS switches layers through a command
  (`FPB.JS: app/fpb/modeling/FpbModeling.js:54-58`) that clears the canvas and re-adds shapes, then
  connections (`FPB.JS: app/fpb/modeling/cmd/SwitchProcess.js:55-95`). As a command, a pure view
  change lands on the undo stack and fires `commandStack.changed`. Keep view state out of the
  command stack.

---

## 11. Exporting a model

### What to do

Write an explicit serializer that walks the element registry (or your model root) and emits the
exchange format: for shapes `id`, `type`, semantic attributes, `x`, `y`, `width`, `height`, label
offset; for connections `id`, `type`, `source.id`, `target.id`, bendpoints as plain `{x, y}`.
Round numbers deliberately and check with a byte-for-byte round trip.

```js
// AMLPetriNet: web/src/pnml/index.js:209-214
function coordinate(value) {
  const n = Number(value);
  if (!Number.isFinite(n)) return '0';
  const rounded = Math.sign(n) * Math.round(Math.abs(n) * 100) / 100;
  return String(rounded === 0 ? 0 : rounded);
}
```

The starter's `exportModel` is such a whitelist serializer for JSON: explicit keys per node and
flow, bend points only (`waypoints.slice(1, -1)`), every number through a two-decimal `round`
(`starter/web/src/io/json.js:109-141`, `161`).

Label positions: store them relative to an anchor both sides compute identically. AMLPetriNet
measures an arc label from the middle of the polyline's middle segment, copied from the modeler's
own `getWaypointsMid`, because a label offset only means something next to the point it is
measured from (`AMLPetriNet: web/src/pnml/index.js:177-198`).

### Why

- **`JSON.stringify` on live diagram objects fails or leaks internals.** Business objects reference
  each other in cycles. FPB.JS's export needs a replacer that turns references into ids and removes
  `di`, `children`, `labels` and the `TemporaryFlowHint` side channel
  (`FPB.JS: app/fpb/layer-panel/components/DownloadModal.js:225-283`), and the AML plugin had to
  copy that replacer because the facade returns the live graph
  (`AMLFPB.js: Aml.Editor.Plugin.FPB/fpbjs-assets/index.html:182-233`,
  `FPB.JS: src/index.js:167-170`). A serializer with a whitelist does not break when someone adds a
  property.
- **Float noise breaks strict formats.** `String()` prints values like `5.551115123125783e-17`,
  which is not an `xs:decimal`; PNML node sizes allow at most one decimal
  (`AMLPetriNet: web/src/pnml/index.js:200-227`). AMLPetriNet rounds the same way as its C# mapper so
  both spell a coordinate identically, and tests it (`AMLPetriNet: web/tools/verify-numbers.mjs:1-8`).
- **Waypoints can live in two places and drift apart.** FPB.JS keeps waypoints on the connection
  element and mirrors them into its DI objects on `connection.layout`, `connection.move` and
  `connection.updateWaypoints` (`FPB.JS: app/fpb/modeling/updater/ConnectionUpdater.js:87-103`,
  `364-366`). Imported connections nobody touched had DI without waypoints until the importer
  mirrored them as well (`FPB.JS: app/fpb/importer/JSONImporter.js:529-542`), and the layouter
  prefers the live connection over DI for the same reason
  (`FPB.JS: app/fpb/modeling/FpbLayouter.js:117-139`). Serialize from one source of truth: the
  diagram elements.
- **A round trip is the cheapest complete test.** AMLPetriNet builds a net in the real modeler,
  exports, imports the export and exports again, and requires byte identity
  (`AMLPetriNet: web/tools/roundtrip.mjs:1-7`, `22-74`). Anything the converter drops shows up as a
  diff. The starter's `verify-modeler.mjs` runs twelve such checks in Chromium, among them an unchanged
  import-export round trip, flow ends on the node outlines, a newer format version shown with a
  warning, a broken file read as far as possible,
  a palette drop with the real mouse, a flow drawn with the palette's connect tool and the mouse,
  rules, undo of rename and value, delete, move, and an SVG without editor furniture
  (`starter/web/tools/verify-modeler.mjs:12-177`). Tests do not see a label drawn in the wrong place
  or a flow drawn over another one: look at `npm run screenshot` (the example as stored) and
  `npm run screenshot -- --arranged` (layout stripped and placed again by the mapper) after every
  change to the renderer or the layouter (`starter/web/tools/screenshot.mjs:1-20`). See
  [07-testing-and-ci.md](07-testing-and-ci.md).

---

## 12. Change events and dirty tracking

### What to do

Listen to `commandStack.changed`, debounce, serialize, and compare against a baseline. Suppress
reports while you import or while a mode (simulation, layer switch) writes through the command
stack, and after every import send your own export as the new baseline:

```js
// AMLPetriNet: web/src/bridge.js:139-157
const exportPnml = async () => {
  try {
    return await modeler.savePNML();
  } catch (e) {
    post({ type: 'error', message: 'PNML export failed: ' + (e?.stack || e) });
    return null;
  }
};

const scheduleChange = () => {
  if (importing || simulating) return;
  clearTimeout(pending);
  pending = setTimeout(async () => {
    const pnml = await exportPnml();
    if (pnml !== null) post({ type: 'changed', pnml });
  }, CHANGE_DEBOUNCE_MS);
};

eventBus.on('commandStack.changed', scheduleChange);
```

The starter's bridge has the same shape without the simulation flag: debounce at
`starter/web/src/bridge.js:96-103`, serialised imports that post `imported` with the page's own
export and the reader's warnings at `:105-127`. A failed import posts only `error`, no `imported`
(`:117-122`): the host reads `imported` as "the canvas shows what I sent" and would drop its unsaved
edits.

Register listeners that must see the final state with an explicit priority relative to other
listeners; AMLPetriNet listens to the simulation toggle below the simulator's own priority so the
marking is already restored (`AMLPetriNet: web/src/bridge.js:31-35`, `175-186`).

### Why

- **Not every command stack change is a user edit.** Token replay changes markings through
  commands (`AMLPetriNet: web/src/bridge.js:130-137`); FPB.JS layer switches are commands
  (section 10); `commandStack.clear()` fires `changed` with `trigger: 'clear'` unless told otherwise
  (`diagram-js 15.26.0: lib/command/CommandStack.js:240-246`). A timed suppression window is not
  enough: the AML plugin for FPB.JS moved to a content baseline because timers either swallowed real
  edits or let import fallout through, and rAF based settling stalls in a hidden WebView2 tab
  (`AMLFPB.js: Aml.Editor.Plugin.FPB/fpbjs-assets/index.html:275-299`). The host protocol for this
  is in [05-editor-plugin.md](05-editor-plugin.md).
- **Test the suppression.** AMLPetriNet's bridge test checks that an import raises no change, that a
  user edit does, that two back-to-back imports raise none, and that a simulation reports nothing
  until it ends with the original marking (`AMLPetriNet: web/tools/verify-bridge.mjs:78-94`,
  `116-166`).
- **Remove listeners you add from outside diagram-js.** UI components that subscribe to the event
  bus must unsubscribe; FPB.JS's properties panel does it in the effect cleanup
  (`FPB.JS: app/fpb/properties-panel/PropertiesView.js:47-73`). A facade `on` without `off` leaks
  per component mount (`FPB.JS: src/index.js:134-150` offers both).

---

## 13. Exporting SVG

### What to do

Build the SVG from the active layer, include `<defs>`, give it a margin, and inline any style that
only a stylesheet provided. Return a promise.

```js
// AMLPetriNet: web/src/svg.js:10-20
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

### Why

- **The bounding box cuts strokes.** `saveSVG` sizes the picture to the geometry's bounding box
  (`@bptlab/openbpt-modeler-petri-net 1.0.2: lib/BaseViewer.js:197-219`), which halves the stroke at
  the edge and leaves labels flush with the border (`AMLPetriNet: web/src/svg.js:1-9`).
- **Styles from `diagram-js.css` do not travel with the file.** Paths that rely on the stylesheet
  for `fill: none` turn black in a standalone SVG; FPB.JS adds `fill: none` to such paths and strips
  grid patterns from `<defs>` (`FPB.JS: app/fpb/FpbModeler.js:378-401`). Set fill and stroke as
  attributes in the renderer and you avoid the post-processing.
- **Two calling conventions exist.** FPB.JS's `saveSVG` takes a callback
  (`FPB.JS: app/fpb/FpbModeler.js:360-425`), the Petri net modeler returns a promise of `{ svg }`.
  Wrap whichever you have into one promise returning a string (`FPB.JS: src/index.js:177-187`).
- **Editor furniture turns into black boxes.** The active layer also holds hit areas, selection
  outlines and bend point handles that only the page's CSS hides. The starter builds the SVG from a
  clone of the layer with `.djs-hit`, `.djs-outline`, `.djs-bendpoints` and draggers removed
  (`starter/web/src/EflModeler.js:125-152`).
- **Check that every label is present.** AMLPetriNet's SVG test loads a real model and checks labels,
  inline styles and the margin (`AMLPetriNet: web/tools/verify-svg.mjs:1-7`).

---

## 14. Bundling into one file

### What to do

Produce one JavaScript file, one CSS file with fonts and images inlined, and one `index.html`.
No code splitting, no dynamic chunks, nothing fetched from a CDN at runtime.

```js
// AMLPetriNet: web/build.mjs:62-77
await esbuild.build({
  ...shared,
  entryPoints: [resolve(pkg, 'assets/pn-js.css')],
  outfile: resolve(dist, 'ptnjs.css'),
  loader: {
    '.woff': 'dataurl',
    '.woff2': 'dataurl',
    '.ttf': 'dataurl',
    '.eot': 'dataurl',
    '.svg': 'dataurl',
    '.png': 'dataurl',
  },
});
```

With webpack, force a single chunk and disable async chunk loading, as FPB.JS's library build does
(`FPB.JS: webpack.lib.config.js:18`, `73`, `101`, `146`):

```js
output: { /* ... */ asyncChunks: false },
plugins: [ new webpack.optimize.LimitChunkCountPlugin({ maxChunks: 1 }) ]
```

Make the container fill the viewport explicitly; diagram-js creates `.djs-container` inside your
element (`AMLPetriNet: web/index.html:18-29`). Show boot failures in the page and forward them to
the host (`AMLPetriNet: web/index.html:66-83`).

### Minification and `$inject`

- **Default: minification off** for a bundle the host loads from disk. Size does not matter there;
  boot correctness does (`AMLPetriNet: web/build.mjs:24-43`, `starter/web/build.mjs:8-11`, `30`).
- In AMLPetriNet a minified esbuild bundle failed twice: first because an upstream handler had no
  `$inject` (fixed), then with `No provider for "l"! (Resolving: keyboardMove -> distributeElements
  -> alignElements -> l)`, whose cause was not found; all 118 `$inject` annotations survived
  (`AMLPetriNet: web/build.mjs:30-38`).
- FPB.JS's library is built with webpack in production mode (`FPB.JS: package.json:24`,
  `webpack.lib.config.js:85`) and ships minified; it runs because every component it owns declares
  `$inject`.
- If you turn minification on, keep a browser test that boots the bundle and exercises the command
  stack; AMLPetriNet's bridge test reproduces the failure in seconds (`AMLPetriNet: web/build.mjs:40-42`).

### Why

- **A missing chunk is a blank canvas without an error.** The host serves files from a folder
  through a virtual host name (`starter/web/build.mjs:4-6`,
  `starter/plugin/Aml.Editor.Plugin.Efl/Bridge/ModelerView.cs:26-31`, [05-editor-plugin.md](05-editor-plugin.md)).
  FPB.JS's entry uses dynamic `import()` for all heavy modules (`FPB.JS: src/index.js:58-73`), which
  webpack would split into chunks without `LimitChunkCountPlugin`.
- **Peer dependencies must be supplied by the page.** FPB.JS's ESM build keeps React external
  (`FPB.JS: webpack.lib.config.js:141-144`), so the plugin page provides it through an import map
  (`AMLFPB.js: Aml.Editor.Plugin.FPB/fpbjs-assets/index.html:90-98`). Avoid externals for a
  modeler you build for the plugin.
- **Node-only dependencies do not bundle.** The upstream PNML converter needed `url` and `events`
  (`AMLPetriNet: web/src/pnml/index.js:6-8`). Use `DOMParser` and `XMLSerializer`, which WebView2
  and every browser provide.
- **Module resolution of a published package follows `exports`.** FPB.JS's `package.json` declares
  `exports` (`FPB.JS: package.json:8-14`); bundlers pick that over `main` and `module`, so consumers
  that need the UMD build must alias it explicitly.

---

## 15. Version pinning

### What to do

- Pin `diagram-js` and `diagram-js-direct-editing` to exact versions in `package.json`, commit the
  lock file, and upgrade deliberately with the browser tests.
- For a reused modeler, pin the modeler exactly and read its transitive diagram-js version from the
  lock file.
- Record the versions in the third-party notices and in your docs.

| Project | Declared | Resolved |
|---|---|---|
| starter | `diagram-js` `15.26.0`, `diagram-js-direct-editing` `3.5.1` exact (`starter/web/package.json:14-15`) | same (`starter/web/package-lock.json:482-483`, `502-503`) |
| AMLPetriNet | `@bptlab/openbpt-modeler-petri-net` `1.0.2` exact (`AMLPetriNet: web/package.json:21`); the package itself asks for `diagram-js` `^15.5.0` | `diagram-js` 15.26.0, direct editing 3.5.1 (`AMLPetriNet: web/package-lock.json:560-561`, `580-581`) |
| FPB.JS | `diagram-js` `^15.26.0`, `diagram-js-direct-editing` `^3.2.0` (`FPB.JS: package.json:76-77`) | 15.26.0 and 3.5.1 in the lock file |

### Why

- **Caret ranges drift between machines and CI.** The reused package declares `^15.5.0`; without a
  lock file a fresh install can resolve any 15.x. FPB.JS's own project notes still name 15.3.0 as the
  base while the lock file resolves 15.26.0; documentation and code drifted apart.
- **Patches against upstream internals are version-specific.** AMLPetriNet's prototype patches
  target `UpdateLabelHandler.prototype.postExecute` and a renderer method by name
  (`AMLPetriNet: web/src/index.js:26-45`, `68-96`); an upgrade that renames either silently disables
  the fix. The browser tests are the guard (`AMLPetriNet: web/package.json:10`).

---

## 16. Known pitfalls, collected

| Pitfall | Symptom | Prevention | Evidence |
|---|---|---|---|
| Missing `$inject` | Minified bundle: `No provider for "x"` | Annotate every class and init function | `AMLPetriNet: web/src/index.js:9-18` |
| Rule returns `undefined` for "no" | Forbidden connection allowed or created without type | Return `false` | `FPB.JS: app/fpb/rules/FpbRuleProvider.js:284-325`, `FPB.JS: app/fpb/modeling/FpbModeling.js:43-52` |
| No `connection.start` rule | Global connect tool never starts | Add the rule | `diagram-js 15.26.0: lib/features/global-connect/GlobalConnect.js:144`, `lib/command/CommandStack.js:221-224` |
| `textRenderer` injected on bare diagram-js | Boot fails with a missing provider | Own `Text` instance | `starter/web/src/draw/Renderer.js:34-36` |
| Implicit root element | Export throws reading the root's business object | Explicit root on import | `starter/web/src/io/json.js:34-39` |
| Direct writes to business objects | No undo, no redraw, host never notified | Command handler with `execute`/`revert` | `starter/web/src/modeling/UpdatePropertiesHandler.js:1-10` |
| Writes in `postExecute` | Undo leaves partial state | Record and revert them, or issue commands | `FPB.JS: app/fpb/label/cmd/UpdateLabelHandler.js:25-38` |
| Empty string labels | Blank label rendered, empty attribute exported | Store `null`, trim before checking | `FPB.JS: app/fpb/core/shapes/BaseShapeRenderer.js:42-46` |
| Default text on cleared label | Place named "1" | Scope defaults to the element type | `AMLPetriNet: web/src/index.js:20-45` |
| Counter ids | `element with id ... already added` after import | Random or deterministic ids | `diagram-js 15.26.0: lib/core/ElementFactory.js:106-107` |
| Label id collisions | Import fails on ids ending in `_label` | Validate ids on import | `FPB.JS: app/fpb/label/cmd/UpdateLabelHandler.js:65-68` |
| Arrays not initialised | `Cannot read properties of undefined (reading 'push')` | Initialise in the factory | `FPB.JS: app/fpb/modeling/FpbElementFactory.js:94-135` |
| Shape vs business object | `.businessObject` of undefined, string ids where objects expected | Typed references, one resolution pass | `FPB.JS: app/fpb/importer/JSONImporter.js:703-723` |
| Connections added before shapes | `notYetDrawn` error | Shapes first, then connections | `FPB.JS: app/fpb/FpbModeler.js:290-292` |
| Uncropped waypoints | Arrow heads inside shapes | Dock on import, crop on layout | `AMLPetriNet: web/src/pnml/index.js:722-734` |
| Base layouter | Bends straightened when a node moves | Own layouter keeping bendpoints | `diagram-js 15.26.0: lib/layout/BaseLayouter.js:39-47` |
| `x || original.x` | Coordinate 0 replaced | Compare with `undefined` | `FPB.JS: app/fpb/xml/XMLMapper.js:2051` |
| Listener leaks | Handlers run twice after remount, memory grows | `off` in cleanup, `diagram.destroy` hook | `FPB.JS: app/fpb/services/KeyboardAlignService.js:30-39` |
| Import appends | Duplicate layers or elements after reimport | Reset all collections in `clear()` | `FPB.JS: app/fpb/FpbModeler.js:223-229` |
| Import mutates input | Second import of the same data is empty | Clone input | `FPB.JS: app/fpb/importer/JSONImporter.js:187-193` |
| Import reported early | Host baseline taken before the canvas is filled | Await import completion | `FPB.JS: app/fpb/importer/JSONImporter.js:135-156` |
| Swallowed import errors | Elements missing, no error | Forward console to host log | `@bptlab/openbpt-modeler-petri-net 1.0.2: lib/import/CustomTreeWalker.js:61-78` |
| UI state on shapes | Extra properties in the export | Keep UI state in contexts | `FPB.JS: app/fpb/context-pad/FpbContextPadProvider.js:160` |
| `canRender` returns `true` | Other renderers never used | Restrict to own types | `@bptlab/openbpt-modeler-petri-net 1.0.2: lib/draw/CustomRenderer.js:35-37` |
| No keyboard module | Ctrl+Z does nothing | Add `keyboard` and `editor-actions` | `FPB.JS: app/fpb/FpbModeler.js:108-131` |
| Chunks in bundle | Blank canvas in WebView2 | One chunk, inline assets | `FPB.JS: webpack.lib.config.js:73` |

---

## Checklist

- [ ] Searched for an existing diagram-js modeler of the language; decision (reuse or build) and
      reasons written down.
- [ ] Every reused-package patch is guarded against double application, commented with the reason,
      and covered by a browser test.
- [ ] `diagram-js`, `diagram-js-direct-editing` and any reused modeler pinned to exact versions;
      lock file committed; versions listed in the third-party notices.
- [ ] Module list includes modeling, rules, palette, create, connect, context pad, move, selection,
      lasso, bendpoints, direct editing, keyboard and editor actions.
- [ ] Every service class and `__init__` function has a `$inject` array matching its parameters.
- [ ] One file defines all element type strings and default sizes.
- [ ] Business objects are either all plain or all moddle; element id equals business object id
      equals exchange-format id.
- [ ] Element factory initialises all array properties and generates collision-free ids.
- [ ] Renderer: priority above default, `canRender` limited to own types, `getShapePath` matches the
      drawn outline for every type, markers defined once with unique ids.
- [ ] `CroppingConnectionDocking` registered; layouter keeps bendpoints and crops end points.
- [ ] Rules return `false` (not `undefined`) to forbid; `connection.reconnect` and
      `connection.start` covered; rules have no side effects.
- [ ] Palette create entries have both `click` and `dragstart`; context pad returns nothing for labels.
- [ ] Label provider implements `activate` returning `{ text, bounds, style?, options? }` or
      `undefined`, and `update` calling a command.
- [ ] All edits go through command handlers with `execute` and `revert`; no plain assignments in
      `postExecute`; undo tested for rename, clear and property change.
- [ ] Cleared names become `null` or are removed, never `''`.
- [ ] Import validates first, works on a copy, clears the diagram, adds shapes before connections,
      docks waypoints, resolves references in one pass, and returns a promise.
- [ ] Export uses a whitelist serializer, writes only `x`/`y` of waypoints, rounds numbers, and a
      byte-identical export-import-export test passes.
- [ ] `commandStack.changed` is debounced, suppressed during import and non-edit modes, and the host
      receives an export baseline after each import.
- [ ] Listeners added from outside diagram-js are removed on teardown.
- [ ] SVG export has a margin, inline styles, and all labels.
- [ ] Bundle is one JS and one CSS file with inlined fonts and icons; no chunks; minification off or
      covered by a boot test.
- [ ] Console output and boot errors are forwarded to the host.

---

## Where to look

| Topic | File |
|---|---|
| Reused modeler, patches | `AMLPetriNet: web/src/index.js` |
| Bridge, dirty tracking, import queue | `AMLPetriNet: web/src/bridge.js` |
| Lossless PNML converter, docking on import, number formatting | `AMLPetriNet: web/src/pnml/index.js` |
| esbuild single-file bundle, minification note | `AMLPetriNet: web/build.mjs` |
| SVG export with margin | `AMLPetriNet: web/src/svg.js` |
| Host page, boot error | `AMLPetriNet: web/index.html` |
| Browser tests (round trip, labels, bridge, numbers, SVG) | `AMLPetriNet: web/tools/*.mjs` |
| Licences of bundled packages | `AMLPetriNet: THIRD-PARTY-NOTICES.md` |
| Full module list of a diagram-js modeler | `@bptlab/openbpt-modeler-petri-net 1.0.2: lib/CustomModeler.js` |
| Template rules, renderer, label editing, importer | `@bptlab/openbpt-modeler-petri-net 1.0.2: lib/rules/CustomRuleProvider.js`, `lib/draw/CustomRenderer.js`, `lib/modeling/CustomLabelEditing.js`, `lib/import/CustomImporter.js` |
| Modeler class, module list, `addFpbElements`, `saveSVG` | `FPB.JS: app/fpb/FpbModeler.js` |
| Renderer and shape paths | `FPB.JS: app/fpb/core/FpbRenderer.js`, `app/fpb/core/paths/PathCalculator.js`, `app/fpb/core/shapes/BaseShapeRenderer.js` |
| Rules | `FPB.JS: app/fpb/rules/FpbRuleProvider.js` |
| Palette | `FPB.JS: app/fpb/palette/FpbPaletteProvider.js`, `app/fpb/palette/PaletteUtils.js` |
| Context pad | `FPB.JS: app/fpb/context-pad/FpbContextPadProvider.js` |
| Label editing provider and command | `FPB.JS: app/fpb/label/LabelProvider.js`, `app/fpb/label/cmd/UpdateLabelHandler.js` |
| Element factory, moddle factory | `FPB.JS: app/fpb/modeling/FpbElementFactory.js`, `app/fpb/modeling/FpbFactory.js` |
| Modeling API and handler registration | `FPB.JS: app/fpb/modeling/FpbModeling.js`, `app/fpb/modeling/index.js` |
| Layouter, cropping, DI sync | `FPB.JS: app/fpb/modeling/FpbLayouter.js`, `app/fpb/modeling/updater/DiUpdater.js`, `app/fpb/modeling/updater/ConnectionUpdater.js` |
| Behaviors (labels, data store) | `FPB.JS: app/fpb/modeling/behavior/LabelBehavior.js`, `app/fpb/modeling/behavior/DataBehavior.js` |
| JSON importer | `FPB.JS: app/fpb/importer/JSONImporter.js` |
| Public facade, event names | `FPB.JS: src/index.js` |
| webpack single-chunk library build | `FPB.JS: webpack.lib.config.js` |
| FPB.JS inside WebView2, echo baseline | `AMLFPB.js: Aml.Editor.Plugin.FPB/fpbjs-assets/index.html` |
| Starter modeler class: module list, import, export, SVG | `starter/web/src/EflModeler.js` |
| Starter types, renderer, factory, layouter, command | `starter/web/src/types.js`, `starter/web/src/draw/Renderer.js`, `starter/web/src/modeling/` |
| Starter rules, palette, context pad, label editing | `starter/web/src/rules/Rules.js`, `starter/web/src/palette/Palette.js`, `starter/web/src/context-pad/ContextPad.js`, `starter/web/src/label/LabelEditing.js` |
| Starter JSON import and export | `starter/web/src/io/json.js` |
| Starter bridge and bundle | `starter/web/src/bridge.js`, `starter/web/build.mjs`, `starter/web/index.html` |
| Starter browser checks | `starter/web/tools/harness.mjs`, `verify-modeler.mjs` (12 checks), `verify-bridge.mjs` (6), `verify-webapp.mjs` (7, starts the built `Efl.Web.dll`), `screenshot.mjs` (pictures of the stored and the arranged example, not a test) |
| diagram-js internals referenced above | `diagram-js 15.26.0: lib/layout/BaseLayouter.js`, `lib/layout/CroppingConnectionDocking.js`, `lib/features/rules/Rules.js`, `lib/command/CommandStack.js`, `lib/features/connect/Connect.js` |
