# Starter: the Example Flow Language (EFL)

A complete, tested stack for one very small graphical language, built the way the playbook describes. Do not copy it by hand: `node tools/new-language.mjs <folder> <Prefix> "<Long name>"` in the playbook root makes a renamed copy.

EFL has two node types and one connection type:

| Element | Drawn as | Own attribute |
|---|---|---|
| `Step` | rounded rectangle | `Duration` (seconds) |
| `Store` | ellipse | `Capacity` |
| `Flow` | arrow from one node to another | none (a name is kept if present) |

That is enough to exercise every part: two node types with different attributes, a directed connection, layout, identity, an exchange format, both connection encodings and a round trip into AutomationML.

One thing here is for teaching and differs from what a real language should do:

- **Both connection encodings.** `EflConnectionStyle.Link` (InternalLink between node interfaces) and `EflConnectionStyle.Element` (a flow element with two links) are both implemented and tested. A real language picks one per connection type from its Phase 1 analysis and deletes the other.

Layout already uses the shared `OMG_DD_AttributeTypeLib` (`DD_Bounds`, `DD_Point`, `DD_Waypoint`), as a real language should. The published file is in `libraries/`, embedded into the mapper assembly, and carried in every document the mapper creates; keep it that way. The ObjectReferences library is not included: a language with cross-diagram references gets the published file through the library manager of the AutomationML Editor and embeds it the same way (`EflDiagramInterchange`).

## Contents

| Path | What it is |
|---|---|
| `web/` | The modeler: diagram-js 15 without moddle. Renderer, rules, palette, context pad, label editing, JSON import and export, SVG export, the WebView2 bridge, an esbuild build, Playwright checks, a screenshot tool |
| `dotnet/Efl.Conversion/` | The mapper: libraries (own four, OMG_DD embedded from `libraries/`), JSON reader and writer with a format version, model to CAEX and back in both encodings (one node port per connection end), update in place, validator (EFL01 to EFL07), layout of nodes and routing of their flows, deterministic ids, safe AML loading |
| `dotnet/Efl.Tests/` | Round trip, update, determinism, validator, geometry, layout and layout library tests (46) |
| `dotnet/Efl.Web/` | The mapper as a web app: one page with the modeler, and `/api/to-json`, `/api/to-aml`, `/api/update`, `/api/validate`, `/api/library` and `/api/library/layout` (the OMG_DD file it references), `/api/health` |
| `dotnet/Efl.Tool/` | `eflmap`, the mapper on the command line: `to-aml`, `to-json`, `update`, `validate`, `library`, `arrange`; it prints what it wrote |
| `plugin/Aml.Editor.Plugin.Efl/` | The AutomationML Editor plugin: WebView2 host, update flow, diagram picker, findings, settings, log, packaging |
| `plugin/Aml.Editor.Plugin.Efl.Tests/` | Checks on the built package |
| `examples/` | The example diagram as JSON, as AML in both encodings, the library file and the OMG_DD file it references |
| `libraries/` | `OMG_DD_AttributeTypeLib_v0.1.aml`, the published shared layout library; the one copy the mapper embeds |
| `.github/workflows/ci.yml` | Build and test on Windows, a check that the example files are current, package upload, release on a version tag |
| `.gitattributes` | `*.aml -text`, so git keeps the example files byte for byte |

## Running it

```bash
cd web
npm install
npm run build                     # writes web/dist; the .NET projects need it
npx playwright install chromium
npm test                          # modeler (12 checks) and bridge (6) in a browser
npm run test:webapp               # starts Efl.Web and checks page and API (7)
npm run screenshot                # web/screenshots/bottling-line.png and .svg, not kept in git
npm run screenshot -- --arranged  # the same with all layout removed and placed again by eflmap arrange

cd ../dotnet
dotnet test Efl.sln               # mapper tests (46)
dotnet run --project Efl.Web      # http://localhost:5210
dotnet run --project Efl.Tool -- to-aml ../examples/bottling-line.json out.aml --style element
dotnet run --project Efl.Tool -- arrange model-without-layout.json arranged.json

cd ../plugin
dotnet test                       # builds the package and checks its contents (13 tests)
# install plugin/build/Aml.Editor.Plugin.Efl/Debug/Aml.Editor.Plugin.Efl.0.1.0.nupkg
# (or Release) through the AutomationML Editor's plugin manager
```

## Writing the example files again

The AML examples are written by the tool from the JSON example, with a fixed writing time so that writing them again with an unchanged mapper changes no byte. After a change to the libraries or the mapper:

```bash
cd dotnet
dotnet run --project Efl.Tool -- to-aml ../examples/bottling-line.json ../examples/bottling-line.links.aml --timestamp 2026-01-01T00:00:00Z
dotnet run --project Efl.Tool -- to-aml ../examples/bottling-line.json ../examples/bottling-line.elements.aml --style element --timestamp 2026-01-01T00:00:00Z
dotnet run --project Efl.Tool -- library ../examples/EFL_DomainLibrary_v0.1.0.aml   # also writes OMG_DD_AttributeTypeLib_v0.1.aml beside it
```

CI writes the two diagram files again the same way and fails when `git diff` finds a change, so commit them together with the mapper change.

All .NET projects, under `dotnet/` and `plugin/`, build with warnings as errors.

Adapting it to your language is described step by step in `docs/10-new-language-recipe.md` of the playbook.
