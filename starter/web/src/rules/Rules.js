import RuleProvider from 'diagram-js/lib/features/rules/RuleProvider';

import { FLOW, isFlow, isNode } from '../types.js';

/**
 * What the canvas allows.
 *
 * How diagram-js decides: an action that is a command (shape.create,
 * elements.move, ...) is allowed when no rule answers, as long as a handler
 * exists. An action that is not a command, such as `connection.start` for the
 * global connect tool, is refused when no rule answers: without the rule below
 * the palette's flow tool does nothing. And a rule that returns `undefined`
 * counts as "no answer", so return `false` to forbid. Each rule returns true,
 * false, or (for connections) the attributes of the connection to create.
 *
 * The structural rules of the language are enforced twice on purpose: here,
 * so a user cannot draw a wrong diagram, and in the mapper's validator
 * (Efl.Conversion/EflValidator.cs), because a file from another tool never saw
 * these rules.
 */
export default class Rules extends RuleProvider {
  constructor(eventBus) {
    super(eventBus);
  }

  init() {
    // Nodes sit directly on the root. A language with containers (FPB's system
    // limit, a BPMN pool) decides here what may go inside what.
    this.addRule('shape.create', ({ shape, target }) => isNode(shape) && isRoot(target));

    this.addRule('elements.move', ({ shapes, target }) =>
      shapes.every((s) => isNode(s) || isFlow(s)) && (!target || isRoot(target)));

    this.addRule('shape.resize', ({ shape }) => isNode(shape));

    // Asked by the global connect tool before a flow is started from a node.
    this.addRule('connection.start', ({ source }) => isNode(source));

    this.addRule('connection.create', ({ source, target }) => canConnect(source, target));

    this.addRule('connection.reconnect', ({ connection, source, target }) =>
      isFlow(connection) && Boolean(canConnect(source, target)));

    this.addRule('connection.updateWaypoints', ({ connection }) => isFlow(connection));

    this.addRule('elements.delete', () => true);
  }
}

Rules.$inject = ['eventBus'];

/**
 * A flow runs between two different nodes (validator rule EFL04). Parallel
 * flows are allowed and only warned about (EFL05), so they are not blocked.
 */
export function canConnect(source, target) {
  if (!isNode(source) || !isNode(target) || source === target) return false;
  return { type: FLOW };
}

function isRoot(element) {
  return Boolean(element) && !element.parent;
}
