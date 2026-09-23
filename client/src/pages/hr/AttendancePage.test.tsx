import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import 'fake-indexeddb/auto';
import { IDBFactory } from 'fake-indexeddb';

vi.mock('../../api/attendance', () => ({
  attendanceApi: { list: vi.fn(), upsert: vi.fn(), remove: vi.fn() },
}));
vi.mock('../../api/hr', () => ({ employeesApi: { list: vi.fn() } }));
vi.mock('../../api/sync', () => ({ syncApi: { applyMutations: vi.fn() } }));

import AttendancePage from './AttendancePage';
import { attendanceApi } from '../../api/attendance';
import { employeesApi } from '../../api/hr';
import { syncApi } from '../../api/sync';
import { replaceCollection, resetOfflineDbConnection } from '../../offline/db';
import { useOfflineStore } from '../../offline/connectivity';
import { getCounts, listScopeMutations } from '../../offline/outbox';
import { useQueueStore } from '../../offline/queueEvents';
import { resetSyncEngineForTests } from '../../offline/syncEngine';
import { useSyncStore } from '../../offline/syncStatus';
import { useAuthStore } from '../../stores/authStore';
import { useFarmStore } from '../../stores/farmStore';
import type { AttendanceRecord, Employee, Farm, User } from '../../types';

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

const employee: Employee = {
  id: 'e-1',
  firstName: 'Asha',
  lastName: 'Khan',
  salaryType: 'Monthly',
  salaryTypeName: 'Monthly',
  salaryRate: 1000,
  hireDate: '2025-01-01T00:00:00Z',
  isActive: true,
};

const record: AttendanceRecord = {
  id: 'r-1',
  employeeId: 'e-2',
  employeeName: 'Bilal Ahmed',
  date: '2026-09-23T00:00:00.000Z',
  status: 'Present',
  statusName: 'Present',
  checkInAt: '2026-09-23T06:00:00.000Z',
  hoursWorked: 0,
};

const twoDaysAgo = new Date(Date.now() - 2 * 24 * 60 * 60 * 1000).toISOString();

const res = (data: unknown) => ({ data }) as never;

const accepted = (items: { clientMutationId: string }[]) =>
  ({
    data: {
      requestedCount: items.length,
      successCount: items.length,
      acceptedCount: items.length,
      supersededCount: 0,
      rejectedCount: 0,
      items: items.map((item, index) => ({
        index,
        clientMutationId: item.clientMutationId,
        outcome: 'Accepted',
        message: null,
        targetEntityId: 'r-1',
        result: null,
      })),
      failures: [],
    },
  }) as never;

const seedCache = async () => {
  await replaceCollection(scope, 'employees@options', [{ id: employee.id, data: employee }], {
    fetchedAt: twoDaysAgo,
  });
  await replaceCollection(scope, 'attendance@firstPage', [{ id: record.id, data: record }], {
    fetchedAt: twoDaysAgo,
  });
};

/**
 * Attendance is one of the three offline targets in the approved architecture, so its work list
 * has to be readable with no signal and writable through the same queue as a weight (4.5.5).
 */
