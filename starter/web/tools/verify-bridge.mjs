/**
 * The bridge as a host sees it: which messages come back, and when.
 *
 * The host side is simulated by calling the bridge's handler directly and
 * reading what the page posted. The protocol failures that cost the most time
 * in the source projects were all about timing: a change reported during an
 * import, two imports interleaving, a change reported twice.
 */
import { assert, assertEqual, example, run } from './harness.mjs';

const sample = JSON.stringify(await example());

const posted = (page, type) => page.evaluate((t) => window.__posted.filter((m) => m.type === t), type);
const clearPosted = (page) => page.evaluate(() => { window.__posted.length = 0; });
const settle = (page) => page.waitForTimeout(600); // longer than the change debounce

await run('bridge', {
  async 'the page says it is ready'(page) {
    assertEqual((await posted(page, 'ready')).length, 1, 'ready messages');
  },

  async 'an import answers with the model as baseline and reports no change'(page) {
    await clearPosted(page);
    await page.evaluate((model) => window.efl.bridge.handle({ type: 'importModel', model }), sample);
    await settle(page);

    const imported = await posted(page, 'imported');
    assertEqual(imported.length, 1, 'imported messages');
    assertEqual(JSON.parse(imported[0].model), JSON.parse(sample), 'baseline');
    assertEqual((await posted(page, 'changed')).length, 0, 'changed messages during import');
  },

  async 'an edit is reported once, after the debounce'(page) {
    await page.evaluate((model) => window.efl.bridge.handle({ type: 'importModel', model }), sample);
    await settle(page);
    await clearPosted(page);

    await page.evaluate(() => {
      const { modeler } = window.efl;
      const registry = modeler.get('elementRegistry');
      const modeling = modeler.get('modeling');
      // Three quick edits, as a drag produces them.
      for (let i = 0; i < 3; i++) modeling.moveElements([registry.get('cap')], { x: 10, y: 0 });
    });
    assertEqual((await posted(page, 'changed')).length, 0, 'reported before the debounce');
    await settle(page);

    const changed = await posted(page, 'changed');
    assertEqual(changed.length, 1, 'changed messages');
    assertEqual(JSON.parse(changed[0].model).nodes[2].bounds.x, 430, 'moved position');
  },

  async 'two imports in a row end with the second and report no change'(page) {
    await clearPosted(page);
    const second = JSON.parse(sample);
    second.nodes = second.nodes.slice(0, 1);
    second.flows = [];

    await page.evaluate(([a, b]) => Promise.all([
      window.efl.bridge.handle({ type: 'importModel', model: a }),
      window.efl.bridge.handle({ type: 'importModel', model: b }),
    ]), [sample, JSON.stringify(second)]);
    await settle(page);

    const imported = await posted(page, 'imported');
    assertEqual(imported.length, 2, 'imported messages');
    assertEqual(JSON.parse(imported[1].model).nodes.map((n) => n.id), ['fill'], 'final canvas');
    assertEqual((await posted(page, 'changed')).length, 0, 'changed messages');
  },

  async 'a broken import is reported as an error and leaves the page working'(page) {
    await clearPosted(page);
    await page.evaluate(() => window.efl.bridge.handle({ type: 'importModel', model: '{ not json' }));
    const errors = await posted(page, 'error');
    assertEqual(errors.length, 1, 'error messages');
    // No acknowledgement: the host would take it as "the canvas shows the new
    // diagram" and drop its unsaved edits.
    assertEqual((await posted(page, 'imported')).length, 0, 'imported after a failed import');
    await page.evaluate((model) => window.efl.bridge.handle({ type: 'importModel', model }), sample);
    const imported = await posted(page, 'imported');
    assertEqual(JSON.parse(imported[imported.length - 1].model).nodes.length, 3, 'nodes after recovery');
  },

  async 'export, SVG and selection requests are answered'(page) {
    await page.evaluate((model) => window.efl.bridge.handle({ type: 'importModel', model }), sample);
    await clearPosted(page);
    await page.evaluate(async () => {
      await window.efl.bridge.handle({ type: 'requestExport' });
      await window.efl.bridge.handle({ type: 'requestSvg' });
      await window.efl.bridge.handle({ type: 'selectElement', id: 'buffer' });
    });

    assertEqual((await posted(page, 'changed')).length, 1, 'export answer');
    const svg = await posted(page, 'svg');
    assert(svg.length === 1 && svg[0].svg.startsWith('<?xml'), 'svg answer');
    const selected = await page.evaluate(() => window.efl.modeler.get('selection').get().map((e) => e.id));
    assertEqual(selected, ['buffer'], 'selection');
  },
});
