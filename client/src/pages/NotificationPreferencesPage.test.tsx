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
      getPushSettings: vi.fn(),
      registerPushSubscription: vi.fn(),
      unregisterPushSubscription: vi.fn(),
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
    { alertType: 'OverdueVaccination', inAppEnabled: true, emailEnabled: true, pushEnabled: true },
    { alertType: 'OverdueWeightCheck', inAppEnabled: true, emailEnabled: false, pushEnabled: false },
    { alertType: 'Medicine', inAppEnabled: true, emailEnabled: false, pushEnabled: false },
    { alertType: 'OverdueTask', inAppEnabled: true, emailEnabled: true, pushEnabled: true },
    { alertType: 'DueBirth', inAppEnabled: true, emailEnabled: false, pushEnabled: false },
    { alertType: 'LowInventory', inAppEnabled: true, emailEnabled: false, pushEnabled: false },
  ],
  channels: ['InApp', 'Email', 'Push'],
  emailMinSeverityOnByDefault: 'Critical',
  minSeverityOnByDefault: 'Critical',
};

// Push is on offer and this browser is already subscribed — the state in which the push
// column is usable.
const pushSettings = {
  enabled: true,
  vapidPublicKey: 'BExampleVapidKey',
  maxSubscriptionsPerUser: 5,
  minSeverityOnByDefault: 'Critical',
  subscriptions: [
    {
      id: 'device-1',
      deviceLabel: 'Chrome on Android',
      createdAt: '2026-09-01T10:00:00Z',
      lastSeenAtUtc: '2026-09-20T10:00:00Z',
      isActive: true,
      lastFailureReason: null,
    },
  ],
};

/** \n * Waits for the push card's settings to have arrived.
 *
 * The device list is its own request, one tick behind the matrix, and under the fully parallel
 * suite testing-library's 1s default is occasionally not enough — a failure there would read as
 * "the device is missing" when it was merely late. Waiting on the settings keeps every later
 * assertion about push itself.
 */
const findPushDevices = () =>
  screen.findByText(pushSettings.subscriptions[0].deviceLabel, undefined, { timeout: 5000 });

const renderPage = () =>
  render(
    <MemoryRouter>
      <NotificationPreferencesPage />
    </MemoryRouter>,
  );

/**
 * Gives the page a browser that really can push.
 *
 * jsdom ships neither `serviceWorker`, `PushManager`, nor `Notification`, so out of the box the
 * real `browserPush` reports `unsupported` and every push assertion would be testing that dead
 * branch instead of the feature. Stubbing the three globals keeps the real module (and its
 * `pushState` ordering) in the test, rather than replacing the thing under test with a fake.
 */
function stubBrowserPush(options: {
  permission?: NotificationPermission;
  endpoint?: string | null;
} = {}) {
  const { permission = 'granted', endpoint = 'https://push.example/device-1' } = options;

  const subscription = endpoint
    ? {
        endpoint,
        toJSON: () => ({ endpoint, keys: { p256dh: 'p256dh-key', auth: 'auth-key' } }),
        unsubscribe: async () => true,
      }
    : null;

  Object.defineProperty(navigator, 'serviceWorker', {
    configurable: true,
    value: {
      ready: Promise.resolve({
        pushManager: {
          getSubscription: async () => subscription,
          subscribe: async () => subscription,
        },
      }),
    },
  });

  (window as unknown as { PushManager?: unknown }).PushManager = class {};
  (globalThis as unknown as { Notification?: unknown }).Notification = {
    permission,
    requestPermission: async () => permission,
  };
}

/** The Android WebView the farm app actually ships in: none of the three globals exist. */
function removeBrowserPush() {
  delete (navigator as unknown as { serviceWorker?: unknown }).serviceWorker;
  delete (window as unknown as { PushManager?: unknown }).PushManager;
  delete (globalThis as unknown as { Notification?: unknown }).Notification;
}

beforeEach(() => {
  vi.clearAllMocks();
  stubBrowserPush();
  useFarmStore.setState({ farms: [farm], activeFarm: farm, isLoading: false, error: null });
  vi.mocked(notificationsApi.getPreferences).mockResolvedValue({ data: defaults } as never);
  vi.mocked(notificationsApi.updatePreferences).mockResolvedValue({ data: defaults } as never);
  vi.mocked(notificationsApi.getPushSettings).mockResolvedValue({ data: pushSettings } as never);
  vi.mocked(notificationsApi.registerPushSubscription)
    .mockResolvedValue({ data: pushSettings.subscriptions[0] } as never);
  vi.mocked(notificationsApi.unregisterPushSubscription).mockResolvedValue({ data: null } as never);
});

