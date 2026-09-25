import { describe, expect, it } from 'vitest';
// The checker is a plain .mjs tool imported directly, so the rules the CI gate enforces
// are tested here rather than trusted. Its own limits are in docs/I18N.md.
import {
  MAX_ALLOWLIST_ENTRIES,
  compareBundles,
  evaluate,
  flatten,
  placeholders,
  readAllowlist,
  runCheck,
} from './check-locale-coverage.mjs';

const bundle = (entries: Record<string, string>) => ({ common: entries });

/** An allowlist with a reason long enough to pass the hygiene rule. */
const allow = (...keys: string[]) => ({
  byKey: new Map(keys.map((key) => [key, { key, value: '', reason: 'a genuine cognate, reviewed by hand' }])),
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
      byKey: new Map([['common:one', { key: 'common:one', value: 'One', reason: '' }]]),
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
});

describe('the repository as it stands', () => {
  it('has no locale-coverage gaps and a justified allowlist', () => {
    const { locales, total } = runCheck();

    expect(locales.map((locale) => locale.locale)).toEqual(['es']);
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
