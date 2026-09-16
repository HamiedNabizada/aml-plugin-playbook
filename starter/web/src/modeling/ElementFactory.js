import BaseElementFactory from 'diagram-js/lib/core/ElementFactory';

import { FLOW, NODE_SIZE, isNode } from '../types.js';

/**
 * Creates canvas elements with an id and a business object of their own.
 *
 * The default factory names new elements "<type>_<counter>", starting at the
 * same counter every time the page loads. A diagram imported from a file can
 * already contain such an id, and the element registry refuses a second
 * element with the same id: the user drops a shape and nothing happens. Ids
 * here are random, so a new element never collides with a stored one.
 *
 * The business object is a plain object. diagram-js does not care what it is;
 * a moddle schema (as bpmn-js and FPB.JS use) only pays off once a language
 * has an XML format of its own to read and write.
 */
export default class ElementFactory extends BaseElementFactory {
  create(elementType, attrs = {}) {
    const type = attrs.type || (elementType === 'connection' ? FLOW : undefined);
    const withDefaults = { ...attrs };
    if (type) withDefaults.type = type;

    if (!withDefaults.id) withDefaults.id = newId(type);
    if (!withDefaults.businessObject) withDefaults.businessObject = {};
    if (withDefaults.businessObject.id === undefined) withDefaults.businessObject.id = withDefaults.id;

    if (elementType === 'shape' && isNode(withDefaults)) {
      if (withDefaults.width === undefined) withDefaults.width = NODE_SIZE.width;
      if (withDefaults.height === undefined) withDefaults.height = NODE_SIZE.height;
    }

    return super.create(elementType, withDefaults);
  }
}

/** A short random id, prefixed with the local type name for readable files. */
export function newId(type) {
  const prefix = (type || 'element').split(':').pop().toLowerCase();
  const random = globalThis.crypto?.randomUUID
    ? globalThis.crypto.randomUUID().slice(0, 8)
    : Math.random().toString(16).slice(2, 10);
  return `${prefix}-${random}`;
}
