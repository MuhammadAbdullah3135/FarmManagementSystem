import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import 'fake-indexeddb/auto';
import { IDBFactory } from 'fake-indexeddb';

vi.mock('../../api/attendance', () => ({ lookupsApi: { animals: vi.fn() } }));
vi.mock('../../api/sync', () => ({ syncApi: { applyMutations: vi.fn() } }));

import RecordWeightPage from './RecordWeightPage';
import { lookupsApi } from '../../api/attendance';
import { syncApi, type SyncMutationResult } from '../../api/sync';
import type { MutationEnvelope, WeightRecordPayload } from '../../offline/mutationKinds';
import { replaceCollection, resetOfflineDbConnection, touchSessionMarker } from '../../offline/db';
import { useOfflineStore } from '../../offline/connectivity';
import { useAuthStore } from '../../stores/authStore';
import { useFarmStore } from '../../stores/farmStore';
import { getCounts } from '../../offline/outbox';
import { useQueueStore } from '../../offline/queueEvents';
import { resetSyncEngineForTests } from '../../offline/syncEngine';
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

const res = (data: unknown) => ({ data }) as never;
const ANIMALS = [{ id: 'animal-1', tagNumber: 'C-001', name: 'Bessie' }];

const renderPage = () =>
  render(
    <MemoryRouter initialEntries={['/dashboard/records/weight']}>
      <Routes>
        <Route path="/dashboard/records/weight" element={<RecordWeightPage />} />
      </Routes>
    </MemoryRouter>,
  );

/** antd v6 renders select options as hidden a11y mirrors; click the content element. */
const selectOption = (label: string) => {
  const content = [...document.querySelectorAll('.ant-select-item-option-content')].find(
    (element) => element.textContent === label,
  );
  expect(content).toBeTruthy();
  fireEvent.click(content!.closest('.ant-select-item-option') as HTMLElement);
};

const recordWeight = async (user_: ReturnType<typeof userEvent.setup>, weight: string) => {
  // The picker is only enabled once it has rows to offer (the cache read offline, the lookup
  // request online), so wait for that before opening it.
  await waitFor(() => expect(screen.getByRole('combobox')).not.toBeDisabled());
  await user_.click(screen.getByRole('combobox'));
  // The picker's options arrive from the cache read (offline) or the lookup request (online),
  // so wait for them rather than assuming the drop-down is already populated.
  await waitFor(() => {
    const options = document.querySelectorAll('.ant-select-item-option-content');
    if (options.length === 0) throw new Error('the animal picker has no options yet');
  });
  selectOption('C-001 — Bessie');
  await user_.type(screen.getByRole('spinbutton'), weight);
  await user_.click(screen.getByRole('button', { name: /Save weight/ }));
};

const accepted = (items: MutationEnvelope[]): { data: SyncMutationResult } => ({
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
      targetEntityId: `weight-${index}`,
      result: null,
    })),
    failures: [],
  },
});

/**
 * Weight recording is the one write the queue carries in 4.5.4. Offline it must land on the
 * device immediately and be visible as waiting; online it must go through the same queue and
 * come back as the server's own row — and the animal can only ever be one the device holds.
 */
