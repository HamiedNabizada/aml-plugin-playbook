/**
 * Writes THIRD-PARTY-NOTICES.md for everything the starter redistributes.
 *
 *   node tools/notices.mjs          write ../THIRD-PARTY-NOTICES.md
 *   node tools/notices.mjs --check  fail if the file is missing a runtime dependency
 *
 * The modeler bundle contains the code of every runtime npm dependency, and the
 * plugin package contains the WebView2 SDK. Their licenses require their notices
 * to travel with the binaries: the file is packed into the plugin package, and a
 * package test checks that it is there. The check mode runs with the browser
 * tests, so adding a dependency without writing the notices again fails.
 */
import { existsSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const web = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const target = resolve(web, '..', 'THIRD-PARTY-NOTICES.md');
const lock = JSON.parse(readFileSync(join(web, 'package-lock.json'), 'utf8'));

const runtime = Object.entries(lock.packages)
  .filter(([path, info]) => path && !info.dev)
  .map(([path, info]) => ({
    path,
    name: path.replace(/^.*node_modules\//, ''),
    version: info.version,
    license: info.license,
  }))
  // One entry per name and version: npm nests the same package in several places.
  .filter((p, i, all) => all.findIndex((q) => q.name === p.name && q.version === p.version) === i)
  .sort((a, b) => a.name.localeCompare(b.name) || a.version.localeCompare(b.version));

if (process.argv.includes('--check')) {
  const text = existsSync(target) ? readFileSync(target, 'utf8') : '';
  const missing = runtime.filter((p) => !text.includes(`### ${p.name} ${p.version}`));
  if (missing.length) {
    console.log('THIRD-PARTY-NOTICES.md lacks: ' + missing.map((p) => `${p.name} ${p.version}`).join(', '));
    console.log('Run: node tools/notices.mjs');
    process.exit(1);
  }
  console.log(`notices: all ${runtime.length} runtime dependencies listed`);
  process.exit(0);
}

const WEBVIEW2 = `Copyright (C) Microsoft Corporation. All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are
met:

   * Redistributions of source code must retain the above copyright
notice, this list of conditions and the following disclaimer.
   * Redistributions in binary form must reproduce the above
copyright notice, this list of conditions and the following disclaimer
in the documentation and/or other materials provided with the
distribution.
   * The name of Microsoft Corporation, or the names of its contributors
may not be used to endorse or promote products derived from this
software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.`;

const out = [
  '# Third-party notices',
  '',
  'Components this project redistributes, with their licenses. The license of this project does not',
  'apply to them. Written by `web/tools/notices.mjs`; run it again after changing dependencies.',
  '',
  '## Bundled into the modeler (`efl.esm.js`, `efl.css`)',
  '',
  '| Component | Version | License |',
  '|---|---|---|',
  ...runtime.map((p) => `| ${p.name} | ${p.version} | ${p.license} |`),
  '',
  '## Redistributed in the plugin package',
  '',
  '| Component | License |',
  '|---|---|',
  '| Microsoft.Web.WebView2 (`Microsoft.Web.WebView2.Core.dll`, `Microsoft.Web.WebView2.Wpf.dll`, `Microsoft.Web.WebView2.Core.winmd`, `WebView2Loader.dll`) | BSD-3-Clause style, below |',
  '',
  '## Referenced, not redistributed',
  '',
  'Aml.Engine, Aml.Editor.Plugin.Contract and Aml.Editor.API (AutomationML e.V.) are NuGet packages the',
  'AutomationML Editor provides at runtime; they are not in the plugin package.',
  '',
  '## License texts',
  '',
  '### Microsoft.Web.WebView2',
  '',
  '```',
  WEBVIEW2,
  '```',
  '',
];

for (const p of runtime) {
  const dir = join(web, p.path);
  const files = readdirSync(dir).filter((f) => /^(license|licence|notice|copying)/i.test(f)).sort();
  out.push(`### ${p.name} ${p.version}`, '');
  if (!files.length) out.push(`License: ${p.license} (the package ships no license file).`, '');
  for (const f of files) {
    out.push(`\`${f}\`:`, '', '```', readFileSync(join(dir, f), 'utf8').trim().replaceAll('```', "'''"), '```', '');
  }
}

writeFileSync(target, out.join('\n').trimEnd() + '\n');
console.log(`wrote ${target}: ${runtime.length} runtime dependencies`);
