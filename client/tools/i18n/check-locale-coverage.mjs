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

/**
 * i18next's plural suffixes. A key ending in one of these is a *form* of the key without it.
 *
 * The distinction matters because a language is not required to have the same number of
 * plural forms as English. Arabic has six (zero, one, two, few, many, other) where English
 * has two, so `intervalDays_one … intervalDays_zero` in Arabic is not six extra keys — it is
 * the one English `intervalDays` said six ways. Without this, every plural in a richer
 * language would be reported as a missing key *and* six strays.
 */
export const PLURAL_SUFFIXES = ['zero', 'one', 'two', 'few', 'many', 'other', 'plural'];

/** `intervalDays_few` → `intervalDays`. Anything else is returned unchanged. */
export const logicalKey = (key) => {
  const cut = key.lastIndexOf('_');
  if (cut === -1) return key;
  return PLURAL_SUFFIXES.includes(key.slice(cut + 1)) ? key.slice(0, cut) : key;
};

export const isPluralVariant = (key) => logicalKey(key) !== key;

/**
 * The plural categories a language actually selects from, straight from ICU.
 *
 * Not a hard-coded table, because the point of the rule that uses it is to catch a *missing*
 * form, and the only honest source for "how many forms does Arabic need" is the same data
 * i18next resolves counts against: English needs two (`one`, `other`), Arabic six, Polish
 * four. `resolvedOptions().pluralCategories` is where V8 reports them; the sample-based
 * fallback exists so a runtime without it degrades to a real answer rather than to none.
 */
export function pluralCategories(locale) {
  const rules = new Intl.PluralRules(locale);
  const reported = rules.resolvedOptions?.().pluralCategories;
  if (Array.isArray(reported) && reported.length > 0) return [...reported];

  const found = new Set();
  for (const value of [0, 1, 2, 3, 6, 11, 40, 100, 1000]) found.add(rules.select(value));
  return [...found];
}

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
 * Checks one translated value against its English source, pushing anything wrong.
 *
 * `plural` relaxes the placeholder rule from equality to *subset*: a plural form may drop
 * `{{count}}` when the language's own form carries the number — Arabic's dual "يومان" is
 * "two days" with no numeral in it — but it may never introduce a placeholder English does
 * not have, which is what a renamed `{{count}}` would look like.
 */
function checkValue({ issues, identical, id, english, translated, plural }) {
  if (isBlank(translated)) {
    issues.push({ kind: 'empty-value', key: id, detail: 'blank or not a string' });
    return;
  }

  const base = placeholders(english);
  const other = placeholders(translated);
  const baseJoined = base.join(',');
  const otherJoined = other.join(',');
  if (plural) {
    if (!other.every((name) => base.includes(name))) {
      issues.push({
        kind: 'placeholder-drift',
        key: id,
        detail: `English [${baseJoined}] vs [${otherJoined}] — a plural form may drop a placeholder, not add one`,
      });
    }
  } else if (baseJoined !== otherJoined) {
    issues.push({
      kind: 'placeholder-drift',
      key: id,
      detail: `English [${baseJoined}] vs [${otherJoined}]`,
    });
  }

  const baseLines = String(english).split('\n').length;
  const otherLines = String(translated).split('\n').length;
  if (baseLines !== otherLines) {
    issues.push({
      kind: 'line-break-drift',
      key: id,
      detail: `${baseLines} line(s) in English vs ${otherLines}`,
    });
  }

  if (translated === english) identical.push(id);
}

/**
 * Compares one locale against English. Pure — no file access — so the rules it enforces
 * can be tested with fixtures instead of by editing the real resources.
 *
 * Returns every difference as a `{ kind, key, detail }` entry plus the counts the caller
 * prints, so a report can state "1327/1327 keys have a Spanish value" rather than "the
 * check passed".
 */
