// CI guard for Phase 7.2: a second language must be a *complete mirror* of English.
//
// What it enforces, per non-English locale:
//
//   1. the same namespaces exist;
//   2. the same keys exist inside each one — no gaps, no strays;
//   3. no key is empty or whitespace-only (a blank label is worse than English, because
//      nothing on screen tells the reader something is missing);
//   4. every `{{placeholder}}` in the English string is present in the translation, since
//      i18next substitutes by name and a renamed placeholder silently prints nothing;
//   5. a value identical to English is allowed only if it is on the reviewed allowlist with
//      a written reason, and the allowlist stays short — this is the check that stops
//      "copy English over and call it translated" from passing.
//
// What it deliberately does NOT do: judge the *quality* of a translation. A machine-
// translated sentence that reads awkwardly is not detectable by a file comparison, and
// pretending otherwise would be the most dangerous kind of green. It is why the allowlist
// carries reasons and why `locales/translation-status.json` records that Spanish is
// machine-generated and unreviewed.
//
// Run with `--quiet` to print only the verdict (the test imports the functions instead).
//
// Usage:
//   node tools/i18n/check-locale-coverage.mjs
//   node tools/i18n/check-locale-coverage.mjs --quiet

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));

export const CLIENT = path.resolve(here, '../..');
export const LOCALES_DIR = path.join(CLIENT, 'src/i18n/locales');
export const BASE_LOCALE = 'en';
export const ALLOWLIST_FILE = path.join(LOCALES_DIR, 'untranslated-allowlist.json');

/**
 * A hard cap on how many keys may be identical to English.
 *
 * Not arbitrary: with two locales the allowlist is 15 entries, every one a cognate,
 * a unit symbol or a product name. The cap exists so the allowlist cannot quietly
 * grow into "most of the file" one plausible-looking exception at a time.
 */
export const MAX_ALLOWLIST_ENTRIES = 30;

/** The shortest reason that describes something rather than restating the value. */
export const MIN_ALLOWLIST_REASON = 25;

/** Flattens `{ a: { b: 'x' } }` into `{ 'a.b': 'x' }`, the form keys are looked up in. */
export function flatten(value, prefix = '') {
  const out = {};
  for (const [key, child] of Object.entries(value)) {
    const full = prefix ? `${prefix}.${key}` : key;
    if (child && typeof child === 'object' && !Array.isArray(child)) {
      Object.assign(out, flatten(child, full));
    } else {
      out[full] = child;
    }
  }
  return out;
}

/** Every locale directory that holds resources, English excluded. */
export function readLocaleDirs(dir = LOCALES_DIR) {
  return fs
    .readdirSync(dir, { withFileTypes: true })
    .filter((entry) => entry.isDirectory() && entry.name !== BASE_LOCALE)
    .map((entry) => entry.name)
    .sort();
}

/** `{ namespace: { 'flat.key': 'value' } }` for one locale, read from disk. */
export function loadLocaleBundles(locale, dir = path.join(LOCALES_DIR, locale)) {
  const bundles = {};
  for (const file of fs.readdirSync(dir)) {
    if (!file.endsWith('.json')) continue;
    const namespace = file.slice(0, -'.json'.length);
    bundles[namespace] = flatten(JSON.parse(fs.readFileSync(path.join(dir, file), 'utf8')));
  }
  return bundles;
}

/** The `{{name}}` placeholders in a string, sorted, so two spellings of the same idea match. */
export function placeholders(value) {
  return [...String(value).matchAll(/\{\{\s*([A-Za-z0-9_]+)\s*\}\}/g)]
    .map((match) => match[1])
    .sort();
}

const isBlank = (value) => typeof value !== 'string' || value.trim() === '';

/**
 * Compares one locale against English. Pure — no file access — so the rules it enforces
 * can be tested with fixtures instead of by editing the real resources.
 *
 * Returns every difference as a `{ kind, key, detail }` entry plus the counts the caller
 * prints, so a report can state "1327/1327 keys have a Spanish value" rather than "the
 * check passed".
 */
export function compareBundles(base, other) {
  const issues = [];
  let checked = 0;
  const identical = [];

  const baseNamespaces = Object.keys(base).sort();
  const otherNamespaces = Object.keys(other).sort();

  for (const namespace of baseNamespaces) {
    if (!(namespace in other)) {
      issues.push({ kind: 'missing-namespace', key: namespace, detail: 'no such file in this locale' });
    }
  }
  for (const namespace of otherNamespaces) {
    if (!(namespace in base)) {
      issues.push({ kind: 'extra-namespace', key: namespace, detail: 'not a namespace English ships' });
    }
  }

  for (const namespace of baseNamespaces) {
    // A whole missing file is reported once. Walking it key by key would bury that one
    // actionable fact under a hundred "missing key" lines from the same cause.
    if (!(namespace in other)) continue;

    const baseKeys = base[namespace] ?? {};
    const otherKeys = other[namespace] ?? {};

    for (const key of Object.keys(baseKeys).sort()) {
      const id = `${namespace}:${key}`;
      checked += 1;

      if (!(key in otherKeys)) {
        issues.push({ kind: 'missing-key', key: id, detail: `English has ${JSON.stringify(baseKeys[key])}` });
        continue;
      }

      const translated = otherKeys[key];
      if (isBlank(translated)) {
        issues.push({ kind: 'empty-value', key: id, detail: 'blank or not a string' });
        continue;
      }

      const basePlaceholders = placeholders(baseKeys[key]).join(',');
      const otherPlaceholders = placeholders(translated).join(',');
      if (basePlaceholders !== otherPlaceholders) {
        issues.push({
          kind: 'placeholder-drift',
          key: id,
          detail: `English [${basePlaceholders}] vs [${otherPlaceholders}]`,
        });
      }

      const baseLines = String(baseKeys[key]).split('\n').length;
      const otherLines = String(translated).split('\n').length;
      if (baseLines !== otherLines) {
        issues.push({
          kind: 'line-break-drift',
          key: id,
          detail: `${baseLines} line(s) in English vs ${otherLines}`,
        });
      }

      if (translated === baseKeys[key]) identical.push(id);
    }

    for (const key of Object.keys(otherKeys).sort()) {
      if (!(key in baseKeys)) {
        issues.push({ kind: 'extra-key', key: `${namespace}:${key}`, detail: 'no English key of that name' });
      }
    }
  }

  return { checked, issues, identical };
}

