import { afterEach, describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import i18n from './index';
import {
  formatDate,
  formatDateLong,
  formatDateTime,
  formatInteger,
  formatMoney,
  formatNumber,
  formatPercent,
  formatTime,
  localeTag,
  useFormat,
} from './format';

/**
 * Two things are being pinned here, and the second one is the reason this file exists:
 *
 *   1. English output is *unchanged* — `2026-09-24`, `$1,234.57`. Every existing assertion
 *      in the suite, and every screenshot anyone remembers, still describes the app.
 *   2. A bare `$` stays a bare `$` in every language. Only the grouping and decimal
 *      separators follow the language. There is no currency setting yet, so inventing a
 *      symbol or moving it would silently change what a farm's numbers mean.
 */
afterEach(async () => {
  await i18n.changeLanguage('en');
});

describe('dates', () => {
  it('keeps the English forms exactly as they were', () => {
    expect(formatDate('2026-09-24T10:00:00Z', 'en')).toBe('2026-09-24');
    expect(formatDateTime('2026-09-24T14:05:00Z', 'en')).toBe('2026-09-24 14:05');
    expect(formatDateLong('2026-08-03T00:00:00Z', 'en')).toBe('Aug 3, 2026');
  });

  it('writes dates the way the language writes them', () => {
    // Day first, and a month name in Spanish: the two things a reader notices first.
    expect(formatDate('2026-09-24T10:00:00Z', 'es')).toBe('24/09/2026');
    expect(formatDateTime('2026-09-24T14:05:00Z', 'es')).toBe('24/09/2026 14:05');
    expect(formatDateLong('2026-08-03T00:00:00Z', 'es')).toContain('ago');
  });

  it('writes Arabic dates day-first, with an Arabic month name', () => {
    // `<day>/<month>/<year>` with Latin digits, the same numerals a farm types into a tag
    // number, and the month named in Arabic — see `localeTag` for why the digits stay 0-9.
    expect(formatDate('2026-09-24T10:00:00Z', 'ar')).toBe('24/09/2026');
    expect(formatDateTime('2026-09-24T14:05:00Z', 'ar')).toBe('24/09/2026 14:05');
    expect(formatDateLong('2026-08-03T00:00:00Z', 'ar')).toContain('أغسطس');
    expect(formatDateLong('2026-08-03T00:00:00Z', 'ar')).toContain('2026');
    expect(formatTime('2026-09-24T14:05:00Z', 'ar')).toBe('14:05');
  });

  it('renders an empty string for a missing date rather than "Invalid Date"', () => {
    expect(formatDate(null, 'es')).toBe('');
    expect(formatDate('not a date', 'es')).toBe('');
    expect(formatDate(undefined, 'en')).toBe('');
    expect(formatDate(null, 'ar')).toBe('');
  });
});

describe('numbers', () => {
  it('decimalises per language', () => {
    expect(formatNumber(1234.5, 'en')).toBe('1,234.5');
    // A comma for the decimals — and no thousands separator: Spanish groups only from five
    // digits up (CLDR's minimumGroupingDigits for es), so `1234,5` is correct Spanish and
    // not a locale that failed to load.
    expect(formatNumber(1234.5, 'es')).toBe('1234,5');
  });

  it('groups large numbers with the language separator', () => {
    expect(formatInteger(43016, 'en')).toBe('43,016');
    expect(formatInteger(43016, 'es')).toBe('43.016');
    expect(formatNumber(12345.6, 'es')).toBe('12.345,6');
  });

  it("follows the app language, not the browser's — the behaviour change of 7.2", () => {
    // jsdom reports en-US. Before 7.2 these numbers went through `toLocaleString()` with no
    // locale argument, so they followed the *browser*: a Spanish-speaking user on an
    // en-US device saw `1,234.5`. The app language now decides, and this asserts the two
    // can no longer disagree.
    expect(formatNumber(1234.5, 'es')).toBe('1234,5');
    expect(formatNumber(1234.5, 'en')).toBe('1,234.5');
  });

  it('formats a percentage without moving the sign', () => {
    expect(formatPercent(0.734, 'en')).toBe('73.4%');
    expect(formatPercent(0.734, 'es')).toBe('73,4%');
  });

  it('uses the locale tag the language maps to', () => {
    expect(localeTag('en')).toBe('en-US');
    expect(localeTag('es')).toBe('es-ES');
    // `ar-EG` for the region's conventions and its month names; `u-nu-latn` because the
    // numbers must stay comparable character by character with the paper they came from.
    expect(localeTag('ar')).toBe('ar-EG-u-nu-latn');
  });

  it('keeps Arabic numerals in Latin digits, which is the decision `localeTag` records', () => {
    const arabic = formatNumber(43016.5, 'ar');

    expect(arabic).toBe('43,016.5');
    // Arabic-Indic numerals (٤٣٬٠١٦٫٥) would be the same number in a different script; the
    // assertion is that no digit ever leaves 0-9, so a weight or an amount can be compared
    // with the sheet it was copied from.
    expect(/[0-9]/.test(arabic)).toBe(true);
    expect(/[\u0660-\u0669]/.test(arabic)).toBe(false);
  });
});

describe('money', () => {
  it('never changes the currency symbol — only the separators', () => {
    // The decision, stated as a test: `$` in, `$` out, in front, in every language.
    expect(formatMoney(1234.57, 'en')).toBe('$1,234.57');
    expect(formatMoney(1234.57, 'es')).toBe('$1234,57');
    // Arabic lands on the English separators because its tag carries `u-nu-latn` — a bare
    // `$` in front of a number whose separators follow the same script as the digits.
    expect(formatMoney(1234.57, 'ar')).toBe('$1,234.57');
    // The separator really does localise, so the assertion above is not passing by luck:
    // five digits and up, Spanish groups with a full stop.
    expect(formatMoney(43016, 'es')).toBe('$43.016,00');
    // Nothing about the amount's *meaning* moved: same digits, same sign, same symbol.
    expect(formatMoney(-500, 'es')).toBe('$-500,00');
    expect(formatMoney(1234.57, 'es')).not.toContain('€');
    expect(formatMoney(1234.57, 'es')).not.toContain('1.234');
  });

  it('shows what the caller asked for when there is no amount', () => {
    expect(formatMoney(null, 'en')).toBe('—');
    expect(formatMoney(undefined, 'es')).toBe('—');
    expect(formatMoney(null, 'en', '')).toBe('');
  });
});

describe('useFormat', () => {
  const Probe = () => {
    const format = useFormat();
    return (
      <div>
        <span data-testid="money">{format.money(1234.57)}</span>
        <span data-testid="date">{format.date('2026-09-24T10:00:00Z')}</span>
        <span data-testid="direction">{format.direction}</span>
      </div>
    );
  };

  it('binds the formatters to the active language, so a component cannot lag behind', async () => {
    await i18n.changeLanguage('es');
    render(<Probe />);

    expect(screen.getByTestId('money').textContent).toBe('$1234,57');
    expect(screen.getByTestId('date').textContent).toBe('24/09/2026');
  });

  it('binds to Arabic too, rather than falling back to English', async () => {
    // The bug this pins: the hook used to resolve the locale with
    // `language === 'es' ? 'es' : 'en'`, so every Arabic date and amount was formatted with
    // English rules — a chart tooltip reading `2026-09-24` inside an otherwise Arabic page.
    await i18n.changeLanguage('ar');
    render(<Probe />);

    expect(screen.getByTestId('date').textContent).toBe('24/09/2026');
    expect(screen.getByTestId('direction').textContent).toBe('rtl');
  });

  it('renders the English forms while English is active', () => {
    render(<Probe />);

    expect(screen.getByTestId('money').textContent).toBe('$1,234.57');
    expect(screen.getByTestId('date').textContent).toBe('2026-09-24');
    expect(screen.getByTestId('direction').textContent).toBe('ltr');
  });
});
