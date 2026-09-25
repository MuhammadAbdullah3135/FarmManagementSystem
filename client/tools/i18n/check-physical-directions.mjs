// CI guard for Phase 7.3: fail when *layout* code assumes a left-to-right page.
//
// Why a second checker rather than a rule inside the translation guard: nothing here is
// about text. A `margin-left: 8` or an `ArrowLeftOutlined` on a back button reads perfectly
// in a Spanish review and is still wrong in Arabic, because Arabic lays the page out from
// the right. The translation completeness guard cannot see it, and neither can a translator
// reading a JSON file.
//
// What it enforces, in every non-test file under src/:
//
//   1. no physical CSS property in a stylesheet — `margin-left`, `padding-right`,
//      `border-left`, a bare `left:`/`right:`, `text-align: left|right`, `float: left|right`;
//   2. no physical inline style in TSX — the camelCase equivalents, and
//      `textAlign: 'left' | 'right'`;
//   3. no physical column pin — `fixed: 'left' | 'right'` (antd v6 pins on
//      `fixed: 'start'`, which the browser mirrors on its own);
//   4. no gradient written `to left` / `to right`, which has no logical keyword — the
//      direction belongs in `--fms-fade-direction`, published by `applyDocumentLocale`;
//   5. no directional icon — an arrow, caret, `Login`/`Logout` — outside
//      `src/i18n/DirectionalIcon.tsx`, the one file allowed to name them;
//   6. no arrow glyph baked into a string (`→`, `←`, …), which no stylesheet can mirror.
//
// Deliberately NOT enforced, and printed as a count so the decision stays visible rather
// than silent: `align: 'left' | 'right'` on a Table column. All 48 uses are numeric columns
// (quantities, costs, sizes), where right-alignment is a property of the *figure* rather
// than of the language — digits run left to right in Arabic too, so a mirrored numeric
// column would move the decimal points away from the edge a reader scans. A reviewer who
// disagrees with that reading can change this rule to hard and the count becomes a failure
// list. Recorded in docs/I18N.md.
//
// Run with `--quiet` to print only the verdict (the test imports the functions instead).
//
// Usage:
//   node tools/i18n/check-physical-directions.mjs
//   node tools/i18n/check-physical-directions.mjs --quiet

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));

export const CLIENT = path.resolve(here, '../..');
export const SRC = path.join(CLIENT, 'src');
export const ALLOWLIST_FILE = path.join(here, 'physical-directions-allowlist.json');

/** The one module allowed to name a directional icon: it is the mapping itself. */
export const DIRECTION_HELPER = 'i18n/DirectionalIcon.tsx';

/**
 * Physical properties in a stylesheet, each with the logical property that replaces it.
 * `target` is what a reviewer should write instead — the guard is meant to be actionable,
 * not just red.
 */
