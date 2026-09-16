/**
 * The element types of the language, as diagram-js sees them, and their
 * default sizes.
 *
 * Keep the names of the exchange format and the names on the canvas in one
 * place. A type string spelled differently in two files is a shape that
 * renders as nothing and exports as nothing, without an error.
 */
export const STEP = 'efl:Step';
export const STORE = 'efl:Store';
export const FLOW = 'efl:Flow';

export const NODE_TYPES = [STEP, STORE];

/** Default size of a node, the same the mapper uses when a file gives none. */
export const NODE_SIZE = { width: 100, height: 60 };

/** The type on the canvas for a node kind in the exchange format, and back. */
export const typeOfKind = (kind) => (kind === 'Store' ? STORE : STEP);
export const kindOfType = (type) => (type === STORE ? 'Store' : 'Step');

export const isNode = (element) => NODE_TYPES.includes(element?.type);
export const isFlow = (element) => element?.type === FLOW;

/** The attribute a node's value means, for labels and tooltips. */
export const valueLabel = (type) => (type === STORE ? 'Capacity' : 'Duration (s)');
