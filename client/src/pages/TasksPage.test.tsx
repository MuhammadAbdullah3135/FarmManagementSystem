import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import 'fake-indexeddb/auto';
import { IDBFactory } from 'fake-indexeddb';

vi.mock('../api/tasks', () => ({
  tasksApi: {
    list: vi.fn(),
    start: vi.fn(),
    complete: vi.fn(),
    cancel: vi.fn(),
    reopen: vi.fn(),
    remove: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
  },
}));
vi.mock('../api/hr', () => ({ employeesApi: { list: vi.fn() } }));
vi.mock('../api/sync', () => ({ syncApi: { applyMutations: vi.fn() } }));

import TasksPage from './TasksPage';
import { tasksApi } from '../api/tasks';
import { syncApi } from '../api/sync';
import { replaceCollection, resetOfflineDbConnection } from '../offline/db';
import { useOfflineStore } from '../offline/connectivity';
import { getCounts, listScopeMutations } from '../offline/outbox';
import { useQueueStore } from '../offline/queueEvents';
import { resetSyncEngineForTests } from '../offline/syncEngine';
import { useSyncStore } from '../offline/syncStatus';
import { useAuthStore } from '../stores/authStore';
import { useFarmStore } from '../stores/farmStore';
import type { Farm, User } from '../types';

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

/**
 * The offline acceptance criterion for the cached work lists (4.5.2): with the API
 * unreachable, the page renders the last-known rows and says how old they are — no
 * throw, no blank table, and no request attempted.
 */
describe('TasksPage offline', () => {
  beforeEach(() => {
    (globalThis as { indexedDB: IDBFactory }).indexedDB = new IDBFactory();
    resetOfflineDbConnection();
    vi.clearAllMocks();
    resetSyncEngineForTests();
    useQueueStore.setState({ revision: 0 });
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
  });

  it('renders the cached task list with a freshness label instead of failing', async () => {
    await replaceCollection(
      scope,
      'tasks@firstPage',
      [
        {
          id: 't-1',
          data: {
            id: 't-1',
            title: 'Repair the fence',
            priority: 'High',
            status: 'Pending',
            dueDate: '2026-09-24T00:00:00.000Z',
            isOverdue: false,
          },
        },
      ],
      { fetchedAt: twoDaysAgo },
    );
    await replaceCollection(
      scope,
      'employees@options',
      [{ id: 'e-1', data: { id: 'e-1', firstName: 'Asha', lastName: 'Khan' } }],
      { fetchedAt: twoDaysAgo },
    );

    render(<TasksPage />);

    expect(await screen.findByText('Repair the fence')).toBeInTheDocument();
    expect(screen.getByText('Synced 2 days ago')).toBeInTheDocument();
    // The table is a real table, not a broken/blank state.
    expect(screen.getByText('Title')).toBeInTheDocument();
    // Offline: the fetch is never attempted, so nothing can fail or hang.
    expect(tasksApi.list).not.toHaveBeenCalled();
  });

  it('shows no freshness label when the device has nothing stored', async () => {
    render(<TasksPage />);

    // An empty table (as an empty farm looks online) — honest, and no sync claim.
    expect(await screen.findByText('Title')).toBeInTheDocument();
    expect(screen.queryByText(/Synced/)).not.toBeInTheDocument();
  });
});

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
        targetEntityId: 't-1',
        result: null,
      })),
      failures: [],
    },
  }) as never;

const cacheTask = async () => {
  await replaceCollection(
    scope,
    'tasks@firstPage',
    [
      {
        id: 't-1',
        data: {
          id: 't-1',
          title: 'Repair the fence',
          priority: 'High',
          status: 'Pending',
          dueDate: '2026-09-24T00:00:00.000Z',
          isOverdue: false,
        },
      },
    ],
    { fetchedAt: twoDaysAgo },
  );
};

/** Completes the cached task through the modal and returns nothing: the queue is the assertion. */
const completeTask = async (notes: string) => {
  render(<TasksPage />);
  await screen.findByText('Repair the fence');

  await userEvent.click(screen.getByLabelText('Complete task'));
  const dialog = await screen.findByRole('dialog');
  await userEvent.type(within(dialog).getByRole('textbox'), notes);
  await userEvent.click(within(dialog).getByRole('button', { name: 'OK' }));
};

/**
 * Task completion is fieldwork, so 4.5.5 puts it on the same outbox machinery as a weight:
 * one write path, the device's own time, and the server's answer (or its refusal) replacing
 * the row afterwards.
 */
describe('TasksPage completion queue', () => {
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
  });

  it('queues a completion with its notes and the device time, and never calls the live endpoint', async () => {
    await cacheTask();
    const before = Date.now();

    await completeTask('Fence repaired');

    // The task row says it is waiting rather than pretending the server has it — and so does
    // the queue card below, which is the same state stated twice on purpose.
    expect((await screen.findAllByText('Waiting to sync')).length).toBeGreaterThanOrEqual(2);

    const queued = await listScopeMutations(scope);
    expect(queued).toHaveLength(1);
    expect(queued[0].kind).toBe('task.complete');
    expect(queued[0].targetId).toBe('t-1');
    expect(queued[0].payload).toEqual({
      taskId: 't-1',
      completionNotes: 'Fence repaired',
      occurredAt: expect.any(String),
    });

    const occurredAt = Date.parse((queued[0].payload as { occurredAt: string }).occurredAt);
    expect(occurredAt).toBeGreaterThanOrEqual(before - 1_000);
    expect(occurredAt).toBeLessThanOrEqual(Date.now() + 1_000);

    // One write path: the completion reached the queue, not the single-record endpoint, and
    // offline nothing was sent.
    expect(tasksApi.complete).not.toHaveBeenCalled();
    expect(syncApi.applyMutations).not.toHaveBeenCalled();
    expect(await getCounts(scope.accountId)).toMatchObject({ pending: 1 });
  });

  it('sends the completion with the payload the server binds, and marks it synced', async () => {
    await cacheTask();
    useOfflineStore.setState({ isOnline: true });
    vi.mocked(syncApi.applyMutations).mockImplementation(async (_farmId, items) => accepted(items));

    await completeTask('Fence repaired');

    await waitFor(() => expect(syncApi.applyMutations).toHaveBeenCalledTimes(1));
    const [farmId, envelopes] = vi.mocked(syncApi.applyMutations).mock.calls[0];

    expect(farmId).toBe(farm.id);
    expect(envelopes).toHaveLength(1);
    expect(envelopes[0].operation).toBe('task.complete');
    // Byte for byte what `TaskCompletionMutation` binds.
    expect(envelopes[0].payload).toEqual({
      taskId: 't-1',
      completionNotes: 'Fence repaired',
      occurredAt: expect.any(String),
    });

    expect(await screen.findByText('Synced')).toBeInTheDocument();
    expect(tasksApi.complete).not.toHaveBeenCalled();
    expect(await getCounts(scope.accountId)).toMatchObject({ pending: 0, applied: 1 });
  });

  it('keeps cancelling a live request: it is not one of the offline workflows', async () => {
    await cacheTask();
    vi.mocked(tasksApi.cancel).mockResolvedValue({ data: {} } as never);

    render(<TasksPage />);
    await screen.findByText('Repair the fence');
    await userEvent.click(screen.getByLabelText('Cancel task'));

    const dialog = await screen.findByRole('dialog');
    await userEvent.click(within(dialog).getByRole('button', { name: 'OK' }));

    await waitFor(() => expect(tasksApi.cancel).toHaveBeenCalledTimes(1));
    expect(await listScopeMutations(scope)).toHaveLength(0);
  });
});
