/**
 * Changes attributes of an element's business object through the command
 * stack.
 *
 * Writing `element.businessObject.name = ...` directly works on screen and
 * breaks everything around it: undo does not know the change, the shape is
 * not redrawn, and `commandStack.changed` never fires, so the host is not told
 * that the model changed and the edit never reaches the AML document. Every
 * change a user makes goes through a command.
 */
export default class UpdatePropertiesHandler {
  constructor(eventBus) {
    this._eventBus = eventBus;
  }

  execute(context) {
    const { element, properties } = context;
    const bo = element.businessObject;

    context.oldProperties = {};
    for (const key of Object.keys(properties)) {
      context.oldProperties[key] = bo[key];
      assignOrDelete(bo, key, properties[key]);
    }

    return [element];
  }

  revert(context) {
    const { element, oldProperties } = context;
    const bo = element.businessObject;

    for (const key of Object.keys(oldProperties)) {
      assignOrDelete(bo, key, oldProperties[key]);
    }

    return [element];
  }
}

UpdatePropertiesHandler.$inject = ['eventBus'];

/**
 * An empty value removes the attribute. A name that was cleared is no name,
 * not an empty string: an empty string is written to the file, shows up as an
 * empty label and survives into AML as an attribute without meaning.
 */
function assignOrDelete(target, key, value) {
  if (value === undefined || value === null || value === '') delete target[key];
  else target[key] = value;
}
