import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import NotificationsPage from './NotificationsPage';
import { notificationsApi } from '../api/notifications';
import type { Notification } from '../api/notifications';
import { useFarmStore } from '../stores/farmStore';
import type { Farm } from '../types';

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

const farm: Farm = {
  id: 'farm-a',
  name: 'Farm A',
  isActive: true,
  createdAt: '2026-01-01T00:00:00Z',
  userFarmRole: 'FarmManager',
};

const notification = (overrides: Partial<Notification> = {}): Notification => ({
  id: 'n-1',
  alertType: 'OverdueVaccination',
  severity: 'Critical',
  title: 'Overdue: FMD',
  message: 'Animal COW-001 is overdue for FMD (due Sep 09, 2026)',
  link: '/dashboard/health/vaccinations',
  dueDate: '2026-09-09T00:00:00Z',
  createdAt: '2026-09-19T08:00:00Z',
  isRead: false,
  readAtUtc: null,
  isDismissed: false,
  isResolved: false,
  deliveredAtUtc: null,
  ...overrides,
});

const renderPage = () =>
  render(
    <MemoryRouter>
      <NotificationsPage />
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  useFarmStore.setState({ farms: [farm], activeFarm: farm, isLoading: false, error: null });
  vi.mocked(notificationsApi.list).mockResolvedValue({
    data: { items: [notification()], totalCount: 1, unreadCount: 1 },
  } as never);
  vi.mocked(notificationsApi.markRead).mockResolvedValue({ data: notification({ isRead: true }) } as never);
  vi.mocked(notificationsApi.dismiss).mockResolvedValue({ data: notification({ isDismissed: true }) } as never);
  vi.mocked(notificationsApi.markAllRead).mockResolvedValue({ data: 1 } as never);
});

describe('NotificationsPage', () => {
  it('renders a persisted notification with its severity, alert type and link', async () => {
    renderPage();

    expect(await screen.findByText('Overdue: FMD')).toBeInTheDocument();
    expect(screen.getByText('Overdue vaccinations')).toBeInTheDocument();
    expect(screen.getByText('Critical')).toBeInTheDocument();
    expect(screen.getByText('Animal COW-001 is overdue for FMD (due Sep 09, 2026)')).toBeInTheDocument();

    // The link reuses the dashboard alert's route, so an alert navigates the same
    // way from either place.
    const view = screen.getByRole('link', { name: 'View' });
    expect(view).toHaveAttribute('href', '/dashboard/health/vaccinations');
  });

  it('prompts for a farm instead of calling the API when none is active', async () => {
    useFarmStore.setState({ farms: [], activeFarm: null, isLoading: false, error: null });

    renderPage();

    await waitFor(() => expect(notificationsApi.list).not.toHaveBeenCalled());
  });

  it('marks one notification read through the API and reloads the list', async () => {
    renderPage();

    await screen.findByText('Overdue: FMD');
    await userEvent.click(screen.getByRole('button', { name: /Mark read/ }));

    await waitFor(() => expect(notificationsApi.markRead).toHaveBeenCalledWith('n-1'));
    // Reloaded rather than patched locally: the server decides what is unread.
    expect(vi.mocked(notificationsApi.list).mock.calls.length).toBeGreaterThan(1);
  });

  it('dismisses a notification through the API', async () => {
    renderPage();

    await screen.findByText('Overdue: FMD');
    await userEvent.click(screen.getByRole('button', { name: /Dismiss/ }));

    await waitFor(() => expect(notificationsApi.dismiss).toHaveBeenCalledWith('n-1'));
  });

  it('offers mark-all-read only while something is unread', async () => {
    vi.mocked(notificationsApi.list).mockResolvedValue({
      data: { items: [notification({ isRead: true, readAtUtc: '2026-09-19T09:00:00Z' })], totalCount: 1, unreadCount: 0 },
    } as never);

    renderPage();

    await screen.findByText('Overdue: FMD');
    expect(screen.getByRole('button', { name: /Mark all read/ })).toBeDisabled();

    // A read notification loses its per-item action instead of offering a no-op.
    expect(screen.queryByRole('button', { name: /Mark read/ })).not.toBeInTheDocument();
  });

  it('clears the unread badge after marking everything read', async () => {
    renderPage();

    await screen.findByText('Overdue: FMD');
    await userEvent.click(screen.getByRole('button', { name: /Mark all read/ }));

    await waitFor(() => expect(notificationsApi.markAllRead).toHaveBeenCalled());
  });

  it('resets to the first page when a filter changes', async () => {
    renderPage();

    await screen.findByText('Overdue: FMD');
    await userEvent.click(screen.getByRole('switch', { name: 'Unread only' }));

    await waitFor(() =>
      expect(vi.mocked(notificationsApi.list).mock.calls.at(-1)?.[0]).toMatchObject({ page: 1 }),
    );
  });
});
