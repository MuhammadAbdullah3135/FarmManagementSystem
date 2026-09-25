import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import {
  DEFAULT_LOCALE,
  SUPPORTED_LOCALES,
  normalizeLocale,
  readStoredLocale,
  storeLocale,
  type Locale,
} from './locale';

// Every locale is bundled into the build (static JSON imports, no HTTP backend).
// That is deliberate: a locale can therefore never "fail to load" at runtime and
// leave a page blank or half-translated — the worst case is an English string.
import common from './locales/en/common.json';
import nav from './locales/en/nav.json';
import auth from './locales/en/auth.json';
import dashboard from './locales/en/dashboard.json';
import animals from './locales/en/animals.json';
import breeding from './locales/en/breeding.json';
import feed from './locales/en/feed.json';
import health from './locales/en/health.json';
import hr from './locales/en/hr.json';
import finance from './locales/en/finance.json';
import inventory from './locales/en/inventory.json';
import tasks from './locales/en/tasks.json';
import reports from './locales/en/reports.json';
import configuration from './locales/en/configuration.json';
import admin from './locales/en/admin.json';
import notifications from './locales/en/notifications.json';
import offline from './locales/en/offline.json';
import imports from './locales/en/imports.json';
import validation from './locales/en/validation.json';
import errors from './locales/en/errors.json';

// Spanish, machine-translated and not yet reviewed by a translator (see
// locales/README.md and locales/translation-status.json). The keys mirror English
// exactly, so the worst case is again an English string rather than a blank screen.
import esCommon from './locales/es/common.json';
import esNav from './locales/es/nav.json';
import esAuth from './locales/es/auth.json';
import esDashboard from './locales/es/dashboard.json';
import esAnimals from './locales/es/animals.json';
import esBreeding from './locales/es/breeding.json';
import esFeed from './locales/es/feed.json';
import esHealth from './locales/es/health.json';
import esHr from './locales/es/hr.json';
import esFinance from './locales/es/finance.json';
import esInventory from './locales/es/inventory.json';
import esTasks from './locales/es/tasks.json';
import esReports from './locales/es/reports.json';
import esConfiguration from './locales/es/configuration.json';
import esAdmin from './locales/es/admin.json';
import esNotifications from './locales/es/notifications.json';
import esOffline from './locales/es/offline.json';
import esImports from './locales/es/imports.json';
import esValidation from './locales/es/validation.json';
import esErrors from './locales/es/errors.json';

/** One namespace per feature area, so a translation file maps onto a screen. */
export const NAMESPACES = [
  'common',
  'nav',
  'auth',
  'dashboard',
  'animals',
  'breeding',
  'feed',
  'health',
  'hr',
  'finance',
  'inventory',
  'tasks',
  'reports',
  'configuration',
  'admin',
  'notifications',
  'offline',
  'imports',
  'validation',
  'errors',
] as const;

export type Namespace = (typeof NAMESPACES)[number];

export const resources = {
  en: {
    common,
    nav,
    auth,
    dashboard,
    animals,
    breeding,
    feed,
    health,
    hr,
    finance,
    inventory,
    tasks,
    reports,
    configuration,
    admin,
    notifications,
    offline,
    imports,
    validation,
    errors,
  },
  es: {
    common: esCommon,
    nav: esNav,
    auth: esAuth,
    dashboard: esDashboard,
    animals: esAnimals,
    breeding: esBreeding,
    feed: esFeed,
    health: esHealth,
    hr: esHr,
    finance: esFinance,
    inventory: esInventory,
    tasks: esTasks,
    reports: esReports,
    configuration: esConfiguration,
    admin: esAdmin,
    notifications: esNotifications,
    offline: esOffline,
    imports: esImports,
    validation: esValidation,
    errors: esErrors,
  },
} as const;

/**
 * Turns a missing key into something readable rather than the raw dotted key.
 *
 * This is a guard of last resort. Keys missing for the *active* locale fall back to
 * English through `fallbackLng` (silent, by design); a key missing in English too can
 * only be a coding mistake, which an audit test in the suite fails on before it ships.
 * Should one still reach a user, they get the last path segment in words — never
 * `animals.form.tagRequired` on screen — and the console records it loudly.
 */
export const humanizeMissingKey = (key: string): string => {
  if (typeof console !== 'undefined') {
    console.error(`[i18n] missing translation key: ${key}`);
  }
  const last = key.includes('.') ? key.slice(key.lastIndexOf('.') + 1) : key;
  const words = last
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/[_-]+/g, ' ')
    .trim()
    .toLowerCase();
  if (!words) return '';
  return words.charAt(0).toUpperCase() + words.slice(1);
};

/**
 * Initialises i18next once for the whole app.
 *
 * The starting language is the device's last choice, read synchronously from
 * localStorage, so a reload paints in the right language immediately. It is only a
 * starting point: once `/auth/me` answers, the account's stored locale replaces it (see
 * `applyAccountLocale` in `src/i18n/localeSync.ts`) and that is what the switcher writes.
 *
 * `fallbackLng` stays English: a key missing from Spanish is silently English rather
 * than a blank or a raw key.
 */
void i18n.use(initReactI18next).init({
  resources,
  lng: readStoredLocale() ?? DEFAULT_LOCALE,
  fallbackLng: 'en',
  supportedLngs: [...SUPPORTED_LOCALES],
  // A browser reporting `es-MX` gets `es`, not a fall back to English.
  load: 'languageOnly',
  nonExplicitSupportedLngs: true,
  defaultNS: 'common',
  ns: [...NAMESPACES],
  keySeparator: '.',
  nsSeparator: ':',
  interpolation: {
    // React escapes rendered values; escaping again would double-encode quotes.
    escapeValue: false,
  },
  returnNull: false,
  returnEmptyString: false,
  // Every resource is already in the bundle, so there is nothing to wait for: initialising
  // synchronously means the first render is already translated rather than showing keys
  // for a frame.
  initAsync: false,
  // Never surface a raw key to a user (see humanizeMissingKey).
  parseMissingKeyHandler: humanizeMissingKey,
});

/**
 * Switches the whole app to `locale` and remembers it on this device.
 *
 * Deliberately not `async`: the resources are already bundled, so the new language is
 * active on the next render and a caller that wants to persist the choice on the account
 * can fire that request without waiting for anything. Storing on the device first means a
 * reload before that request lands still comes back in the new language.
 */
export const changeLocale = (locale: Locale): void => {
  storeLocale(locale);
  void i18n.changeLanguage(locale);
};

/** The locale i18next is actually using right now, normalised to one this build ships. */
export const activeLocale = (): Locale => normalizeLocale(i18n.language) ?? DEFAULT_LOCALE;

export default i18n;
