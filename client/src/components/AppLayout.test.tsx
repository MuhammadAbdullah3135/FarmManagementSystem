import { describe, expect, it, vi, beforeEach } from 'vitest';
import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import api, { setFarmAccessDeniedHandler } from '../api/axios';
import { useAuthStore } from '../stores/authStore';
import { useFarmStore } from '../stores/farmStore';
import type { Farm, User } from '../types';

vi.mock('../api/axios', () => ({
  default: {
    get: vi.fn(() => Promise.resolve({ data: [] })),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
  setFarmAccessDeniedHandler: vi.fn(),
}));

import AppLayout, { appMenuItems } from './AppLayout';
import { filterMenuByRole } from '../utils/permissions';

const userWithRoles = (roles: string[]): User => ({
  userId: 'user-1',
  accountId: 'account-1',
  email: 'owner@example.com',
  firstName: 'Owner',
  lastName: 'Person',
  roles,
});

const farmA: Farm = {
  id: 'farm-a',
  name: 'Farm A',
  isActive: true,
  createdAt: '2026-01-01T00:00:00Z',
  userFarmRole: 'SystemOwner',
};

const renderLayout = (roles: string[]) => {
  useAuthStore.setState({ user: userWithRoles(roles), isAuthenticated: true });
  useFarmStore.setState({ farms: [], activeFarm: null, isLoading: false, error: null });

  return render(
    <MemoryRouter initialEntries={['/dashboard']}>
      <Routes>
        <Route path="/dashboard" element={<AppLayout />} />
      </Routes>
    </MemoryRouter>,
  );
};

describe('AppLayout role-filtered menu', () => {
  beforeEach(() => {
    vi.mocked(api.get).mockResolvedValue({ data: [] } as never);
    vi.mocked(setFarmAccessDeniedHandler).mockClear();
    localStorage.clear();
  });

  it('hides the Admin and Inventory groups from a Viewer', async () => {
    renderLayout(['Viewer']);

    expect(await screen.findByText('Dashboard')).toBeInTheDocument();
    expect(screen.queryAllByText('Admin')).toHaveLength(0);
    expect(screen.queryAllByText('Inventory')).toHaveLength(0);
  });

  it('shows the Admin and Inventory groups to a FarmManager', async () => {
    renderLayout(['FarmManager']);

    expect(await screen.findByText('Dashboard')).toBeInTheDocument();
    expect(screen.queryAllByText('Admin').length).toBeGreaterThan(0);
    expect(screen.queryAllByText('Inventory').length).toBeGreaterThan(0);
  });

  it('keeps Inventory but hides Admin from a Veterinarian', async () => {
    renderLayout(['Veterinarian']);

    expect(await screen.findByText('Dashboard')).toBeInTheDocument();
    expect(screen.queryAllByText('Inventory').length).toBeGreaterThan(0);
    expect(screen.queryAllByText('Admin')).toHaveLength(0);
  });
});

/*
 * Revocation mid-session: the layout clears the farm, refetches the list, and
 * the content area falls back to a farm-selection or no-access state instead of
 * letting every page fail against a farm the user can no longer reach.
 */
describe('AppLayout no-farm states', () => {
  beforeEach(() => {
    vi.mocked(api.get).mockResolvedValue({ data: [] } as never);
    vi.mocked(setFarmAccessDeniedHandler).mockClear();
    localStorage.clear();
  });

  it('shows a no-farm-access state when the user belongs to no farm', async () => {
    renderLayout(['Viewer']);

    expect(await screen.findByText('No farm access')).toBeInTheDocument();
  });

  it('asks the user to pick a farm when they have one but none selected', async () => {
    // The layout refetches farms on mount, so the API (not just the store) must
    // report the farm the user still belongs to.
    vi.mocked(api.get).mockResolvedValue({ data: [farmA] } as never);
    useAuthStore.setState({ user: userWithRoles(['SystemOwner']), isAuthenticated: true });
    useFarmStore.setState({ farms: [farmA], activeFarm: null, isLoading: false, error: null });

    render(
      <MemoryRouter initialEntries={['/dashboard']}>
        <Routes>
          <Route path="/dashboard" element={<AppLayout />} />
        </Routes>
      </MemoryRouter>,
    );

    expect(await screen.findByText('Select a farm to continue')).toBeInTheDocument();
  });

  it('registers a handler that clears the active farm', async () => {
    useAuthStore.setState({ user: userWithRoles(['SystemOwner']), isAuthenticated: true });
    useFarmStore.setState({ farms: [farmA], activeFarm: farmA, isLoading: false, error: null });
    localStorage.setItem('activeFarmId', farmA.id);

    render(
      <MemoryRouter initialEntries={['/dashboard']}>
        <Routes>
          <Route path="/dashboard" element={<AppLayout />} />
        </Routes>
      </MemoryRouter>,
    );

    await screen.findByText('Dashboard');

    const handler = vi.mocked(setFarmAccessDeniedHandler).mock.calls.at(-1)?.[0];
    expect(typeof handler).toBe('function');

    handler!();

    expect(useFarmStore.getState().activeFarm).toBeNull();
    expect(localStorage.getItem('activeFarmId')).toBeNull();
  });
});

/*
 * The active farm's membership role is authoritative for farm-scoped UI: the
 * menu must follow `activeFarm.userFarmRole` — the value the API enforces on
 * farm-scoped routes — not the account role baked into the JWT.
 */
describe('AppLayout farm-role menu', () => {
  const renderWithFarm = (accountRoles: string[], userFarmRole: string) => {
    const farm: Farm = { ...farmA, userFarmRole };
    // The layout refetches farms on mount; returning the farm keeps it active.
    vi.mocked(api.get).mockResolvedValue({ data: [farm] } as never);
    useAuthStore.setState({ user: userWithRoles(accountRoles), isAuthenticated: true });
    useFarmStore.setState({ farms: [farm], activeFarm: farm, isLoading: false, error: null });
    localStorage.setItem('activeFarmId', farm.id);

    return render(
      <MemoryRouter initialEntries={['/dashboard']}>
        <Routes>
          <Route path="/dashboard" element={<AppLayout />} />
        </Routes>
      </MemoryRouter>,
    );
  };

  beforeEach(() => {
    vi.mocked(api.get).mockClear();
    vi.mocked(setFarmAccessDeniedHandler).mockClear();
    localStorage.clear();
  });

  it('follows the farm role even when the account role is higher', async () => {
    // Account says SystemOwner, but the farm membership is Viewer.
    renderWithFarm(['SystemOwner'], 'Viewer');

    expect(await screen.findByText('Dashboard')).toBeInTheDocument();
    expect(screen.queryAllByText('Admin')).toHaveLength(0);
    expect(screen.queryAllByText('Inventory')).toHaveLength(0);
  });

  it('recomputes the menu when the active farm changes', async () => {
    renderWithFarm(['SystemOwner'], 'Viewer');
    expect(await screen.findByText('Dashboard')).toBeInTheDocument();
    expect(screen.queryAllByText('Admin')).toHaveLength(0);

    // Switch to a farm where the same user is FarmManager.
    await act(async () => {
      useFarmStore.getState().setActiveFarm({ ...farmA, userFarmRole: 'FarmManager' });
    });

    expect(await screen.findByText('Admin')).toBeInTheDocument();
  });
});

/*
 * The header's notification bell: an at-a-glance unread count and a way in. The
 * count is read for the *active* farm and refreshed on navigation, so returning
 * from the notification centre after reading shows the new number.
 */
describe('AppLayout notification bell', () => {
  const renderWithUnread = (unread: number, userFarmRole = 'FarmManager') => {
    const farm: Farm = { ...farmA, userFarmRole };
    // One mocked transport for both calls the layout makes: the farm list, and
    // the unread count for the active farm.
    vi.mocked(api.get).mockImplementation(
      ((url: string) =>
        Promise.resolve({
          data: String(url).includes('unread-count') ? unread : [farm],
        })) as never,
    );
    useAuthStore.setState({ user: userWithRoles([userFarmRole]), isAuthenticated: true });
    useFarmStore.setState({ farms: [farm], activeFarm: farm, isLoading: false, error: null });
    localStorage.setItem('activeFarmId', farm.id);

    return render(
      <MemoryRouter initialEntries={['/dashboard']}>
        <Routes>
          <Route path="/dashboard" element={<AppLayout />}>
            <Route path="notifications" element={<div>notification centre</div>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
  };

  beforeEach(() => {
    vi.mocked(api.get).mockClear();
    localStorage.clear();
  });

  it('badges the unread count and opens the notification centre', async () => {
    renderWithUnread(3);

    // antd renders the count as the badge element's title attribute.
    expect(await screen.findByTitle('3')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Notifications' }));

    expect(await screen.findByText('notification centre')).toBeInTheDocument();
  });

  it('shows no badge when nothing is unread', async () => {
    renderWithUnread(0);

    await screen.findByText('Dashboard');
    expect(screen.queryByTitle('0')).not.toBeInTheDocument();
  });
});

/*
 * Every member owns their notifications: the dispatcher already withholds
 * alerts a role cannot act on, so the menu entry is not gated by a module.
 */
describe('AppLayout notifications entry', () => {
  beforeEach(() => {
    vi.mocked(api.get).mockResolvedValue({ data: [] } as never);
    localStorage.clear();
  });

  it('offers Notifications to every farm role, including a Viewer', () => {
    const topLevelKeys = (roles: string[]) =>
      filterMenuByRole(appMenuItems, roles).map((item) => String(item.key));

    for (const role of ['SystemOwner', 'FarmManager', 'Veterinarian', 'Employee', 'Accountant', 'Viewer']) {
      expect(topLevelKeys([role])).toContain('/dashboard/notifications');
    }
  });

  it('needs an active farm, because notifications are farm-scoped', async () => {
    useAuthStore.setState({ user: userWithRoles(['FarmManager']), isAuthenticated: true });
    useFarmStore.setState({ farms: [], activeFarm: null, isLoading: false, error: null });

    render(
      <MemoryRouter initialEntries={['/dashboard/notifications']}>
        <Routes>
          <Route path="/dashboard" element={<AppLayout />}>
            <Route path="notifications" element={<div>notification centre</div>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );

    // No farm selected: the content area prompts for one rather than rendering a
    // list it cannot fetch.
    expect(await screen.findByText('No farm access')).toBeInTheDocument();
  });
});

/*
 * The scheduled-jobs view is the one account-scoped screen under Admin: only an
 * account SystemOwner may see it, and it must not require an active farm
 * because its endpoint carries no farm.
 */
describe('AppLayout scheduled jobs entry', () => {
  const navKeys = (roles: string[]) =>
    filterMenuByRole(appMenuItems, roles).flatMap((group) =>
      (group.children ?? []).map((child) => String(child.key)),
    );

  beforeEach(() => {
    vi.mocked(api.get).mockResolvedValue({ data: [] } as never);
    localStorage.clear();
  });

  it('offers Scheduled Jobs to a SystemOwner but not to other roles', () => {
    expect(navKeys(['SystemOwner'])).toContain('/dashboard/admin/jobs');
    expect(navKeys(['FarmManager'])).not.toContain('/dashboard/admin/jobs');
    expect(navKeys(['Viewer'])).not.toContain('/dashboard/admin/jobs');
  });

  it('renders the account-scoped route without an active farm', async () => {
    useAuthStore.setState({ user: userWithRoles(['SystemOwner']), isAuthenticated: true });
    useFarmStore.setState({ farms: [], activeFarm: null, isLoading: false, error: null });

    render(
      <MemoryRouter initialEntries={['/dashboard/admin/jobs']}>
        <Routes>
          <Route path="/dashboard" element={<AppLayout />}>
            <Route path="admin/jobs" element={<div>job status table</div>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );

    expect(await screen.findByText('job status table')).toBeInTheDocument();
    expect(screen.queryByText('No farm access')).not.toBeInTheDocument();
  });
});
