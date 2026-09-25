import { describe, expect, it } from 'vitest';
// The checker is a plain .mjs tool imported directly, so the rules the CI gate enforces
// are tested here rather than trusted. Its own limits are in docs/I18N.md.
import {
  MAX_ALLOWLIST_ENTRIES,
  compareBundles,
  evaluate,
  flatten,
  isPluralVariant,
  logicalKey,
  placeholders,
  pluralCategories,
  readAllowlist,
  runCheck,
} from './check-locale-coverage.mjs';

const bundle = (entries: Record<string, string>) => ({ common: entries });

/** An allowlist with a reason long enough to pass the hygiene rule. */
const allow = (...keys: string[]) => ({
  entries: keys.map((key) => ({ key, value: '', reason: 'a genuine cognate, reviewed by hand' })),
});

describe('flatten', () => {
  it('turns nested resources into the dotted keys i18next looks up', () => {
    expect(flatten({ form: { name: 'Name', tags: { one: 'Tag' } }, heading: 'Head' })).toEqual({
      'form.name': 'Name',
      'form.tags.one': 'Tag',
      heading: 'Head',
    });
  });
});

describe('placeholders', () => {
  it('reads named placeholders regardless of spacing', () => {
    expect(placeholders('Adds up with {{total}} and {{ count }}')).toEqual(['count', 'total']);
  });
});

describe('compareBundles', () => {
  it('reports a complete mirror as having no issues', () => {
    const report = compareBundles(bundle({ one: 'One', two: 'Two' }), bundle({ one: 'Uno', two: 'Dos' }));

    expect(report.checked).toBe(2);
    expect(report.issues).toEqual([]);
    expect(report.identical).toEqual([]);
  });

  it('catches a key the translation is missing', () => {
    const report = compareBundles(bundle({ one: 'One', two: 'Two' }), bundle({ one: 'Uno' }));

    expect(report.issues.map((issue) => issue.kind)).toEqual(['missing-key']);
    expect(report.issues[0].key).toBe('common:two');
  });

  it('catches a key the translation invented', () => {
    const report = compareBundles(bundle({ one: 'One' }), bundle({ one: 'Uno', three: 'Tres' }));

    expect(report.issues.map((issue) => issue.kind)).toEqual(['extra-key']);
    expect(report.issues[0].key).toBe('common:three');
  });

  it('catches a blank value, which reads to the user as a blank label', () => {
    const report = compareBundles(bundle({ one: 'One' }), bundle({ one: '   ' }));

    expect(report.issues.map((issue) => issue.kind)).toEqual(['empty-value']);
  });

  it('catches a renamed placeholder, which would silently interpolate nothing', () => {
    const report = compareBundles(
      bundle({ one: 'Cannot exceed {{max}} characters' }),
      bundle({ one: 'No puede superar {{limite}} caracteres' }),
    );

    expect(report.issues.map((issue) => issue.kind)).toEqual(['placeholder-drift']);
    expect(report.issues[0].detail).toContain('max');
  });

  it('catches a lost line break, which collapses two lines into one', () => {
    const report = compareBundles(bundle({ one: 'First\nSecond' }), bundle({ one: 'Primero Segundo' }));

    expect(report.issues.map((issue) => issue.kind)).toEqual(['line-break-drift']);
  });

  it('reports a whole missing namespace rather than 40 missing keys', () => {
    const report = compareBundles({ common: { one: 'One' }, health: { two: 'Two' } }, { common: { one: 'Uno' } });

    expect(report.issues).toEqual([
      { kind: 'missing-namespace', key: 'health', detail: expect.any(String) },
    ]);
  });
});

describe('plural forms', () => {
  it('reads the base name out of a plural key', () => {
    expect(logicalKey('intervalDays_few')).toBe('intervalDays');
    expect(logicalKey('intervalDays')).toBe('intervalDays');
    // A key that merely ends in an underscore-word is not a plural form.
    expect(logicalKey('signIn_andOut')).toBe('signIn_andOut');
    expect(isPluralVariant('moreAlerts_two')).toBe(true);
    expect(isPluralVariant('moreAlerts')).toBe(false);
  });

  it('accepts a language with more plural forms than English', () => {
    // Arabic says "{{count}} days" six ways where English has one string. That is coverage,
    // not six extra keys and a missing one — the mistake this rule exists to prevent.
    const report = compareBundles(
      bundle({ intervalDays: '{{count}} days' }),
      bundle({
        intervalDays_zero: 'لا أيام',
        intervalDays_one: 'يوم واحد',
        intervalDays_two: 'يومان',
        intervalDays_few: '{{count}} أيام',
        intervalDays_many: '{{count}} يومًا',
        intervalDays_other: '{{count}} يوم',
      }),
      { locale: 'ar' },
    );

    expect(report.issues).toEqual([]);
    expect(report.checked).toBe(1);
  });

  it('reads the plural categories the language itself needs, from ICU', () => {
    // The data i18next itself resolves counts against, so the rule below cannot drift from it.
    expect(pluralCategories('en').sort()).toEqual(['one', 'other']);
    expect(pluralCategories('ar').sort()).toEqual(['few', 'many', 'one', 'other', 'two', 'zero']);
    expect(pluralCategories('es').sort()).toEqual(['many', 'one', 'other']);
  });

  it('catches a form the language needs and the translation lost', () => {
    // Arabic without `_many` still *renders* — i18next falls back to `_other` — so nothing on
    // screen would show the Arabic for 40 reading like the Arabic for 100. Only this notices.
    const report = compareBundles(
      bundle({ intervalDays: '{{count}} days' }),
      bundle({
        intervalDays_zero: 'لا أيام',
        intervalDays_one: 'يوم واحد',
        intervalDays_two: 'يومان',
        intervalDays_few: '{{count}} أيام',
        intervalDays_other: '{{count}} يوم',
      }),
      { locale: 'ar' },
    );

    expect(report.issues.map((issue) => issue.kind)).toEqual(['missing-plural-form']);
    expect(report.issues[0].detail).toContain('intervalDays_many');
  });

  it('lets a plural form drop the count, which the dual form carries in the word itself', () => {
    const report = compareBundles(
      bundle({ intervalDays: '{{count}} days' }),
      bundle({ intervalDays_one: 'يوم واحد', intervalDays_other: '{{count}} يوم' }),
      { locale: 'ar' },
    );

    // The two forms it does have are fine; the four it does not have are the failure.
    expect(report.issues.map((issue) => issue.kind)).toEqual(['missing-plural-form']);
  });

  it('refuses a placeholder English does not have, which is what a rename looks like', () => {
    const report = compareBundles(
      bundle({ intervalDays: '{{count}} days' }),
      bundle({ intervalDays_other: '{{total}} يوم' }),
      { locale: 'ar' },
    );

    expect(report.issues.map((issue) => issue.kind)).toEqual([
      'missing-plural-form',
      'placeholder-drift',
    ]);
  });

  it('requires the _other form, i18next\'s fallback inside a language, on its own', () => {
    const report = compareBundles(
      bundle({ intervalDays: '{{count}} days' }),
      bundle({ intervalDays_one: 'يوم واحد' }),
      { locale: 'ar' },
    );

    // Reported alone: with `_other` missing, that is the one fact worth acting on.
    expect(report.issues.map((issue) => issue.kind)).toEqual(['plural-forms-without-other']);
  });

  it('still counts a plural form left in English as untranslated', () => {
    const report = compareBundles(
      bundle({ moreAlerts_one: '{{count}} more alert', moreAlerts_other: '{{count}} more alerts' }),
      bundle({ moreAlerts_one: '{{count}} more alert', moreAlerts_other: '{{count}} alertas más' }),
      { locale: 'es' },
    );

    expect(report.identical).toEqual(['common:moreAlerts_one']);
  });
});

