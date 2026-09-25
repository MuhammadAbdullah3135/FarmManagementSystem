import { describe, expect, it } from 'vitest';
// The checker is a plain .mjs tool, imported directly so the rule it enforces is
// tested rather than assumed. Its own limits are documented in docs/I18N.md.
import { newStrings, scanSource } from './check-hardcoded-strings.mjs';

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
