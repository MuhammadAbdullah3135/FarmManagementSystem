import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import 'fake-indexeddb/auto';
import { IDBFactory } from 'fake-indexeddb';

vi.mock('../../api/animals', () => ({
  animalsApi: { get: vi.fn(), list: vi.fn(), getWeights: vi.fn() },
}));
vi.mock('../../api/health', () => ({
  weightCheckStatusApi: { all: vi.fn(), overdue: vi.fn() },
  weightCheckSchedulesApi: { list: vi.fn(), create: vi.fn(), update: vi.fn(), remove: vi.fn() },
}));
vi.mock('../../api/breeding', () => ({ breedingRecordsApi: { list: vi.fn() } }));

import AnimalDetailPage from './AnimalDetailPage';
import { animalsApi } from '../../api/animals';
import { weightCheckStatusApi } from '../../api/health';
import { enqueueMutation, markApplied } from '../../offline/outbox';
import { WEIGHT_RECORD, type WeightRecordPayload } from '../../offline/mutationKinds';
import { getMeta, replaceCollection, resetOfflineDbConnection } from '../../offline/db';
import { useQueueStore } from '../../offline/queueEvents';
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

const ANIMAL = {
  id: 'animal-1',
  farmId: farm.id,
  tagNumber: 'C-001',
  name: 'Bessie',
  animalTypeId: 'type-1',
  animalTypeName: 'Cattle',
  breedName: 'Holstein',
  sexValue: 'Female',
  statusCategory: 0,
  statusName: 'Active',
  locationName: 'Barn A',
  ageCategoryName: 'Adult',
  dateOfBirth: '2023-01-01T00:00:00.000Z',
  acquisitionDate: '2023-02-01T00:00:00.000Z',
  sireTagNumber: 'C-000',
  damTagNumber: 'C-010',
  notes: 'Healthy',
  weightRecordsCount: 3,
  imagesCount: 0,
  createdAt: '2023-01-01T00:00:00.000Z',
};

const renderPage = () =>
  render(
    <MemoryRouter initialEntries={['/dashboard/animals/animal-1']}>
      <Routes>
        <Route path="/dashboard/animals/:id" element={<AnimalDetailPage />} />
      </Routes>
    </MemoryRouter>,
  );

/**
 * The weights tab is the weight-check status work list. A user who opened the animal
 * while online can lose signal and still open this tab: it must render the last-known
 * statuses (with a freshness label) rather than going blank or failing.
 *
 * (The animal record itself is not a cached collection in 4.5.2 — a cold *offline* launch
 * of this page cannot load it and still redirects. This test covers the reachable case:
 * the page is already open when connectivity drops.)
 */
describe('AnimalDetailPage weights tab offline', () => {
  beforeEach(() => {
    (globalThis as { indexedDB: IDBFactory }).indexedDB = new IDBFactory();
    resetOfflineDbConnection();
    vi.clearAllMocks();
    vi.mocked(animalsApi.get).mockResolvedValue({ data: ANIMAL } as never);
    vi.mocked(animalsApi.getWeights).mockResolvedValue({
      data: { items: [], totalCount: 0, page: 1, pageSize: 20 },
    } as never);
    vi.mocked(weightCheckStatusApi.all).mockRejectedValue(new Error('network unreachable'));

    useOfflineStore.setState({
      isOnline: true,
      storageAvailable: true,
      stats: { recordCount: 0, collectionCount: 0, lastSyncedAt: null },
    });
    useAuthStore.setState({ user, isAuthenticated: true });
    useFarmStore.setState({ farms: [farm], activeFarm: farm, isLoading: false, error: null });
  });

  it('renders the cached weight-check statuses with a freshness label', async () => {
    await replaceCollection(
      scope,
      'weightCheckStatus@all',
      [
        {
          id: 'animal-1',
          data: {
            animalId: 'animal-1',
            animalTagNumber: 'C-001',
            nextDueDate: '2026-09-30T00:00:00.000Z',
            status: 'Overdue',
            statusName: 'Overdue',
            daysUntilDue: -3,
          },
        },
      ],
      { fetchedAt: twoDaysAgo },
    );

    renderPage();

    // The animal loads as it did while online, then the connection drops.
    expect(await screen.findByText('Cattle')).toBeInTheDocument();
    useOfflineStore.setState({ isOnline: false });

    await userEvent.click(screen.getByRole('tab', { name: 'Weights' }));

    expect(await screen.findByText(/Next due:/)).toBeInTheDocument();
    expect(screen.getByText(/Overdue/)).toBeInTheDocument();
    expect(screen.getByText('Synced 2 days ago')).toBeInTheDocument();
    // The list came from the store: the status endpoint was never called.
    await waitFor(() => expect(weightCheckStatusApi.all).not.toHaveBeenCalled());
  });
});

