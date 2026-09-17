#!/usr/bin/env node
/**
 * Copies the starter into a new folder and renames the language in it.
 *
 *   node tools/new-language.mjs <target-folder> <Prefix> "<Long name>"
 *   node tools/new-language.mjs ../sm-aml-plugin Sm "State Machine"
 *
 * <Prefix> is the short name in PascalCase (Sm, Bpmn, Fmea). It becomes
 *   SM_  in CAEX class and library names (EFL_Step  -> SM_Step)
 *   Sm   in .NET namespaces, types and project names (Efl.Conversion -> Sm.Conversion)
 *   sm   in JavaScript, file names, the asset folder, the virtual host (efl-assets -> sm-assets)
 *
 * Why a script: a hand rename misses some of the places a language name lives
 * (the plugin's asset folder, the WebView2 host name, the settings and log
 * folders, the id namespace, the palette type strings, the response header).
 * Each missed one either keeps working by accident or fails silently inside
 * the editor. The element types (Step, Store, Flow) are NOT renamed: they are
 * the part you replace with your own language, following
 * docs/10-new-language-recipe.md.
 *
 * Words that merely contain the letters (reflect, deflection) are left alone:
 * lower case "efl" must not follow a letter. "Efl" may follow a lower case
 * letter, which is a camel case boundary (CaexToEfl -> CaexToSm).
 */
import { cp, mkdir, readdir, readFile, stat, writeFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { basename, dirname, extname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const [target, prefix, longName] = process.argv.slice(2);

if (!target || !prefix || !longName || !/^[A-Z][A-Za-z0-9]*$/.test(prefix)) {
  console.error('usage: node tools/new-language.mjs <target-folder> <Prefix> "<Long name>"');
  console.error('       <Prefix> in PascalCase, letters and digits, e.g. Sm');
  process.exit(2);
}

const here = dirname(fileURLToPath(import.meta.url));
const starter = resolve(here, '..', 'starter');
const destination = resolve(target);

if (existsSync(destination) && (await readdir(destination)).length > 0) {
  console.error(`${destination} exists and is not empty.`);
  process.exit(2);
}

/** Build output, installed packages and staged copies do not travel. */
const SKIP = new Set(['node_modules', 'bin', 'obj', 'dist', 'build', '.vs', 'TestResults', 'publish', 'screenshots']);
const STAGED = [/Efl\.Web[\\/]wwwroot[\\/]efl\.(esm\.js|css)$/, /Efl\.Web[\\/]wwwroot[\\/]examples$/];

const TEXT = new Set(['.cs', '.csproj', '.sln', '.xaml', '.xml', '.json', '.js', '.mjs', '.html', '.css', '.md', '.aml', '.yml', '.gitignore', '']);

const upper = prefix.toUpperCase();
const lower = prefix.toLowerCase();

function renameText(text) {
  return text
    .replaceAll('Example Flow Language', longName)
    .replace(/(?<![A-Za-z])EFL(?![a-z])/g, upper)
    .replace(/(?<![A-Z])Efl(?![a-z])/g, prefix)
    .replace(/(?<![A-Za-z])efl/g, lower);
}

const renameName = (name) =>
  name.replace(/(?<![A-Za-z])EFL(?![a-z])/g, upper).replace(/(?<![A-Z])Efl(?![a-z])/g, prefix).replace(/(?<![A-Za-z])efl/g, lower);

let files = 0;

async function copy(from, to) {
  for (const entry of await readdir(from, { withFileTypes: true })) {
    const source = join(from, entry.name);
    if (SKIP.has(entry.name) || STAGED.some((re) => re.test(source))) continue;

    // The AML examples carry CAEX ids derived from the starter's id namespace.
    // Renamed as text they would no longer match what the renamed mapper
    // writes, and the CI check that examples are current would fail on the
    // first run. They are written again by the commands printed below.
    if (basename(from) === 'examples' && entry.name.endsWith('.aml')) continue;

    const dest = join(to, renameName(entry.name));
    if (entry.isDirectory()) {
      await mkdir(dest, { recursive: true });
      await copy(source, dest);
      continue;
    }

    const extension = extname(entry.name) || (entry.name.startsWith('.') ? '' : extname(entry.name));
    if (TEXT.has(extension) && (await stat(source)).size < 5_000_000) {
      await writeFile(dest, renameText(await readFile(source, 'utf8')));
    } else {
      await cp(source, dest);
    }
    files++;
  }
}

await mkdir(destination, { recursive: true });
await copy(starter, destination);

// The starter's README describes EFL. Renamed, it would claim the new language
// has steps and stores. Replaced by an outline of what the new README needs.
await writeFile(join(destination, 'README.md'), `# ${longName}

AutomationML tooling for ${longName}: mapper, modeler, web app and AutomationML Editor plugin.

This README is an outline. Fill it in before the first release (recipe §6):

- What the language is, with a small example diagram.
- The Phase 1 decisions and their reasons: element and connection types, connection
  encoding, stored or computed layout, exchange format, cross-diagram references,
  embedded shared libraries, rules with severities.
- How to build, test and install (see the commands the copy script printed).
- The AutomationML Editor version the plugin was tested with.
- A LICENSE file and the author: the copy has neither. Replace "Your name" in
  plugin/*/*.csproj (Authors) and plugin/*/Metadata.xml (Author) before the first package.
- THIRD-PARTY-NOTICES.md is written by web/tools/notices.mjs; keep it with the package.
- The method it follows: the three-phase method for representing graphical description
  languages in AutomationML (Nabizada, Drath, Fay).
`);

// The examples hold CAEX ids derived from the old id namespace; they are
// written again from the JSON example once the new mapper builds.
console.log(`Copied ${files} files to ${destination}`);
console.log(`
Next, all of these have to pass before you change the language itself. The two
to-aml commands and the library command write the example AML files, which the
copy does not contain; commit them together with the rest.
  cd ${relative(process.cwd(), destination) || '.'}
  cd web && npm install && npm run build && npx playwright install chromium && npm test
  cd ../dotnet && dotnet test ${prefix}.sln
  dotnet run --project ${prefix}.Tool -- to-aml ../examples/bottling-line.json ../examples/bottling-line.links.aml --timestamp 2026-01-01T00:00:00Z
  dotnet run --project ${prefix}.Tool -- to-aml ../examples/bottling-line.json ../examples/bottling-line.elements.aml --style element --timestamp 2026-01-01T00:00:00Z
  dotnet run --project ${prefix}.Tool -- library ../examples/${upper}_DomainLibrary_v0.1.0.aml --timestamp 2026-01-01T00:00:00Z
  cd ../web && npm run test:webapp
  cd ../plugin && dotnet test

Then follow docs/10-new-language-recipe.md of the playbook from §1.`);
