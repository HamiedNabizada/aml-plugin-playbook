# aml-plugin-playbook: instructions for the coding assistant

You are helping someone build tooling for a graphical modelling language on top of AutomationML: a mapper between the language and CAEX, a browser modeler, optionally a web app, and a plugin for the AutomationML Editor. This repository holds what two finished projects learned doing exactly that, and a small working stack to start from.

Do not read everything. Use the routing below, then look things up when a step needs it.

## Start here

| The person wants to | Read first | Then |
|---|---|---|
| Build tooling for a new language | [docs/10-new-language-recipe.md](docs/10-new-language-recipe.md), completely | The chapter named by the current recipe step |
| Understand how the parts fit | "In short" of [docs/01-architecture.md](docs/01-architecture.md) | The sections it points to |
| Fix a bug in such a plugin or mapper | Search [docs/09-pitfalls.md](docs/09-pitfalls.md) for the symptom | The chapter of the area |
| Know why something is done a certain way | [docs/decisions.md](docs/decisions.md) | The cited code |
| Copy a piece of code for a task | [docs/snippets.md](docs/snippets.md) | The starter file it cites |
| Look up a term | [docs/glossary.md](docs/glossary.md) | |

Every chapter starts with an "In short" block and says when to read it fully. Read the block, not the chapter, unless the block tells you to.

## Rules that apply to every task

1. **Start from the starter.** `node tools/new-language.mjs <folder> <Prefix> "<Long name>"` makes a renamed copy. Run its tests before changing anything; they must be green. Never build the stack from an empty folder: the starter contains fixes for failures that are silent (a plugin that does not appear, a blank canvas, a document that loses content on update).
2. **Stop and ask at the stop points.** The recipe lists the decisions that belong to the person who knows the language in one table (S1 to S8: prefix, repository and license, the analysis worksheet, connection encoding, stored layout, exchange format, cross-diagram references, embedding shared libraries). Ask them in one round, with options, consequences and your recommendation, and wait for the answers. Do not guess them; a wrong guess costs a migration of documents later.
3. **Prove each step.** Every recipe step ends with a command. Run it and report the actual output. A step without a passing proof is not done. Look at a screenshot of the modeler after visual changes; tests do not see a misplaced label.
4. **The AML document is the master.** Update in place, never regenerate a hierarchy. Keep what the language does not own.
5. **One place per name.** CAEX names in `<Prefix>Names.cs`, canvas types in `web/src/types.js`. No string literal for them anywhere else.
6. **Keep the bridge and plugin lifecycle code unless you know the reason for each line.** Most of it exists because of a failure that took days to find; the comments say which. Read [docs/05-editor-plugin.md](docs/05-editor-plugin.md) before changing it.
7. **The editor test is manual.** Nothing here can load the plugin into the AutomationML Editor automatically. Hand the protocol in recipe §5 to the person, or run it with them, and record the editor version.
8. **When the starter and a document disagree, the starter wins.** It is tested; the text may lag behind. Fix the text.

## Facts that are easy to get wrong

- Aml.Engine gives every created object a random ID. Set deterministic IDs yourself after creating (`<Prefix>Ids`).
- Aml.Engine wrapper objects are not reference stable. Compare by `ID`.
- `CAEXDocument.LoadFromFile` and `SaveToFile` drop `CAEX_ClassModel_V.3.0.xsd` next to the file. Use `<Prefix>Documents.LoadFile` and `SaveFile`.
- `CAEXDocument.LoadFromString` does not reliably reject non-AML text; use `<Prefix>Documents.Load`.
- The AutomationML Editor does not follow file references: embed the small shared libraries into documents you create. The starter already embeds OMG_DD from `starter/libraries/` (`<Prefix>DiagramInterchange`); keep it and declare no layout types of your own. ObjectReferences is not in the repository: if the language needs references, get the published file through the AutomationML Editor's library manager and embed it the same way. Never rebuild a published library's types from a description.
- Give a node one port per connection **end**, keyed by node, connection and direction. Keyed without the direction, a connection from a node to itself gets one port for both links.
- The exchange format carries `formatVersion`; a missing field means 1, a newer version is read with a warning. Raise it in `<Prefix>Json.cs` and `web/src/io/json.js` together.
- Example AML files are written with `--timestamp` and CI fails when writing them again changes a byte. After a mapper change, write them again in the same commit.
- Never ship `Aml.Editor.Plugin.Contract.dll` in the plugin package; the editor then skips the plugin without a message. The package tests check it.
- The plugin `DisplayName` may contain letters, digits and underscore only.
- Never raise `IsDocumentLoaded` from `DocumentLoaded`; the editor calls back and loops.
- Plain diagram-js has no `textRenderer` service; use `diagram-js/lib/util/Text`. The implicit root element has no business object; set an explicit root.
- A diagram-js rule that returns `undefined` gives no answer. With no answer a command (`shape.create`, `elements.move`) is allowed, but a non-command action such as `connection.start` is refused: without an explicit rule the palette's connect tool does nothing. Return `false` to forbid.

## Versions the starter is tested with

.NET 8 SDK, Node 20, Aml.Engine 4.x (4.5.2 at the time of writing), Aml.Editor.Plugin.Contract 4.3.0, Aml.Editor.API 2.3.0, Microsoft.Web.WebView2 1.0.2903.40, diagram-js 15.26.0, diagram-js-direct-editing 3.5.1, esbuild 0.25, Playwright with Chromium, Windows for the plugin. Libraries: OMG_DD AttributeTypeLib 0.1 (included in `starter/libraries/`), AutomationML base libraries AMLEd2 2.11.0 (referenced by file name), ObjectReferences AttributeTypeLib 1.1.1-beta (not included; AutomationML Editor library manager).

## Working with the person

- Explain decisions in terms of the language, not the code.
- Report failing tests with their output. Do not call something done that you have not run.
- Do not commit, push or publish unless asked. Follow the person's own repository rules if they have any.
