import BaseRenderer from 'diagram-js/lib/draw/BaseRenderer';
import { append as svgAppend, attr as svgAttr, create as svgCreate } from 'tiny-svg';
import { componentsToPath, createLine } from 'diagram-js/lib/util/RenderUtil';
import Text from 'diagram-js/lib/util/Text';

import { FLOW, STEP, STORE } from '../types.js';

const STROKE = { stroke: '#222', strokeWidth: 2 };
const ARROW_ID = 'efl-arrow';

/**
 * Written onto every text element, so an exported SVG looks the same without
 * the page's stylesheet.
 */
const LABEL_STYLE = { fontFamily: 'Arial, sans-serif', fontSize: 12, fill: '#222' };

/**
 * Draws the elements of the language.
 *
 * A renderer answers three questions for diagram-js: can it draw an element,
 * how does the element look, and what is the outline of the element. The
 * outline (getShapePath) is what connection docking crops an arrow against,
 * so it has to match the drawing, or arrows end inside or before the shape.
 *
 * The label is drawn inside the shape here. A language whose names are long
 * should draw them below the shape instead; a word broken in the middle of a
 * narrow box is the first complaint a user makes.
 */
export default class Renderer extends BaseRenderer {
  constructor(eventBus, canvas) {
    // Higher priority than the default renderer, so these types are ours.
    super(eventBus, 2000);
    this._canvas = canvas;
    // diagram-js's text layout. bpmn-js wraps it in a 'textRenderer' service;
    // plain diagram-js has no such service, so the renderer owns one.
    this._text = new Text({ style: LABEL_STYLE });
    this._markerAdded = false;
  }

  canRender(element) {
    return [STEP, STORE, FLOW].includes(element.type);
  }

  drawShape(parent, element) {
    const shape = element.type === STORE
      ? svgCreate('ellipse', {
        cx: element.width / 2,
        cy: element.height / 2,
        rx: element.width / 2,
        ry: element.height / 2,
        fill: '#fff',
        ...STROKE,
      })
      : svgCreate('rect', {
        width: element.width,
        height: element.height,
        rx: 6,
        ry: 6,
        fill: '#fff',
        ...STROKE,
      });
    svgAppend(parent, shape);

    const bo = element.businessObject || {};
    const lines = [bo.name || ''];
    if (bo.value !== undefined && bo.value !== null && bo.value !== '') lines.push(String(bo.value));

    const text = this._text.createText(lines.join('\n'), {
      box: { width: element.width, height: element.height },
      align: 'center-middle',
      padding: 4,
    });
    svgAppend(parent, text);

    return shape;
  }

  drawConnection(parent, element) {
    this._ensureArrow();
    const line = createLine(element.waypoints, {
      ...STROKE,
      fill: 'none',
      markerEnd: `url(#${ARROW_ID})`,
    });
    svgAppend(parent, line);
    return line;
  }

  getShapePath(element) {
    const { x, y, width, height } = element;

    if (element.type === STORE) {
      const rx = width / 2;
      const ry = height / 2;
      return componentsToPath([
        ['M', x + rx, y],
        ['a', rx, ry, 0, 1, 1, 0, 2 * ry],
        ['a', rx, ry, 0, 1, 1, 0, -2 * ry],
        ['z'],
      ]);
    }

    return componentsToPath([
      ['M', x, y],
      ['l', width, 0],
      ['l', 0, height],
      ['l', -width, 0],
      ['z'],
    ]);
  }

  getConnectionPath(connection) {
    const [first, ...rest] = connection.waypoints;
    return componentsToPath([['M', first.x, first.y], ...rest.map((p) => ['L', p.x, p.y])]);
  }

  /** One arrow head, defined once in the canvas's defs. */
  _ensureArrow() {
    if (this._markerAdded) return;
    this._markerAdded = true;

    const svg = this._canvas._svg;
    let defs = svg.querySelector('defs');
    if (!defs) {
      defs = svgCreate('defs');
      svgAppend(svg, defs);
    }

    const marker = svgCreate('marker', {
      id: ARROW_ID,
      viewBox: '0 0 10 10',
      refX: 10,
      refY: 5,
      markerWidth: 5,
      markerHeight: 5,
      orient: 'auto',
    });
    const head = svgCreate('path', { d: 'M 0 0 L 10 5 L 0 10 z', fill: '#222' });
    svgAppend(marker, head);
    svgAppend(defs, marker);
    svgAttr(marker, { markerUnits: 'strokeWidth' });
  }
}

Renderer.$inject = ['eventBus', 'canvas'];
