/**
 * Shared test harness: serves dist/ over http (module scripts do not load from
 * file://), opens it in headless Chromium and collects page errors.
 *
 * The checks drive diagram-js through its services (window.efl.modeler.get)
 * instead of clicking at pixel positions. Pixel clicks depend on zoom, palette
 * width and fonts and break on another machine; services do not. One check
 * per file still uses the real mouse, so the wiring of palette and canvas is
 * covered too.
 */
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { dirname, extname, join, normalize, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

const here = dirname(fileURLToPath(import.meta.url));
export const dist = resolve(here, '..', 'dist');
export const examples = resolve(here, '..', '..', 'examples');

const types = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css' };

function serve() {
  const server = createServer(async (req, res) => {
    const path = decodeURIComponent(new URL(req.url, 'http://x').pathname).replace(/^\/+/, '');
    const file = path === '' ? join(dist, 'index.html') : join(dist, normalize(path));
    if (!file.startsWith(dist)) return res.writeHead(403).end();
    try {
      res.writeHead(200, { 'content-type': types[extname(file)] ?? 'application/octet-stream' })
        .end(await readFile(file));
    } catch {
      res.writeHead(404).end();
    }
  });
  return new Promise((ok) => server.listen(0, '127.0.0.1', () => ok(server)));
}

/** Runs the named checks against a fresh page each and exits non-zero on failure. */
export async function run(title, checks) {
  const server = await serve();
  const browser = await chromium.launch();
  let failed = 0;

  for (const [name, check] of Object.entries(checks)) {
    const page = await browser.newPage({ viewport: { width: 1200, height: 800 } });
    const errors = [];
    page.on('pageerror', (e) => errors.push(e.message));
    page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });

    // Record everything the page posts to a host before any page script runs.
    await page.addInitScript(() => {
      window.__posted = [];
      window.addEventListener('efl-bridge-out', (e) => window.__posted.push(e.detail));
    });

    try {
      await page.goto(`http://127.0.0.1:${server.address().port}/`);
      await page.waitForFunction(() => window.efl?.modeler, null, { timeout: 10000 });
      await check(page);
      if (errors.length) throw new Error('page errors: ' + errors.join(' | '));
      console.log(`  ok    ${name}`);
    } catch (e) {
      failed++;
      console.log(`  FAIL  ${name}\n        ${String(e.stack || e).split('\n').join('\n        ')}`);
    } finally {
      await page.close();
    }
  }

  await browser.close();
  server.close();
  console.log(`${title}: ${Object.keys(checks).length - failed} passed, ${failed} failed`);
  if (failed) process.exit(1);
}

export function assert(condition, message) {
  if (!condition) throw new Error(message);
}

export function assertEqual(actual, expected, message) {
  const a = JSON.stringify(actual);
  const e = JSON.stringify(expected);
  if (a !== e) throw new Error(`${message}\n  expected ${e}\n  actual   ${a}`);
}

export async function example(name = 'bottling-line.json') {
  return JSON.parse(await readFile(join(examples, name), 'utf8'));
}
