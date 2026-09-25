// Extraction codemod for Phase 7.1.
//
// Reads every non-test .tsx page/component, finds user-facing English literals,
// replaces them with t('namespace.key') lookups, inserts the useTranslation hook
// into the owning component, and writes the English resources.
//
// It is a *codemod*, not a formatter: it only ever replaces the exact span of a
// string it understood, and it reports every literal it could not place instead of
// guessing. Run with --dry first; the printed "skipped" list is the honest list of
// what still needs a human.
//
// Usage:
//   node tools/i18n/extract.mjs --dry
//   node tools/i18n/extract.mjs

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import ts from 'typescript';

const here = path.dirname(fileURLToPath(import.meta.url));
const CLIENT = path.resolve(here, '../..');
const SRC = path.join(CLIENT, 'src');
const LOCALES = path.join(SRC, 'i18n/locales/en');
const DRY = process.argv.includes('--dry');
const DEBUG = (process.argv.find((a) => a.startsWith('--debug=')) || '').slice('--debug='.length);

const NAMESPACES = [
  'common', 'nav', 'auth', 'dashboard', 'animals', 'breeding', 'feed', 'health',
  'hr', 'finance', 'inventory', 'tasks', 'reports', 'configuration', 'admin',
  'notifications', 'offline', 'imports', 'validation', 'errors',
];

/** Props whose string value is rendered to the user. */
const USER_ATTRS = new Set([
  'title', 'label', 'placeholder', 'message', 'description', 'okText', 'cancelText',
  'subTitle', 'subtitle', 'tooltip', 'header', 'emptyText', 'help', 'extra', 'text',
  'aria-label', 'alt', 'content', 'confirmText',
]);

/** message.<kind>('...') / notification.<kind>('...') calls carry user copy. */
const TOAST_KINDS = new Set(['success', 'error', 'warning', 'info', 'open']);

const PAGE_NS = {
  LoginPage: 'auth',
  RegisterPage: 'auth',
  ResetPasswordPage: 'auth',
  ConfirmResetPasswordPage: 'auth',
  AcceptInvitationPage: 'auth',
  DashboardPage: 'dashboard',
  TasksPage: 'tasks',
  NotificationsPage: 'notifications',
  NotificationPreferencesPage: 'notifications',
};

function namespaceFor(relPosix) {
  const parts = relPosix.split('/');
  const file = parts[parts.length - 1];
  if (/Import(Page|Wizard)/.test(file)) return 'imports';
  if (parts[0] === 'components') return 'common';
  if (parts[0] === 'pages') {
    if (parts.length === 2) return PAGE_NS[file.replace(/\.tsx$/, '')] ?? 'common';
    return NAMESPACES.includes(parts[1]) ? parts[1] : 'common';
  }
  return 'common';
}

function walk(dir, acc = []) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walk(p, acc);
    else acc.push(p);
  }
  return acc;
}

function slugify(value) {
  const words = value
    .replace(/[^A-Za-z0-9]+/g, ' ')
    .trim()
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 8);
  const camel = words
    .map((w, i) => (i === 0 ? w.toLowerCase() : w.charAt(0).toUpperCase() + w.slice(1).toLowerCase()))
    .join('');
  return camel || 'text';
}

/** Key/English maps, shared across files so identical copy reuses one key. */
const resources = new Map();
for (const ns of NAMESPACES) resources.set(ns, new Map());

function keyFor(ns, english) {
  const table = resources.get(ns);
  for (const [key, value] of table) if (value === english) return key;
  const base = slugify(english);
  let key = base;
  let n = 2;
  while (table.has(key)) key = `${base}${n++}`;
  table.set(key, english);
  return key;
}

function isFunctionLike(node) {
  return (
    ts.isFunctionDeclaration(node) ||
    ts.isFunctionExpression(node) ||
    ts.isArrowFunction(node) ||
    ts.isMethodDeclaration(node)
  );
}

