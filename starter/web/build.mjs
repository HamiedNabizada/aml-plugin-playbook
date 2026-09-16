/**
 * Bundles the modeler into one ESM file and one stylesheet.
 *
 * One file, no chunks, nothing fetched at runtime: the WebView2 host loads the
 * bundle from a folder on disk through a virtual host name, and a chunk it
 * cannot find is a blank canvas with no error.
 *
 * Minification is off. diagram-js resolves services through didi, which falls
 * back to reading constructor parameter names when a component has no $inject
 * annotation. A minifier renames those parameters and the injector fails at
 * boot. Every component in src/ declares $inject, but a dependency may not.
 */
import * as esbuild from 'esbuild';
import { cp, mkdir, rm } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const dist = resolve(here, 'dist');

await rm(dist, { recursive: true, force: true });
await mkdir(dist, { recursive: true });

await esbuild.build({
  entryPoints: [resolve(here, 'src/index.js')],
  bundle: true,
  format: 'esm',
  target: 'es2022',
  outfile: resolve(dist, 'efl.esm.js'),
  minify: false,
  legalComments: 'eof',
  logLevel: 'warning',
});

await esbuild.build({
  entryPoints: [resolve(here, 'src/efl.css')],
  bundle: true,
  outfile: resolve(dist, 'efl.css'),
  loader: { '.svg': 'dataurl', '.woff': 'dataurl', '.woff2': 'dataurl' },
  logLevel: 'warning',
});

await cp(resolve(here, 'index.html'), resolve(dist, 'index.html'));

console.log('dist/ ready');
