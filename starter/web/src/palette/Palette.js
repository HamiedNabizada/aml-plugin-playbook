import { STEP, STORE } from '../types.js';
import { icon } from '../icons.js';

/**
 * The entries on the left edge of the canvas.
 *
 * `dragstart` and `click` both start the create tool, so an entry works by
 * dragging it onto the canvas and by clicking it and then clicking the canvas.
 * Offering only one of the two is a common source of "the palette does not
 * work" reports.
 */
export default class Palette {
  constructor(palette, create, elementFactory, lassoTool, handTool, globalConnect) {
    this._create = create;
    this._elementFactory = elementFactory;
    this._lassoTool = lassoTool;
    this._handTool = handTool;
    this._globalConnect = globalConnect;

    palette.registerProvider(this);
  }

  getPaletteEntries() {
    const startCreate = (type) => (event) => {
      const shape = this._elementFactory.createShape({ type });
      this._create.start(event, shape);
    };

    return {
      'hand-tool': {
        group: 'tools',
        imageUrl: icon('hand'),
        title: 'Move the canvas',
        action: { click: (event) => this._handTool.activateHand(event) },
      },
      'lasso-tool': {
        group: 'tools',
        imageUrl: icon('lasso'),
        title: 'Select several elements',
        action: { click: (event) => this._lassoTool.activateSelection(event) },
      },
      'global-connect-tool': {
        group: 'tools',
        imageUrl: icon('flow'),
        title: 'Draw a flow',
        action: { click: (event) => this._globalConnect.start(event) },
      },
      'tool-separator': { group: 'tools', separator: true },
      'create-step': {
        group: 'elements',
        imageUrl: icon('step'),
        title: 'Step',
        action: { dragstart: startCreate(STEP), click: startCreate(STEP) },
      },
      'create-store': {
        group: 'elements',
        imageUrl: icon('store'),
        title: 'Store',
        action: { dragstart: startCreate(STORE), click: startCreate(STORE) },
      },
    };
  }
}

Palette.$inject = ['palette', 'create', 'elementFactory', 'lassoTool', 'handTool', 'globalConnect'];
