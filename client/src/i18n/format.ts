import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import { activeLocale } from './index';
import type { Locale } from './locale';

/**
 * Dates and numbers, formatted for the language the person is reading.
 *
 * Why this exists at all: before it, five screens each had their own
 * `$${v.toLocaleString('en-US', …)}` and thirty-odd cells called
 * `dayjs(x).format('YYYY-MM-DD')`. Those disagree with each other, ignore the app's
 * language (they followed the *browser's*, via the implicit locale of `toLocaleString`),
 * and made a currency or date change a 30-file search.
 *
 * ## English output is deliberately unchanged
 *
 * Every formatter here renders **exactly** what the app rendered before when the language
 * is English — `2026-09-24`, `Aug 3, 2026`, `$1,234.57`. That is not laziness: it keeps
 * this subphase's diff about Spanish, keeps hundreds of existing assertions meaningful,
 * and means a formatting regression shows up as an English diff rather than hiding inside
 * a Spanish one. A separate piece of work can modernise the English forms (US users would
 * expect `09/24/2026`) without dragging a translation release along with it.
 *
 * ## Currency is not localised, on purpose
 *
 * `formatMoney` prefixes the same bare `$` the app has always shown and localises only
 * the *number*: Spanish gets `$1234,57` and `$43.016,00`, never `1234,57 $`, `€` or a
 * reformatted amount. (Spanish groups only from five digits up — CLDR's
 * `minimumGroupingDigits` for `es` — so a four-figure amount has no thousands separator in
 * Spanish. That is the locale's own rule, not a missing separator.) The farm has no
 * currency setting yet, so inventing a symbol or a position would be a guess; recording it
 * as a real setting is tracked as separate work. What this module fixes is that the
 * decimal and thousands separators now agree with the language.
 *
 * Those decisions are stated in `docs/I18N.md` too, because they are the kind of thing
 * that looks like an oversight otherwise.
 */

/** The BCP-47 tag behind a locale: what `Intl` and dayjs are actually given. */
export const localeTag = (locale: Locale): string => (locale === 'es' ? 'es-ES' : 'en-US');

/**
 * Numeric date/time patterns, per locale.
 *
 * Held as dayjs patterns rather than `Intl` options because the English forms here are
 * fixed strings this app has always shown, and `Intl` would rewrite them (`09/24/2026`).
 * Month *names* do go through `Intl` (see `formatDateLong`), where there is no such
 * constraint and where dayjs's global locale would otherwise leak into the result.
 */
const PATTERNS: Record<Locale, { date: string; dateTime: string; dateTimeSeconds: string }> = {
  en: { date: 'YYYY-MM-DD', dateTime: 'YYYY-MM-DD HH:mm', dateTimeSeconds: 'YYYY-MM-DD HH:mm:ss' },
  es: { date: 'DD/MM/YYYY', dateTime: 'DD/MM/YYYY HH:mm', dateTimeSeconds: 'DD/MM/YYYY HH:mm:ss' },
};

type DateInput = string | number | Date | null | undefined;

/** Null for anything unparseable, so a caller can decide what "no date" looks like. */
const toDayjs = (value: DateInput) => {
  if (value === null || value === undefined || value === '') return null;
  const parsed = dayjs(value);
  return parsed.isValid() ? parsed : null;
};

/** `2026-09-24` in English, `24/09/2026` in Spanish. */
export const formatDate = (value: DateInput, locale: Locale = activeLocale()): string => {
  const parsed = toDayjs(value);
  return parsed ? parsed.format(PATTERNS[locale].date) : '';
};

/** Date and 24-hour time: `2026-09-24 14:05` / `24/09/2026 14:05`. */
export const formatDateTime = (value: DateInput, locale: Locale = activeLocale()): string => {
  const parsed = toDayjs(value);
  return parsed ? parsed.format(PATTERNS[locale].dateTime) : '';
};

export const formatDateTimeSeconds = (value: DateInput, locale: Locale = activeLocale()): string => {
  const parsed = toDayjs(value);
  return parsed ? parsed.format(PATTERNS[locale].dateTimeSeconds) : '';
};

/**
 * A date with a month name, for prose rather than a table: `Aug 3, 2026` / `3 ago 2026`.
 *
 * Through `Intl` so the month name comes from the locale's own data instead of from
 * dayjs's global locale, which is set by a React effect and would otherwise be one render
 * behind the first paint.
 */
