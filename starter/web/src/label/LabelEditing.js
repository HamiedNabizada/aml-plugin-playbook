import { UPDATE_PROPERTIES } from '../modeling/index.js';
import { isNode } from '../types.js';

/**
 * Edits a node's name in place on double click.
 *
 * diagram-js-direct-editing draws the text box; a provider tells it which
 * elements are editable (`activate` returns the box and the current text, or
 * nothing) and receives the result (`update`). The update goes through the
 * command stack, see UpdatePropertiesHandler for why.
 */
export default class LabelEditing {
  constructor(eventBus, canvas, directEditing, commandStack) {
    this._canvas = canvas;
    this._commandStack = commandStack;

    directEditing.registerProvider(this);

    eventBus.on('element.dblclick', (event) => {
      if (isNode(event.element)) directEditing.activate(event.element);
    });

    // Leave the editor before anything else happens to the diagram, so a half
    // typed name is not applied to an element that was deleted meanwhile.
    eventBus.on(['commandStack.changed', 'drag.init', 'canvas.viewbox.changing', 'autoPlace'], () => {
      if (directEditing.isActive()) directEditing.complete();
    });
  }

  activate(element) {
    if (!isNode(element)) return undefined;

    const zoom = this._canvas.zoom();
    const { x, y } = this._canvas.viewbox();
    return {
      text: element.businessObject.name || '',
      bounds: {
        x: (element.x - x) * zoom,
        y: (element.y - y) * zoom,
        width: element.width * zoom,
        height: element.height * zoom,
      },
      style: { fontSize: `${12 * zoom}px`, textAlign: 'center' },
      options: { centerVertically: true },
    };
  }

  update(element, newText) {
    const name = newText.trim();
    if ((element.businessObject.name || '') === name) return;
    this._commandStack.execute(UPDATE_PROPERTIES, { element, properties: { name } });
  }
}

LabelEditing.$inject = ['eventBus', 'canvas', 'directEditing', 'commandStack'];
