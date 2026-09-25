// CI guard for Phase 7.1: fail when a *new* hardcoded user-facing string appears.
//
// Honesty about what this is (and is not):
//
//   False positives — any literal that is data rather than copy. A domain value passed
//   to a function, a CSS class, a unit suffix, a currency format are all strings a user
//   may see but must NOT be translated in place. The extractor separates these by
//   context; a regex-shaped checker cannot, so they are enumerated in the baseline with
//   a reason rather than silently trusted.
//
//   False negatives — text that never appears as a literal: a string assigned to a const
//   and rendered later, a sentence built by concatenation, an interpolation that a
//   translator would need to reorder. Those are invisible here and were found by the
//   extraction codemod's own "skipped" report instead.
//
// Because of both, the checker is a *tripping hazard for regressions*, not a proof of
// completeness: it compares against a baseline of known/reviewed strings and fails only
// when the set grows. Run with --update to accept the current set after review.
//
// Usage:
//   node tools/i18n/check-hardcoded-strings.mjs
//   node tools/i18n/check-hardcoded-strings.mjs --update

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import ts from 'typescript';

const here = path.dirname(fileURLToPath(import.meta.url));
const CLIENT = path.resolve(here, '../..');
const SRC = path.join(CLIENT, 'src');
const BASELINE = path.join(here, 'hardcoded-strings-baseline.json');

export const USER_ATTRS = new Set([
  'title', 'label', 'placeholder', 'message', 'description', 'okText', 'cancelText',
  'subTitle', 'subtitle', 'tooltip', 'header', 'emptyText', 'help', 'extra', 'text',
  'aria-label', 'alt', 'content', 'confirmText',
]);

export const TOAST_KINDS = new Set(['success', 'error', 'warning', 'info', 'open']);

const looksLikeUiText = (value) => {
  if (!value || value.length < 2) return false;
  if (!/[A-Za-z]/.test(value)) return false;
  if (/^https?:\/\//.test(value)) return false;
  if (/^[a-z0-9-]+$/.test(value) && value.includes('-')) return false;
  if (value.startsWith('/') || value.startsWith('.')) return false;
  return true;
};

/** Scans one source string; returns violations. Pure, so it is unit-testable. */
export function scanSource(rel, code) {
  const kind = rel.endsWith('.tsx') ? ts.ScriptKind.TSX : ts.ScriptKind.TS;
  const sf = ts.createSourceFile(rel, code, ts.ScriptTarget.Latest, true, kind);
  const out = [];
  const add = (kind, value) => {
    if (looksLikeUiText(value)) out.push({ rel, kind, value: value.trim().replace(/\s+/g, ' ') });
  };

  const visit = (node) => {
    if (ts.isJsxText(node)) {
      const t = node.getText(sf).trim();
      if (t && /[A-Za-z]/.test(t) && !t.includes('&')) add('jsx-text', t);
      return;
    }
    if (ts.isJsxAttribute(node)) {
      const name = node.name.getText(sf);
      if (USER_ATTRS.has(name) && node.initializer && ts.isStringLiteral(node.initializer)) {
        add('attribute', node.initializer.text);
      }
      return;
    }
    if (ts.isCallExpression(node)) {
      const e = node.expression;
      if (
        ts.isPropertyAccessExpression(e) &&
        ts.isIdentifier(e.expression) &&
        (e.expression.text === 'message' || e.expression.text === 'notification') &&
        TOAST_KINDS.has(e.name.text) &&
        node.arguments[0] &&
        ts.isStringLiteral(node.arguments[0])
      ) {
        add('toast', node.arguments[0].text);
      }
      ts.forEachChild(node, visit);
      return;
    }
    if (ts.isPropertyAssignment(node)) {
      const name = ts.isIdentifier(node.name) || ts.isStringLiteral(node.name) ? node.name.text : null;
      if (name && USER_ATTRS.has(name) && ts.isStringLiteral(node.initializer)) {
        add('property', node.initializer.text);
      } else if (ts.isTemplateExpression(node.initializer)) {
        // Dynamic copy: reported for review, never auto-translatable.
        const head = node.initializer.head.text;
        if (/\s/.test(head) && /[A-Za-z]/.test(head)) add('template', node.initializer.getText(sf));
      }
      ts.forEachChild(node, visit);
      return;
    }
    ts.forEachChild(node, visit);
  };
  visit(sf);
  return out;
}

function walkFiles(dir, acc = []) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walkFiles(p, acc);
    else if (/\.(tsx|ts)$/.test(p) && !/\.test\.(tsx|ts)$/.test(p)) acc.push(p);
  }
  return acc;
}

const idOf = (v) => `${v.rel}::${v.kind}::${v.value}`;

/** Ids present now that the baseline does not already cover — the CI failure set. */
export function newStrings(violations, baselineIds) {
  const base = baselineIds instanceof Set ? baselineIds : new Set(baselineIds);
  return [...new Set(violations.map(idOf))].filter((id) => !base.has(id)).sort();
}

export function collectViolations(root = SRC) {
  const files = walkFiles(root).filter(
    (p) =>
      (p.endsWith('.tsx') || p.endsWith('.ts')) &&
      (p.includes(`${path.sep}pages${path.sep}`) || p.includes(`${path.sep}components${path.sep}`)),
  );
  const violations = [];
  for (const file of files) {
    const rel = path.relative(SRC, file).split(path.sep).join('/');
    violations.push(...scanSource(rel, fs.readFileSync(file, 'utf8')));
  }
  return violations;
}

function main() {
  const update = process.argv.includes('--update');
  const current = collectViolations();
  const currentIds = new Set(current.map(idOf));

  if (update) {
    const list = [...currentIds].sort();
    fs.writeFileSync(BASELINE, `${JSON.stringify({ note: 'Reviewed hardcoded strings; see docs/I18N.md.', strings: list }, null, 2)}\n`, 'utf8');
    console.log(`baseline updated with ${list.length} reviewed strings`);
    return 0;
  }

  const baseline = fs.existsSync(BASELINE)
    ? new Set(JSON.parse(fs.readFileSync(BASELINE, 'utf8')).strings)
    : new Set();

  const added = newStrings(current, baseline);
  const removed = [...baseline].filter((id) => !currentIds.has(id)).sort();

  if (removed.length) {
    console.log(`${removed.length} baseline string(s) no longer present (ran --update to accept):`);
    for (const id of removed.slice(0, 10)) console.log(`   - ${id}`);
  }

  if (added.length) {
    console.error(`\n${added.length} hardcoded user-facing string(s) introduced without a translation key:`);
    for (const id of added) console.error(`   + ${id}`);
    console.error('\nWrap them in t(\'…\') and add the English value to src/i18n/locales/en/, or run with --update after review.');
    return 1;
  }

  console.log(`no new hardcoded user-facing strings (${currentIds.size} reviewed in baseline)`);
  return 0;
}

if (import.meta.url === `file://${process.argv[1]}` || process.argv[1]?.endsWith('check-hardcoded-strings.mjs')) {
  process.exit(main());
}
