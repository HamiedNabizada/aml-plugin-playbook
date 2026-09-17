#!/usr/bin/env node
/**
 * Keeps the documents free of references that break when a repository moves on.
 *
 *   node tools/check-docs.mjs
 *
 * Fails on:
 *   - line numbers in code references (`File.cs:120`, `:40-60`): the next commit
 *     in the cited repository makes them wrong without anyone noticing;
 *   - commit hashes: a commit that was never pushed, or is rewritten, is a link
 *     to nothing;
 *   - branch names of other repositories;
 *   - paths into this repository's starter that do not exist;
 *   - relative Markdown links to files that do not exist.
 *
 * Cite a file and a symbol instead: `AMLPetriNet: dotnet/PtMapper.Conversion/PtDiPaths.cs`,
 * `Resolve`. A symbol survives edits around it and is found by a search.
 */
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');

const files = [
  'README.md',
  'CLAUDE.md',
  'starter/README.md',
  ...readdirSync(join(root, 'docs')).filter((f) => f.endsWith('.md')).map((f) => `docs/${f}`),
];

const problems = [];
const report = (file, line, what, text) => problems.push(`${file}:${line}: ${what}: ${text.trim().slice(0, 140)}`);

for (const file of files) {
  const lines = readFileSync(join(root, file), 'utf8').split(/\r?\n/);
  let fence = false;

  lines.forEach((text, index) => {
    const line = index + 1;
    if (/^\s*```/.test(text)) {
      fence = !fence;
      return;
    }
    // Code blocks show code, not references; they are checked by the tests.
    if (fence) return;

    // A file reference with a line number: `Something.ext:12`, `:12-30`, `, :45`.
    if (/\.(cs|js|mjs|ts|tsx|jsx|xaml|csproj|json|yml|yaml|md|aml|xml|html|css|py)[`]?\s*:\s*\d+/.test(text)
        || /`:\d+(-\d+)?`/.test(text)) {
      report(file, line, 'line number', text);
    }

    // Commit hashes, in backticks or after the word commit.
    if (/`[0-9a-f]{7,40}`/.test(text) || /\bcommits?\s+`?[0-9a-f]{7,40}\b/i.test(text)) {
      report(file, line, 'commit hash', text);
    }

    // Branch names of other repositories.
    if (/\b(branch|branches)\b[^.]*`[\w./-]+\/[\w./-]+`/i.test(text) || /`(fix|feature|chore|release)\/[\w.-]+`/.test(text)) {
      report(file, line, 'branch name', text);
    }

    // Paths into the starter must exist.
    for (const match of text.matchAll(/`(starter\/[^`\s:,]+)`/g)) {
      const path = match[1].replace(/[)]+$/, '');
      if (path.includes('<') || path.includes('*') || path.endsWith('...')) continue;
      if (!existsSync(join(root, path))) report(file, line, `missing starter path ${path}`, text);
    }

    // Relative Markdown links must resolve.
    for (const match of text.matchAll(/\]\(([^)#\s]+)(#[^)]*)?\)/g)) {
      const target = match[1];
      if (/^[a-z]+:/i.test(target)) continue;
      if (!existsSync(resolve(join(root, dirname(file)), target))) report(file, line, `broken link ${target}`, text);
    }
  });
}

if (problems.length) {
  console.log(problems.join('\n'));
  console.log(`\n${problems.length} problem(s)`);
  process.exit(1);
}
console.log(`documents: ${files.length} checked, no line numbers, hashes or broken paths`);
