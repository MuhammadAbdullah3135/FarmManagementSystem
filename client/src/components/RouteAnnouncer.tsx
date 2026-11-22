import React, { useEffect, useRef, useState } from 'react';
import { useLocation } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { appMenuItems } from './AppLayout';
import type { AppMenuItem } from '../utils/permissions';

/*
 * The name of the page you are on, for the two places a single-page app forgets to say it.
 *
 * A browser load tells a screen reader the document changed twice: the title in the tab, and
 * the page being read. The router swap does neither — the title stays whatever `index.html`
 * shipped, and the new view appears in silence — so the user gets no confirmation their click
 * did anything, and every open tab reads "Farm Management System". This component sets the
 * title per route and repeats the page's name into a polite live region once the arrival is
 * done.
 *
 * The names come from the menu tree (`nav:*`) rather than a second list of titles: a screen is
 * named once, so a renamed menu entry cannot leave the title behind, and the title matches the
 * words the user just clicked. Routes with no menu entry — imports, detail views, the
 * account-scoped pages — carry their own entry in `extraRoutes` below.
 */

/** Every menu path, with the key that names it. Groups (no `/` in the key) are skipped. */
const menuTitles = new Map<string, string>();
const collectMenuTitles = (items: readonly AppMenuItem[]): void => {
  for (const item of items) {
    if (item.key.startsWith('/')) menuTitles.set(item.key, item.labelKey);
    if (item.children) collectMenuTitles(item.children);
  }
};
collectMenuTitles(appMenuItems);

/** Longest first, so a parent is only used when no more specific entry matches. */
const menuPaths = [...menuTitles.keys()].sort((a, b) => b.length - a.length);

const extraRoutes: ReadonlyArray<readonly [string, string]> = [
  // Public pages.
  ['/login', 'auth:signIn'],
  ['/register', 'auth:createAccount'],
  ['/reset-password', 'auth:resetPassword'],
  ['/confirm-reset-password', 'auth:setNewPassword'],
  ['/accept-invitation', 'auth:acceptInvitation'],
  // Screens reached from a page rather than from the sidebar, so they have no menu entry.
  // Static segments before `:id`, as in the route table: checked in order, so an import page
  // listed after the matcher would be titled as if it were one animal.
  ['/dashboard/animals/import', 'imports:importAnimals'],
  ['/dashboard/animals/:id', 'animals:animals'],
  ['/dashboard/hr/employees/import', 'imports:importEmployees'],
  ['/dashboard/finance/expenses/import', 'imports:importExpenses'],
  ['/dashboard/finance/income-records/import', 'imports:importIncome'],
  ['/dashboard/inventory/items/import', 'imports:importInventoryItems'],
  ['/dashboard/inventory/suppliers/import', 'imports:importSuppliers'],
  ['/dashboard/inventory/customers/import', 'imports:importCustomers'],
  ['/dashboard/notifications/preferences', 'notifications:notificationPreferences'],
  ['/dashboard/records/weight', 'offline:recordWeight'],
  ['/dashboard/sync', 'nav:syncStatus'],
];

/** `:id` matches one path segment; compiled once rather than per render. */
const extraMatchers = extraRoutes.map(
  ([pattern, key]) => [new RegExp(`^${pattern.replace(/:[^/]+/g, '[^/]+')}$`), key] as const,
);

/**
 * The deploy's base path, which `useLocation().pathname` still carries (react-router leaves the
 * basename on it; `AppLayout` strips it for its selected menu key for the same reason). Without
 * this every route in a deployed build would miss the menu tree and fall back to the app name.
 */
const base = import.meta.env.BASE_URL.replace(/\/+$/, '');
const stripBase = (pathname: string): string =>
  base && pathname.startsWith(base) ? pathname.slice(base.length) || '/' : pathname;

/** The `nav:*`-style key for a pathname, or null where the app has no name for it. */
const pageTitleKey = (pathname: string): string | null => {
  // Only a trailing slash is left to tidy once the base is off.
  const path = stripBase(pathname).replace(/\/+$/, '') || '/';

  const exact = menuTitles.get(path);
  if (exact) return exact;

  for (const [matcher, key] of extraMatchers) {
    if (matcher.test(path)) return key;
  }

  const parent = menuPaths.find((candidate) => path.startsWith(`${candidate}/`));
  return parent ? (menuTitles.get(parent) ?? null) : null;
};

/**
 * Hidden from sight but not from assistive technology: the region must be in the document
 * before its text changes, or there is nothing to announce into.
 */
const SCREEN_READER_ONLY: React.CSSProperties = {
  position: 'absolute',
  width: 1,
  height: 1,
  margin: -1,
  padding: 0,
  overflow: 'hidden',
  clip: 'rect(0 0 0 0)',
  whiteSpace: 'nowrap',
  border: 0,
};

const RouteAnnouncer: React.FC = () => {
  const { pathname } = useLocation();
  // Every namespace a title key can come from: the menu's, and the ones above.
  const { t } = useTranslation([
    'nav',
    'animals',
    'auth',
    'imports',
    'notifications',
    'offline',
  ]);

  const titleKey = pageTitleKey(pathname);
  const pageTitle = titleKey ? t(titleKey) : '';
  const appName = t('auth:farmManagementSystem');

  const lastPath = useRef<string | null>(null);
  const [announcement, setAnnouncement] = useState('');

  useEffect(() => {
    // Set on arrival and again on a language change, so the tab always matches the page.
    document.title = pageTitle ? `${pageTitle} · ${appName}` : appName;

    const previous = lastPath.current;
    lastPath.current = pathname;

    // The first render is the load itself: the title carries it, and announcing it would talk
    // over the page the browser is already reading. A re-run over the same URL — what StrictMode
    // does to every effect in development — is not a navigation either.
    if (previous === null || previous === pathname) return;

    setAnnouncement(pageTitle || appName);
  }, [pathname, pageTitle, appName]);

  return (
    <div role="status" aria-live="polite" style={SCREEN_READER_ONLY}>
      {announcement}
    </div>
  );
};

export default RouteAnnouncer;
