/**
 * The set of languages this build ships, and where the choice is remembered on the device.
 *
 * Kept apart from `index.ts` on purpose: this module has no i18next import, so anything
 * that only needs the list of languages (a switcher, a test, the API layer reading the
 * server's stored preference) never pulls in — or races — the initialisation in `index.ts`.
 *
 * Two places remember the choice, and they answer different questions:
 *
 *   - this device (localStorage), so the first paint after a reload is already in the right
 *     language. Without it, a Spanish user would see an English frame before the account's
 *     preference arrived from the API.
 *   - the account (User.Locale on the server), so the choice follows the person to another
 *     device or browser. The device cache is only a hint for the frame before `/auth/me`
 *     answers; the account always wins when the two disagree.
 */
export const SUPPORTED_LOCALES = ['en', 'es', 'ar'] as const;

export type Locale = (typeof SUPPORTED_LOCALES)[number];

export const DEFAULT_LOCALE: Locale = 'en';

/**
 * What the switcher shows. Each language is named in its own language — a list of
 * endonyms is the one thing a user who cannot read the current language can still read.
 */
export const LOCALE_LABELS: Record<Locale, string> = {
  en: 'English',
  es: 'Español',
  ar: 'العربية',
};

/**
 * The languages written right to left.
 *
 * A list rather than `locale === 'ar'` scattered through the app: the direction of a script
 * is a property of the language, and the next RTL language (Farsi, Hebrew, Urdu) joins this
 * array and nothing else.
 */
export const RTL_LOCALES = ['ar'] as const;

export type Direction = 'ltr' | 'rtl';

export const directionOf = (locale: Locale): Direction =>
  (RTL_LOCALES as readonly string[]).includes(locale) ? 'rtl' : 'ltr';

/**
 * Where `applyDocumentLocale` publishes the direction a `linear-gradient` should use.
 *
 * The one thing CSS cannot express logically: a gradient has no `inline-start` keyword, so
 * the scroll hint in AppLayout.css reads its direction from this variable. Publishing it on
 * the root keeps the direction in a single place instead of spawning `[dir='rtl']`
 * overrides that a reader has to find.
 */
export const GRADIENT_DIRECTION_VAR = '--fms-fade-direction';

/**
 * Puts the active language on the document itself: `lang` for assistive technology and the
 * browser's own hyphenation and spell-check, `dir` for everything CSS does not control —
 * scrollbar side, text selection, form controls, and the direction of the page's own
 * default text alignment.
 *
 * antd's `direction` prop does not set either of these (it drives its own styles), so both
 * halves are needed: the prop for the component library, the attribute for the document.
 */
export const applyDocumentLocale = (locale: Locale): void => {
  const root = globalThis.document?.documentElement;
  if (!root) return; // a non-DOM environment (a node-side test) has nothing to set
  const direction = directionOf(locale);
  root.lang = locale;
  root.dir = direction;
  root.style.setProperty(GRADIENT_DIRECTION_VAR, direction === 'rtl' ? 'to right' : 'to left');
};

/** The app's own localStorage key for the language. Namespaced like the other app keys. */
export const LOCALE_STORAGE_KEY = 'fms.locale';

export const isLocale = (value: unknown): value is Locale =>
  typeof value === 'string' && (SUPPORTED_LOCALES as readonly string[]).includes(value);

/**
 * Accepts what a browser or an older build might hand over (`es-MX`, `ES`) and returns a
 * locale this build ships, or null when there is nothing usable in it.
 */
export const normalizeLocale = (value: unknown): Locale | null => {
  if (typeof value !== 'string') return null;
  const base = value.trim().toLowerCase().split(/[-_]/)[0];
  return isLocale(base) ? base : null;
};

/** The language this device last used, or null on a first visit (or with storage blocked). */
export const readStoredLocale = (): Locale | null => {
  try {
    return normalizeLocale(globalThis.localStorage?.getItem(LOCALE_STORAGE_KEY));
  } catch {
    // Private mode, disabled storage, or a non-browser test environment: not an error.
    return null;
  }
};

export const storeLocale = (locale: Locale): void => {
  try {
    globalThis.localStorage?.setItem(LOCALE_STORAGE_KEY, locale);
  } catch {
    // A device that cannot persist the choice still uses it for this session.
  }
};
