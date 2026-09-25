import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import i18n, { NAMESPACES, resources } from './index';
import { renderKeyedMessage } from './serverMessage';
// The comparison helpers the coverage test uses, so this file and that one agree on what
// "a key" and "a value" mean.
import { flatten } from '../../tools/i18n/check-locale-coverage.mjs';

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

vi.mock('../api/notifications', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/notifications')>();
  return {
    ...actual,
    notificationsApi: {
      list: vi.fn(),
      unreadCount: vi.fn(),
      markRead: vi.fn(),
      markAllRead: vi.fn(),
      dismiss: vi.fn(),
      getPreferences: vi.fn(),
      updatePreferences: vi.fn(),
    },
  };
});

vi.mock('../api/jobs', () => ({ jobsApi: { status: vi.fn() } }));

vi.mock('../api/farmExport', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/farmExport')>();
  return { ...actual, farmExportApi: { status: vi.fn(), request: vi.fn(), download: vi.fn() } };
});

vi.mock('../utils/export', () => ({
  exportCsv: vi.fn(),
  exportExcel: vi.fn(),
  exportPdf: vi.fn(),
  downloadBlob: vi.fn(),
}));

import AppLayout from '../components/AppLayout';
import DashboardPage from '../pages/DashboardPage';
import NotificationsPage from '../pages/NotificationsPage';
import JobsPage from '../pages/admin/JobsPage';
import DataExportPage from '../pages/configuration/DataExportPage';
import { useAuthStore } from '../stores/authStore';
import { useFarmStore } from '../stores/farmStore';
import { dashboardApi } from '../api/dashboard';
import type { DashboardAlert, DashboardSummary } from '../api/dashboard';
import { notificationsApi } from '../api/notifications';
import type { Notification } from '../api/notifications';
import { jobsApi } from '../api/jobs';
import type { JobStatus } from '../api/jobs';
import { farmExportApi } from '../api/farmExport';
import type { Farm, User } from '../types';

/**
 * Real pages, rendered in Spanish.
 *
 * The point is not that `t()` works — the coverage test proves every key resolves. The point
 * is what a Spanish-speaking user actually sees: no dotted key on screen, and not the
 * English copy either. So each page below is its real component with only its API modules
 * mocked, and every expectation is derived from the *bundles* rather than typed in by hand,
 * which is what keeps this honest when the wording changes.
 *
 * The pages are the highest-traffic ones (dashboard, notifications, admin jobs, data export)
 * plus the shell's navigation. Server-supplied text — a job's error message, an export
 * manifest's notes, data values such as a job's last state — is deliberately still English
 * and is never asserted against: see docs/I18N.md for what stays English and why.
 */

type FlatBundle = Record<string, string>;

const flat = (locale: 'en' | 'es', namespace: string): FlatBundle =>
  flatten((resources as unknown as Record<string, Record<string, unknown>>)[locale][namespace]);

/** The Spanish wording for a key — the string the assertion expects to be on screen. */
const es = (namespace: string, key: string): string => flat('es', namespace)[key];

/** The English wording for the same key — the string that must *not* be on screen. */
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

/** Fails when a page shows a raw key (`nav:dashboard`, `alerts.something`) instead of copy. */
const expectNoRawKeys = (container: HTMLElement) => {
  const leaked = textNodes(container).filter((text) => KEY_IDS.some((id) => text.includes(id)));
  expect(leaked).toEqual([]);
};