/** The reviewed exceptions, as `{ byKey, entries }`, with the file's own shape validated. */
export function readAllowlist(file = ALLOWLIST_FILE) {
  const raw = JSON.parse(fs.readFileSync(file, 'utf8'));
  const entries = Array.isArray(raw.entries) ? raw.entries : [];
  return { byKey: new Map(entries.map((entry) => [entry.key, entry])), entries };
}

/**
 * The failures `main` reports and the test asserts on: coverage problems, plus the
 * allowlist's own hygiene — an entry must exist in English, must still be identical
 * (otherwise it is stale and hiding nothing), and must carry a real reason.
 */
export function evaluate(report, allowlist, { maxEntries = MAX_ALLOWLIST_ENTRIES } = {}) {
  const failures = [...report.issues];

  const allowance = allowlist.byKey ?? new Map(allowlist.entries.map((e) => [e.key, e]));
  const entries = allowlist.entries ?? [...allowance.values()];

  for (const [index, entry] of entries.entries()) {
    const reason = typeof entry.reason === 'string' ? entry.reason.trim() : '';
    if (reason.length < MIN_ALLOWLIST_REASON) {
      failures.push({
        kind: 'allowlist-reason-missing',
        key: entry.key ?? `#${index}`,
        detail: `a reason of at least ${MIN_ALLOWLIST_REASON} characters is required`,
      });
    }
  }

  if (entries.length > maxEntries) {
    failures.push({
      kind: 'allowlist-too-large',
      key: `${entries.length} entries`,
      detail: `at most ${maxEntries}; growth here is how "untranslated" starts passing`,
    });
  }

  const identicalInAllowlist = new Set();
  for (const id of report.identical) {
    if (allowance.has(id)) {
      identicalInAllowlist.add(id);
      continue;
    }
    failures.push({ kind: 'untranslated', key: id, detail: 'identical to English and not on the allowlist' });
  }

  for (const entry of entries) {
    if (!identicalInAllowlist.has(entry.key)) {
      failures.push({
        kind: 'allowlist-stale',
        key: entry.key ?? '(no key)',
        detail: 'allowlisted but no longer identical to English — remove it so the list stays reviewable',
      });
    }
  }

  return { failures, allowed: identicalInAllowlist.size };
}

/** Runs the whole check and returns what `main` prints and exits on. */
export function runCheck({ dir = LOCALES_DIR } = {}) {
  const base = loadLocaleBundles(BASE_LOCALE, path.join(dir, BASE_LOCALE));
  const allowlist = readAllowlist(path.join(dir, 'untranslated-allowlist.json'));

  const locales = readLocaleDirs(dir).map((locale) => {
    const report = compareBundles(base, loadLocaleBundles(locale, path.join(dir, locale)));
    const { failures, allowed } = evaluate(report, allowlist);
    return { locale, checked: report.checked, identical: report.identical.length, allowed, failures };
  });

  const total = Object.values(base).reduce((sum, keys) => sum + Object.keys(keys).length, 0);
  return { base, locales, total };
}

function main() {
  const quiet = process.argv.includes('--quiet');
  const { locales, total } = runCheck();

  let failed = false;
  for (const locale of locales) {
    // Only a missing or blank value makes a key uncovered; a drifted placeholder or an
    // unexplained identical value is a problem with a value that is nonetheless present.
    const uncovered = locale.failures.filter(
      (failure) => failure.kind === 'missing-key' || failure.kind === 'empty-value',
    ).length;
    console.log(
      `${locale.locale}: ${locale.checked - uncovered}/${locale.checked} keys carry a value ` +
        `(${locale.identical} identical to English, ${locale.allowed} allowlisted)`,
    );
    if (locale.failures.length === 0) continue;

    failed = true;
    console.error(`\n${locale.failures.length} locale-coverage problem(s) in "${locale.locale}":`);
    for (const failure of locale.failures) {
      console.error(`   ${failure.kind}: ${failure.key} — ${failure.detail}`);
    }
  }

  if (failed) {
    console.error(
      '\nEvery key in src/i18n/locales/en must exist in every other locale with a value of its own.\n' +
        'Translate it, or — only when the two languages genuinely share the word — add it to\n' +
        'untranslated-allowlist.json with a reason. See docs/I18N.md.',
    );
    return 1;
  }

  if (!quiet) console.log(`no locale-coverage gaps (${total} English keys)`);
  return 0;
}

if (
  import.meta.url === `file://${process.argv[1]}` ||
  process.argv[1]?.endsWith('check-locale-coverage.mjs')
) {
  process.exit(main());
}
