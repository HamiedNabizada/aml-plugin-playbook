/** The modeler on its own: import, export, editing, rules, undo, SVG. */
import { assert, assertEqual, example, run } from './harness.mjs';

const sample = await example();

/** Imports the sample and returns what the page exports right after. */
const importSample = (page) => page.evaluate(async (model) => {
  const { warnings } = await window.efl.modeler.importModel(model);
  return { warnings, model: JSON.parse(await window.efl.modeler.exportModel()) };
}, sample);

await run('modeler', {
  async 'the page boots with an empty diagram'(page) {
    const model = await page.evaluate(async () => JSON.parse(await window.efl.modeler.exportModel()));
    assertEqual(model.nodes, [], 'nodes');
    assertEqual(model.flows, [], 'flows');
  },

  async 'a model survives import and export unchanged'(page) {
    const { warnings, model } = await importSample(page);
    assertEqual(warnings, [], 'warnings');
    assertEqual(model, sample, 'exported model');
  },

  async 'flow ends sit on the node outlines, not in their centres'(page) {
    await importSample(page);
    const ends = await page.evaluate(() => {
      const registry = window.efl.modeler.get('elementRegistry');
      const f1 = registry.get('f1').waypoints;
      return { start: f1[0], end: f1[f1.length - 1] };
    });
    // Fill is the box 0..100 x 0..60, Buffer the ellipse in 200..300 x 0..60.
    assertEqual({ x: Math.round(ends.start.x), y: Math.round(ends.start.y) }, { x: 100, y: 30 }, 'start on the box edge');
    assertEqual({ x: Math.round(ends.end.x), y: Math.round(ends.end.y) }, { x: 200, y: 30 }, 'end on the ellipse');
  },

  async 'a file from a newer format version is shown with a warning'(page) {
    const { warnings } = await page.evaluate(
      (model) => window.efl.modeler.importModel({ ...model, formatVersion: 2 }), sample);
    assertEqual(warnings.length, 1, 'warnings');
    assert(warnings[0].includes('format version 2'), warnings[0]);
  },

  async 'a broken file is read as far as possible and the rest is reported'(page) {
    const result = await page.evaluate(async () => window.efl.modeler.importModel({
      id: 'x',
      nodes: [
        { id: 'a', type: 'Step', bounds: { x: 0, y: 0, width: 0, height: 'wide' } },
        { id: 'a', type: 'Step' },
        { id: 'b', type: 'Machine' },
        { type: 'Store' },
      ],
      flows: [{ id: 'f', source: 'a', target: 'nowhere' }],
    }));
    assertEqual(result.warnings.length, 4, 'warnings: ' + result.warnings.join(' / '));
    const model = await page.evaluate(async () => JSON.parse(await window.efl.modeler.exportModel()));
    assertEqual(model.nodes.map((n) => [n.id, n.bounds.width, n.bounds.height]), [['a', 100, 60]], 'default size');
  },

  async 'a node can be dropped from the palette with the mouse'(page) {
    await page.click('.djs-palette [data-action="create-step"]');
    await page.mouse.move(600, 400);
    await page.mouse.click(600, 400);
    const nodes = await page.evaluate(async () => JSON.parse(await window.efl.modeler.exportModel()).nodes);
    assertEqual(nodes.map((n) => n.type), ['Step'], 'created nodes');
    assert(/^step-[0-9a-f]{8}$/.test(nodes[0].id), 'random id, got ' + nodes[0].id);
  },

  async 'a flow can be drawn with the palette tool and the mouse'(page) {
    await importSample(page);
    // Screen position of a node's centre, from the canvas's own viewbox.
    const centre = (id) => page.evaluate((elementId) => {
      const { modeler } = window.efl;
      const canvas = modeler.get('canvas');
      const element = modeler.get('elementRegistry').get(elementId);
      const box = canvas.viewbox();
      const rect = canvas._container.getBoundingClientRect();
      return {
        x: rect.left + (element.x + element.width / 2 - box.x) * box.scale,
        y: rect.top + (element.y + element.height / 2 - box.y) * box.scale,
      };
    }, id);

    const from = await centre('fill');
    const to = await centre('cap');
    await page.click('.djs-palette [data-action="global-connect-tool"]');
    await page.mouse.move(from.x, from.y);
    await page.mouse.down();
    await page.mouse.move((from.x + to.x) / 2, from.y + 40, { steps: 5 });
    await page.mouse.move(to.x, to.y, { steps: 5 });
    await page.mouse.up();

    const flows = await page.evaluate(async () => JSON.parse(await window.efl.modeler.exportModel()).flows);
    assertEqual(flows.length, 4, 'flows after drawing one');
    const drawn = flows[3];
    assertEqual([drawn.source, drawn.target], ['fill', 'cap'], 'ends of the drawn flow');
  },

  async 'rules allow a flow between two nodes and refuse one to itself'(page) {
    await importSample(page);
    const allowed = await page.evaluate(() => {
      const rules = window.efl.modeler.get('rules');
      const registry = window.efl.modeler.get('elementRegistry');
      const fill = registry.get('fill');
      return {
        other: rules.allowed('connection.create', { source: fill, target: registry.get('cap') }),
        self: rules.allowed('connection.create', { source: fill, target: fill }),
        // Not a command: refused unless a rule answers, and the palette's flow
        // tool would silently do nothing.
        start: rules.allowed('connection.start', { source: fill }),
      };
    });
    assertEqual(allowed, { other: { type: 'efl:Flow' }, self: false, start: true }, 'rules');
  },

  async 'renaming, setting a value and undoing go through the command stack'(page) {
    await importSample(page);
    const steps = await page.evaluate(async () => {
      const { modeler } = window.efl;
      const registry = modeler.get('elementRegistry');
      const directEditing = modeler.get('directEditing');
      const stack = modeler.get('commandStack');
      const name = async () => JSON.parse(await modeler.exportModel()).nodes[0];

      const fill = registry.get('fill');
      directEditing.activate(fill);
      directEditing._textbox.content.innerText = 'Fill bottles';
      directEditing.complete();
      const renamed = await name();

      modeler.get('eflContextPad').editValue(fill, '15');
      const valued = await name();

      directEditing.activate(fill);
      directEditing._textbox.content.innerText = '';
      directEditing.complete();
      const cleared = await name();

      stack.undo(); stack.undo(); stack.undo();
      const undone = await name();
      return { renamed: renamed.name, value: valued.value, cleared: 'name' in cleared, undone: [undone.name, undone.value] };
    });
    assertEqual(steps, { renamed: 'Fill bottles', value: 15, cleared: false, undone: ['Fill', 12] }, 'edits');
  },

  async 'deleting a node removes its flows and nothing else'(page) {
    await importSample(page);
    const model = await page.evaluate(async () => {
      const { modeler } = window.efl;
      modeler.get('modeling').removeElements([modeler.get('elementRegistry').get('buffer')]);
      return JSON.parse(await modeler.exportModel());
    });
    assertEqual(model.nodes.map((n) => n.id), ['fill', 'cap'], 'nodes');
    assertEqual(model.flows.map((f) => f.id), ['f3'], 'flows');
  },

  async 'moving a node keeps its flows attached'(page) {
    await importSample(page);
    const f1 = await page.evaluate(async () => {
      const { modeler } = window.efl;
      const registry = modeler.get('elementRegistry');
      modeler.get('modeling').moveElements([registry.get('buffer')], { x: 0, y: 100 });
      return registry.get('f1').waypoints.map((p) => ({ x: Math.round(p.x), y: Math.round(p.y) }));
    });
    const last = f1[f1.length - 1];
    // The ellipse now spans y 100..160; the flow has to end on it.
    assert(last.x >= 200 && last.x <= 300 && last.y >= 100 && last.y <= 160, 'end point ' + JSON.stringify(last));
  },

  async 'the SVG picture has the drawing and none of the editor furniture'(page) {
    await importSample(page);
    const svg = await page.evaluate(async () => (await window.efl.modeler.saveSVG()).svg);
    for (const name of ['Fill', 'Buffer', 'Cap']) assert(svg.includes(`>${name}<`), 'label ' + name);
    assert(!svg.includes('djs-hit') && !svg.includes('djs-outline'), 'hit areas or outlines in the picture');
    assert(svg.includes('marker'), 'arrow heads');
  },
});