describe('evaluate (the rules the build fails on)', () => {
  const mirror = compareBundles(bundle({ one: 'One' }), bundle({ one: 'Uno' }));

  it('fails a value left in English that nobody justified', () => {
    const untranslated = compareBundles(bundle({ one: 'One' }), bundle({ one: 'One' }));
    const { failures } = evaluate(untranslated, allow());

    expect(failures.map((failure) => failure.kind)).toEqual(['untranslated']);
    expect(failures[0].key).toBe('common:one');
  });

  it('accepts the same value once it is on the allowlist with a reason', () => {
    const untranslated = compareBundles(bundle({ one: 'One' }), bundle({ one: 'One' }));
    const { failures, allowed } = evaluate(untranslated, allow('common:one'));

    expect(failures).toEqual([]);
    expect(allowed).toBe(1);
  });

  it('fails an allowlist entry with no reason, so the list cannot become a rubber stamp', () => {
    const untranslated = compareBundles(bundle({ one: 'One' }), bundle({ one: 'One' }));
    const { failures } = evaluate(untranslated, {
      entries: [{ key: 'common:one', value: 'One', reason: '' }],
    });

    expect(failures.map((failure) => failure.kind)).toEqual(['allowlist-reason-missing']);
  });

  it('fails a stale entry: one that is no longer identical to English', () => {
    const { failures } = evaluate(mirror, allow('common:one'));

    expect(failures.map((failure) => failure.kind)).toEqual(['allowlist-stale']);
  });

  it('fails when the allowlist itself gets too big to review', () => {
    const { failures } = evaluate(mirror, allow('common:one'), { maxEntries: 0 });

    expect(failures.map((failure) => failure.kind)).toContain('allowlist-too-large');
  });

  it('keeps the shipped cap low enough that growing it is a deliberate act', () => {
    expect(MAX_ALLOWLIST_ENTRIES).toBeLessThanOrEqual(30);
  });

  it('applies an entry only to the locales it was reviewed for', () => {
    const untranslated = compareBundles(bundle({ total: 'Total' }), bundle({ total: 'Total' }));
    const spanishOnly = {
      entries: [
        { key: 'common:total', value: 'Total', reason: 'a genuine cognate, reviewed by hand', locales: ['es'] },
      ],
    };

    expect(evaluate(untranslated, spanishOnly, { locale: 'es' }).failures).toEqual([]);
    // For Arabic the entry does not apply, so the value is unexcused — and, correctly, not
    // also reported as stale: it is a reviewed Spanish cognate, not an obsolete row.
    expect(evaluate(untranslated, spanishOnly, { locale: 'ar' }).failures.map((f) => f.kind)).toEqual([
      'untranslated',
    ]);
  });
});

describe('the repository as it stands', () => {
  it('has no locale-coverage gaps and a justified allowlist', () => {
    const { locales, total } = runCheck();

    expect(locales.map((locale) => locale.locale)).toEqual(['ar', 'es']);
    for (const locale of locales) {
      expect(locale.failures).toEqual([]);
      // Every English key has a value in this locale — the number the build reports.
      expect(locale.checked).toBe(total);
    }
    expect(total).toBeGreaterThan(1000);
  });

  it('keeps the allowlist small, so it cannot hide a half-finished translation', () => {
    const { entries } = readAllowlist();

    expect(entries.length).toBeGreaterThan(0);
    expect(entries.length).toBeLessThanOrEqual(MAX_ALLOWLIST_ENTRIES);
    for (const entry of entries) {
      expect(entry.reason.length).toBeGreaterThanOrEqual(25);
      expect(entry.value).toBe(entry.value.trim());
    }
  });
});
