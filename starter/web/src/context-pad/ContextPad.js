import { UPDATE_PROPERTIES } from '../modeling/index.js';
import { isNode, valueLabel } from '../types.js';
import { icon } from '../icons.js';

/**
 * The small menu next to a selected element.
 *
 * The value entry asks with a plain prompt. That is enough for one number per
 * element; a language with more attributes needs a properties panel, and the
 * edit still goes through the same command.
 */
export default class ContextPad {
  constructor(contextPad, modeling, connect, rules, commandStack) {
    this._modeling = modeling;
    this._connect = connect;
    this._rules = rules;
    this._commandStack = commandStack;

    contextPad.registerProvider(this);
  }

  getContextPadEntries(element) {
    const entries = {};

    if (isNode(element)) {
      entries.connect = {
        group: 'connect',
        imageUrl: icon('flow'),
        title: 'Draw a flow from here',
        action: {
          click: (event, target) => this._connect.start(event, target),
          dragstart: (event, target) => this._connect.start(event, target),
        },
      };

      entries.value = {
        group: 'edit',
        imageUrl: icon('value'),
        title: `Set ${valueLabel(element.type).toLowerCase()}`,
        action: { click: () => this.editValue(element) },
      };
    }

    if (this._rules.allowed('elements.delete', { elements: [element] })) {
      entries.delete = {
        group: 'edit',
        imageUrl: icon('delete'),
        title: 'Delete',
        action: { click: () => this._modeling.removeElements([element]) },
      };
    }

    return entries;
  }

  /** Asks for a value and stores it; exposed so a test can call it directly. */
  editValue(element, answer) {
    const current = element.businessObject.value;
    const text = answer !== undefined
      ? answer
      : window.prompt(valueLabel(element.type), current === undefined ? '' : String(current));
    if (text === null) return;

    const trimmed = String(text).trim();
    const value = trimmed === '' ? undefined : Number(trimmed.replace(',', '.'));
    if (value !== undefined && !Number.isFinite(value)) return;

    this._commandStack.execute(UPDATE_PROPERTIES, { element, properties: { value } });
  }
}

ContextPad.$inject = ['contextPad', 'modeling', 'connect', 'rules', 'commandStack'];