const recordWeight = async (weightKg: number) => (await enqueueMutation<WeightRecordPayload>({
    scope,
    kind: WEIGHT_RECORD,
    targetId: ANIMAL.id,
    occurredAt: '2026-09-23T06:15:00.000Z',
    payload: {
      animalId: ANIMAL.id,
      weightKg,
      recordedAt: '2026-09-23T06:15:00.000Z',
      notes: null,
    },
  })).item;

/**
 * The animal's weights are three sources merged into one list (4.5.4): the server's records,
 * this device's queued ones, and the ones just applied. A weight recorded with no connection
 * has to appear immediately — and must not appear twice once the server list carries it.
 */
describe('AnimalDetailPage weights list', () => {
  beforeEach(() => {
    (globalThis as { indexedDB: IDBFactory }).indexedDB = new IDBFactory();
    resetOfflineDbConnection();
    useQueueStore.setState({ revision: 0 });
    vi.clearAllMocks();
    vi.mocked(animalsApi.get).mockResolvedValue({ data: ANIMAL } as never);
    vi.mocked(animalsApi.getWeights).mockResolvedValue({
      data: { items: [], totalCount: 0, page: 1, pageSize: 20 },
    } as never);
    // The status endpoint answers with the delta envelope since 5.6, not a bare array.
    vi.mocked(weightCheckStatusApi.all).mockResolvedValue({
      data: { items: [], page: 1, pageSize: 0, totalCount: 0, deletedIds: [], cursor: 'c-1', requiresFullSync: false },
    } as never);

    useOfflineStore.setState({
      isOnline: false,
      storageAvailable: true,
      stats: { recordCount: 0, collectionCount: 0, lastSyncedAt: null },
    });
    useAuthStore.setState({ user, isAuthenticated: true });
    useFarmStore.setState({ farms: [farm], activeFarm: farm, isLoading: false, error: null });
  });

  const openWeights = async () => {
    renderPage();
    expect(await screen.findByText('Cattle')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('tab', { name: 'Weights' }));
  };

  it('shows a weight recorded on this device as waiting to sync', async () => {
    await recordWeight(412);

    await openWeights();

    expect(await screen.findByText('412 kg')).toBeInTheDocument();
    expect(screen.getByText('Waiting to sync')).toBeInTheDocument();
    expect(
      screen.getByText(/Records marked “Waiting to sync” exist only on this device/),
    ).toBeInTheDocument();
    // The way to record another one, preselected to this animal.
    expect(screen.getByRole('button', { name: /Record weight/ })).toBeInTheDocument();
  });

  it('does not list a queued weight twice once the server has it', async () => {
    const item = (await recordWeight(412))!;
    await markApplied(scope, item.mutationId, { entityId: 'weight-1' });
    vi.mocked(animalsApi.getWeights).mockResolvedValue({
      data: {
        items: [
          {
            id: 'weight-1',
            animalId: ANIMAL.id,
            weightKg: 412,
            recordedAt: '2026-09-23T06:15:00.000Z',
            notes: null,
            createdAt: '2026-09-23T06:16:00.000Z',
          },
        ],
        totalCount: 1,
        page: 1,
        pageSize: 20,
      },
    } as never);

    await openWeights();

    // One row: the server's. The local copy of the same record is not repeated beside it.
    expect(await screen.findByText('Saved')).toBeInTheDocument();
    expect(screen.getAllByText('412 kg')).toHaveLength(1);
    expect(screen.queryByText('Synced')).not.toBeInTheDocument();
  });

  it('shows a refused weight with the server message and says it is not saved', async () => {
    const item = (await recordWeight(412))!;
    const { markQuarantined } = await import('../../offline/outbox');
    await markQuarantined(scope, item.mutationId, 'Weight must be greater than zero');

    await openWeights();

    expect(await screen.findByText('Not saved')).toBeInTheDocument();
    expect(screen.getByText('Weight must be greater than zero')).toBeInTheDocument();
  }, 20_000);

  it('reads the status rows out of the delta envelope the endpoint now returns', async () => {
    useOfflineStore.setState({ isOnline: true });
    vi.mocked(weightCheckStatusApi.all).mockResolvedValue({
      data: {
        items: [{
          animalId: ANIMAL.id,
          animalTagNumber: ANIMAL.tagNumber,
          animalName: ANIMAL.name,
          lastWeightDate: null,
          nextDueDate: '2026-10-01T00:00:00.000Z',
          status: 'Due',
          statusName: 'Due',
          daysUntilDue: 0,
        }],
        page: 1,
        pageSize: 1,
        totalCount: 1,
        deletedIds: [],
        cursor: '2026-09-23T08:00:00.000Z',
        requiresFullSync: false,
      },
    } as never);

    await openWeights();

    // The rows live under `items`: a page reading the old bare array would render nothing.
    expect(await screen.findByText(/Due — Next due: 2026-10-01/)).toBeInTheDocument();
    // And the first read stores the server's cursor, which is what makes the next one a delta.
    await waitFor(async () => {
      const meta = await getMeta(scope, 'weightCheckStatus@all');
      expect(meta?.cursor).toBe('2026-09-23T08:00:00.000Z');
    });
  }, 20_000);
});
