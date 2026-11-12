// CI guard for the tooltip pass: every locale ships every tooltip key, and no
// tooltip copy runs past the style guide's 120-character limit.
//
// What it enforces:
//
//   1. every locale directory ships a tooltip.json — a missing file is a missing
//      key for every key English has;
//   2. every key English ships exists in every other locale, either spelled the
//      same way or as one of the plural forms a language needs in place of a
//      single key (Arabic's six, where English has one);
//   3. no value in any locale, English included, is longer than 120 characters.
//
// Length is measured on the stored string, placeholder and all: `{{count}}` is nine
// characters and renders as a handful of digits, so the stored measure is the
// stricter of the two and a copy that passes here passes after interpolation.
//
// What it deliberately does NOT do: judge whether a value is really translated —
// `client/tools/i18n/check-locale-coverage.mjs` owns that, with its allowlist. This
// guard owns "is it here, and is it short".
//
// Usage:
//   node scripts/check-tooltips.mjs
//   npm run check:tooltips          (from client/)

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

/** The style guide's limit, duplicated from FieldTooltip.tsx (a .mjs script cannot import TS). */
export const MAX_CHARS = 120;

const NAMESPACE_FILE = 'tooltip.json';

/** i18next's plural suffixes; a key ending in one of these stands in for the base key. */
const PLURAL_SUFFIXES = ['zero', 'one', 'two', 'few', 'many', 'other', 'plural'];

const here = path.dirname(fileURLToPath(import.meta.url));
export const LOCALES_DIR = path.resolve(here, '../client/src/i18n/locales');

/** Flattens `{ a: { b: 'x' } }` into `{ 'a.b': 'x' }`, the form keys are compared in. */
export function flatten(value, prefix = '', out = {}) {
  for (const [key, child] of Object.entries(value)) {
    const full = prefix ? `${prefix}.${key}` : key;
    if (child && typeof child === 'object' && !Array.isArray(child)) {
      flatten(child, full, out);
    } else {
      out[full] = child;
    }
  }
  return out;
}

/** A base key counts as present when the locale has it or any plural form of it. */
export function isPresent(keys, base) {
  return keys.has(base) || PLURAL_SUFFIXES.some((suffix) => keys.has(`${base}_${suffix}`));
}

/** Runs the whole check; returns what `main` prints and exits on. */
export function runCheck({ dir = LOCALES_DIR } = {}) {
  const errors = [];
  const locales = fs
    .readdirSync(dir, { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .map((entry) => entry.name)
    .sort();

  if (!locales.includes('en')) {
    return { errors: [`no English source locale in ${dir}`], locales, keyCount: 0 };
  }

  const read = (locale) => {
    const file = path.join(dir, locale, NAMESPACE_FILE);
    if (!fs.existsSync(file)) return null;
    return flatten(JSON.parse(fs.readFileSync(file, 'utf8')));
  };

  const english = read('en');
  if (!english) {
    return { errors: [`en is missing ${NAMESPACE_FILE}`], locales, keyCount: 0 };
  }
  const englishKeys = new Set(Object.keys(english));

  for (const locale of locales) {
    const values = read(locale);
    if (!values) {
      errors.push(`${locale}: missing ${NAMESPACE_FILE}`);
      continue;
    }

    const keys = new Set(Object.keys(values));
    for (const key of englishKeys) {
      if (!isPresent(keys, key)) {
        errors.push(`${locale}: missing key "${key}"`);
      }
    }

    for (const [key, value] of Object.entries(values)) {
      if (typeof value === 'string' && value.length > MAX_CHARS) {
        errors.push(`${locale}: "${key}" is ${value.length} characters (limit ${MAX_CHARS})`);
      }
    }
  }

  return { errors, locales, keyCount: englishKeys.size };
}

function main() {
  const { errors, locales, keyCount } = runCheck();

  if (errors.length > 0) {
    console.error(`tooltip locale check failed (${errors.length} problem(s)):`);
    for (const error of errors) console.error(`  - ${error}`);
    console.error(
      '\nEvery locale needs every key from en/tooltip.json, each value at most ' +
        `${MAX_CHARS} characters. See docs/I18N.md for how a key is added.`,
    );
    return 1;
  }

  console.log(
    `tooltip locale check passed: ${locales.length} locale(s), ${keyCount} English key(s), ` +
      `every value <= ${MAX_CHARS} characters`,
  );
  return 0;
}

if (import.meta.url === `file://${process.argv[1]}` || process.argv[1]?.endsWith('check-tooltips.mjs')) {
  process.exit(main());
}
