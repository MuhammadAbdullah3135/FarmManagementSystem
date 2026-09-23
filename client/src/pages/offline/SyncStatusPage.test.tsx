import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import 'fake-indexeddb/auto';
import { IDBFactory } from 'fake-indexeddb';

import SyncStatusPage from './SyncStatusPage';
import { enqueueMutation, markDismissed, markQuarantined, requeueMutation } from '../../offline/outbox';
import { WEIGHT_RECORD, type WeightRecordPayload } from '../../offline/mutationKinds';
import { getOutboxItem, replaceCollection, resetOfflineDbConnection, touchSessionMarker } from '../../offline/db';
import { MAX_QUEUED_ITEMS } from '../../offline/offlinePolicy';
import { useSyncStore } from '../../offline/syncStatus';
import { useOfflineStore } from '../../offline/connectivity';
import { useAuthStore } from '../../stores/authStore';
import { useFarmStore } from '../../stores/farmStore';
import type { Farm, User } from '../../types';

const farmA: Farm = {
  id: 'farm-a',
  name: 'Farm A',
  isActive: true,
  createdAt: '2026-01-01T00:00:00Z',
  userFarmRole: 'FarmManager',
};
const farmB: Farm = { ...farmA, id: 'farm-b', name: 'Farm B' };
const accountId = 'acct-1';

const user: User = {
  userId: 'user-1',
  accountId,
  email: 'owner@example.com',
  firstName: 'Owner',
  lastName: 'Person',
  roles: ['FarmManager'],
};

const enqueueWeight = async (
  farmId: string,
  animalId: string,
  weightKg: number,
  recordedAt: string,
) => (await enqueueMutation<WeightRecordPayload>({
    scope: { accountId, farmId },
    kind: WEIGHT_RECORD,
    targetId: animalId,
    occurredAt: recordedAt,
    payload: { animalId, weightKg, recordedAt, notes: null },
  })).item;

const renderPage = () =>
  render(
    <MemoryRouter initialEntries={['/dashboard/sync']}>
      <Routes>
        <Route path="/dashboard/sync" element={<SyncStatusPage />} />
      </Routes>
    </MemoryRouter>,
  );

/**
 * The sync screen is the user's view of the queue: what is waiting, what the server refused
 * (with its own words), and the two ways out of quarantine. It is account-scoped, so it works
 * with no farm selected — a farm whose access was removed still has items to deal with.
 */