function nameOfFunction(node) {
  if (node.name && ts.isIdentifier(node.name)) return node.name.text;
  const parent = node.parent;
  if (parent && ts.isVariableDeclaration(parent) && ts.isIdentifier(parent.name)) return parent.name.text;
  return null;
}

/** A component: a named function-like whose body renders JSX. */
function isComponent(node) {
  const name = nameOfFunction(node);
  if (!name || !/^[A-Z]/.test(name)) return false;
  return containsJsxOutsideNested(node);
}

function containsJsxOutsideNested(root) {
  let found = false;
  const visit = (node) => {
    if (found) return;
    if (node !== root && isFunctionLike(node)) return;
    if (ts.isJsxElement(node) || ts.isJsxFragment(node) || ts.isJsxSelfClosingElement(node)) {
      found = true;
      return;
    }
    ts.forEachChild(node, visit);
  };
  ts.forEachChild(root, visit);
  return found;
}

/** Every name bound by a parameter/variable/function in the file. */
function collectBindingNames(sf) {
  const names = new Set();
  const visit = (node) => {
    if (ts.isIdentifier(node)) {
      const p = node.parent;
      const isBinding =
        (ts.isParameter(p) || ts.isVariableDeclaration(p) || ts.isBindingElement(p)) && p.name === node;
      const isDecl =
        (ts.isFunctionDeclaration(p) || ts.isClassDeclaration(p)) && p.name === node;
      if ((isBinding && !boundByUseTranslation(p)) || isDecl) names.add(node.text);
    }
    ts.forEachChild(node, visit);
  };
  ts.forEachChild(sf, visit);
  return names;
}

/** True when a declaration's initializer is a `useTranslation()` call (so its `t` is ours). */
function boundByUseTranslation(decl) {
  let d = decl;
  while (d && !ts.isVariableDeclaration(d)) d = d.parent;
  if (!d || !ts.isVariableDeclaration(d) || !d.initializer || !ts.isCallExpression(d.initializer)) return false;
  const callee = d.initializer.expression;
  return ts.isIdentifier(callee) && callee.text === 'useTranslation';
}

function findOwner(node) {
  let n = node.parent;
  while (n) {
    if (isFunctionLike(n) && isComponent(n)) return n;
    n = n.parent;
  }
  return null;
}

function isInsideAlreadyTranslated(node) {
  const p = node.parent;
  if (p && ts.isCallExpression(p) && ts.isIdentifier(p.expression) && p.expression.text === 't') return true;
  if (p && ts.isJsxExpression(p) && p.parent && ts.isJsxAttribute(p.parent)) return true;
  return false;
}

