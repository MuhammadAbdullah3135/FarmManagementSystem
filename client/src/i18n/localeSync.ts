import api from '../api/axios';
import { activeLocale, changeLocale } from './index';
import { DEFAULT_LOCALE, normalizeLocale, type Locale } from './locale';

/**
 * The three moments where the device's language and the account's language meet.
 *
 * The rule, stated once: **the account wins, the device is the fallback.** An account that
 * has never chosen should inherit whatever the device is already showing, because someone
 * who has been using the app in Spanish should not lose that by signing in; an account
 * that has chosen must win even when the device disagrees, because that is the whole point
 * of storing it server-side.
 *
 * Every function here is deliberately non-throwing about "nothing to do" and honest about
 * "could not reach the server": a language is a preference, and a preference that fails to
 * save must never block a sign-in or blank a page.
 */

/** True when the server told us the account's language is one this build can show. */
export const accountHasLocale = (accountLocale: unknown): boolean =>
  normalizeLocale(accountLocale) !== null;

/**
 * Applies the account's stored language. A null or unrecognised value changes nothing:
 * `normalizeLocale` returns null for a language this build does not ship (an account
 * created against a newer server, say), and falling back to English there would take away
 * the language the person is reading.
 */
export const applyAccountLocale = (accountLocale: unknown): void => {
  const locale = normalizeLocale(accountLocale);
  if (locale) changeLocale(locale);
};

/** Persists a choice on the account. Throws only so a caller can tell the user it failed. */
export const saveAccountLocale = async (locale: Locale): Promise<void> => {
  await api.put('/auth/me/locale', { locale });
};

/**
 * Sign-in reconciliation: apply what the account has, or — when it has nothing — adopt the
 * device's language as the account's. The second half is what makes the choice stick for a
 * user who picked Spanish before the server knew about it.
 *
 * Never awaited by its callers: sign-in must not depend on it.
 */
export const reconcileLocaleOnSignIn = async (accountLocale: unknown): Promise<void> => {
  if (accountHasLocale(accountLocale)) {
    applyAccountLocale(accountLocale);
    return;
  }

  const device = activeLocale();
  if (device === DEFAULT_LOCALE) return;

  await saveAccountLocale(device);
};

/**
 * Boot-time check on an already-signed-in session: the account's language may have changed
 * on another device since this one stored its copy. Silent on failure — offline, or a
 * server that has not been deployed yet, must not disturb the page.
 */
export const refreshAccountLocale = async (): Promise<void> => {
  const { data } = await api.get<{ locale?: string | null }>('/auth/me');
  applyAccountLocale(data?.locale);
};
