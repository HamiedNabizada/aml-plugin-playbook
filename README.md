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
```

The script prints the commands that build and test the copy and write its example files. Run all of
them; everything has to pass before you change the language. Then follow
[docs/10-new-language-recipe.md](docs/10-new-language-recipe.md) from §1.

## Where this comes from

The playbook distils two projects that map graphical languages to AutomationML and edit them inside the AutomationML Editor. Code references in the documents point into them.

| Project | Language | Parts |
|---|---|---|
| AMLFPB.js ([hsu-aut/AMLFPB.js](https://github.com/hsu-aut/AMLFPB.js)) with [hsu-aut/fpb-aml-mapper](https://github.com/hsu-aut/fpb-aml-mapper) and the FPB.JS modeler ([FPB.JS](https://github.com/HamiedNabizada/FPB.JS)) | Formalised Process Description, VDI/VDE 3682 | Plugin, mapper, web app, OCL based validation |
| AMLPetriNet ([hsu-aut/AMLPetriNet](https://github.com/hsu-aut/AMLPetriNet)) | Place/transition Petri nets, ISO/IEC 15909, PNML exchange | Plugin, mapper, CLI, web app, PNML conformance checks |
| AMLOpcUa ([hsu-aut/AMLOpcUa](https://github.com/hsu-aut/AMLOpcUa)) with the NodeSet.js modeller ([HamiedNabizada/NodeSet.js](https://github.com/HamiedNabizada/NodeSet.js)) | OPC UA information models, OPC 10000-83 Annex A, NodeSet2 exchange | Plugin, importer and exporter, CLI, servers, model rules, a vendored third-party converter |

The library design follows the three-phase method for representing graphical description languages in AutomationML: analysis of the language (A1 to A4), a semantic domain model (P1 to P3) and instantiation conventions (P4 to P8). It was introduced with the FPD (Drath, Nabizada, Fay, EKA 2026), the FPD domain library is described in Nabizada, Drath, Gehlhoff, Fay (ETFA 2026), and its application to a second language with a cross-language comparison is in Nabizada, Drath, Ocker, Fay (at Automatisierungstechnik, submitted). Cross-diagram references use the multi-context reference framework of Drath and Nabizada (ETFA 2026).

Shared AutomationML libraries:

- OMG_DD AttributeTypeLib 0.1 (`DD_Bounds`, `DD_Point` and `DD_Waypoint`, structured after the OMG Diagram Definition), first published with fpb-aml-mapper. **Included** in [starter/libraries/](starter/libraries/); the starter's mapper embeds it into every document it creates.
- AutomationML ObjectReferences AttributeTypeLib 1.1.1-beta, published by AutomationML. **Not included**, only referenced: obtain it through the library manager of the AutomationML Editor when your language needs cross-diagram references ([the recipe](docs/10-new-language-recipe.md), P3).
- AutomationML base libraries, AMLEd2 2.11.0. **Not included**; documents reference them by file name.

## Tested versions

.NET 8, Node 20, Aml.Engine 4.x, Aml.Editor.Plugin.Contract 4.3.0, Aml.Editor.API 2.3.0, Microsoft.Web.WebView2 1.0.2903.40, diagram-js 15.26.0, diagram-js-direct-editing 3.5.1. The plugin is built for the AutomationML Editor 6.4 or later.

## Send your findings back

Everything in here was paid for once, by someone losing a day to it. If you build on this and hit
something the playbook did not know, an entry from you saves the next reader that day.

Useful in order of value:

- **A pitfall** in the format of [docs/09-pitfalls.md](docs/09-pitfalls.md): symptom as a user sees
  it, the cause you actually verified, the fix, and the file and symbol that prove it. A symptom
  without a cause is still worth sending, marked as open.
- **A decision** in the format of [docs/decisions.md](docs/decisions.md), especially when you chose
  differently than this repository and it worked.
- **A version** of the editor, Aml.Engine or the plugin contract that changed behaviour, with what
  broke.

Open an issue or a pull request. Rough notes are fine; the format matters less than the evidence.

If you work with a coding assistant, tell it that this repository takes contributions, and that it
should write down what it had to find out the hard way instead of only fixing your build. That is
rule 9 in [CLAUDE.md](CLAUDE.md), and it is the reason the chapters here are as specific as they are.

## License

MIT, see [LICENSE](LICENSE).

The starter's built modeler bundles diagram-js and its dependencies (MIT, ISC, and Apache-2.0 for
htm), and its plugin package contains the WebView2 SDK (Microsoft, BSD-3-Clause style). Their
notices are in [starter/THIRD-PARTY-NOTICES.md](starter/THIRD-PARTY-NOTICES.md), which the plugin
package carries; `npm test` in `starter/web` fails when a runtime dependency is missing from it.