describe('SyncStatusPage', () => {
  beforeEach(async () => {
    (globalThis as { indexedDB: IDBFactory }).indexedDB = new IDBFactory();
    resetOfflineDbConnection();
    vi.clearAllMocks();

    useOfflineStore.setState({
      isOnline: false,
      storageAvailable: true,
      stats: { recordCount: 0, collectionCount: 0, lastSyncedAt: null },
    });
    useAuthStore.setState({ user, isAuthenticated: true });
    // Deliberately no active farm: the queue is the account's, not the farm's.
    useFarmStore.setState({ farms: [farmA, farmB], activeFarm: null, isLoading: false, error: null });
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
  });

  it('lists each farm queue with its statuses, and names the animal from the cache', async () => {
    const pending = (await enqueueWeight('farm-a', 'animal-1', 412, '2026-09-23T06:00:00.000Z'))!;
    const refused = (await enqueueWeight('farm-a', 'animal-2', 388, '2026-09-23T06:10:00.000Z'))!;
    await enqueueWeight('farm-b', 'animal-3', 500, '2026-09-23T06:20:00.000Z');
    await markQuarantined(
      { accountId, farmId: 'farm-a' },
      refused.mutationId,
      'Weight must be greater than zero',
    );

    // The device knows a tag only for the farm whose lookup it cached.
    await replaceCollection(
      { accountId, farmId: 'farm-a' },
      'animals@lookup',
      [
        { id: 'animal-1', data: { id: 'animal-1', tagNumber: 'C-001', name: 'Bessie' } },
        { id: 'animal-2', data: { id: 'animal-2', tagNumber: 'C-002' } },
      ],
      { fetchedAt: '2026-09-23T05:00:00.000Z' },
    );

    await useSyncStore.getState().refresh();
    renderPage();

    // No active farm, and the screen still renders both farm queues.
    expect(await screen.findByText('Farm A')).toBeInTheDocument();
    expect(screen.getByText('Farm B')).toBeInTheDocument();

    // The refused item carries the server's own wording, verbatim.
    expect(screen.getByText('Weight must be greater than zero')).toBeInTheDocument();

    // Labels come from the cache where it exists, and fall back to the id where it does not.
    expect(await screen.findByText(/412 kg · C-001 — Bessie/)).toBeInTheDocument();
    expect(screen.getByText(/388 kg · C-002/)).toBeInTheDocument();
    expect(screen.getByText(/500 kg · animal-3/)).toBeInTheDocument();

    // The label appears on each pending row and in the summary card's heading.
    expect((await screen.findAllByText('Waiting to sync')).length).toBeGreaterThan(0);
    expect(screen.getByText('Refused')).toBeInTheDocument();
    void pending;
  }, 20_000);

  it('puts a refused record back in the queue on Retry', async () => {
    const refused = (await enqueueWeight('farm-a', 'animal-1', 412, '2026-09-23T06:00:00.000Z'))!;
    await markQuarantined({ accountId, farmId: 'farm-a' }, refused.mutationId, 'Weight must be greater than zero');
    await useSyncStore.getState().refresh();

    renderPage();
    // `fireEvent` rather than a full user gesture: antd's tooltips make the pointer simulation
    // cost seconds per click, and the click itself is the only part under test here.
    fireEvent.click(await screen.findByRole('button', { name: /Retry/ }));

    expect(await screen.findByText('Waiting to sync')).toBeInTheDocument();
    const stored = (await getOutboxItem({ accountId, farmId: 'farm-a' }, refused.mutationId))!;
    expect(stored.status).toBe('pending');
    expect(stored.serverMessage).toBeNull();
  }, 20_000);

  it('requires a reason to dismiss a refused record, and keeps it afterwards', async () => {
    const refused = (await enqueueWeight('farm-a', 'animal-1', 412, '2026-09-23T06:00:00.000Z'))!;
    await markQuarantined({ accountId, farmId: 'farm-a' }, refused.mutationId, 'Weight must be greater than zero');
    await useSyncStore.getState().refresh();

    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: /Dismiss/ }));

    const dialog = await screen.findByRole('dialog');
    const confirm = within(dialog).getByRole('button', { name: 'Dismiss' });
    expect(confirm).toBeDisabled();

    await userEvent.type(
      within(dialog).getByPlaceholderText(/weighed the wrong animal/),
      'weighed the wrong animal',
    );
    fireEvent.click(confirm);

    // Dismissed, not deleted: the record and the reason are both still there.
    expect(await screen.findByText('Dismissed')).toBeInTheDocument();
    expect(screen.getByText('weighed the wrong animal')).toBeInTheDocument();
    const stored = (await getOutboxItem({ accountId, farmId: 'farm-a' }, refused.mutationId))!;
    expect(stored.status).toBe('dismissed');
    expect(stored.payload).toMatchObject({ weightKg: 412 });
  }, 20_000);

  it('offers a retry for a record the user previously dismissed', async () => {
    const gone = (await enqueueWeight('farm-a', 'animal-1', 412, '2026-09-23T06:00:00.000Z'))!;
    await markQuarantined({ accountId, farmId: 'farm-a' }, gone.mutationId, 'nope');
    await markDismissed({ accountId, farmId: 'farm-a' }, gone.mutationId, 'duplicate');
    await useSyncStore.getState().refresh();

    renderPage();

    expect(await screen.findByText(/Dismissed: duplicate/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Retry/ }));

    expect(await screen.findByText('Waiting to sync')).toBeInTheDocument();
    expect((await getOutboxItem({ accountId, farmId: 'farm-a' }, gone.mutationId))!.status).toBe('pending');
  }, 20_000);

  it('says a session expired rather than pretending the records sent', async () => {
    await enqueueWeight('farm-a', 'animal-1', 412, '2026-09-23T06:00:00.000Z');
    useSyncStore.setState({ sessionExpired: true });
    await useSyncStore.getState().refresh();

    renderPage();

    expect(
      await screen.findByText('Your session expired before these records could be sent'),
    ).toBeInTheDocument();
  }, 20_000);

  it('says the session has gone too long without the server, and what to do about it', async () => {
    // Eight days of no contact: past the point where new offline writes are refused, which is
    // exactly what this screen exists to explain before the user hits it recording something.
    await touchSessionMarker(accountId, new Date(Date.now() - 8 * 24 * 60 * 60 * 1000).toISOString());
    await useSyncStore.getState().refresh();

    renderPage();

    expect(
      await screen.findByText('This device has gone too long without reaching the server'),
    ).toBeInTheDocument();
    expect(screen.getByText(/has not reached the server in 8 days/)).toBeInTheDocument();
  }, 20_000);

  it('warns that the queue is nearly full before it refuses anything', async () => {
    await useSyncStore.setState({ capacity: { pendingCount: MAX_QUEUED_ITEMS - 100, remaining: 100, warning: true, full: false } });

    renderPage();

    expect(await screen.findByText('The queue is nearly full')).toBeInTheDocument();
    expect(screen.getByText(/100 left/)).toBeInTheDocument();
  }, 20_000);

  it('is reachable without an active farm even when the queue is empty', async () => {
    renderPage();

    expect(await screen.findByText('Nothing is waiting on this device.')).toBeInTheDocument();
  }, 20_000);
});

/** Guards the assumption the Retry path rests on: a requeue is what the screen offers. */
describe('requeueMutation', () => {
  beforeEach(() => {
    (globalThis as { indexedDB: IDBFactory }).indexedDB = new IDBFactory();
    resetOfflineDbConnection();
  });

  it('clears a refusal so the item can be sent again', async () => {
    const item = (await enqueueWeight('farm-a', 'animal-1', 412, '2026-09-23T06:00:00.000Z'))!;
    await markQuarantined({ accountId, farmId: 'farm-a' }, item.mutationId, 'nope');

    await requeueMutation({ accountId, farmId: 'farm-a' }, item.mutationId);

    const stored = (await getOutboxItem({ accountId, farmId: 'farm-a' }, item.mutationId))!;
    expect(stored.status).toBe('pending');
    expect(stored.attempts).toBe(1);
  }, 20_000);
});
