import { describe, expect, it } from 'vitest';
import i18n, { NAMESPACES, resources } from './index';
import { SUPPORTED_LOCALES } from './locale';
// The comparison rules are the checker's, not a second copy of them: this file asserts the
// bundles the app actually *loads*, the checker asserts what the build ships. Importing the
// pure functions means a rule cannot be true in one place and false in the other.
import {
  MAX_ALLOWLIST_ENTRIES,
  compareBundles,
  evaluate,
  flatten,
  logicalKey,
  readAllowlist,
} from '../../tools/i18n/check-locale-coverage.mjs';

/** Every locale except English — the ones that have to be complete mirrors. */
const TRANSLATED_LOCALES = SUPPORTED_LOCALES.filter((locale) => locale !== 'en');

const table = (locale: string, namespace: string): Record<string, string> =>
  flatten((resources as unknown as Record<string, Record<string, unknown>>)[locale][namespace]);

/** Every English key, flattened, across every namespace — the coverage test's denominator. */
const englishKeys = (): Record<string, Record<string, string>> =>
  Object.fromEntries(NAMESPACES.map((ns) => [ns, table('en', ns)]));

const keysFor = (locale: string): Record<string, Record<string, string>> =>
  Object.fromEntries(NAMESPACES.map((ns) => [ns, table(locale, ns)]));

const englishKeyCount = () =>
  Object.values(englishKeys()).reduce((total, ns) => total + Object.keys(ns).length, 0);

describe.each(TRANSLATED_LOCALES)('%s resources', (locale) => {
  it('are registered for every namespace, so a screen cannot find only half of them', () => {
    for (const ns of NAMESPACES) {
      expect(i18n.hasResourceBundle('en', ns)).toBe(true);
      expect(i18n.hasResourceBundle(locale, ns)).toBe(true);
    }
  });

  it('cover every English key, as a complete mirror', () => {
    const report = compareBundles(englishKeys(), keysFor(locale), { locale });

    // Printed rather than only asserted: the claim this subphase makes is a specific
    // number, and `1334/1334` in the run output is checkable evidence, not a green tick.
    console.log(`locale coverage: ${locale} ${report.checked - report.issues.length}/${report.checked} keys`);

    expect(report.issues).toEqual([]);
    expect(report.checked).toBeGreaterThan(1000);
    // The denominator is the English key count, not this locale's — which is what lets a
    // language with six plural forms where English has one still score a full 100%.
    expect(report.checked).toBe(englishKeyCount());
  });

  it('are genuinely translated, not English copied across', () => {
    const report = compareBundles(englishKeys(), keysFor(locale), { locale });
    const { failures, allowed } = evaluate(report, readAllowlist(), { locale });

    expect(failures).toEqual([]);
    expect(allowed).toBe(report.identical.length);

    // The allowlist is the only way a value may match English, and it is small. If a
    // future "translation" were produced by copying the English file, this fraction
    // collapses and the line below fails long before a reader notices.
    const translatedFraction = 1 - report.identical.length / report.checked;
    console.log(`locale coverage: ${(translatedFraction * 100).toFixed(1)}% of ${locale} values differ from English`);
    expect(translatedFraction).toBeGreaterThan(0.95);
  });

  it('renders its own wording for a key whose English text differs', () => {
    // A direct check on the registered bundle, independent of the file comparison: the
    // string a reader is shown is not the English one.
    expect(table(locale, 'nav').dashboard).not.toBe(table('en', 'nav').dashboard);
  });
});

describe('plural forms', () => {
  it('gives Arabic every form its own plural rules ask for, where English has two', () => {
    // The one place Arabic needs *more* keys than English rather than the same ones. i18next
    // resolves `intervalDays` through `Intl.PluralRules('ar')`, which selects from six
    // categories, so a missing form falls back to English mid-sentence.
    const arabic = table('ar', 'health');
    const english = table('en', 'health');

    expect(logicalKey('intervalDays_few')).toBe('intervalDays');
    expect(Object.keys(english)).toContain('intervalDays');
    for (const form of ['zero', 'one', 'two', 'few', 'many', 'other']) {
      expect(arabic).toHaveProperty(`intervalDays_${form}`);
    }
    // The counted forms interpolate the number; the dual and zero forms carry it in the
    // word itself, which is why the guard's placeholder rule is a subset rather than equality.
    expect(arabic.intervalDays_other).toContain('{{count}}');
    expect(arabic.intervalDays_two).not.toContain('{{count}}');
  });

  it('keeps the same plural surface in every locale that translates it', () => {
    for (const locale of TRANSLATED_LOCALES) {
      const forms = Object.keys(table(locale, 'health')).filter((key) =>
        key.startsWith('intervalDays_'),
      );
      const reported = compareBundles(englishKeys(), keysFor(locale), { locale });

      expect(reported.issues).toEqual([]);
      // English ships the bare key, so a locale may either translate it as one string or
      // expand it into its own forms — but it may not silently do neither.
      expect(forms.length > 0 || 'intervalDays' in table(locale, 'health')).toBe(true);
    }
  });
});

describe('the list of values left in English', () => {
  it('stays short and carries a reason per entry', () => {
    const { entries } = readAllowlist();

    expect(entries.length).toBeGreaterThan(0);
    expect(entries.length).toBeLessThanOrEqual(MAX_ALLOWLIST_ENTRIES);
    for (const entry of entries) {
      // A reason, not a restatement: every entry explains *why* the languages share the
      // word (a cognate, a unit symbol, an acronym) — and for a locale-restricted entry,
      // why the other language does not.
      expect(entry.reason.length).toBeGreaterThanOrEqual(25);
      expect(entry.reason.toLowerCase()).not.toBe(entry.value.toLowerCase());
    }
  });

  it('restricts an entry to the locales it was reviewed for', () => {
    // An entry with no `locales` applies everywhere and must be identical there; an entry
    // with `locales` is only claimed for those languages, so this asserts each locale's
    // allowlisted values really are shared with English *in that locale*.
    const { entries } = readAllowlist();

    for (const locale of TRANSLATED_LOCALES) {
      const applicable = entries.filter((entry) => !entry.locales || entry.locales.includes(locale));
      const table = (key: string) => keysFor(locale)[key.split(':')[0]]?.[key.slice(key.indexOf(':') + 1)];

      for (const entry of applicable) {
        expect(table(entry.key)).toBe(entry.value);
      }
    }
  });
});
