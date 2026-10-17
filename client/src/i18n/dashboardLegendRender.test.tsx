import { describe, it, expect } from 'vitest';
import { render } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { BarChart, Bar, LineChart, Line, XAxis, YAxis, Tooltip, Legend, ResponsiveContainer } from 'recharts';
import i18n, { resources } from './index';
import { flatten } from '../../tools/i18n/check-locale-coverage.mjs';

type FlatBundle = Record<string, string>;

const flat = (locale: 'en' | 'es' | 'ar', namespace: string): FlatBundle =>
  flatten((resources as unknown as Record<string, Record<string, unknown>>)[locale][namespace]);

const es = (namespace: string, key: string): string => flat('es', namespace)[key];
const ar = (namespace: string, key: string): string => flat('ar', namespace)[key];
const en = (namespace: string, key: string): string => flat('en', namespace)[key];

/**
 * Renders a minimal chart that uses t() for legend names exactly like
 * DashboardPage does, so the legend assertions are against the real
 * rendering path and not a mock of it.
 */
const FeedChart = (locale: 'es' | 'ar') => {
  const t = i18n.getFixedT(locale, 'dashboard');
  return (
    <ResponsiveContainer width={300} height={200}>
      <LineChart data={[{ label: 'Sep', quantity: 10, cost: 200 }]}>
        <XAxis dataKey="label" />
        <YAxis />
        <Tooltip />
        <Legend />
        <Line type="monotone" dataKey="quantity" name={t('quantityLegend')} stroke="#1677ff" dot={false} />
        <Line type="monotone" dataKey="cost" name={t('costLegend')} stroke="#52c41a" dot={false} />
      </LineChart>
    </ResponsiveContainer>
  );
};

const ProfitLossChart = (locale: 'es' | 'ar') => {
  const t = i18n.getFixedT(locale, 'dashboard');
  return (
    <ResponsiveContainer width={300} height={200}>
      <BarChart data={[{ monthName: 'September', income: 500, expenses: 300 }]}>
        <XAxis dataKey="monthName" />
        <YAxis />
        <Tooltip />
        <Legend />
        <Bar dataKey="income" name={t('incomeLegend')} fill="#52c41a" />
        <Bar dataKey="expenses" name={t('expensesLegend')} fill="#ff4d4f" />
      </BarChart>
    </ResponsiveContainer>
  );
};

describe('dashboard chart legends are translated, not the English literal', () => {
  const LEGEND_PAIRS = [
    { namespace: 'dashboard', key: 'quantityLegend' },
    { namespace: 'dashboard', key: 'costLegend' },
    { namespace: 'dashboard', key: 'incomeLegend' },
    { namespace: 'dashboard', key: 'expensesLegend' },
  ] as const;

  for (const locale of ['es', 'ar'] as const) {
    for (const { namespace, key } of LEGEND_PAIRS) {
      it(`${locale}/${key}: translated in ${locale}, not the English one`, () => {
        const translated = locale === 'es' ? es(namespace, key) : ar(namespace, key);
        const englishText = en(namespace, key);

        i18n.changeLanguage(locale);

        const { container } = render(
          <MemoryRouter>
            <Routes>
              <Route
                path="/"
                element={
                  key === 'quantityLegend' || key === 'costLegend'
                    ? FeedChart(locale)
                    : ProfitLossChart(locale)
                }
              />
            </Routes>
          </MemoryRouter>,
        );

        // Give recharts time to render the SVG legend
        expect(container.textContent).toContain(translated);

        if (englishText !== translated) {
          // Recharts renders each legend label as its own SVG <text> element,
          // so check the individual text nodes rather than the concatenated
          // textContent (which would make 'Cost' a false positive inside 'Coste').
          const textNodes = Array.from(container.querySelectorAll('svg text')).map(
            (el) => el.textContent,
          );
          expect(textNodes).not.toContain(englishText);
        }
      });
    }
  }
});