describe('RecordWeightPage', () => {
  beforeEach(() => {
    (globalThis as { indexedDB: IDBFactory }).indexedDB = new IDBFactory();
    resetOfflineDbConnection();
    resetSyncEngineForTests();
    useQueueStore.setState({ revision: 0 });
    vi.clearAllMocks();

    useOfflineStore.setState({
      isOnline: true,
      storageAvailable: true,
      stats: { recordCount: 0, collectionCount: 0, lastSyncedAt: null },
    });
    useAuthStore.setState({ user, isAuthenticated: true });
    useFarmStore.setState({ farms: [farm], activeFarm: farm, isLoading: false, error: null });
    vi.mocked(lookupsApi.animals).mockResolvedValue(res({ items: ANIMALS, totalCount: 1 }));
    vi.mocked(syncApi.applyMutations).mockImplementation(
      async (_farmId, items) => accepted(items) as never,
    );
  });

  it('records offline, from cache only, and shows the weight as waiting to sync', async () => {
    useOfflineStore.setState({ isOnline: false });
    await replaceCollection(
      scope,
      'animals@lookup',
      ANIMALS.map((animal) => ({ id: animal.id, data: animal })),
      { fetchedAt: new Date().toISOString() },
    );

    renderPage();
    await recordWeight(userEvent.setup(), '412');

    // The row is on screen immediately, marked as not yet sent.
    expect(await screen.findByText(/Waiting to sync/)).toBeInTheDocument();
    expect(screen.getByText(/412 kg · C-001 — Bessie/)).toBeInTheDocument();

    // Nothing was sent, and — the cache-only rule — the animal lookup was not fetched either:
    // the picker read the device's stored list.
    expect(syncApi.applyMutations).not.toHaveBeenCalled();
    expect(lookupsApi.animals).not.toHaveBeenCalled();

    const counts = await getCounts(scope.accountId);
    expect(counts.pending).toBe(1);
  }, 20_000);

  it('sends a queued weight with the device time and the server payload shape', async () => {
    const before = Date.now();
    renderPage();
    await recordWeight(userEvent.setup(), '412');

    await waitFor(() => expect(syncApi.applyMutations).toHaveBeenCalledTimes(1));
    const [farmId, envelopes] = vi.mocked(syncApi.applyMutations).mock.calls[0];

    expect(farmId).toBe(farm.id);
    // Byte for byte what the server binds: `WeightRecordMutation` — no client-only field, no
    // renamed key. (This is the guard against a payload drift the server would silently
    // default its way past.)
    expect(envelopes).toHaveLength(1);
    expect(envelopes[0].operation).toBe('weight.record');
    expect(envelopes[0].clientMutationId).toBeTruthy();
    expect(envelopes[0].payload).toEqual({
      animalId: 'animal-1',
      weightKg: 412,
      recordedAt: expect.any(String),
      notes: null,
    });

    // The device's clock at capture time, not the upload time.
    const recordedAt = Date.parse((envelopes[0].payload as WeightRecordPayload).recordedAt);
    expect(recordedAt).toBeGreaterThanOrEqual(before - 1_000);
    expect(Date.parse((envelopes[0].payload as WeightRecordPayload).recordedAt)).toBeLessThanOrEqual(Date.now() + 1_000);

    // The optimistic row is replaced by the server's answer.
    expect(await screen.findByText('Synced')).toBeInTheDocument();
    expect(await getCounts(scope.accountId)).toMatchObject({ pending: 0, applied: 1 });
  }, 20_000);

  it('rolls the row back with the server message when the server refuses it', async () => {
    vi.mocked(syncApi.applyMutations).mockImplementation(async (_farmId, items) => ({
      data: {
        requestedCount: items.length,
        successCount: 0,
        acceptedCount: 0,
        supersededCount: 0,
        rejectedCount: items.length,
        items: items.map((item, index) => ({
          index,
          clientMutationId: item.clientMutationId,
          outcome: 'Rejected' as const,
          message: 'Weight must be greater than zero',
          targetEntityId: null,
          result: null,
        })),
        failures: [{ index: 0, message: 'Weight must be greater than zero' }],
      },
    }) as never);

    renderPage();
    await recordWeight(userEvent.setup(), '412');

    // The server's own wording, and the record is not silently dropped: it is quarantined and
    // visible with a way out (the Retry/Dismiss buttons the table renders).
    expect(await screen.findByText('Weight must be greater than zero')).toBeInTheDocument();
    expect(screen.getByText('Refused')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Retry/ })).toBeInTheDocument();
    expect(await getCounts(scope.accountId)).toMatchObject({ pending: 0, quarantined: 1 });
  }, 20_000);

  /*
   * A refusal must not look like a save, and must not throw the measurement away.
   *
   * Both 5.6 refusals (the write window and the queue cap) fail through the same branch — the
   * page shows the reason and returns *before* it clears the form — so this test drives the
   * one that is reachable honestly, by ageing the session marker the way a long spell offline
   * would. The cap's own classification is asserted at `enqueueMutation`, its only writer.
   */
  it('keeps the entered weight when the write window refuses it', async () => {
    await touchSessionMarker(
      scope.accountId,
      new Date(Date.now() - 8 * 24 * 60 * 60 * 1000).toISOString(),
    );

    renderPage();
    await recordWeight(userEvent.setup(), '412');

    // The reason, in the policy's own words, with the way out.
    expect(await screen.findByText(/no longer be delivered reliably/)).toBeInTheDocument();
    // …and the weight is still in the box: a worker who has to re-type a measurement is one
    // who may not bother. (Compared as a number: the input formats what the user typed.)
    const field = screen.getByRole('spinbutton') as HTMLInputElement;
    expect(Number(field.value)).toBe(412);

    // Nothing was queued and nothing was sent, so the screen cannot be read as "saved".
    expect(await getCounts(scope.accountId)).toMatchObject({ pending: 0, applied: 0 });
    expect(syncApi.applyMutations).not.toHaveBeenCalled();
    expect(screen.queryByText(/Waiting to sync/)).not.toBeInTheDocument();
  }, 20_000);

  it('refuses to guess an animal when nothing is stored on the device', async () => {
    useOfflineStore.setState({ isOnline: false });
    vi.mocked(lookupsApi.animals).mockResolvedValue(res({ items: ANIMALS, totalCount: 1 }));

    renderPage();

    expect(
      await screen.findByText(/No animals are stored on this device yet/),
    ).toBeInTheDocument();
    // There is no free-text tag path: the only animal the form can name is a cached one.
    expect(screen.getByText('No animals available')).toBeInTheDocument();
    expect(screen.queryByRole('combobox')).toBeDisabled();
  }, 20_000);

  it('keeps two weights taken at the same instant as two records', async () => {
    renderPage();

    const user_ = userEvent.setup();
    const slow = { timeout: 5_000 };

    // A second animal weighed in the same minute is a second measurement, not a correction:
    // the queue never merges items, and the server's weight index is non-unique on
    // (AnimalId, RecordedAt).
    //
    // The waits are explicit because a completed pass now writes the session marker (5.6's
    // offline-write window) as well as the queue rows, so a flush is a few storage calls longer
    // — and under the whole suite's parallel load the testing-library default of one second is
    // not enough. The assertions are unchanged.
    await recordWeight(user_, '412');
    await waitFor(() => expect(screen.getByText('Synced')).toBeInTheDocument(), slow);

    await recordWeight(user_, '418');
    await waitFor(() => expect(syncApi.applyMutations).toHaveBeenCalledTimes(2), slow);

    const sent = vi.mocked(syncApi.applyMutations).mock.calls
      .flatMap(([, items]) => items)
      .map((item) => (item.payload as WeightRecordPayload).weightKg);
    expect(sent).toEqual([412, 418]);
  }, 20_000);
});
