import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import 'fake-indexeddb/auto';
import { IDBFactory } from 'fake-indexeddb';

vi.mock('../../api/hr', () => ({
  employeesApi: {
    list: vi.fn(),
    get: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    remove: vi.fn(),
  },
  departmentsApi: { list: vi.fn(), create: vi.fn(), remove: vi.fn() },
  employeeRolesApi: { list: vi.fn(), create: vi.fn(), remove: vi.fn() },
}));

import EmployeesPage from './EmployeesPage';
import { employeesApi, departmentsApi, employeeRolesApi } from '../../api/hr';
import { replaceCollection, resetOfflineDbConnection } from '../../offline/db';
import { useOfflineStore } from '../../offline/connectivity';
import { useAuthStore } from '../../stores/authStore';
import { useFarmStore } from '../../stores/farmStore';
import type { Farm, User } from '../../types';

const farm: Farm = {
  id: 'farm-a',
  name: 'Farm A',
  isActive: true,
  createdAt: '2026-01-01T00:00:00Z',
  userFarmRole: 'FarmManager',
};
const scope = { accountId: 'acct-1', farmId: farm.id };
const user: User = {
  userId: 'user-1',
  accountId: scope.accountId,
  email: 'owner@example.com',
  firstName: 'Owner',
  lastName: 'Person',
  roles: ['FarmManager'],
};
const twoDaysAgo = new Date(Date.now() - 2 * 24 * 60 * 60 * 1000).toISOString();

const renderPage = () =>
  render(
    <MemoryRouter>
      <EmployeesPage />
    </MemoryRouter>,
  );

describe('EmployeesPage offline', () => {
  beforeEach(() => {
    (globalThis as { indexedDB: IDBFactory }).indexedDB = new IDBFactory();
    resetOfflineDbConnection();
    vi.clearAllMocks();
    // Offline, and every call that *is* made fails the way an unreachable server does.
    const offline = () => Promise.reject(new Error('network unreachable'));
    vi.mocked(employeesApi.list).mockImplementation(offline as never);
    vi.mocked(departmentsApi.list).mockImplementation(offline as never);
    vi.mocked(employeeRolesApi.list).mockImplementation(offline as never);

    useOfflineStore.setState({
      isOnline: false,
      storageAvailable: true,
      stats: { recordCount: 0, collectionCount: 0, lastSyncedAt: null },
    });
    useAuthStore.setState({ user, isAuthenticated: true });
    useFarmStore.setState({ farms: [farm], activeFarm: farm, isLoading: false, error: null });
  });

  it('renders the cached roster with a freshness label instead of failing', async () => {
    await replaceCollection(
      scope,
      'employees@listPage1',
      [
        {
          id: 'e-1',
          data: {
            id: 'e-1',
            firstName: 'Asha',
            lastName: 'Khan',
            salaryTypeName: 'Monthly',
            salaryRate: 50000,
            hireDate: '2025-01-15T00:00:00.000Z',
            isActive: true,
          },
        },
      ],
      { fetchedAt: twoDaysAgo },
    );

    renderPage();

    expect(await screen.findByText('Asha Khan')).toBeInTheDocument();
    expect(screen.getByText('Synced 2 days ago')).toBeInTheDocument();
    expect(screen.getByText('Employees')).toBeInTheDocument();
    // The cached roster is served from the store, not from a request.
    expect(employeesApi.list).not.toHaveBeenCalled();
  });
});
