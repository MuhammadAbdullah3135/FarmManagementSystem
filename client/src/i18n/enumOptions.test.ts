import { describe, expect, it } from 'vitest';
import { flatten } from '../../tools/i18n/check-locale-coverage.mjs';
import { resources } from './index';
import allowlist from './locales/untranslated-allowlist.json';
import { ENUM_DEFS } from './enumOptions';

/**
 * The guard for the shared enum-label mechanism.
 *
 * The key-resolution audit cannot see the helper's keys — they are built from
 * `ENUM_DEFS` at runtime, not written as literals at a call site — so this test is
 * what makes "add an enum, forget a translation" a build failure instead of a
 * Spanish screen showing English.
 *
 * Two things are enforced per enum, per key segment:
 *   1. every locale carries the key (the coverage guard already fails on a whole
 *      missing file, this catches a key missing from one file);
 *   2. the value is genuinely translated — different from English unless the
 *      untranslated allowlist names it, the same cognate rule the locales follow.
 */

type FlatBundle = Record<string, string>;

const flat = (locale: 'en' | 'es' | 'ar', namespace: string): FlatBundle =>
  flatten((resources as unknown as Record<string, Record<string, unknown>>)[locale][namespace]);

const allowed = (key: string, locale: 'es' | 'ar'): boolean =>
  allowlist.entries.some(
    (entry) =>
      entry.key === `${namespace}:${key}` &&
      entry.value === flat('en', namespace)[key] &&
      (!('locales' in entry) || entry.locales === undefined || entry.locales.includes(locale)),
  );

const namespace = 'enums';

describe('enum labels', () => {
  for (const [name, def] of Object.entries(ENUM_DEFS)) {
    // The union of shapes collapses keyOf's parameter to never; the table
    // guarantees each value fits its own keyOf, so the cast restates that.
    const keyOf = def.keyOf as (v: string | number) => string;
    const segments = [...new Set(def.values.map((value) => keyOf(value)))];

    it(`resolve in en, es and ar for ${name}`, () => {
      for (const segment of segments) {
        for (const locale of ['en', 'es', 'ar'] as const) {
          const key = `${name}.${segment}`;
          expect(flat(locale, namespace)[key], `${locale}:${namespace}:${key} is missing`).toBeTruthy();
        }
      }
    });

    it(`are genuinely translated for ${name}`, () => {
      const english = flat('en', namespace);
      for (const locale of ['es', 'ar'] as const) {
        const translated = flat(locale, namespace);
        for (const segment of segments) {
          const key = `${name}.${segment}`;
          if (english[key] === translated[key]) {
            expect(allowed(key, locale), `${locale}:${namespace}:${key} equals English and is not allowlisted`).toBe(true);
          }
        }
      }
    });
  }

  it('covers every wire value exactly once, with no unused key segment', () => {
    // A key segment no wire value maps to would silently rot in every locale file.
    const allKeys = Object.keys(flat('en', namespace));
    for (const [name, def] of Object.entries(ENUM_DEFS)) {
      const keyOf = def.keyOf as (v: string | number) => string;
      const used = new Set(def.values.map((value) => `${name}.${keyOf(value)}`));
      const declared = allKeys.filter((key) => key.startsWith(`${name}.`));
      for (const key of declared) expect(used.has(key), `unused enum key ${namespace}:${key}`).toBe(true);
    }
  });
});