function looksLikeUiText(value) {
  if (!value || value.length < 2) return false;
  if (!/[A-Za-z]/.test(value)) return false;
  if (/^https?:\/\//.test(value)) return false;
  if (/^[a-z0-9-]+$/.test(value) && value.includes('-')) return false; // css / class-ish
  if (/^[A-Za-z]:[\\/]/.test(value) || value.startsWith('/') || value.startsWith('.')) return false;
  return true;
}

function isInsideAttributeNotTranslated(node, sourceFile) {
  // Skip literals inside attributes we do not translate (className, style, type, ...).
  let n = node.parent;
  while (n) {
    if (ts.isJsxAttribute(n)) return !USER_ATTRS.has(n.name.getText(sourceFile));
    if (isFunctionLike(n)) return false;
    n = n.parent;
  }
  return false;
}

function planFile(file) {
  const rel = path.relative(SRC, file).split(path.sep).join('/');
  const ns = namespaceFor(rel);
  const text = fs.readFileSync(file, 'utf8');
  const sf = ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
  // A local `t` (a render callback parameter, say) would shadow the hook, so the
  // whole file falls back to an alias rather than silently breaking inside that scope.
  const bindings = collectBindingNames(sf);
  const tName = !bindings.has('t') ? 't' : !bindings.has('translate') ? 'translate' : 'i18nT';
  const edits = [];
  const owners = new Map(); // node -> { bodyStart, expressionBody, bodyEnd }
  const skipped = [];
  let count = 0;

  const addJsxText = (node) => {
    const raw = node.getText(sf);
    const trimmed = raw.trim();
    if (!trimmed || !/[A-Za-z]/.test(trimmed)) return;
    if (trimmed.includes('&')) {
      skipped.push({ rel, value: trimmed, reason: 'html entity' });
      return;
    }
    const owner = findOwner(node);
    if (!owner) {
      skipped.push({ rel, value: trimmed, reason: 'module-scope JSX text' });
      return;
    }
    const key = keyFor(ns, trimmed);
    const offset = raw.indexOf(trimmed);
    const start = node.getStart(sf) + offset;
    edits.push({ b: 'jsxtext', start, end: start + trimmed.length, text: `{${tName}('${key}')}`, forJsxChild: true });
    recordOwner(owner, sf, owners);
    count += 1;
  };

  const visit = (node) => {
    if (ts.isJsxText(node)) {
      addJsxText(node);
      return; // no children to walk
    }

    if (ts.isJsxAttribute(node)) {
      const name = node.name.getText(sf);
      if (!USER_ATTRS.has(name)) {
        // Still walk the expression, but a string there is not user copy (className etc.)
        if (node.initializer && ts.isJsxExpression(node.initializer) && node.initializer.expression) {
          walkExpression(node.initializer.expression);
        }
        return;
      }
      if (node.initializer && ts.isStringLiteral(node.initializer)) {
        if (looksLikeUiText(node.initializer.text)) {
          const owner = findOwner(node);
          if (!owner) {
            skipped.push({ rel, value: node.initializer.text, reason: 'module-scope attribute' });
          } else {
            const key = keyFor(ns, node.initializer.text);
            edits.push({
              b: 'attr',
              start: node.initializer.getStart(sf),
              end: node.initializer.getEnd(),
              text: `{${tName}('${key}')}`,
              forJsxChild: false,
            });
            recordOwner(owner, sf, owners);
            count += 1;
          }
        }
        return;
      }
      if (node.initializer && ts.isJsxExpression(node.initializer) && node.initializer.expression) {
        walkExpression(node.initializer.expression);
      }
      return;
    }

    if (ts.isJsxExpression(node) && node.expression) {
      walkExpression(node.expression);
      return;
    }

    if (ts.isCallExpression(node)) {
      const expr = node.expression;
      if (
        ts.isPropertyAccessExpression(expr) &&
        ts.isIdentifier(expr.expression) &&
        (expr.expression.text === 'message' || expr.expression.text === 'notification') &&
        TOAST_KINDS.has(expr.name.text) &&
        node.arguments.length > 0 &&
        ts.isStringLiteral(node.arguments[0]) &&
        looksLikeUiText(node.arguments[0].text)
      ) {
        const arg = node.arguments[0];
        const owner = findOwner(node);
        if (!owner) {
          skipped.push({ rel, value: arg.text, reason: 'module-scope toast' });
        } else {
          const key = keyFor(ns, arg.text);
          edits.push({ b: 'toast', start: arg.getStart(sf), end: arg.getEnd(), text: `${tName}('${key}')`, forJsxChild: false });
          recordOwner(owner, sf, owners);
          count += 1;
        }
        ts.forEachChild(node, visit);
        return;
      }
      ts.forEachChild(node, visit);
      return;
    }

    if (ts.isPropertyAssignment(node)) {
      const name = ts.isIdentifier(node.name) || ts.isStringLiteral(node.name) ? node.name.text : null;
      if (name && USER_ATTRS.has(name) && ts.isStringLiteral(node.initializer) && looksLikeUiText(node.initializer.text)) {
        const owner = findOwner(node);
        if (!owner) {
          skipped.push({ rel, value: node.initializer.text, reason: 'module-scope property' });
        } else {
          const key = keyFor(ns, node.initializer.text);
          edits.push({
            b: 'prop',
            start: node.initializer.getStart(sf),
            end: node.initializer.getEnd(),
            text: `${tName}('${key}')`,
            forJsxChild: false,
          });
          recordOwner(owner, sf, owners);
          count += 1;
        }
        return;
      }
      ts.forEachChild(node, visit);
      return;
    }

    ts.forEachChild(node, visit);
  };

  /**
   * Walks an arbitrary expression, translating string literals rendered in JSX.
   *
   * A JSX node reached from inside an expression (`{cond && (<Button aria-label="x" />)}`)
   * is handed back to `visit`, so its attributes and text get the JSX-aware handling
   * (braces for attributes, `{t(...)}` for text) instead of a bare call. Recursing here
   * was what emitted `aria-label=t('x')` — a syntax error.
   */
  const walkExpression = (node) => {
    if (
      ts.isJsxElement(node) ||
      ts.isJsxSelfClosingElement(node) ||
      ts.isJsxFragment(node) ||
      ts.isJsxAttribute(node) ||
      ts.isJsxExpression(node)
    ) {
      visit(node);
      return;
    }

    if (ts.isStringLiteral(node)) {
      if (isInsideAlreadyTranslated(node)) return;
      if (isInsideAttributeNotTranslated(node, sf)) return;
      if (!looksLikeUiText(node.text)) return;
      const parent = node.parent;
      // Data, not copy: a string handed to a function, compared with ===, held in an
      // array, or assigned to a non-user object property is a value (an enum member,
      // a modal kind, a column source) — translating it would change behaviour, not
      // language. Only prose that ends up on screen is rewritten.
      const isDataValue =
        ts.isCallExpression(parent) ||
        ts.isNewExpression(parent) ||
        ts.isBinaryExpression(parent) ||
        ts.isArrayLiteralExpression(parent) ||
        ts.isSpreadElement(parent) ||
        ((ts.isPropertyAssignment(parent) || ts.isPropertyDeclaration(parent)) &&
          !(ts.isIdentifier(parent.name) && USER_ATTRS.has(parent.name.text)));
      if (isDataValue) return;
      const owner = findOwner(node);
      if (!owner) {
        skipped.push({ rel, value: node.text, reason: 'module-scope expression' });
        return;
      }
      const key = keyFor(ns, node.text);
      edits.push({ b: 'expr', start: node.getStart(sf), end: node.getEnd(), text: `${tName}('${key}')`, forJsxChild: false });
      recordOwner(owner, sf, owners);
      count += 1;
      return;
    }
    if (ts.isTemplateExpression(node)) {
      skipped.push({ rel, value: node.getText(sf).slice(0, 60), reason: 'template literal' });
      return;
    }
    ts.forEachChild(node, walkExpression);
  };

  visit(sf);

  if (edits.length === 0) return { file, rel, ns, edits: [], owners, skipped, count: 0 };

  if (DEBUG && rel.includes(DEBUG)) {
    for (const e of edits.slice(0, 400)) {
      console.log('  >', rel, e.b || 'insert', e.start, e.end, JSON.stringify(text.slice(e.start, e.end)), '=>', JSON.stringify(e.text));
    }
  }

  // Insert the hook into each owning component that received an edit.
  const insertions = [];
  // Scoped to this file's namespace: `useTranslation('animals')` makes a bare
  // `t('listTitle')` resolve in animals.json, which is why the generated keys carry
  // no namespace prefix of their own.
  const hookDecl =
    tName === 't'
      ? `const { t } = useTranslation('${ns}'); `
      : `const { t: ${tName} } = useTranslation('${ns}'); `;
  for (const [owner, info] of owners) {
    // A component that already destructures `t` from useTranslation needs nothing.
    if (owner.body.getText(sf).includes('useTranslation(')) continue;
    if (info.expressionBody) {
      insertions.push({ start: info.bodyStart, end: info.bodyStart, text: `{ ${hookDecl}return ` });
      insertions.push({ start: info.bodyEnd, end: info.bodyEnd, text: '; }' });
    } else {
      insertions.push({ start: info.bodyStart, end: info.bodyStart, text: hookDecl });
    }
  }

  // The import, placed after the last top-level import.
  const hasImport = /from\s+'react-i18next'/.test(text);
  if (!hasImport) {
    let insertAt = 0;
    for (const stmt of sf.statements) {
      if (ts.isImportDeclaration(stmt)) insertAt = stmt.getEnd();
      else break;
    }
    insertions.push({
      start: insertAt,
      end: insertAt,
      text: `${insertAt ? '\n' : ''}import { useTranslation } from 'react-i18next';`,
    });
  }

  return { file, rel, ns, edits: [...edits, ...insertions], owners, skipped, count };
}

function recordOwner(owner, sf, owners) {
  if (owners.has(owner)) return;
  const body = owner.body;
  if (ts.isBlock(body)) {
    owners.set(owner, { bodyStart: body.getStart(sf) + 1, expressionBody: false, bodyEnd: body.getEnd() });
  } else {
    owners.set(owner, { bodyStart: body.getStart(sf), expressionBody: true, bodyEnd: body.getEnd() });
  }
}

function applyEdits(text, edits) {
  const sorted = [...edits].sort((a, b) => b.start - a.start || b.end - a.end);
  let out = text;
  for (const e of sorted) out = out.slice(0, e.start) + e.text + out.slice(e.end);
  return out;
}

// ── run ──────────────────────────────────────────────────────────────────

const targets = walk(SRC).filter(
  (p) =>
    p.endsWith('.tsx') &&
    !p.endsWith('.test.tsx') &&
    (p.includes(`${path.sep}pages${path.sep}`) || p.includes(`${path.sep}components${path.sep}`)),
);

let totalEdits = 0;
let totalSkipped = 0;
let filesChanged = 0;
const skippedByReason = new Map();

for (const file of targets) {
  const plan = planFile(file);
  if (plan.count === 0) continue;
  totalEdits += plan.count;
  filesChanged += 1;
  for (const s of plan.skipped) {
    totalSkipped += 1;
    skippedByReason.set(s.reason, (skippedByReason.get(s.reason) || 0) + 1);
  }
  if (!DRY) {
    fs.writeFileSync(file, applyEdits(fs.readFileSync(file, 'utf8'), plan.edits), 'utf8');
  }
}

console.log(`${DRY ? '[dry-run] ' : ''}files touched: ${filesChanged}`);
console.log(`${DRY ? '[dry-run] ' : ''}strings extracted: ${totalEdits}`);
console.log(`strings skipped (need a human): ${totalSkipped}`);
for (const [reason, n] of [...skippedByReason].sort((a, b) => b[1] - a[1])) {
  console.log(`   ${String(n).padStart(4)}  ${reason}`);
}

if (process.argv.includes('--list-skips')) {
  const all = [];
  for (const file of targets) all.push(...planFile(file).skipped);
  const byFile = new Map();
  for (const s of all) {
    if (!byFile.has(s.rel)) byFile.set(s.rel, []);
    byFile.get(s.rel).push(s);
  }
  for (const [rel, list] of [...byFile].sort((a, b) => b[1].length - a[1].length)) {
    console.log(`\n${rel}  (${list.length})`);
    for (const s of list) console.log(`    [${s.reason}] ${s.value}`);
  }
}

if (!DRY) {
  fs.mkdirSync(LOCALES, { recursive: true });
  let written = 0;
  for (const ns of NAMESPACES) {
    const table = resources.get(ns);
    const obj = {};
    for (const [key, value] of [...table].sort((a, b) => a[0].localeCompare(b[0]))) obj[key] = value;
    const file = path.join(LOCALES, `${ns}.json`);
    fs.writeFileSync(file, `${JSON.stringify(obj, null, 2)}\n`, 'utf8');
    written += Object.keys(obj).length;
  }
  console.log(`wrote ${written} keys across ${NAMESPACES.length} namespace files in ${path.relative(CLIENT, LOCALES)}`);
}