export const formatDateLong = (value: DateInput, locale: Locale = activeLocale()): string => {
  const parsed = toDayjs(value);
  if (!parsed) return '';
  return new Intl.DateTimeFormat(localeTag(locale), {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
  }).format(parsed.toDate());
};

/** Month and day only: `Aug 3` / `3 ago`. Used for period ranges. */
export const formatMonthDay = (value: DateInput, locale: Locale = activeLocale()): string => {
  const parsed = toDayjs(value);
  if (!parsed) return '';
  return new Intl.DateTimeFormat(localeTag(locale), { day: 'numeric', month: 'short' }).format(
    parsed.toDate(),
  );
};

/** Time of day, 24-hour: `14:05`. */
export const formatTime = (value: DateInput, locale: Locale = activeLocale()): string => {
  const parsed = toDayjs(value);
  if (!parsed) return '';
  return new Intl.DateTimeFormat(localeTag(locale), {
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  }).format(parsed.toDate());
};

/** Time of day with seconds, 24-hour: `14:05:09`. For job timestamps, where they matter. */
export const formatTimeSeconds = (value: DateInput, locale: Locale = activeLocale()): string => {
  const parsed = toDayjs(value);
  if (!parsed) return '';
  return new Intl.DateTimeFormat(localeTag(locale), {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false,
  }).format(parsed.toDate());
};

/** A timestamp with a month name and seconds: `Aug 3, 14:05:09` / `3 ago, 14:05:09`. */
export const formatDateLongWithSeconds = (value: DateInput, locale: Locale = activeLocale()): string =>
  `${formatDateLong(value, locale)}, ${formatTimeSeconds(value, locale)}`;

/** `1,234.57` in English, `1.234,57` in Spanish. Grouping matches the old English output. */
export const formatNumber = (
  value: number,
  locale: Locale = activeLocale(),
  options: Intl.NumberFormatOptions = {},
): string => new Intl.NumberFormat(localeTag(locale), options).format(value);

/** Money without a chosen currency: the same bare `$`, the number localised. */
export const formatMoney = (
  value: number | null | undefined,
  locale: Locale = activeLocale(),
  /** What an absent amount looks like. A report cell shows a dash; a label shows nothing. */
  absent = '—',
): string =>
  value === null || value === undefined || Number.isNaN(value)
    ? absent
    : `$${formatNumber(value, locale, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;

/** A whole number with grouping — counts, row totals, file sizes in rows. */
export const formatInteger = (value: number | null | undefined, locale: Locale = activeLocale()): string =>
  value === null || value === undefined || Number.isNaN(value)
    ? '—'
    : formatNumber(value, locale, { maximumFractionDigits: 0 });

/** `73.4%` — one decimal, and a decimal comma where the language wants one. */
export const formatPercent = (fraction: number, locale: Locale = activeLocale(), digits = 1): string =>
  `${formatNumber(fraction * 100, locale, {
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
  })}%`;

/**
 * The same formatters bound to the active language, for components.
 *
 * A page that already calls `useTranslation` re-renders on a language change and can use
 * the plain functions above. This hook is for the components that do not — it subscribes
 * to the language change itself, so a date inside a chart tooltip or a header cannot be
 * left behind in the previous language.
 */
export const useFormat = () => {
  const { i18n } = useTranslation();
  const locale = (i18n.language.split('-')[0] === 'es' ? 'es' : 'en') as Locale;
  return {
    locale,
    tag: localeTag(locale),
    date: (value: DateInput) => formatDate(value, locale),
    dateTime: (value: DateInput) => formatDateTime(value, locale),
    dateTimeSeconds: (value: DateInput) => formatDateTimeSeconds(value, locale),
    dateLong: (value: DateInput) => formatDateLong(value, locale),
    dateLongWithSeconds: (value: DateInput) => formatDateLongWithSeconds(value, locale),
    monthDay: (value: DateInput) => formatMonthDay(value, locale),
    time: (value: DateInput) => formatTime(value, locale),
    number: (value: number, options?: Intl.NumberFormatOptions) => formatNumber(value, locale, options),
    integer: (value: number | null | undefined) => formatInteger(value, locale),
    money: (value: number | null | undefined, absent?: string) => formatMoney(value, locale, absent),
    percent: (fraction: number, digits?: number) => formatPercent(fraction, locale, digits),
  };
};
