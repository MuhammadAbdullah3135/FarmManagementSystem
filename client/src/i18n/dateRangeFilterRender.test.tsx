import { describe, it, expect } from 'vitest';
import { render } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import i18n, { resources } from './index';
import DateRangeFilter from '../components/DateRangeFilter';
import { flatten } from '../../tools/i18n/check-locale-coverage.mjs';

type FlatBundle = Record<string, string>;

const flat = (locale: 'en' | 'es' | 'ar', namespace: string): FlatBundle =>
  flatten((resources as unknown as Record<string, Record<string, unknown>>)[locale][namespace]);

const es = (namespace: string, key: string): string => flat('es', namespace)[key];
const ar = (namespace: string, key: string): string => flat('ar', namespace)[key];
const en = (namespace: string, key: string): string => flat('en', namespace)[key];

describe('DateRangeFilter placeholders are translated, not the English literal', () => {
  const PLACEHOLDER_PAIRS = [
    { namespace: 'common', key: 'fromDatePlaceholder' },
    { namespace: 'common', key: 'toDatePlaceholder' },
  ] as const;

  for (const locale of ['es', 'ar'] as const) {
    for (const { namespace, key } of PLACEHOLDER_PAIRS) {
      it(`${locale}/${key}: placeholder is "${locale === 'es' ? es(namespace, key) : ar(namespace, key)}" in ${locale}, not "${en(namespace, key)}"`, async () => {
        const translated = locale === 'es' ? es(namespace, key) : ar(namespace, key);
        const englishText = en(namespace, key);

        await i18n.changeLanguage(locale);

        const { container } = render(
          <MemoryRouter>
            <Routes>
              <Route path="/" element={<DateRangeFilter onChange={() => {}} />} />
            </Routes>
          </MemoryRouter>,
        );

        // Ant Design renders the DateRangePicker's two date inputs inside the
        // test container; query there rather than the whole document.
        const inputs = Array.from(
          container.querySelectorAll('input'),
        );

        const input = inputs.find(
          (el) => el.getAttribute('placeholder') === translated,
        );

        expect(input).not.toBeNull();
        expect(input?.getAttribute('placeholder')).toBe(translated);

        if (englishText !== translated) {
          // The other input must not carry the English placeholder either.
          const englishInput = inputs.find(
            (el) => el.getAttribute('placeholder') === englishText,
          );
          expect(englishInput).toBeUndefined();
        }
      });
    }
  }
});
