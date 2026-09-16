import ModelingModule from 'diagram-js/lib/features/modeling';
import CroppingConnectionDocking from 'diagram-js/lib/layout/CroppingConnectionDocking';

import ElementFactory from './ElementFactory.js';
import Layouter from './Layouter.js';
import UpdatePropertiesHandler from './UpdatePropertiesHandler.js';

export const UPDATE_PROPERTIES = 'efl.updateProperties';

/** Registers the language's command handlers once the command stack exists. */
function registerHandlers(commandStack) {
  commandStack.registerHandler(UPDATE_PROPERTIES, UpdatePropertiesHandler);
}
registerHandlers.$inject = ['commandStack'];

export default {
  __depends__: [ModelingModule],
  __init__: [registerHandlers],
  // Same names as the core services, so these replace them.
  elementFactory: ['type', ElementFactory],
  layouter: ['type', Layouter],
  connectionDocking: ['type', CroppingConnectionDocking],
};
