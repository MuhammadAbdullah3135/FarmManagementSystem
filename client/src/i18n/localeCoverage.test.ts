import { describe, expect, it } from 'vitest';
import i18n, { NAMESPACES, resources } from './index';
// The comparison rules are the checker's, not a second copy of them: this file asserts the
// bundles the app actually *loads*, the checker asserts what the build ships. Importing the
// pure functions means a rule cannot be true in one place and false in the other.
import {
  MAX_ALLOWLIST_ENTRIES,
  compareBundles,
  evaluate,
  flatten,
  readAllowlist,
} from '../../tools/i18n/check-locale-coverage.mjs';

const table = (locale: string, namespace: string): Record<string, string> =>
  flatten((resources as unknown as Record<string, Record<string, unknown>>)[locale][namespace]);

/** Every English key, flattened, across every namespace — the coverage test's denominator. */
const englishKeys = (): Record<string, Record<string, string>> =>
  Object.fromEntries(NAMESPACES.map((ns) => [ns, table('en', ns)]));

const spanishKeys = (): Record<string, Record<string, string>> =>
  Object.fromEntries(NAMESPACES.map((ns) => [ns, table('es', ns)]));

describe('Spanish resources', () => {
  it('are registered for every namespace, so a screen cannot find only half of them', () => {
    for (const ns of NAMESPACES) {
      expect(i18n.hasResourceBundle('en', ns)).toBe(true);
      expect(i18n.hasResourceBundle('es', ns)).toBe(true);
    }
  });

  it('cover every English key, as a complete mirror', () => {
    const report = compareBundles(englishKeys(), spanishKeys());

    // Printed rather than only asserted: the claim this subphase makes is a specific
    // number, and `1327/1327` in the run output is checkable evidence, not a green tick.
    console.log(`locale coverage: es ${report.checked - report.issues.length}/${report.checked} keys`);

    expect(report.issues).toEqual([]);
    expect(report.checked).toBeGreaterThan(1000);
    expect(report.checked).toBe(Object.values(englishKeys()).reduce((n, ns) => n + Object.keys(ns).length, 0));
  });

  it('are genuinely translated, not English copied across', () => {
    const report = compareBundles(englishKeys(), spanishKeys());
    const { failures, allowed } = evaluate(report, readAllowlist());

    expect(failures).toEqual([]);
    expect(allowed).toBe(report.identical.length);

    // The allowlist is the only way a value may match English, and it is small. If a
    // future "translation" were produced by copying the English file, this fraction
    // collapses and the line below fails long before a reader notices.
    const translatedFraction = 1 - report.identical.length / report.checked;
    console.log(`locale coverage: ${(translatedFraction * 100).toFixed(1)}% of values differ from English`);
    expect(translatedFraction).toBeGreaterThan(0.95);
  });

  it('renders the Spanish wording for a key whose English text differs', () => {
    // A direct check on the registered bundle, independent of the file comparison: the
    // string a Spanish reader is shown is not the English one.
    const spanish = flatten((resources as unknown as Record<string, Record<string, unknown>>).es.nav);
    const english = flatten((resources as unknown as Record<string, Record<string, unknown>>).en.nav);

    expect(spanish.dashboard).not.toBe(english.dashboard);
    expect(spanish).toHaveProperty('dashboard');
  });

  it('keeps the list of untranslated values short and each entry explained', () => {
    const { entries } = readAllowlist();

    expect(entries.length).toBeLessThanOrEqual(MAX_ALLOWLIST_ENTRIES);
    for (const entry of entries) {
      // A reason, not a restatement: every entry in the list today explains *why* the
      // two languages share the word (a cognate, a unit symbol, an acronym).
      expect(entry.reason.length).toBeGreaterThanOrEqual(25);
      expect(entry.reason.toLowerCase()).not.toBe(entry.value.toLowerCase());
    }
  });
});
