import { FLOW, isFlow, isNode, kindOfType, typeOfKind, NODE_SIZE } from '../types.js';

/**
 * Reads and writes the exchange format (see Efl.Conversion/EflJson.cs for the
 * other side, which is the reference for the shape of the file).
 *
 * The same rules as in the mapper hold here:
 * - the reader is tolerant and reports what it dropped as warnings;
 * - the writer is strict and deterministic: same diagram, same bytes;
 * - flows store bend points only; the end points on the node outlines are
 *   recomputed by the layouter, so a moved node never leaves a stale end
 *   point in the file.
 */

/** The format version this code writes; see Efl.Conversion/EflJson.cs. */
export const FORMAT_VERSION = 1;

/** Places a model on an empty canvas. Returns the warnings. */
export function importModel(diagram, model) {
  const canvas = diagram.get('canvas');
  const elementFactory = diagram.get('elementFactory');
  const layouter = diagram.get('layouter');

  const warnings = [];
  if (!model || typeof model !== 'object') throw new Error('The model has to be a JSON object.');

  // A file without the field predates it and is version 1.
  const version = finite(model.formatVersion) ?? 1;
  if (version > FORMAT_VERSION) {
    warnings.push(`The file is format version ${version}; this modeler knows version ${FORMAT_VERSION}. `
      + 'Anything a newer version added is not shown and would be lost on saving.');
  }

  // An explicit root carries the diagram's own id and name. The implicit root
  // canvas creates on demand has no business object at all.
  const businessObject = { id: text(model.id) ?? 'diagram' };
  if (text(model.name)) businessObject.name = text(model.name);
  const root = elementFactory.createRoot({ id: '__root_' + businessObject.id, businessObject });
  canvas.setRootElement(root);

  const shapes = new Map();

  // Shapes first: a connection can only be added once both ends are on the
  // canvas.
  for (const node of array(model.nodes)) {
    const id = text(node?.id);
    if (!id) { warnings.push('A node without an id was left out.'); continue; }
    if (shapes.has(id)) { warnings.push(`The id '${id}' is used more than once; the later node was left out.`); continue; }
    if (!['step', 'store'].includes(String(node.type).toLowerCase())) {
      warnings.push(`Node '${id}' has the unknown type '${node.type}' and was left out.`);
      continue;
    }

    const bounds = node.bounds || {};
    const x = finite(bounds.x);
    const y = finite(bounds.y);
    if (x === undefined || y === undefined) warnings.push(`Node '${id}' has no position and was placed at the origin.`);

    const businessObject = { id };
    if (text(node.name)) businessObject.name = text(node.name);
    if (finite(node.value) !== undefined) businessObject.value = finite(node.value);

    const shape = elementFactory.createShape({
      id,
      type: typeOfKind(String(node.type).toLowerCase() === 'store' ? 'Store' : 'Step'),
      x: x ?? 0,
      y: y ?? 0,
      width: positive(bounds.width) ?? NODE_SIZE.width,
      height: positive(bounds.height) ?? NODE_SIZE.height,
      businessObject,
    });
    canvas.addShape(shape, root);
    shapes.set(id, shape);
  }

  const flowIds = new Set();
  for (const flow of array(model.flows)) {
    const id = text(flow?.id);
    const source = shapes.get(text(flow?.source));
    const target = shapes.get(text(flow?.target));
    if (!id) { warnings.push('A flow without an id was left out.'); continue; }
    if (flowIds.has(id) || shapes.has(id)) { warnings.push(`The id '${id}' is used more than once; flow left out.`); continue; }
    if (!source || !target) {
      warnings.push(`Flow '${id}' runs between nodes the model does not have and was left out.`);
      continue;
    }

    const businessObject = { id };
    if (text(flow.name)) businessObject.name = text(flow.name);

    const bends = array(flow.waypoints)
      .map((p) => ({ x: finite(p?.x), y: finite(p?.y) }))
      .filter((p) => p.x !== undefined && p.y !== undefined);

    const connection = elementFactory.createConnection({ id, type: FLOW, source, target, businessObject });
    connection.waypoints = layouter.layoutConnection(connection, {
      source,
      target,
      waypoints: [centre(source), ...bends, centre(target)],
    });
    canvas.addConnection(connection, root);
    flowIds.add(id);
  }

  return warnings;
}

/** The diagram as a model object in the exchange format. */
export function exportModel(diagram) {
  const canvas = diagram.get('canvas');
  const elementRegistry = diagram.get('elementRegistry');
  const root = canvas.getRootElement().businessObject;

  const model = { formatVersion: FORMAT_VERSION, id: root.id || 'diagram' };
  if (root.name) model.name = root.name;

  model.nodes = elementRegistry.filter(isNode).map((shape) => {
    const bo = shape.businessObject;
    const node = { id: shape.id, type: kindOfType(shape.type) };
    if (bo.name) node.name = bo.name;
    if (bo.value !== undefined) node.value = bo.value;
    node.bounds = {
      x: round(shape.x),
      y: round(shape.y),
      width: round(shape.width),
      height: round(shape.height),
    };
    return node;
  });

  model.flows = elementRegistry.filter(isFlow).map((connection) => {
    const bo = connection.businessObject;
    const flow = { id: connection.id, source: connection.source.id, target: connection.target.id };
    if (bo.name) flow.name = bo.name;
    const bends = connection.waypoints.slice(1, -1);
    if (bends.length) flow.waypoints = bends.map((p) => ({ x: round(p.x), y: round(p.y) }));
    return flow;
  });

  return model;
}

const array = (value) => (Array.isArray(value) ? value : []);
const text = (value) => {
  if (value === undefined || value === null) return undefined;
  const s = String(value);
  return s.trim() === '' ? undefined : s;
};
const finite = (value) => {
  if (value === null || value === '' || value === undefined) return undefined;
  const n = Number(value);
  return Number.isFinite(n) ? n : undefined;
};
const positive = (value) => {
  const n = finite(value);
  return n !== undefined && n > 0 ? n : undefined;
};
const centre = (shape) => ({ x: shape.x + shape.width / 2, y: shape.y + shape.height / 2 });

/** Two decimals: short files, no visible movement, no 0.30000000000000004. */
const round = (value) => Math.round(value * 100) / 100;