describe('NotificationPreferencesPage', { timeout: 20_000 }, () => {
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

  // ── Push ────────────────────────────────────────────────

  it('shows the push row for every alert type and the devices already registered', async () => {
    renderPage();
    await findPushDevices();

    // The third channel is a column in the same matrix, not a separate page: one row per
    // alert type holds all three choices.
    expect(await screen.findByRole('switch', { name: 'Push notifications for Overdue vaccinations' }))
      .toBeChecked();
    expect(screen.getByRole('switch', { name: 'Push notifications for Low inventory stock' }))
      .not.toBeChecked();

    // ...and the device that registered is named, so "on" has something behind it.
    expect(screen.getByText('Chrome on Android')).toBeInTheDocument();
    expect(screen.getByText('On for this device')).toBeInTheDocument();
  });

  it('saves a push choice alongside the other channels', async () => {
    renderPage();
    await findPushDevices();

    await userEvent.click(
      await screen.findByRole('switch', { name: 'Push notifications for Low inventory stock' }),
    );
    await userEvent.click(screen.getByRole('button', { name: /Save/ }));

    await waitFor(() => expect(notificationsApi.updatePreferences).toHaveBeenCalled());

    const sent = vi.mocked(notificationsApi.updatePreferences).mock.calls[0][0];
    expect(sent.find((p) => p.alertType === 'LowInventory')).toMatchObject({
      inAppEnabled: true,
      emailEnabled: false,
      pushEnabled: true,
    });
  });

  it('explains which half of push is missing rather than showing a switch that would do nothing', async () => {
    vi.mocked(notificationsApi.getPushSettings).mockResolvedValue({
      data: { ...pushSettings, enabled: false, vapidPublicKey: null, subscriptions: [] },
    } as never);

    renderPage();

    // The deployment has no keys: the per-type switches are disabled (their stored values are
    // untouched) and the reason is on screen, rather than a column of on switches that would
    // buzz nobody's phone.
    expect(await screen.findByText('Push is turned off on this server, so nothing would be delivered.'))
      .toBeInTheDocument();
    expect(screen.getByRole('switch', { name: 'Push notifications for Overdue vaccinations' }))
      .toBeDisabled();
    expect(screen.getByRole('button', { name: 'Turn on for this device' })).toBeDisabled();
  });

  it('removes a device without touching the matrix', async () => {
    renderPage();
    await findPushDevices();

    await userEvent.click(screen.getByRole('button', { name: 'Remove' }));

    await waitFor(() =>
      expect(notificationsApi.unregisterPushSubscription).toHaveBeenCalledWith('device-1'));

    // The per-type push choices are not part of removing a device: losing a phone must not
    // silently rewrite what the user asked to be told about.
    expect(notificationsApi.updatePreferences).not.toHaveBeenCalled();
  });

  it('keeps working on a deployment without the push endpoints at all', async () => {
    vi.mocked(notificationsApi.getPushSettings).mockRejectedValue(new Error('Request failed with status code 404'));

    renderPage();

    // An older server answers 404 here; the page still renders its matrix and says push is off
    // rather than showing an error page for a feature the deployment does not have.
    expect(await screen.findByText('Overdue vaccinations')).toBeInTheDocument();
    expect(screen.getByText('Push is turned off on this server, so nothing would be delivered.'))
      .toBeInTheDocument();
  });

  it('says a browser that cannot push cannot push, instead of blaming the server', async () => {
    removeBrowserPush();

    renderPage();

    // The Android WebView the farm app ships in has no Push API and no notification permission:
    // the card names that rather than showing an "off" switch the user could never turn on, and
    // the per-type switches stay disabled so nothing looks deliverable.
    expect(await screen.findByText(
      'This browser or app cannot receive push notifications. Open the farm app in a browser to use them.',
    )).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Turn on for this device' })).toBeDisabled();
    expect(screen.getByRole('switch', { name: 'Push notifications for Overdue vaccinations' }))
      .toBeDisabled();
  });
});
