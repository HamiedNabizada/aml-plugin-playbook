/**
 * The web app end to end: starts Efl.Web, opens its page, and runs the API the
 * way the page does. The important check is the last one: an update through
 * the API keeps what somebody else put into the document.
 *
 * Needs the .NET SDK. Run after `npm run build` (Efl.Web stages the bundle).
 */
import { spawn, spawnSync } from 'node:child_process';
import { readFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';
import { assert, assertEqual } from './harness.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const project = resolve(here, '..', '..', 'dotnet', 'Efl.Web');
const port = 5299;
const base = `http://127.0.0.1:${port}`;

// Built first, then the assembly is started directly. `dotnet run` starts the
// app as a separate process that survives killing `dotnet run` itself, and a
// leftover server holds the port and locks the build output for the next run.
const build = spawnSync('dotnet', ['build', project, '--nologo', '-v', 'q'], { encoding: 'utf8' });
if (build.status !== 0) {
  console.log(build.stdout + build.stderr);
  process.exit(1);
}
const server = spawn('dotnet', [resolve(project, 'bin', 'Debug', 'net8.0', 'Efl.Web.dll'), '--urls', base], {
  cwd: project, // the content root, where wwwroot is found
  stdio: ['ignore', 'pipe', 'pipe'],
});
let serverOutput = '';
server.stdout.on('data', (d) => { serverOutput += d; });
server.stderr.on('data', (d) => { serverOutput += d; });

async function waitForServer() {
  for (let i = 0; i < 120; i++) {
    try {
      if ((await fetch(`${base}/api/health`)).ok) return;
    } catch { /* not up yet */ }
    await new Promise((r) => setTimeout(r, 1000));
  }
  throw new Error('Efl.Web did not start:\n' + serverOutput);
}

const post = async (path, body, type) => {
  const response = await fetch(base + path, { method: 'POST', headers: { 'content-type': type }, body });
  return { status: response.status, text: await response.text(), info: response.headers.get('X-Efl-Info') };
};

let failed = 0;
const check = async (name, fn) => {
  try {
    await fn();
    console.log(`  ok    ${name}`);
  } catch (e) {
    failed++;
    console.log(`  FAIL  ${name}\n        ${e.message}`);
  }
};

try {
  await waitForServer();
  const sample = await readFile(resolve(here, '..', '..', 'examples', 'bottling-line.json'), 'utf8');

  await check('the page loads the modeler and the example', async () => {
    const browser = await chromium.launch();
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', (e) => errors.push(e.message));
    await page.goto(base + '/');
    await page.waitForFunction(() => window.efl?.modeler);
    await page.click('#example');
    await page.waitForFunction(() => document.getElementById('status').textContent === 'Example loaded');
    const nodes = await page.evaluate(async () => JSON.parse(await window.efl.modeler.exportModel()).nodes.length);
    await browser.close();
    assertEqual(nodes, 3, 'nodes on the canvas');
    assertEqual(errors, [], 'page errors');
  });

  for (const style of ['link', 'element']) {
    await check(`JSON to AML and back is lossless (${style})`, async () => {
      const aml = await post(`/api/to-aml?style=${style}`, sample, 'application/json');
      assertEqual(aml.status, 200, aml.text);
      const json = await post('/api/to-json', aml.text, 'application/xml');
      assertEqual(json.status, 200, json.text);
      assertEqual(JSON.parse(json.text), JSON.parse(sample), 'model after the round trip');
    });
  }

  await check('an update keeps foreign content of the document', async () => {
    const aml = (await post('/api/to-aml', sample, 'application/json')).text;
    // Somebody else's hierarchy, as another tool or a user would add it.
    const foreign = '<InstanceHierarchy Name="Plant" ID="plant-1"><InternalElement Name="Pump" ID="pump-1"/></InstanceHierarchy>';
    const withForeign = aml.replace(/(<InstanceHierarchy )/, foreign + '$1');

    const model = JSON.parse(sample);
    model.nodes[0].name = 'Fill bottles';
    const updated = await post('/api/update', JSON.stringify({ aml: withForeign, model: JSON.stringify(model) }), 'application/json');

    assertEqual(updated.status, 200, updated.text);
    assert(updated.text.includes('Name="Pump"'), 'the foreign element is gone');
    assert(updated.text.includes('Fill bottles'), 'the rename did not arrive');
    assertEqual(JSON.parse(updated.info).updated > 0, true, 'summary header');
  });

  await check('the domain library and the layout library it references can both be downloaded', async () => {
    const library = await (await fetch(`${base}/api/library`)).text();
    const layout = await fetch(`${base}/api/library/layout`);
    assert(library.includes('OMG_DD_AttributeTypeLib_v0.1.aml'), 'the domain library does not reference the layout library');
    assertEqual(layout.status, 200, 'layout library status');
    assert((await layout.text()).includes('AttributeTypeLib Name="OMG_DD_AttributeTypeLib"'), 'layout library content');
  });

  await check('a broken file is a 400 with a message, not a 500', async () => {
    const result = await post('/api/to-json', '<not aml', 'application/xml');
    assertEqual(result.status, 400, result.text);
    assert(JSON.parse(result.text).error, 'no error message');
  });

  await check('validation names the rule and the element', async () => {
    const model = JSON.parse(sample);
    model.flows[0].target = model.flows[0].source;
    const result = JSON.parse((await post('/api/validate', JSON.stringify(model), 'application/json')).text);
    assert(result.findings.some((f) => f.rule === 'EFL04' && f.element === 'f1'), JSON.stringify(result.findings));
  });
} finally {
  server.kill();
}

console.log(`webapp: ${failed === 0 ? 'all passed' : failed + ' failed'}`);
process.exit(failed ? 1 : 0);
