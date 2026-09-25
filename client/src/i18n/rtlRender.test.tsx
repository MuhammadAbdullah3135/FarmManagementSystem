import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
// The offline pages read and write IndexedDB, so the suite runs against the in-memory one
// and each test starts from an empty database.
import 'fake-indexeddb/auto';
import { IDBFactory } from 'fake-indexeddb';
import i18n, { NAMESPACES, resources } from './index';
import { GRADIENT_DIRECTION_VAR, applyDocumentLocale, directionOf } from './locale';
import { DirectionalGlyph, DirectionalIcon, startSide } from './DirectionalIcon';
// The comparison helper the coverage test uses, so this file and that one agree on what
// "a key" and "a value" mean.
import { flatten, logicalKey } from '../../tools/i18n/check-locale-coverage.mjs';

vi.mock('../api/axios', () => ({
  default: {
    get: vi.fn(() => Promise.resolve({ data: [] })),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
  setFarmAccessDeniedHandler: vi.fn(),
}));

vi.mock('../api/dashboard', () => ({
  dashboardApi: { summary: vi.fn(), alerts: vi.fn(), charts: vi.fn() },
}));

// The two modules the offline pages reach for. RecordWeightPage reads the animal picker from
// the lookup API and sends queued records through the sync API; without these it would call
// the real axios instance, which is mocked to return `[]`.
vi.mock('../api/attendance', () => ({ lookupsApi: { animals: vi.fn() } }));
vi.mock('../api/sync', () => ({ syncApi: { applyMutations: vi.fn() } }));

import AppLayout from '../components/AppLayout';
import DashboardPage from '../pages/DashboardPage';
import RecordWeightPage from '../pages/offline/RecordWeightPage';
import SyncStatusPage from '../pages/offline/SyncStatusPage';
import { useAuthStore } from '../stores/authStore';
import { useFarmStore } from '../stores/farmStore';
import { resetOfflineDbConnection } from '../offline/db';
import { dashboardApi } from '../api/dashboard';
import type { DashboardAlert, DashboardSummary } from '../api/dashboard';
import type { Farm, User } from '../types';
import App from '../App';

/**
 * Real pages, rendered in Arabic — the first right-to-left language this app ships.
 *
 * Two things are checked, and only the first is about words:
 *
 *   1. the same completeness claim as Spanish — no raw key on screen, and not the English
 *      copy either, on the pages with the most traffic and the most custom UI (the offline
 *      screens from Phase 5, whose queue table is hand-built rather than an antd `Table`);
 *   2. the *direction* plumbing, which no translation file can express: the document is
 *      `dir="rtl"` with `lang="ar"`, the gradient fallback flips, and the back/enter/leave
 *      icons are the mirrored ones. A page can be perfectly translated and still lay out
 *      left to right, which is what these assertions exist to catch.
 *
 * Layout *breakage* cannot be measured here — jsdom has no layout engine, so nothing in this
 * file proves the Arabic pages look right, only that they render and are annotated as
 * right-to-left. The part that needs eyes is handed off in docs/VERIFICATION.md.
 */

const flat = (locale: 'en' | 'ar', namespace: string): Record<string, string> =>
  flatten((resources as unknown as Record<string, Record<string, unknown>>)[locale][namespace]);

const ar = (namespace: string, key: string): string => flat('ar', namespace)[key];
const english = (namespace: string, key: string): string => flat('en', namespace)[key];

/** Every key id in the app, so a raw key rendered as text can be spotted generically. */
const KEY_IDS: string[] = NAMESPACES.flatMap((namespace) =>
  Object.keys(flat('en', namespace)).map((key) => `${namespace}:${key}`),
);

const textNodes = (container: HTMLElement): string[] => {
  const out: string[] = [];
  for (const element of container.querySelectorAll('*')) {
    for (const child of Array.from(element.childNodes)) {
      if (child.nodeType === Node.TEXT_NODE) {
        const text = (child.textContent ?? '').trim();
        if (text) out.push(text);
      }
    }
  }
  return out;
};

const expectNoRawKeys = (container: HTMLElement) => {
  const leaked = textNodes(container).filter((text) => KEY_IDS.some((id) => text.includes(id)));
  expect(leaked).toEqual([]);
};

const expectArabicNotEnglish = (namespace: string, key: string) => {
  const translated = ar(namespace, key);
  expect(screen.queryAllByText(translated).length).toBeGreaterThan(0);

  const untranslated = english(namespace, key);
  if (untranslated !== translated) expect(screen.queryAllByText(untranslated)).toHaveLength(0);
};

const user: User = {
  userId: 'user-1',
  accountId: 'account-1',
  email: 'owner@example.com',
  firstName: 'Owner',
  lastName: 'Person',
  roles: ['SystemOwner'],
};

const farm: Farm = {
  id: 'farm-a',
  name: 'Green Acres',
  isActive: true,
  createdAt: '2026-01-01T00:00:00Z',
  userFarmRole: 'SystemOwner',
};

beforeEach(async () => {
  (globalThis as { indexedDB: IDBFactory }).indexedDB = new IDBFactory();
  resetOfflineDbConnection();
  vi.clearAllMocks();
  localStorage.clear();
  useAuthStore.setState({ user, isAuthenticated: true });
  useFarmStore.setState({ farms: [farm], activeFarm: farm, isLoading: false, error: null });
  await i18n.changeLanguage('ar');
  // The same call `App` makes in its language effect. These tests render pages directly
  // rather than through `App`, so without it the document would still be left-to-right and
  // the direction assertions below would be testing the harness instead of the app.
  applyDocumentLocale('ar');
});

afterEach(async () => {
  // The next test file must not inherit an Arabic — or a right-to-left — document.
  await i18n.changeLanguage('en');
  applyDocumentLocale('en');
});

describe('the document itself is annotated as right to left', () => {
  it('sets dir and lang from the language, not from a per-screen decision', () => {
    applyDocumentLocale('ar');

    expect(document.documentElement.dir).toBe('rtl');
    expect(document.documentElement.lang).toBe('ar');

    applyDocumentLocale('es');

    expect(document.documentElement.dir).toBe('ltr');
    expect(document.documentElement.lang).toBe('es');
  });

  it('publishes the gradient direction, which CSS cannot express logically', () => {
    applyDocumentLocale('ar');
    expect(document.documentElement.style.getPropertyValue(GRADIENT_DIRECTION_VAR)).toBe('to right');

    applyDocumentLocale('en');
    expect(document.documentElement.style.getPropertyValue(GRADIENT_DIRECTION_VAR)).toBe('to left');
  });

  it('applies both the moment the app renders in Arabic', async () => {
    window.history.pushState({}, '', '/login');
    render(<App />);

    expect(await screen.findByRole('button', { name: /الدخول|تسجيل/ })).toBeInTheDocument();
    expect(document.documentElement.dir).toBe('rtl');
    expect(document.documentElement.lang).toBe('ar');
  });

  it('knows which languages are right to left, and answers for the side a start-anchored thing takes', () => {
    expect(directionOf('ar')).toBe('rtl');
    expect(directionOf('es')).toBe('ltr');
    expect(startSide('rtl')).toBe('right');
    expect(startSide('ltr')).toBe('left');
  });
});

describe('the icons whose meaning is a direction', () => {
  it('points the back arrow the way the language reads', async () => {
    const { container: rtl } = render(<DirectionalIcon role="back" />);
    expect(rtl.querySelector('[aria-label="arrow-right"]')).not.toBeNull();
    expect(rtl.querySelector('[aria-label="arrow-left"]')).toBeNull();

    await i18n.changeLanguage('en');
    const { container: ltr } = render(<DirectionalIcon role="back" />);
    expect(ltr.querySelector('[aria-label="arrow-left"]')).not.toBeNull();
    expect(ltr.querySelector('[aria-label="arrow-right"]')).toBeNull();
  });

  it('mirrors an enter/leave icon instead of swapping its glyph', async () => {
    const { container: rtl } = render(<DirectionalIcon role="leave" />);
    expect(rtl.querySelector('.anticon')?.getAttribute('style')).toContain('scaleX(-1)');

    await i18n.changeLanguage('en');
    const { container: ltr } = render(<DirectionalIcon role="leave" />);
    expect(ltr.querySelector('.anticon')?.getAttribute('style') ?? '').not.toContain('scaleX(-1)');
  });

  it('draws the nesting elbow the other way round', async () => {
    const { container: rtl } = render(<DirectionalGlyph mark="tree-branch" />);
    expect(rtl.textContent).toBe('\u21B2');

    await i18n.changeLanguage('en');
    const { container: ltr } = render(<DirectionalGlyph mark="tree-branch" />);
    expect(ltr.textContent).toBe('\u21B3');
  });
});

describe('an Arabic session renders real pages', () => {
  it('shows the navigation in Arabic, laid out from the right, with no English label beside it', () => {
    const { container } = render(
      <MemoryRouter initialEntries={['/dashboard']}>
        <Routes>
          <Route path="/dashboard" element={<AppLayout />} />
        </Routes>
      </MemoryRouter>,
    );

    for (const key of ['dashboard', 'animals', 'inventory', 'admin', 'notifications', 'farmTasks']) {
      expectArabicNotEnglish('nav', key);
    }
    expectNoRawKeys(container);
    expect(document.documentElement.dir).toBe('rtl');
  });

  it('shows the dashboard cards and a server-generated alert in Arabic', async () => {
    const summary: DashboardSummary = {
      totalAnimals: 8,
      animalsByType: { Cattle: 8 },
      animalsByStatus: { Healthy: 5 },
      pregnantCount: 1,
      sickCount: 2,
      dueVaccinationCount: 7,
      dueWeightCheckCount: 3,
      overdueTasks: 0,
      upcomingBirths: 0,
      totalFeedStockValue: 100,
      totalInventoryStockValue: 50,
      degradedMetrics: [],
    };

    // The alert the dispatcher writes: English text *and* a key with raw arguments. The
    // Arabic reader must get the second, formatted here — including the Arabic month name.
    const alert: DashboardAlert = {
      alertType: 'OverdueVaccination',
      severity: 'Critical',
      title: 'Overdue: FMD',
      message: 'Animal COW-001 is overdue for FMD (due Sep 09, 2026)',
      titleKey: 'notifications.alertOverdueVaccinationTitle',
      titleArgs: { vaccineType: 'FMD' },
      messageKey: 'notifications.alertOverdueVaccinationMessage',
      messageArgs: { tag: 'COW-001', vaccineType: 'FMD', dueDate: '2026-09-09T00:00:00Z' },
      dueDate: '2026-09-09T00:00:00Z',
      link: '/dashboard/health/vaccinations',
    };

    vi.mocked(dashboardApi.summary).mockResolvedValue({ data: summary } as never);
    vi.mocked(dashboardApi.alerts).mockResolvedValue({ data: [alert] } as never);
    vi.mocked(dashboardApi.charts).mockResolvedValue({
      data: { animalTrends: [], expenseBreakdown: null, feedConsumptionTrend: [], monthlyPL: [] },
    } as never);

    const { container } = render(
      <MemoryRouter initialEntries={['/dashboard']}>
        <Routes>
          <Route path="/dashboard" element={<DashboardPage />} />
        </Routes>
      </MemoryRouter>,
    );

    // The farm name is data and stays as it is; the sentence around it is translated.
    expect(await screen.findByText(i18n.t('dashboard:currentlyManaging', { name: farm.name }))).toBeInTheDocument();
    expect(screen.queryByText(/Currently managing/)).not.toBeInTheDocument();

    for (const key of ['totalAnimals', 'dueWeightChecks', 'upcomingBirths', 'view']) {
      expectArabicNotEnglish('dashboard', key);
    }

    // Rendered through the key, not the English string that travelled with it.
    expect(await screen.findByText(i18n.t('notifications:alertOverdueVaccinationTitle', { vaccineType: 'FMD' }))).toBeInTheDocument();
    expect(screen.queryByText('Overdue: FMD')).not.toBeInTheDocument();

    expectNoRawKeys(container);
    expect(document.documentElement.dir).toBe('rtl');
  });

  it('shows the offline weight-recording page in Arabic, with its hand-built controls', async () => {
    const { container } = render(
      <MemoryRouter initialEntries={['/dashboard/records/weight']}>
        <Routes>
          <Route path="/dashboard/records/weight" element={<RecordWeightPage />} />
        </Routes>
      </MemoryRouter>,
    );

    // Rendered copy only: a `Select` placeholder is an attribute, not text, so the keys
    // asserted here are the ones that appear as words on the screen.
    for (const key of ['recordWeight', 'weight', 'saveWeight', 'recordedOnThisDevice']) {
      expectArabicNotEnglish('offline', key);
    }
    expectNoRawKeys(container);
    expect(document.documentElement.dir).toBe('rtl');
  });

  it('shows the offline sync screen in Arabic, including the queued counts', async () => {
    const { container } = render(
      <MemoryRouter initialEntries={['/dashboard/sync']}>
        <Routes>
          <Route path="/dashboard/sync" element={<SyncStatusPage />} />
        </Routes>
      </MemoryRouter>,
    );

    // The queue's own copy — the part of Phase 5's UI that is not an antd component.
    for (const key of ['syncNow', 'nothingIsWaitingOnThisDevice', 'waitingToSync']) {
      expectArabicNotEnglish('offline', key);
    }
    expectNoRawKeys(container);
    expect(document.documentElement.dir).toBe('rtl');
  });
});

describe('Arabic plural forms render as plural forms', () => {
  it('uses the dual form for two of something, which English cannot say in one string', async () => {
    // The reason Arabic's plural forms are worth a test of their own: the guard proves the
    // keys exist, and only this proves i18next picks the right one for a given count.
    const forms = Object.keys(flat('ar', 'health')).filter((key) => logicalKey(key) === 'intervalDays');

    expect(forms.length).toBeGreaterThan(0);
    // Compared against each form with its own count substituted, so the assertion is about
    // *which* form i18next chose and not about the wording drifting.
    expect(i18n.t('health:intervalDays', { count: 2 })).toBe(ar('health', 'intervalDays_two'));
    expect(i18n.t('health:intervalDays', { count: 2 })).not.toContain('2');
    expect(i18n.t('health:intervalDays', { count: 7 })).toBe(
      i18n.t('health:intervalDays_few', { count: 7 }),
    );
    expect(i18n.t('health:intervalDays', { count: 7 })).toContain('7');
    expect(i18n.t('health:intervalDays', { count: 40 })).toBe(
      i18n.t('health:intervalDays_many', { count: 40 }),
    );

    // And English, which has the single string, is unchanged by any of it.
    await i18n.changeLanguage('en');
    expect(i18n.t('health:intervalDays', { count: 2 })).toBe('2 days');
  });
});