describe('AttendancePage', () => {
  beforeEach(() => {
    (globalThis as { indexedDB: IDBFactory }).indexedDB = new IDBFactory();
    resetOfflineDbConnection();
    resetSyncEngineForTests();
    useQueueStore.setState({ revision: 0 });
    vi.clearAllMocks();

    useOfflineStore.setState({
      isOnline: false,
      storageAvailable: true,
      stats: { recordCount: 0, collectionCount: 0, lastSyncedAt: null },
    });
    useSyncStore.setState({
      pendingCount: 0,
      quarantinedCount: 0,
      appliedCount: 0,
      oldestQueuedAt: null,
      isFlushing: false,
      lastFlushAt: null,
      lastError: null,
      sessionExpired: false,
    });
    useAuthStore.setState({ user, isAuthenticated: true });
    useFarmStore.setState({ farms: [farm], activeFarm: farm, isLoading: false, error: null });

    // The requests exist and answer: the offline tests assert they are never *called*, which is
    // a stronger claim than "they fail when called".
    vi.mocked(employeesApi.list).mockResolvedValue(res({ items: [employee], totalCount: 1 }));
    vi.mocked(attendanceApi.list).mockResolvedValue(res({ items: [record], totalCount: 1 }));
  });

  it('renders the cached register and employee cards offline, with a freshness label', async () => {
    await seedCache();

    render(<AttendancePage />);

    // The employee the check-in buttons act on comes from the cached list.
    expect(await screen.findByText('Asha Khan')).toBeInTheDocument();
    // And the register shows what the device last knew.
    expect(await screen.findByText('Bilal Ahmed')).toBeInTheDocument();
    expect(screen.getAllByText('Synced 2 days ago').length).toBeGreaterThan(0);

    // Nothing was fetched: the cache is the only source offline.
    expect(attendanceApi.list).not.toHaveBeenCalled();
    expect(employeesApi.list).not.toHaveBeenCalled();
  });

  it('queues a check-in with the device time when there is no connection', async () => {
    await seedCache();
    const before = Date.now();

    render(<AttendancePage />);
    await userEvent.click(await screen.findByRole('button', { name: /In/ }));

    // Visible immediately as waiting, and the queue holds exactly one item.
    expect(await screen.findByText('Check-in waiting to sync')).toBeInTheDocument();

    const queued = await listScopeMutations(scope);
    expect(queued).toHaveLength(1);
    expect(queued[0].kind).toBe('attendance.checkIn');
    expect(queued[0].targetId).toBe(employee.id);
    expect(queued[0].status).toBe('pending');
    expect(queued[0].payload).toEqual({
      employeeId: employee.id,
      occurredAt: expect.any(String),
    });

    // The device's clock at the moment of the tap, not the time it eventually reaches the server.
    const occurredAt = Date.parse((queued[0].payload as { occurredAt: string }).occurredAt);
    expect(occurredAt).toBeGreaterThanOrEqual(before - 1_000);
    expect(occurredAt).toBeLessThanOrEqual(Date.now() + 1_000);
    expect(queued[0].occurredAt).toBe((queued[0].payload as { occurredAt: string }).occurredAt);

    // Offline, nothing is sent — and nothing is attempted.
    expect(syncApi.applyMutations).not.toHaveBeenCalled();
    expect(await getCounts(scope.accountId)).toMatchObject({ pending: 1 });
  });

  it('queues a check-out through the same machinery, as its own operation', async () => {
    await seedCache();

    render(<AttendancePage />);
    await userEvent.click(await screen.findByRole('button', { name: /Out/ }));

    expect(await screen.findByText('Check-out waiting to sync')).toBeInTheDocument();

    const queued = await listScopeMutations(scope);
    expect(queued).toHaveLength(1);
    expect(queued[0].kind).toBe('attendance.checkOut');
  });

  it('sends the queued check-in with the payload the server binds, and marks it synced', async () => {
    useOfflineStore.setState({ isOnline: true });
    vi.mocked(syncApi.applyMutations).mockImplementation(async (_farmId, items) => accepted(items));

    render(<AttendancePage />);
    await userEvent.click(await screen.findByRole('button', { name: /In/ }));

    await waitFor(() => expect(syncApi.applyMutations).toHaveBeenCalledTimes(1));
    const [farmId, envelopes] = vi.mocked(syncApi.applyMutations).mock.calls[0];

    expect(farmId).toBe(farm.id);
    expect(envelopes).toHaveLength(1);
    expect(envelopes[0].operation).toBe('attendance.checkIn');
    expect(envelopes[0].clientMutationId).toBeTruthy();
    // Byte for byte what `AttendanceMutation` binds — no client-only field, no renamed key.
    expect(envelopes[0].payload).toEqual({
      employeeId: employee.id,
      occurredAt: expect.any(String),
    });

    // The server's answer replaces the optimistic row.
    expect(await screen.findByText('Synced')).toBeInTheDocument();
    expect(await getCounts(scope.accountId)).toMatchObject({ pending: 0, applied: 1 });
    // A longer budget than the default: this path opens the store for the session marker and the
    // queue's count before it saves anything, and under a full parallel suite run the default
    // five seconds is a coin toss. The same budget is used by the other antd-heavy suites.
  }, 20_000);
});
