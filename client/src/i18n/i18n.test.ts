import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import i18next from 'i18next';
import i18n, { NAMESPACES, humanizeMissingKey, resources } from './index';

const SRC = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

function walk(dir: string, acc: string[] = []): string[] {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, entry.name);
    if (entry.isDirectory()) walk(p, acc);
    else if (/\.tsx?$/.test(p) && !/\.test\.tsx?$/.test(p)) acc.push(p);
  }
  return acc;
}

/** Comments can mention `t('…')` as prose; none of them should be treated as a usage. */
const stripComments = (code: string) =>
  code.replace(/\/\*[\s\S]*?\*\//g, '').replace(/(^|\s)\/\/[^\n]*/g, '$1');

const tableFor = (ns: string): Record<string, string> =>
  ((resources.en as unknown as Record<string, Record<string, string>>)[ns] ?? {});

/**
 * i18next's plural suffixes. A call site that passes `count` resolves `key_one` / `key_other`
 * rather than the bare `key`, so a key that exists only as plural forms is not a missing key
 * — the source is naming a set, and the count selects from it at runtime.
 */
const PLURAL_SUFFIXES = ['zero', 'one', 'two', 'few', 'many', 'other', 'plural'];

/** True when the namespace holds this key, or any plural form of it. */
const resolvesIn = (ns: string, key: string): boolean => {
  const table = tableFor(ns);
  if (table[key] !== undefined) return true;
  return PLURAL_SUFFIXES.some((suffix) => table[`${key}_${suffix}`] !== undefined);
};

describe('english resources', () => {
  it('initialises with every namespace and is ready before the first render', () => {
    expect(i18n.isInitialized).toBe(true);
    for (const ns of NAMESPACES) {
      expect(i18n.hasResourceBundle('en', ns)).toBe(true);
    }
  });

  it('resolves every key the source actually uses', () => {
    const missing: string[] = [];
    let checked = 0;

    for (const file of walk(SRC)) {
      const code = stripComments(fs.readFileSync(file, 'utf8'));
      const namespaces = [...code.matchAll(/useTranslation\('([A-Za-z]+)'\)/g)].map((m) => m[1]);
      if (namespaces.length === 0) continue;
      const rel = path.relative(SRC, file).split(path.sep).join('/');

      for (const match of code.matchAll(/\b(?:t|translate)\((['"])([^'"]+)\1/g)) {
        const raw = match[2];
        // `nav:dashboard` names its namespace; a bare key belongs to the file's own.
        const [ns, key] = raw.includes(':') ? raw.split(':') : [namespaces[0], raw];
        checked += 1;
        if (!key) continue;
        if (!resolvesIn(ns, key)) missing.push(`${rel} → ${ns}:${key}`);
      }
    }

    expect(checked).toBeGreaterThan(1000);
    expect(missing).toEqual([]);
  });

  it('never renders a raw key: a totally missing key is humanised, not echoed', () => {
    const rendered = humanizeMissingKey('animals.form.someUnwrittenKey');
    expect(rendered).not.toContain('.');
    expect(rendered).toBe('Some unwritten key');
  });
});

describe('fallback behaviour', () => {
  it('falls back to English silently when the active locale lacks a key', async () => {
    const instance = i18next.createInstance();
    await instance.init({
      resources: {
        en: { common: { greet: 'Hello', only: 'English only' } },
        fr: { common: { greet: 'Bonjour' } },
      },
      lng: 'fr',
      fallbackLng: 'en',
      defaultNS: 'common',
      initAsync: false,
    });

    expect(instance.t('greet')).toBe('Bonjour');
    // Present in French it uses French; absent, it is the English string, not "fr.common.only".
    expect(instance.t('only')).toBe('English only');
  });

  it('keeps behaving when a locale bundle is entirely empty', async () => {
    const instance = i18next.createInstance();
    await instance.init({
      resources: { en: { common: { greet: 'Hello' } }, de: { common: {} } },
      lng: 'de',
      fallbackLng: 'en',
      defaultNS: 'common',
      initAsync: false,
    });

    expect(instance.t('greet')).toBe('Hello');
  });
});
