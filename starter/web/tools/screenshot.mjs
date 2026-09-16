/**
 * Pictures of the modeler, for looking at what the tests cannot see.
 *
 *   node tools/screenshot.mjs                       the example, as stored
 *   node tools/screenshot.mjs model.json            another model
 *   node tools/screenshot.mjs --arranged            the example with its layout removed
 *                                                   and arranged again by the mapper
 *   node tools/screenshot.mjs model.json --arranged
 *
 * Writes screenshots/<name>.png and screenshots/<name>.svg (the SVG export).
 *
 * Why this exists: in the trial run of the playbook, twenty green browser tests
 * passed on a diagram whose arranged form was unusable (a back edge drawn on
 * top of a forward edge, a flow through a node) and on a label drawn far from
 * its line. The first screenshot showed both. Look at the pictures after every
 * change to the renderer, the layouter or the layout code.
 *
 * --arranged needs the .NET SDK: the layout lives in the mapper, and the browser
 * alone never runs it.
 */
import { spawnSync } from 'node:child_process';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { basename, dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { tmpdir } from 'node:os';
import { examples, run } from './harness.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const arranged = args.includes('--arranged');
const file = resolve(args.find((a) => !a.startsWith('--')) ?? join(examples, 'bottling-line.json'));
const out = resolve(here, '..', 'screenshots');

let model = JSON.parse(await readFile(file, 'utf8'));
let name = basename(file, '.json');

if (arranged) {
  // Strip every piece of layout, then let the mapper place it again.
  for (const node of model.nodes ?? []) delete node.bounds;
  for (const flow of model.flows ?? []) delete flow.waypoints;

  const stripped = join(tmpdir(), `${name}-stripped.json`);
  const placed = join(tmpdir(), `${name}-arranged.json`);
  await writeFile(stripped, JSON.stringify(model, null, 2));

  const project = resolve(here, '..', '..', 'dotnet', 'Efl.Tool');
  const result = spawnSync('dotnet', ['run', '--project', project, '--', 'arrange', stripped, placed], { encoding: 'utf8' });
  if (result.status !== 0) {
    console.error(result.stdout + result.stderr);
    process.exit(1);
  }
  model = JSON.parse(await readFile(placed, 'utf8'));
  name += '-arranged';
}

await mkdir(out, { recursive: true });

await run('screenshot', {
  async [name](page) {
    const { warnings } = await page.evaluate((m) => window.efl.modeler.importModel(m), model);
    if (warnings.length) console.log('  warnings: ' + warnings.join(' | '));
    await page.screenshot({ path: join(out, `${name}.png`) });
    const svg = await page.evaluate(async () => (await window.efl.modeler.saveSVG()).svg);
    await writeFile(join(out, `${name}.svg`), svg);
    console.log(`  wrote ${join(out, name)}.png and .svg`);
  },
});