/** The Spanish wording is present, and the English one does not also appear beside it. */
const expectSpanishNotEnglish = (namespace: string, key: string) => {
  const translated = es(namespace, key);
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

const signedIn = () => {
  useAuthStore.setState({ user, isAuthenticated: true });
  useFarmStore.setState({ farms: [farm], activeFarm: farm, isLoading: false, error: null });
};

beforeEach(async () => {
  vi.clearAllMocks();
  localStorage.clear();
  signedIn();
  await i18n.changeLanguage('es');
});

afterEach(async () => {
  // The next test file must not inherit a Spanish i18next instance.
  await i18n.changeLanguage('en');
});

describe('a Spanish session renders real pages', () => {
  it('shows the navigation in Spanish and no English label beside it', () => {
    const { container } = render(
      <MemoryRouter initialEntries={['/dashboard']}>
        <Routes>
          <Route path="/dashboard" element={<AppLayout />} />
        </Routes>
      </MemoryRouter>,
    );

    for (const key of ['dashboard', 'animals', 'inventory', 'admin', 'notifications', 'farmTasks']) {
      expectSpanishNotEnglish('nav', key);
    }
    expectNoRawKeys(container);
  });

  it('shows the dashboard cards and a server-generated alert in Spanish', async () => {
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
    // Spanish reader must get the second, formatted here — dates included.
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
    expect(
      await screen.findByText(i18n.t('dashboard:currentlyManaging', { name: farm.name })),
    ).toBeInTheDocument();
    expect(screen.queryByText(/Currently managing/)).not.toBeInTheDocument();

    for (const key of ['totalAnimals', 'dueWeightChecks', 'upcomingBirths', 'view']) {
      expectSpanishNotEnglish('dashboard', key);
    }

    // Rendered through the key, not the English string that travelled with it.
    expect(
      await screen.findByText(
        i18n.t('notifications:alertOverdueVaccinationTitle', { vaccineType: 'FMD' }),
      ),
    ).toBeInTheDocument();
    expect(screen.queryByText('Overdue: FMD')).not.toBeInTheDocument();
    // The sentence, with the due date formatted for the reader rather than the ISO string
    // the server sent — the renderer's own output, so the assertion cannot drift from it.
    expect(
      screen.getByText(renderKeyedMessage(alert.messageKey, alert.messageArgs, alert.message)),
    ).toBeInTheDocument();

    expectNoRawKeys(container);
  });

  it('shows the notification centre in Spanish, including severity and alert type', async () => {
    const row: Notification = {
      id: 'n-1',
      alertType: 'OverdueVaccination',
      severity: 'Critical',
      title: 'Overdue: FMD',
      message: 'Animal COW-001 is overdue for FMD (due Sep 09, 2026)',
      titleKey: 'notifications.alertOverdueVaccinationTitle',
      titleArgs: { vaccineType: 'FMD' },
      messageKey: 'notifications.alertOverdueVaccinationMessage',
      messageArgs: { tag: 'COW-001', vaccineType: 'FMD', dueDate: '2026-09-09T00:00:00Z' },
      link: '/dashboard/health/vaccinations',
      dueDate: '2026-09-09T00:00:00Z',
      createdAt: '2026-09-19T08:00:00Z',
      isRead: false,
      readAtUtc: null,
      isDismissed: false,
      isResolved: false,
      deliveredAtUtc: null,
    };

    vi.mocked(notificationsApi.list).mockResolvedValue({
      data: { items: [row], totalCount: 1, unreadCount: 1 },
    } as never);

    const { container } = render(
      <MemoryRouter>
        <NotificationsPage />
      </MemoryRouter>,
    );

    // The stored values are still the stored values — only their labels are translated.
    expect(await screen.findByText(/Vencida: FMD/)).toBeInTheDocument();
    expect(screen.queryByText('Overdue: FMD')).not.toBeInTheDocument();
    expectSpanishNotEnglish('notifications', 'severityCritical');
    expectSpanishNotEnglish('notifications', 'alertTypeOverdueVaccination');
    expectSpanishNotEnglish('notifications', 'markRead');
    expectSpanishNotEnglish('notifications', 'dismiss');

    // The English severity word is a *value*, so it is also checked through the vocabulary
    // table rather than assumed: a translated tag must not fall back to the raw value.
    expect(screen.queryAllByText('Critical')).toHaveLength(0);

    expectNoRawKeys(container);
  });

  it('shows the admin job page in Spanish', async () => {
    const status: JobStatus = {
      enabled: true,
      readAtUtc: '2026-09-19T10:00:00Z',
      counters: { enqueued: 2, processing: 1, scheduled: 4, failed: 3, succeeded: 42 },
      recurringJobs: [
        {
          id: 'feeding-task-generation-fan-out',
          cron: '5 0 * * *',
          queue: 'default',
          nextRunUtc: '2026-09-20T00:05:00Z',
          lastRunUtc: '2026-09-19T00:05:00Z',
          lastState: 'Succeeded',
          lastError: null,
          registered: true,
        },
      ],
      recentFailures: [],
      warnings: [],
    };

    vi.mocked(jobsApi.status).mockResolvedValue({ data: status } as never);

    const { container } = render(<JobsPage />);

    await screen.findByText(es('admin', 'scheduledJobs'));
    for (const key of ['scheduledJobs', 'recurringJobs', 'enqueued', 'succeeded']) {
      expectSpanishNotEnglish('admin', key);
    }

    // A job's stored state (`Succeeded`) is a fixed vocabulary, so the tag is translated
    // too — the raw server word must not be what a Spanish reader sees.
    expect(screen.queryAllByText('Succeeded')).toHaveLength(0);

    expectNoRawKeys(container);
  });

  it('shows the data-export page and its build states in Spanish', async () => {
    const queued = {
      id: 'export-1',
      farmId: 'farm-a',
      status: 'Queued',
      requestedByUserId: 'user-1',
      requestedAtUtc: '2026-09-24T08:59:00Z',
      startedAtUtc: null,
      completedAtUtc: null,
      fileName: null,
      sizeBytes: null,
      error: null,
      isReady: false,
      manifest: null,
    };

    // Nothing built yet: the page's own explanation, not an empty screen.
    vi.mocked(farmExportApi.status).mockResolvedValue({ data: null } as never);
    vi.mocked(farmExportApi.request).mockResolvedValue({ data: queued } as never);

    const { container } = render(
      <MemoryRouter>
        <DataExportPage />
      </MemoryRouter>,
    );

    await screen.findByText(es('configuration', 'dataExport'));
    for (const key of ['dataExport', 'noExportHasBeenCreatedYet', 'download']) {
      expectSpanishNotEnglish('configuration', key);
    }

    // Asking for a build answers in the reader's language: the confirmation is a toast,
    // which is exactly the kind of copy a render test can otherwise miss.
    // A regex, not the bare string: antd's icons contribute their own aria-label, so the
    // button's accessible name is "<icon> Crear exportación".
    await userEvent.click(
      screen.getByRole('button', { name: new RegExp(es('configuration', 'createExport')) }),
    );
    expect(
      await screen.findByText(es('configuration', 'exportQueuedYouWillBeNotifiedWhenIt')),
    ).toBeInTheDocument();

    // The status tag is a fixed vocabulary, so `Queued` becomes Spanish rather than
    // falling through as a raw server value.
    // The state is shown twice — beside the card title and in its details — so the
    // assertion looks for the state rather than for a unique node.
    expect((await screen.findAllByText(es('configuration', 'statusQueued'))).length).toBeGreaterThan(0);
    expect(screen.queryAllByText('Queued')).toHaveLength(0);

    expectNoRawKeys(container);
  });
});
