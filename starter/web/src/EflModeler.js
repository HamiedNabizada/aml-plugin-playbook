import Diagram from 'diagram-js';

import BendpointsModule from 'diagram-js/lib/features/bendpoints';
import ConnectModule from 'diagram-js/lib/features/connect';
import ConnectionPreviewModule from 'diagram-js/lib/features/connection-preview';
import ContextPadModule from 'diagram-js/lib/features/context-pad';
import CreateModule from 'diagram-js/lib/features/create';
import EditorActionsModule from 'diagram-js/lib/features/editor-actions';
import GlobalConnectModule from 'diagram-js/lib/features/global-connect';
import HandToolModule from 'diagram-js/lib/features/hand-tool';
import KeyboardModule from 'diagram-js/lib/features/keyboard';
import LassoToolModule from 'diagram-js/lib/features/lasso-tool';
import MoveModule from 'diagram-js/lib/features/move';
import MoveCanvasModule from 'diagram-js/lib/navigation/movecanvas';
import OutlineModule from 'diagram-js/lib/features/outline';
import PaletteModule from 'diagram-js/lib/features/palette';
import ResizeModule from 'diagram-js/lib/features/resize';
import RulesModule from 'diagram-js/lib/features/rules';
import SelectionModule from 'diagram-js/lib/features/selection';
import SnappingModule from 'diagram-js/lib/features/snapping';
import ZoomScrollModule from 'diagram-js/lib/navigation/zoomscroll';
import DirectEditingModule from 'diagram-js-direct-editing';

import { innerSVG } from 'tiny-svg';

import ModelingModule from './modeling/index.js';
import Renderer from './draw/Renderer.js';
import Rules from './rules/Rules.js';
import Palette from './palette/Palette.js';
import ContextPad from './context-pad/ContextPad.js';
import LabelEditing from './label/LabelEditing.js';
import { exportModel, importModel } from './io/json.js';

/** The language's own parts, as one didi module. */
const EflModule = {
  __init__: ['eflRenderer', 'eflRules', 'eflPalette', 'eflContextPad', 'eflLabelEditing'],
  eflRenderer: ['type', Renderer],
  eflRules: ['type', Rules],
  eflPalette: ['type', Palette],
  eflContextPad: ['type', ContextPad],
  eflLabelEditing: ['type', LabelEditing],
};

/**
 * The modules a working editor needs. Leaving one out does not fail loudly:
 * without `rules` nothing can be created, without `connection-preview` a flow
 * is drawn without feedback, without `keyboard` the delete key does nothing.
 */
const MODULES = [
  SelectionModule,
  OutlineModule,
  MoveCanvasModule,
  ZoomScrollModule,
  KeyboardModule,
  EditorActionsModule,
  RulesModule,
  ModelingModule,
  MoveModule,
  ResizeModule,
  SnappingModule,
  CreateModule,
  ConnectModule,
  ConnectionPreviewModule,
  BendpointsModule,
  GlobalConnectModule,
  LassoToolModule,
  HandToolModule,
  PaletteModule,
  ContextPadModule,
  DirectEditingModule,
  EflModule,
];

/**
 * A diagram-js editor for EFL with an import, an export and an SVG export.
 *
 * Diagram is diagram-js's plain editor shell; bpmn-js and FPB.JS put the same
 * import/export surface around it. Keeping the surface small (importModel,
 * exportModel, saveSVG, get, on) is what lets the bridge and the tests stay
 * identical when the language changes.
 */
export default class EflModeler extends Diagram {
  constructor(options = {}) {
    super({
      canvas: { container: options.container },
      modules: [...MODULES, ...(options.additionalModules || [])],
    });
  }

  /** Replaces the diagram. Resolves with the reader's warnings. */
  async importModel(model) {
    if (typeof model === 'string') model = JSON.parse(model);

    const eventBus = this.get('eventBus');
    eventBus.fire('import.start', { model });

    this.clear();
    let warnings;
    try {
      warnings = importModel(this, model);
    } finally {
      // An import is not an edit: nothing to undo into.
      this.get('commandStack').clear();
    }

    this.get('canvas').zoom('fit-viewport', 'auto');
    eventBus.fire('import.done', { warnings });
    return { warnings };
  }

  /** An empty diagram with a fresh id. */
  async createNew(name) {
    return this.importModel({ id: 'diagram', name, nodes: [], flows: [] });
  }

  /** The current diagram in the exchange format, as a string. */
  async exportModel() {
    return JSON.stringify(exportModel(this), null, 2);
  }

  /**
   * The diagram as a standalone SVG picture, with a margin so strokes and
   * labels at the edge are not cut off.
   */
  async saveSVG(margin = 10) {
    const canvas = this.get('canvas');
    const layer = canvas.getActiveLayer();
    const box = layer.getBBox();
    const defs = canvas._svg.querySelector('defs');

    const x = box.x - margin;
    const y = box.y - margin;
    const width = box.width + 2 * margin;
    const height = box.height + 2 * margin;

    // The layer also holds editor furniture: invisible hit areas, selection
    // outlines and bend point handles. They are styled by the page's CSS, which
    // does not travel with the file, so outside the editor they show up as
    // black boxes. Only the drawing goes into the picture.
    const copy = layer.cloneNode(true);
    copy.querySelectorAll('.djs-hit, .djs-outline, .djs-bendpoints, .djs-segment-dragger, .djs-visual.djs-dragger')
      .forEach((node) => node.remove());

    const svg = '<?xml version="1.0" encoding="utf-8"?>\n'
      + `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" `
      + `viewBox="${x} ${y} ${width} ${height}" version="1.1">`
      + (defs ? `<defs>${innerSVG(defs)}</defs>` : '')
      + innerSVG(copy)
      + '</svg>';

    return { svg };
  }
}
