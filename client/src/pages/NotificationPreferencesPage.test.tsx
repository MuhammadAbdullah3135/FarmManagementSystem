import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import NotificationPreferencesPage from './NotificationPreferencesPage';
import { notificationsApi } from '../api/notifications';
import type { NotificationPreferenceSettings } from '../api/notifications';
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

// The defaults the API reports with nothing stored: in-app on, email on only for
// the alert types whose declared severity is Critical.
const defaults: NotificationPreferenceSettings = {
  preferences: [
    { alertType: 'OverdueVaccination', inAppEnabled: true, emailEnabled: true },
    { alertType: 'OverdueWeightCheck', inAppEnabled: true, emailEnabled: false },
    { alertType: 'Medicine', inAppEnabled: true, emailEnabled: false },
    { alertType: 'OverdueTask', inAppEnabled: true, emailEnabled: true },
    { alertType: 'DueBirth', inAppEnabled: true, emailEnabled: false },
    { alertType: 'LowInventory', inAppEnabled: true, emailEnabled: false },
  ],
  channels: ['InApp', 'Email'],
  emailMinSeverityOnByDefault: 'Critical',
};

const renderPage = () =>
  render(
    <MemoryRouter>
      <NotificationPreferencesPage />
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  useFarmStore.setState({ farms: [farm], activeFarm: farm, isLoading: false, error: null });
  vi.mocked(notificationsApi.getPreferences).mockResolvedValue({ data: defaults } as never);
  vi.mocked(notificationsApi.updatePreferences).mockResolvedValue({ data: defaults } as never);
});

describe('NotificationPreferencesPage', () => {
  it('renders the whole matrix from the server vocabulary', async () => {
    renderPage();

    // Every alert type the API knows about appears, labelled — the client does
    // not invent or filter the list.
    expect(await screen.findByText('Overdue vaccinations')).toBeInTheDocument();
    expect(screen.getByText('Overdue weight checks')).toBeInTheDocument();
    expect(screen.getByText('Medicine (expiry & low stock)')).toBeInTheDocument();
    expect(screen.getByText('Overdue farm tasks')).toBeInTheDocument();
    expect(screen.getByText('Upcoming births')).toBeInTheDocument();
    expect(screen.getByText('Low inventory stock')).toBeInTheDocument();
  });

  it('shows the email default explained, so emailing everything is not the baseline', async () => {
    renderPage();

    expect(await screen.findByText('Email is reserved for what matters by default')).toBeInTheDocument();
    expect(notificationsApi.getPreferences).toHaveBeenCalled();
  });

  it('offers no save until something changes, then persists the whole matrix', async () => {
    renderPage();

    await screen.findByText('Overdue vaccinations');
    const save = screen.getByRole('button', { name: /Save/ });
    expect(save).toBeDisabled();

    // Turn email on for a Warning-severity type.
    await userEvent.click(
      screen.getByRole('switch', { name: 'Email notifications for Overdue weight checks' }),
    );

    await waitFor(() => expect(screen.getByRole('button', { name: /Save/ })).toBeEnabled());
    await userEvent.click(screen.getByRole('button', { name: /Save/ }));

    await waitFor(() => expect(notificationsApi.updatePreferences).toHaveBeenCalled());

    const sent = vi.mocked(notificationsApi.updatePreferences).mock.calls[0][0];
    expect(sent).toHaveLength(6);
    expect(sent.find((p) => p.alertType === 'OverdueWeightCheck')).toMatchObject({
      inAppEnabled: true,
      emailEnabled: true,
    });
    // Everything else travels unchanged, so saving never resets other choices.
    expect(sent.find((p) => p.alertType === 'OverdueVaccination')).toMatchObject({
      emailEnabled: true,
    });
  });

  it('can turn a channel off entirely for one alert type', async () => {
    renderPage();

    await screen.findByText('Overdue vaccinations');
    await userEvent.click(
      screen.getByRole('switch', { name: 'In-app notifications for Low inventory stock' }),
    );
    await userEvent.click(screen.getByRole('button', { name: /Save/ }));

    await waitFor(() => expect(notificationsApi.updatePreferences).toHaveBeenCalled());

    const sent = vi.mocked(notificationsApi.updatePreferences).mock.calls[0][0];
    expect(sent.find((p) => p.alertType === 'LowInventory')).toMatchObject({ inAppEnabled: false });
  });
});