export const CSS_RULES = [
  { rule: 'margin-left', re: /(^|[^-\w])margin-left\s*:/, target: 'margin-inline-start' },
  { rule: 'margin-right', re: /(^|[^-\w])margin-right\s*:/, target: 'margin-inline-end' },
  { rule: 'padding-left', re: /(^|[^-\w])padding-left\s*:/, target: 'padding-inline-start' },
  { rule: 'padding-right', re: /(^|[^-\w])padding-right\s*:/, target: 'padding-inline-end' },
  { rule: 'border-left', re: /(^|[^-\w])border-left(-color|-width|-style)?\s*:/, target: 'border-inline-start' },
  { rule: 'border-right', re: /(^|[^-\w])border-right(-color|-width|-style)?\s*:/, target: 'border-inline-end' },
  // `text-align` and `float` come before the bare `left:`/`right:` patterns on purpose:
  // `text-align: right` is a colon-prefixed `right` too, and reporting it as a position
  // would send a reader to the wrong property.
  { rule: 'text-align', re: /text-align\s*:\s*(left|right)\b/, target: 'text-align: start / end' },
  { rule: 'float', re: /float\s*:\s*(left|right)\b/, target: 'no logical equivalent; reorder the DOM instead' },
  {
    // `.*?` rather than `\s*` so the property's *fallback* is caught too:
    // `linear-gradient(var(--fms-fade-direction, to left), …)` still hardcodes a physical
    // direction, and is exactly the case the allowlist exists to record.
    rule: 'gradient-direction',
    re: /linear-gradient\(.*?to\s+(left|right)\b/,
    target: 'linear-gradient(var(--fms-fade-direction, …), …)',
  },
  { rule: 'position-left', re: /(^|[\s;{])left\s*:/, target: 'inset-inline-start' },
  { rule: 'position-right', re: /(^|[\s;{])right\s*:/, target: 'inset-inline-end' },
];

/** Physical inline styles and directional icons in TS/TSX. */
export const TS_RULES = [
  { rule: 'inline-margin-left', re: /\bmarginLeft\s*:/, target: 'marginInlineStart' },
  { rule: 'inline-margin-right', re: /\bmarginRight\s*:/, target: 'marginInlineEnd' },
  { rule: 'inline-padding-left', re: /\bpaddingLeft\s*:/, target: 'paddingInlineStart' },
  { rule: 'inline-padding-right', re: /\bpaddingRight\s*:/, target: 'paddingInlineEnd' },
  { rule: 'inline-border-left', re: /\bborderLeft(Width|Color|Style)?\s*:/, target: 'borderInlineStart…' },
  { rule: 'inline-border-right', re: /\bborderRight(Width|Color|Style)?\s*:/, target: 'borderInlineEnd…' },
  { rule: 'inline-text-align', re: /\btextAlign\s*:\s*['"](left|right)['"]/, target: "textAlign: 'start' / 'end'" },
  { rule: 'fixed-column', re: /\bfixed\s*:\s*['"](left|right)['"]/, target: "fixed: 'start' / 'end'" },
  {
    rule: 'directional-icon',
    re: /\b(ArrowLeft|ArrowRight|Left|Right|CaretLeft|CaretRight|DoubleLeft|DoubleRight|StepBackward|StepForward|Login|Logout|Enter|MenuFold|MenuUnfold)Outlined\b/,
    target: 'the matching role from i18n/DirectionalIcon',
  },
  { rule: 'arrow-glyph', re: /[\u2190-\u21FF\u27A1\u2B05\u2B06\u2794]/, target: 'text, not a glyph baked into a string' },
];

/** Table column alignment. Counted, never failed — see the header. */
export const NUMERIC_ALIGN_RULE = 'numeric-align';
export const NUMERIC_ALIGN_RE = /\balign\s*:\s*['"](left|right)['"]/;

/** Blanks out `/* … *\/` without moving any character, so line numbers survive. */
export const stripBlockComments = (code) =>
  code.replace(/\/\*[\s\S]*?\*\//g, (match) => match.replace(/[^\n]/g, ' '));

const stripLineComment = (line) => line.replace(/\/\/.*$/, '');

/** Flattens one file's text into `{ line, rule, value, target }` entries. */
function scanWith(rules, text, { numeric = false } = {}) {
  const out = [];
  const numericHits = [];
  text.split('\n').forEach((raw, index) => {
    const line = stripLineComment(raw);
    for (const { rule, re, target } of rules) {
      if (re.test(line)) {
        out.push({ line: index + 1, rule, value: line.trim(), target });
        return;
      }
    }
    if (numeric && NUMERIC_ALIGN_RE.test(line)) {
      numericHits.push({ line: index + 1, rule: NUMERIC_ALIGN_RULE, value: line.trim() });
    }
  });
  return { violations: out, numericHits };
}

/** Violations in one stylesheet. Pure, so the rules can be tested with fixtures. */
export function scanCss(rel, code) {
  return scanWith(CSS_RULES, stripBlockComments(code)).violations.map((v) => ({ rel, ...v }));
}

/** Violations in one TS/TSX source: physical inline styles, icons, arrow glyphs. */
export function scanSource(rel, code) {
  if (rel === DIRECTION_HELPER) return [];
  const { violations } = scanWith(TS_RULES, stripBlockComments(code));
  return violations.map((v) => ({ rel, ...v }));
}

/** `align:` sites, reported as a count rather than a failure. */
export function scanNumericAlign(rel, code) {
  return scanWith([], stripBlockComments(code), { numeric: true }).numericHits.map((v) => ({
    rel,
    ...v,
  }));
}

function walk(dir, acc = []) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) walk(full, acc);
    else if (/\.(css|tsx|ts)$/.test(full) && !/\.test\.(tsx|ts)$/.test(full)) acc.push(full);
  }
  return acc;
}

const relOf = (file, root) => path.relative(root, file).split(path.sep).join('/');

/** Reads every scanned file under `root` and returns the violations plus the numeric count. */
export function collect(root = SRC) {
  const violations = [];
  const numeric = [];
  for (const file of walk(root)) {
    const rel = relOf(file, root);
    const code = fs.readFileSync(file, 'utf8');
    if (file.endsWith('.css')) violations.push(...scanCss(rel, code));
    else violations.push(...scanSource(rel, code));
    numeric.push(...scanNumericAlign(rel, code));
  }
  return { violations, numeric };
}

export function readAllowlist(file = ALLOWLIST_FILE) {
  if (!fs.existsSync(file)) return { entries: [] };
  const raw = JSON.parse(fs.readFileSync(file, 'utf8'));
  return { entries: Array.isArray(raw.entries) ? raw.entries : [] };
}

const entryMatches = (entry, v) =>
  entry.rel === v.rel &&
  entry.rule === v.rule &&
  (!entry.contains || v.value.includes(entry.contains));

/**
 * What `main` fails on: the violations no allowlist entry explains, plus the allowlist's own
 * hygiene — an entry that no longer matches anything is stale, and a missing reason means
 * nobody can review the decision it records.
 */
export function evaluate(violations, allowlist) {
  const entries = allowlist.entries ?? [];
  const used = new Set();
  const failures = [];

  for (const [index, entry] of entries.entries()) {
    const reason = typeof entry.reason === 'string' ? entry.reason.trim() : '';
    if (reason.length < 25) {
      failures.push({
        kind: 'allowlist-reason-missing',
        id: `${entry.rel ?? '(no file)'}::${entry.rule ?? '(no rule)'}`,
        detail: `a reason of at least 25 characters is required (#${index})`,
      });
    }
  }

  for (const v of violations) {
    const entry = entries.find((e) => entryMatches(e, v));
    if (entry) used.add(entry);
    else
      failures.push({
        kind: 'physical-direction',
        id: `${v.rel}:${v.line}`,
        detail: `${v.rule} — use ${v.target}`,
        value: v.value,
      });
  }

  for (const entry of entries) {
    if (!used.has(entry)) {
      failures.push({
        kind: 'allowlist-stale',
        id: `${entry.rel}::${entry.rule}`,
        detail: 'no longer matches any violation — remove it so the list stays reviewable',
      });
    }
  }

  return failures;
}

/** The whole check, for `main` and for the test that guards this repo. */
export function runCheck({ root = SRC, allowlistFile = ALLOWLIST_FILE } = {}) {
  const { violations, numeric } = collect(root);
  return {
    violations,
    numeric,
    failures: evaluate(violations, readAllowlist(allowlistFile)),
  };
}

function main() {
  const quiet = process.argv.includes('--quiet');
  const { numeric, failures } = runCheck();

  if (failures.length === 0) {
    if (!quiet) {
      console.log(
        `no unmirrored layout directions (0 physical rules; ${numeric.length} numeric column ` +
          'alignments excluded by design)',
      );
    }
    return 0;
  }

  console.error(`\n${failures.length} physical-direction problem(s):`);
  for (const failure of failures) {
    console.error(`   ${failure.kind}: ${failure.id} — ${failure.detail}`);
    if (failure.value) console.error(`      ${failure.value}`);
  }
  console.error(
    '\nLayout must mirror itself in a right-to-left language. Use the logical property\n' +
      '(margin-inline-start, inset-inline-end, text-align: start/end) instead of the physical\n' +
      'one, and the matching role from src/i18n/DirectionalIcon instead of a raw arrow icon.\n' +
      'If a physical value is genuinely correct — a gradient direction, which CSS has no\n' +
      'logical keyword for — record it in tools/i18n/physical-directions-allowlist.json with a\n' +
      'reason. See docs/I18N.md.',
  );
  return 1;
}

if (
  import.meta.url === `file://${process.argv[1]}` ||
  process.argv[1]?.endsWith('check-physical-directions.mjs')
) {
  process.exit(main());
}
