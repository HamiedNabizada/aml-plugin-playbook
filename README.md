# aml-plugin-playbook

Everything needed to build tooling for your own graphical modelling language on top of AutomationML: a mapper between the language and CAEX, a browser modeler based on diagram-js, a web app, and a plugin for the AutomationML Editor that edits diagrams inside AML documents.

It is written so that you can hand the repository to a coding assistant together with a description of your language and get a working start instead of rediscovering the pitfalls. It works just as well read by a person.

## What is in here

| Path | What it is |
|---|---|
| [CLAUDE.md](CLAUDE.md) | Entry point for a coding assistant: routing, rules, facts that are easy to get wrong |
| [docs/10-new-language-recipe.md](docs/10-new-language-recipe.md) | The working order for a new language: analysis worksheet, decisions, engineering steps with proofs, editor test protocol, definition of done |
| [docs/01-architecture.md](docs/01-architecture.md) to [docs/09-pitfalls.md](docs/09-pitfalls.md) | What the source projects learned, by area, with code references |
| [docs/decisions.md](docs/decisions.md) | Why things are done the way they are, including the alternatives that failed |
| [docs/snippets.md](docs/snippets.md) | Code for the most common tasks, taken from the starter |
| [docs/glossary.md](docs/glossary.md) | Terms from CAEX, Aml.Engine, diagram-js, WebView2 and the plugin contract |
| [starter/](starter/) | A complete, tested stack for a toy language (EFL): mapper, CLI, web app, modeler, plugin, tests, CI, and the shared layout library OMG_DD in `starter/libraries/` |
| [tools/new-language.mjs](tools/new-language.mjs) | Copies the starter, renames the language in every place it lives, replaces the README with an outline and prints the commands that prove the copy works |

## Quick start

Requirements: Windows (for the plugin), .NET 8 SDK, Node 20, and the AutomationML Editor for the final check.

```bash
node tools/new-language.mjs ../my-language-aml Ml "My Language"
cd ../my-language-aml/web && npm install && npm run build && npx playwright install chromium && npm test
cd ../dotnet && dotnet test Ml.sln
cd ../web && npm run test:webapp
cd ../plugin && dotnet test
```

When everything is green, follow [docs/10-new-language-recipe.md](docs/10-new-language-recipe.md) from Phase 1.

## Where this comes from

The playbook distils two projects that map graphical languages to AutomationML and edit them inside the AutomationML Editor. Code references in the documents point into them.

| Project | Language | Parts |
|---|---|---|
| AMLFPB.js ([hsu-aut/AMLFPB.js](https://github.com/hsu-aut/AMLFPB.js)) with [hsu-aut/fpb-aml-mapper](https://github.com/hsu-aut/fpb-aml-mapper) and the FPB.JS modeler ([hsu-aut/FPB.JS](https://github.com/hsu-aut/FPB.JS)) | Formalised Process Description, VDI/VDE 3682 | Plugin, mapper, web app, OCL based validation |
| AMLPetriNet | Place/transition Petri nets, ISO/IEC 15909, PNML exchange | Plugin, mapper, CLI, web app, PNML conformance checks |

The library design follows the three-phase method for representing graphical description languages in AutomationML: analysis of the language (A1 to A4), a semantic domain model (P1 to P3) and instantiation conventions (P4 to P8). It was introduced with the FPD (Drath, Nabizada, Fay, EKA 2026), the FPD domain library is described in Nabizada, Drath, Gehlhoff, Fay (ETFA 2026), and its application to a second language with a cross-language comparison is in Nabizada, Drath, Fay (at Automatisierungstechnik, 2026, forthcoming). Cross-diagram references use the multi-context reference framework of Drath and Nabizada (ETFA 2026).

Shared AutomationML libraries:

- OMG_DD AttributeTypeLib 0.1 (`DD_Bounds`, `DD_Point` and `DD_Waypoint`, structured after the OMG Diagram Definition), first published with fpb-aml-mapper and AMLPetriNet. **Included** in [starter/libraries/](starter/libraries/); the starter's mapper embeds it into every document it creates.
- AutomationML ObjectReferences AttributeTypeLib 1.1.1-beta, published by AutomationML. **Not included**, only referenced: obtain it through the library manager of the AutomationML Editor when your language needs cross-diagram references ([the recipe](docs/10-new-language-recipe.md), P3).
- AutomationML base libraries, AMLEd2 2.11.0. **Not included**; documents reference them by file name.

## Tested versions

.NET 8, Node 20, Aml.Engine 4.x, Aml.Editor.Plugin.Contract 4.3.0, Aml.Editor.API 2.3.0, Microsoft.Web.WebView2 1.0.2903.40, diagram-js 15.26.0, diagram-js-direct-editing 3.5.1. The plugin is built for the AutomationML Editor 6.4 or later.

## License

MIT, see [LICENSE](LICENSE). The starter's npm dependencies (diagram-js, diagram-js-direct-editing, min-dash, tiny-svg) are MIT licensed and are bundled into the built modeler; keep their notices when you distribute a build.