export function compareBundles(base, other, { locale = BASE_LOCALE } = {}) {
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
    const baseLogical = new Set(Object.keys(baseKeys).map(logicalKey));

    // Every translation of one logical key, so a base key can be satisfied by a language's
    // own plural forms rather than only by a key spelled exactly like the English one.
    const otherByLogical = new Map();
    for (const key of Object.keys(otherKeys)) {
      const bucket = otherByLogical.get(logicalKey(key)) ?? [];
      bucket.push(key);
      otherByLogical.set(logicalKey(key), bucket);
    }

    for (const key of Object.keys(baseKeys).sort()) {
      const id = `${namespace}:${key}`;
      checked += 1;

      if (key in otherKeys) {
        checkValue({
          issues,
          identical,
          id,
          english: baseKeys[key],
          translated: otherKeys[key],
          plural: isPluralVariant(key),
        });
        continue;
      }

      const forms = otherByLogical.get(logicalKey(key)) ?? [];
      if (forms.length > 0) {
        // `_other` is i18next's guaranteed fallback inside a language, so a language that
        // supplies plural forms must supply it; without it a count with no matching form
        // silently falls through to English. Reported on its own, because when `_other` is
        // missing that is the one fact worth acting on.
        if (!forms.some((form) => form === `${logicalKey(key)}_other`)) {
          issues.push({
            kind: 'plural-forms-without-other',
            key: `${namespace}:${logicalKey(key)}`,
            detail: `English is a single string; this locale supplies ${forms.join(', ')} but no _other form`,
          });
          continue;
        }

        // Having opted into plural forms, the locale must carry *every* form its own rules
        // select from. This is the rule that catches a translation losing one: Arabic without
        // `_many` still renders, because i18next falls back to `_other`, so nothing else in
        // this file or on screen would ever notice — the Arabic for 40 would simply read like
        // the Arabic for 100.
        const baseName = logicalKey(key);
        const missingForms = pluralCategories(locale)
          .map((category) => `${baseName}_${category}`)
          .filter((form) => !(form in otherKeys));
        if (missingForms.length > 0) {
          issues.push({
            kind: 'missing-plural-form',
            key: `${namespace}:${baseName}`,
            detail: `${locale} selects ${pluralCategories(locale).join(', ')}; no value for ${missingForms.join(', ')}`,
          });
        }

        for (const form of forms.sort()) {
          checkValue({
            issues,
            identical,
            id: `${namespace}:${form}`,
            english: baseKeys[key],
            translated: otherKeys[form],
            plural: true,
          });
        }
        continue;
      }

      issues.push({ kind: 'missing-key', key: id, detail: `English has ${JSON.stringify(baseKeys[key])}` });
    }

    for (const key of Object.keys(otherKeys).sort()) {
      // A plural form is extra only when English has no key of that base name at all.
      if (!(key in baseKeys) && !baseLogical.has(logicalKey(key))) {
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
export function evaluate(report, allowlist, { maxEntries = MAX_ALLOWLIST_ENTRIES, locale } = {}) {
  const failures = [...report.issues];

  const allEntries = allowlist.entries ?? [];
  // An entry may be restricted to the locales it was reviewed for (`"locales": ["es"]`).
  // "Total" is a genuine cognate in Spanish and a translated word in Arabic, so a single
  // global list would either hide a real Arabic gap or fail on a Spanish one that is right.
  const entries = locale ? allEntries.filter((e) => !e.locales || e.locales.includes(locale)) : allEntries;
  const allowance = new Map(entries.map((e) => [e.key, e]));

  for (const [index, entry] of allEntries.entries()) {
    const reason = typeof entry.reason === 'string' ? entry.reason.trim() : '';
    if (reason.length < MIN_ALLOWLIST_REASON) {
      failures.push({
        kind: 'allowlist-reason-missing',
        key: entry.key ?? `#${index}`,
        detail: `a reason of at least ${MIN_ALLOWLIST_REASON} characters is required`,
      });
    }
  }

  if (allEntries.length > maxEntries) {
    failures.push({
      kind: 'allowlist-too-large',
      key: `${allEntries.length} entries`,
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
    const report = compareBundles(base, loadLocaleBundles(locale, path.join(dir, locale)), { locale });
    const { failures, allowed } = evaluate(report, allowlist, { locale });
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
