import { describe, expect, it } from 'vitest';
// The checker is a plain .mjs tool, imported directly so the rule it enforces is
// tested rather than assumed. Its own limits are documented in docs/I18N.md.
import { baselineIdsFrom, buildBaselineEntries, DEFAULT_BASELINE_REASON, newStrings, scanSource } from './check-hardcoded-strings.mjs';

const scan = (code: string) => scanSource('fixture.tsx', code);

describe('scanSource', () => {
  it('flags JSX text that is not translated', () => {
    const found = scan('const A = () => <Button>Save</Button>;');
    expect(found.some((v) => v.kind === 'jsx-text' && v.value === 'Save')).toBe(true);
  });

  it('accepts JSX text rendered through t()', () => {
    const found = scan("const A = () => <Button>{t('save')}</Button>;");
    expect(found.some((v) => v.kind === 'jsx-text')).toBe(false);
  });

  it('flags a user-facing attribute literal', () => {
    const found = scan('const A = () => <Input placeholder="Email" />;');
    expect(found.some((v) => v.kind === 'attribute' && v.value === 'Email')).toBe(true);
  });

  it('ignores attribute literals that are not user copy', () => {
    const found = scan('const A = () => <Button type="primary" htmlType="submit">x</Button>;');
    expect(found.some((v) => v.kind === 'attribute')).toBe(false);
  });

  it('flags a hardcoded toast message', () => {
    const found = scan("const A = () => { message.success('Animal created'); };");
    expect(found.some((v) => v.kind === 'toast' && v.value === 'Animal created')).toBe(true);
  });

  it('ignores data strings passed to functions', () => {
    // A modal kind, not copy: translating it would change behaviour.
    const found = scan("const A = () => { openModal('animalType'); };");
    expect(found.some((v) => v.value === 'animalType')).toBe(false);
  });
  it('flags a label literal nested inside a JSX attribute array (Tabs items)', () => {
    // The regression this fixture pins: the identical literal as a plain object
    // property was always flagged, but inside items={[...]} the old scanner
    // returned early on the non-string initializer and never looked inside.
    const found = scan("const A = () => <Tabs items={[{ key: 'types', label: 'Feed Types' }]} />;");
    expect(found.some((v) => v.kind === 'property' && v.value === 'Feed Types')).toBe(true);
  });

  it('flags a user-facing property inside an inline object attribute', () => {
    const found = scan("const A = () => <Card extra={{ label: 'More', other: 1 }} />;");
    expect(found.some((v) => v.kind === 'property' && v.value === 'More')).toBe(true);
  });

  it('accepts a label rendered through t() inside a JSX attribute array', () => {
    const found = scan("const A = () => <Tabs items={[{ key: 'types', label: t('feed:feedTypes') }]} />;");
    expect(found.some((v) => v.value.includes('feedTypes'))).toBe(false);
  });

  it('flags the raw value side of an untranslated option pair, not the wire value', () => {
    const found = scan("const A = () => <Select options={[{ value: 'Create', label: 'Create' }]} />;");
    expect(found.filter((v) => v.value === 'Create')).toHaveLength(1);
  });
});

describe('newStrings (the CI gate)', () => {
  it('catches an intentionally introduced hardcoded string', () => {
    const introduced = scan('const A = () => <Button>Save</Button>;');
    expect(newStrings(introduced, new Set())).toHaveLength(1);
    expect(newStrings(introduced, new Set())[0]).toContain('jsx-text::Save');
  });

  it('stays quiet for a string already reviewed in the baseline', () => {
    const reviewed = scan('const A = () => <Button>Save</Button>;');
    const baseline = new Set(newStrings(reviewed, new Set()));
    expect(newStrings(reviewed, baseline)).toHaveLength(0);
  });
});
describe('baseline schema (id + reason)', () => {
  it('reads bare-string entries from the pre-reason format', () => {
    expect(baselineIdsFrom({ strings: ['a::b::c'] })).toEqual(new Set(['a::b::c']));
  });

  it('reads { id, reason } entries, ignoring unrelated fields', () => {
    const ids = baselineIdsFrom({ strings: [{ id: 'a::b::c', reason: '7.5 deferred' }, { id: 'd::e::f', note: 'x' }] });
    expect([...ids].sort()).toEqual(['a::b::c', 'd::e::f']);
  });

  it('builds entries that preserve an existing reason and tag new ids with the default', () => {
    const entries = buildBaselineEntries(
      [{ id: 'old::x::y', reason: 'legacy' }, 'old::bare::string'],
      ['old::x::y', 'old::bare::string', 'new::z::w'],
    );
    expect(entries).toEqual([
      { id: 'new::z::w', reason: DEFAULT_BASELINE_REASON },
      { id: 'old::bare::string', reason: DEFAULT_BASELINE_REASON },
      { id: 'old::x::y', reason: 'legacy' },
    ]);
  });
});
