/**
 * Message bridge between the modeler and a WebView2 host.
 *
 * The protocol is the one AMLPetriNet and AMLFPB.js use, with the payload
 * renamed to the language's exchange format. The C# side lives in
 * plugin/Aml.Editor.Plugin.Efl/Bridge.
 *
 * Host -> page:
 *   { type: 'importModel', model }   replace the diagram (model is a JSON string)
 *   { type: 'requestExport' }        answer with 'changed' carrying the current model
 *   { type: 'requestSvg' }           answer with 'svg'
 *   { type: 'selectElement', id }    select and scroll to an element
 *   { type: 'setTheme', theme }      'light' or 'dark', from the editor's theme
 *
 * Page -> host:
 *   { type: 'ready', url }            the page is loaded and listening
 *   { type: 'imported', model, warnings }  import finished; model is the echo baseline
 *   { type: 'changed', model }        a user edit (debounced)
 *   { type: 'svg', svg }
 *   { type: 'log', level, message }
 *   { type: 'error', message }
 *
 * Why 'imported' carries the model: the host compares later 'changed'
 * payloads with it. An import makes the canvas normalise the model (rounded
 * numbers, recomputed end points), and without a baseline the host would see
 * that normalisation as an edit and offer to write it into the document.
 */

const CHANGE_DEBOUNCE_MS = 300;

const nativeConsole = {
  log: console.log.bind(console),
  info: console.info.bind(console),
  warn: console.warn.bind(console),
  error: console.error.bind(console),
  debug: (console.debug ?? console.log).bind(console),
};

/** Sends a message to the host; a page opened in a normal browser has none. */
export function post(message) {
  try {
    window.chrome?.webview?.postMessage(message);
  } catch (e) {
    nativeConsole.error('postMessage failed', e);
  }
  window.dispatchEvent(new CustomEvent('efl-bridge-out', { detail: message }));
}

/**
 * Forwards console output and unhandled errors to the host log. Inside
 * WebView2 there is no visible console, and a script error otherwise ends as
 * an empty canvas with nothing in any log.
 */
export function installDiagnostics() {
  for (const level of Object.keys(nativeConsole)) {
    console[level] = (...args) => {
      try {
        post({ type: 'log', level, message: args.map(format).join(' ') });
      } catch { /* logging must never break the page */ }
      nativeConsole[level](...args);
    };
  }
  window.addEventListener('error', (e) =>
    post({ type: 'log', level: 'error', message: 'window.error: ' + (e.error?.stack || e.message) }));
  window.addEventListener('unhandledrejection', (e) =>
    post({ type: 'log', level: 'error', message: 'unhandledRejection: ' + (e.reason?.stack || e.reason) }));
}

function format(value) {
  if (value instanceof Error) return value.stack || value.message;
  if (typeof value === 'string') return value;
  try { return JSON.stringify(value); } catch { return String(value); }
}

/** Connects a modeler to the host. `source` is where host messages come from. */
export function connectBridge(modeler, source = window.chrome?.webview) {
  const eventBus = modeler.get('eventBus');
  const canvas = modeler.get('canvas');
  const selection = modeler.get('selection');
  const elementRegistry = modeler.get('elementRegistry');

  // An import clears the command stack, which fires 'commandStack.changed'.
  // That is not a user edit and must not be reported as one.
  let importing = false;
  let pending = null;

  const exportNow = async () => {
    try {
      return await modeler.exportModel();
    } catch (e) {
      post({ type: 'error', message: 'Export failed: ' + (e?.stack || e) });
      return null;
    }
  };

  eventBus.on('commandStack.changed', () => {
    if (importing) return;
    clearTimeout(pending);
    pending = setTimeout(async () => {
      const model = await exportNow();
      if (model !== null) post({ type: 'changed', model });
    }, CHANGE_DEBOUNCE_MS);
  });

  // Imports run one after the other. Two arriving close together (a document
  // switch right after a refresh) would otherwise interleave at the await, and
  // the first one's `finally` would re-enable change reports while the second
  // is still filling the canvas.
  let queue = Promise.resolve();

  const runImport = async (json) => {
    importing = true;
    clearTimeout(pending);
    let warnings;
    try {
      ({ warnings } = await modeler.importModel(json));
    } catch (e) {
      // No 'imported' after a failure. The host treats 'imported' as "the
      // canvas now shows what I sent" and drops its unsaved edits; after a
      // failed import that is not true, and the edits would be lost.
      post({ type: 'error', message: 'Import failed: ' + (e?.stack || e) });
      return;
    } finally {
      importing = false;
    }
    post({ type: 'imported', model: await exportNow(), warnings });
  };

  const handle = async (msg) => {
    if (!msg || typeof msg !== 'object') return;
    try {
      switch (msg.type) {
        case 'importModel':
          queue = queue.then(() => runImport(msg.model));
          await queue;
          break;
        case 'requestExport': {
          const model = await exportNow();
          if (model !== null) post({ type: 'changed', model });
          break;
        }
        case 'requestSvg':
          post({ type: 'svg', svg: (await modeler.saveSVG()).svg });
          break;
        case 'selectElement': {
          const element = elementRegistry.get(msg.id);
          if (element) {
            selection.select(element);
            canvas.scrollToElement(element);
          } else {
            post({ type: 'log', level: 'info', message: 'selectElement: not on the canvas: ' + msg.id });
          }
          break;
        }
        case 'setTheme':
          document.documentElement.setAttribute('data-theme', msg.theme === 'dark' ? 'dark' : 'light');
          break;
        default:
          post({ type: 'log', level: 'warn', message: 'Unknown message type: ' + msg.type });
      }
    } catch (e) {
      post({ type: 'error', message: `Handling ${msg.type} failed: ` + (e?.stack || e) });
    }
  };

  source?.addEventListener('message', (e) => handle(e.data));

  post({ type: 'ready', url: location.href });

  // Returned so a test or a web page without WebView2 can drive the bridge.
  return { handle };
}
