import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import 'fake-indexeddb/auto';
import { IDBFactory } from 'fake-indexeddb';
import type { AxiosError, AxiosResponse } from 'axios';

// ── mocks declared before the engine is imported ─────────────

vi.mock('../api/sync', () => ({
  syncApi: { applyMutations: vi.fn() },
}));

vi.mock('../api/accessToken', () => ({
  TOKEN_REFRESH_WINDOW_MS: 120_000,
  isAccessTokenExpiringWithin: vi.fn(() => false),
}));

vi.mock('../api/axios', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/axios')>();
  return {
    ...actual,
    ensureFreshAccessToken: vi.fn(async () => 'fresh-token'),
    setSyncTriggerHandler: vi.fn(),
  };
});

import { syncApi, type SyncMutationResult } from '../api/sync';
import { ensureFreshAccessToken, setSyncTriggerHandler } from '../api/axios';
import { isAccessTokenExpiringWithin } from '../api/accessToken';
import {
  BATCH_SPACING_MS,
  MAX_ITEMS_PER_REQUEST,
  initSyncEngine,
  requestFlush,
  resetSyncEngineForTests,
} from './syncEngine';
import { enqueueMutation, getCounts, listPendingMutations } from './outbox';
import {
  ATTENDANCE_CHECK_IN,
  TASK_COMPLETE,
  WEIGHT_RECORD,
  type AttendanceMutationPayload,
  type MutationEnvelope,
  type TaskCompletionPayload,
  type WeightRecordPayload,
} from './mutationKinds';
import {
  getOutboxItem,
  getSessionMarker,
  resetOfflineDbConnection,
  touchSessionMarker,
  type OfflineScope,
  type OutboxItem,
} from './db';
import { currentSessionState } from './offlineData';
import { useSyncStore } from './syncStatus';
import { useOfflineStore } from './connectivity';
import { useAuthStore } from '../stores/authStore';
import { useFarmStore } from '../stores/farmStore';
import type { Farm, User } from '../types';

const account = 'acct-1';
const farmId = 'farm-a';
const scope = { accountId: account, farmId };

const user: User = {
  userId: 'user-1',
  accountId: account,
  email: 'owner@example.com',
  firstName: 'Owner',
  lastName: 'Person',
  roles: ['FarmManager'],
};

const farm: Farm = {
  id: farmId,
  name: 'Farm A',
  isActive: true,
  createdAt: '2026-01-01T00:00:00Z',
  userFarmRole: 'FarmManager',
};

// ── helpers ─────────────────────────────────────────────────

const weightPayload = (animalId: string, weightKg: number, recordedAt: string): WeightRecordPayload => ({
  animalId,
  weightKg,
  recordedAt,
  notes: null,
});

/**
 * Enqueues a weight one simulated second after the previous one.
 *
 * The queue's order is its `queuedAt`, and three items queued in the same millisecond are
 * ordered by mutation id — which is stable but not what a test asserting "oldest first" means.
 * Moving the clock makes the assertions describe the ordering rule rather than the tie-break.
 */
const enqueueWeight = async (animalId: string, weightKg: number, recordedAt: string, target = scope) => {
  vi.setSystemTime(new Date(Date.now() + 1_000));
  const result = await enqueueMutation<WeightRecordPayload>({
    scope: target,
    kind: WEIGHT_RECORD,
    targetId: animalId,
    occurredAt: recordedAt,
    payload: weightPayload(animalId, weightKg, recordedAt),
  });

  if (!result.item) throw new Error(`enqueue refused: ${result.refusal ?? 'unknown'}`);
  return result.item;
};

/*
 * Fake timers here control the *engine's* clock and nothing else.
 *
 * IndexedDB work in this suite is scheduled through a real `setImmediate` (fake-indexeddb's
 * own task queue, see its `lib/scheduling.js`), which `advanceTimersByTimeAsync` does not
 * yield to — advancing time alone leaves every database read and write parked, and the flush
 * under test never gets past its first storage call. These are captured before any test
 * installs fake timers, so they stay real, and every step below mixes a fake-time advance
 * with a real macrotask turn.
 */
const realSetTimeout = globalThis.setTimeout.bind(globalThis);
const realSetImmediate = (globalThis as { setImmediate?: (fn: () => void) => void }).setImmediate;

const realYield = () =>
  new Promise<void>((resolve) => {
    if (typeof realSetImmediate === 'function') realSetImmediate(resolve);
    else realSetTimeout(resolve, 0);
  });

/**
 * Several real turns per fake-time step.
 *
 * Applying a batch of 200 results is 200 sequential storage transactions, each of which needs
 * its own macrotask turn; one turn per second of fake time would take longer than the test's
 * timeout to work through them.
 */
const drain = async (turns = 16) => {
  for (let turn = 0; turn < turns; turn++) await realYield();
};

const STEP_MS = 1_000;

/** Advances fake time in steps, letting real (storage) tasks run between them. */
const advance = async (ms: number) => {
  for (let elapsed = 0; elapsed < ms; elapsed += STEP_MS) {
    await vi.advanceTimersByTimeAsync(Math.min(STEP_MS, ms - elapsed));
    await drain();
  }
};

/**
 * Advances fake time until a flush settles.
 *
 * A pass that has to wait (a rate limit, a backoff, the pause between batches) finishes only
 * once its timers fire, and the timer is scheduled by work that runs *during* the advance —
 * so this steps the clock rather than jumping it once.
 */
const settle = async (pass: Promise<void>) => {
  let done = false;
  void pass.then(() => {
    done = true;
  });

  for (let step = 0; step < 200 && !done; step++) {
    await vi.advanceTimersByTimeAsync(STEP_MS);
    await drain(32);
  }

  await pass;
};

/**
 * Advances fake time until `predicate` holds, or the fake-time budget runs out.
 *
 * For the cases a single pass cannot cover: a retry the engine scheduled for itself runs as
 * its own pass, so there is no promise to await — only a condition to wait for.
 */
const until = async (predicate: () => boolean, budgetMs = 30_000) => {
  for (let elapsed = 0; elapsed < budgetMs && !predicate(); elapsed += STEP_MS) {
    await vi.advanceTimersByTimeAsync(STEP_MS);
    await drain();
  }
  return predicate();
};

/** The bit of an axios response the engine reads. */
const responseWith = (data: SyncMutationResult): AxiosResponse<SyncMutationResult> =>
  ({ data, status: 200, statusText: '', headers: {}, config: {} }) as AxiosResponse<SyncMutationResult>;

const accepted = (envelopes: MutationEnvelope[]): AxiosResponse<SyncMutationResult> =>
  responseWith({
    requestedCount: envelopes.length,
    successCount: envelopes.length,
    acceptedCount: envelopes.length,
    supersededCount: 0,
    rejectedCount: 0,
    items: envelopes.map((envelope, index) => ({
      index,
      clientMutationId: envelope.clientMutationId,
      outcome: 'Accepted' as const,
      message: null,
      targetEntityId: `created-${index}`,
      result: null,
    })),
    failures: [],
  });

const rejected = (
  envelopes: MutationEnvelope[],
  rejectedIndexes: number[],
  message: string,
): AxiosResponse<SyncMutationResult> => {
  const items = envelopes.map((envelope, index) => {
    const isRejected = rejectedIndexes.includes(index);
    return {
      index,
      clientMutationId: envelope.clientMutationId,
      outcome: (isRejected ? 'Rejected' : 'Accepted') as 'Rejected' | 'Accepted',
      message: isRejected ? message : null,
      targetEntityId: isRejected ? null : `created-${index}`,
      result: null,
    };
  });

  return responseWith({
    requestedCount: envelopes.length,
    successCount: envelopes.length - rejectedIndexes.length,
    acceptedCount: envelopes.length - rejectedIndexes.length,
    supersededCount: 0,
    rejectedCount: rejectedIndexes.length,
    items,
    failures: rejectedIndexes.map((index) => ({ index, message })),
  });
};

const httpError = (
  status: number,
  data: unknown = '',
  headers: Record<string, string> = {},
): AxiosError => {
  const error = new Error('Request failed') as AxiosError;
  error.config = { headers: {} } as AxiosError['config'];
  error.response = { status, data, headers, config: {}, statusText: '' } as AxiosError['response'];
  error.isAxiosError = true;
  return error;
};

const pendingIds = async (): Promise<string[]> =>
  (await listPendingMutations(account)).map((item) => item.mutationId);

const syncCalls = () => vi.mocked(syncApi.applyMutations).mock.calls;

/** Every mutation id the engine ever put on the wire, in order. */
const sentMutationIds = (): string[] =>
  syncCalls().flatMap(([, items]) => items.map((item) => item.clientMutationId));

const configure = () => {
  useAuthStore.setState({ user, isAuthenticated: true });
  useFarmStore.setState({ farms: [farm], activeFarm: farm, isLoading: false, error: null });
  useOfflineStore.setState({
    isOnline: true,
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
};

describe('sync engine', () => {
  beforeEach(() => {
    (globalThis as { indexedDB: IDBFactory }).indexedDB = new IDBFactory();
    resetOfflineDbConnection();
    resetSyncEngineForTests();
    configure();
    vi.mocked(syncApi.applyMutations).mockReset();
    vi.mocked(ensureFreshAccessToken).mockReset();
    vi.mocked(ensureFreshAccessToken).mockResolvedValue('fresh-token');
    vi.mocked(isAccessTokenExpiringWithin).mockReset();
    vi.mocked(isAccessTokenExpiringWithin).mockReturnValue(false);
  });

  afterEach(() => {
    resetSyncEngineForTests();
    vi.useRealTimers();
  });

  it('sends nothing while the device is offline, and keeps the work', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    vi.setSystemTime(new Date('2026-09-23T08:00:00.000Z'));
    await enqueueWeight('a-1', 412, '2026-09-23T06:00:00.000Z');
    useOfflineStore.setState({ isOnline: false });

    await requestFlush();

    expect(syncApi.applyMutations).not.toHaveBeenCalled();
    expect(await pendingIds()).toHaveLength(1);
  });

  it('sends every pending item in one batch, oldest first, with the device time', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    vi.setSystemTime(new Date('2026-09-23T08:00:00.000Z'));

    await enqueueWeight('a-1', 412, '2026-09-23T06:00:00.000Z');
    await enqueueWeight('a-2', 388, '2026-09-23T06:30:00.000Z');
    await enqueueWeight('a-1', 415, '2026-09-23T07:00:00.000Z');

    vi.mocked(syncApi.applyMutations).mockImplementation(async (_farmId, items) => accepted(items));

    await requestFlush();

    expect(syncCalls()).toHaveLength(1);
    const [calledFarmId, envelopes] = syncCalls()[0];
    expect(calledFarmId).toBe(farmId);
    expect(envelopes.map((envelope) => envelope.payload)).toEqual([
      { animalId: 'a-1', weightKg: 412, recordedAt: '2026-09-23T06:00:00.000Z', notes: null },
      { animalId: 'a-2', weightKg: 388, recordedAt: '2026-09-23T06:30:00.000Z', notes: null },
      { animalId: 'a-1', weightKg: 415, recordedAt: '2026-09-23T07:00:00.000Z', notes: null },
    ]);

    const counts = await getCounts(account);
    expect(counts.pending).toBe(0);
    expect(counts.applied).toBe(3);
    expect(useSyncStore.getState().pendingCount).toBe(0);
    expect(useSyncStore.getState().lastFlushAt).not.toBeNull();
  });

  it('two weights at the same timestamp from two devices are both kept', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    vi.setSystemTime(new Date('2026-09-23T08:00:00.000Z'));

    // The same instant, the same animal, recorded on two devices (the second item's id is a
    // separate mutation because the payload is a separate measurement).
    await enqueueWeight('a-1', 412, '2026-09-23T06:00:00.000Z');
    await enqueueWeight('a-1', 413, '2026-09-23T06:00:00.000Z');
    vi.mocked(syncApi.applyMutations).mockImplementation(async (_farmId, items) => accepted(items));

    await requestFlush();

    expect(syncCalls()).toHaveLength(1);
    expect(syncCalls()[0][1]).toHaveLength(2);
    expect((await getCounts(account)).applied).toBe(2);
    // Nothing merged them: the server's weight index is non-unique on (AnimalId, RecordedAt),
    // and the queue does not dedupe either.
    const sent = syncCalls()[0][1].map((envelope) => (envelope.payload as WeightRecordPayload).weightKg);
    expect(sent).toEqual([412, 413]);
  });

  it('refreshes a nearly expired token before sending', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    vi.setSystemTime(new Date('2026-09-23T08:00:00.000Z'));
    await enqueueWeight('a-1', 412, '2026-09-23T06:00:00.000Z');
    vi.mocked(isAccessTokenExpiringWithin).mockReturnValue(true);
    vi.mocked(syncApi.applyMutations).mockImplementation(async (_farmId, items) => accepted(items));

    await requestFlush();

    expect(vi.mocked(ensureFreshAccessToken).mock.invocationCallOrder[0])
      .toBeLessThan(vi.mocked(syncApi.applyMutations).mock.invocationCallOrder[0]);
    expect(syncCalls()).toHaveLength(1);
  });

  it('sends nothing when the session cannot be renewed', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    vi.setSystemTime(new Date('2026-09-23T08:00:00.000Z'));
    await enqueueWeight('a-1', 412, '2026-09-23T06:00:00.000Z');
    vi.mocked(isAccessTokenExpiringWithin).mockReturnValue(true);
    vi.mocked(ensureFreshAccessToken).mockRejectedValue(new Error('No refresh token stored'));

    await requestFlush();

    expect(syncApi.applyMutations).not.toHaveBeenCalled();
    expect(useSyncStore.getState().sessionExpired).toBe(true);
    expect(await pendingIds()).toHaveLength(1);
  });

  it('refreshes once on a 401 and retries the same items', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    vi.setSystemTime(new Date('2026-09-23T08:00:00.000Z'));

    const first = (await enqueueWeight('a-1', 412, '2026-09-23T06:00:00.000Z'))!;
    vi.mocked(syncApi.applyMutations).mockImplementationOnce(async () => {
      throw httpError(401, '');
    });
    vi.mocked(syncApi.applyMutations).mockImplementation(async (_farmId, items) => accepted(items));

    await requestFlush();

    expect(syncCalls()).toHaveLength(2);
    expect(ensureFreshAccessToken).toHaveBeenCalledTimes(1);
    // The retry is the same mutation, not a new one.
    expect(syncCalls()[0][1][0].clientMutationId).toBe(first.mutationId);
    expect(syncCalls()[1][1][0].clientMutationId).toBe(first.mutationId);
    expect(await getCounts(account)).toMatchObject({ pending: 0, applied: 1 });
  });

  it('stops without quarantining anything when the session is gone', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    vi.setSystemTime(new Date('2026-09-23T08:00:00.000Z'));

    await enqueueWeight('a-1', 412, '2026-09-23T06:00:00.000Z');
    vi.mocked(syncApi.applyMutations).mockImplementation(async () => {
      throw httpError(401, '');
    });
    vi.mocked(ensureFreshAccessToken).mockRejectedValue(new Error('No refresh token stored'));

    await requestFlush();

    // One attempt, one refresh attempt, then stop: no loop against a 401.
    expect(syncCalls()).toHaveLength(1);
    expect(useSyncStore.getState().sessionExpired).toBe(true);
    // Pending, not quarantined: nothing is wrong with the record itself.
    expect(await getCounts(account)).toMatchObject({ pending: 1, quarantined: 0 });
  });

  it('honours the limiter retryAfter and waits the window out', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    vi.setSystemTime(new Date('2026-09-23T08:00:00.000Z'));

    await enqueueWeight('a-1', 412, '2026-09-23T06:00:00.000Z');
    vi.mocked(syncApi.applyMutations).mockImplementationOnce(async () => {
      // The API's limiter answers 429 with `{ error, retryAfter: 60 }` and no Retry-After
      // header, so the body field is the one that has to be read.
      throw httpError(429, { error: 'Rate limit exceeded', retryAfter: 60 });
    });
    vi.mocked(syncApi.applyMutations).mockImplementation(async (_farmId, items) => accepted(items));

    const pass = requestFlush();
    await until(() => syncCalls().length >= 1);
    expect(syncCalls()).toHaveLength(1);
    expect(useSyncStore.getState().lastError).toContain('Waiting');

    // Well inside the limiter's window: nothing is retried early.
    await advance(30_000);
    expect(syncCalls()).toHaveLength(1);

    // Past it, the scheduled retry goes — with the same mutation id.
    await until(() => syncCalls().length >= 2, 90_000);
    await until(() => useSyncStore.getState().pendingCount === 0);
    void pass;

    expect(syncCalls()).toHaveLength(2);
    expect(await getCounts(account)).toMatchObject({ pending: 0, applied: 1 });
  });

  it('prefers a Retry-After header when the server sends one', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    vi.setSystemTime(new Date('2026-09-23T08:00:00.000Z'));

    await enqueueWeight('a-1', 412, '2026-09-23T06:00:00.000Z');
    vi.mocked(syncApi.applyMutations).mockImplementationOnce(async () => {
      throw httpError(429, { error: 'Rate limit exceeded' }, { 'retry-after': '10' });
    });
    vi.mocked(syncApi.applyMutations).mockImplementation(async (_farmId, items) => accepted(items));

    const pass = requestFlush();
    await until(() => syncCalls().length >= 1);

    // The header says 10 seconds, not the body's 60: five seconds in, still waiting.
    await advance(5_000);
    expect(syncCalls()).toHaveLength(1);

    await until(() => syncCalls().length >= 2, 30_000);
    void pass;
    expect(syncCalls()).toHaveLength(2);
  });

  it('backs off on a server error and never tight-loops', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    vi.setSystemTime(new Date('2026-09-23T08:00:00.000Z'));

    const item = (await enqueueWeight('a-1', 412, '2026-09-23T06:00:00.000Z'))!;
    vi.mocked(syncApi.applyMutations).mockImplementation(async () => {
      throw httpError(500, { error: 'Server error' });
    });

    await requestFlush();
    expect(syncCalls()).toHaveLength(1);

    // The first retry is a couple of seconds away (base 2s, jittered downwards).
    await advance(1_000);
    expect(syncCalls()).toHaveLength(1);

    // A minute of a permanently failing server is a handful of attempts, not hundreds.
    await advance(60_000);
    const attemptsInAMinute = syncCalls().length;
    expect(attemptsInAMinute).toBeGreaterThan(1);
    expect(attemptsInAMinute).toBeLessThan(8);

    const stored = (await getOutboxItem(scope, item.mutationId))!;
    expect(stored.status).toBe('pending');
    expect(stored.attempts).toBe(attemptsInAMinute);
    expect(stored.lastError).toContain('Server error');
    expect(useSyncStore.getState().lastError).toContain('Retrying in');
  });

  it('quarantines one refused item with the server message and applies the rest', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    vi.setSystemTime(new Date('2026-09-23T08:00:00.000Z'));

    await enqueueWeight('a-1', 412, '2026-09-23T06:00:00.000Z');
    const bad = (await enqueueWeight('a-2', -5, '2026-09-23T06:10:00.000Z'))!;
    await enqueueWeight('a-3', 300, '2026-09-23T06:20:00.000Z');

    const serverMessage = 'Weight must be greater than zero';
    vi.mocked(syncApi.applyMutations).mockImplementation(async (_farmId, items) =>
      rejected(items, [1], serverMessage),
    );

    await requestFlush();

    const counts = await getCounts(account);
    expect(counts.pending).toBe(0);
    expect(counts.applied).toBe(2);
    expect(counts.quarantined).toBe(1);

    const quarantined = (await getOutboxItem(scope, bad.mutationId))!;
    expect(quarantined.serverMessage).toBe(serverMessage);
    expect(quarantined.status).toBe('quarantined');
    expect(useSyncStore.getState().quarantinedCount).toBe(1);
  });

  it('quarantines every item of a farm whose membership is gone, without a request per item', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    vi.setSystemTime(new Date('2026-09-23T08:00:00.000Z'));

    await enqueueWeight('a-1', 412, '2026-09-23T06:00:00.000Z');
    await enqueueWeight('a-2', 388, '2026-09-23T06:10:00.000Z');
    vi.mocked(syncApi.applyMutations).mockImplementation(async () => {
      throw httpError(403, { error: 'Access denied to this farm' });
    });

    await requestFlush();

    expect(syncCalls()).toHaveLength(1);
    const counts = await getCounts(account);
    expect(counts).toMatchObject({ pending: 0, quarantined: 2, applied: 0 });
    const pending = await listPendingMutations(account);
    expect(pending).toHaveLength(0);
  });

  it('falls back to per-item sends when the batch itself is refused', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    vi.setSystemTime(new Date('2026-09-23T08:00:00.000Z'));

    await enqueueWeight('a-1', 412, '2026-09-23T06:00:00.000Z');
    await enqueueWeight('a-2', 388, '2026-09-23T06:10:00.000Z');

    let call = 0;
    vi.mocked(syncApi.applyMutations).mockImplementation(async (_farmId, items) => {
      call += 1;
      if (call === 1) throw httpError(400, 'A sync request cannot contain more than 200 items');
      return accepted(items);
    });

    await settle(requestFlush());

    // One refused batch, then one request per item — each getting its own verdict.
    expect(syncCalls()).toHaveLength(3);
    expect(await getCounts(account)).toMatchObject({ pending: 0, applied: 2 });
  });

  it('leaves a record it cannot send pending, and says why', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    vi.setSystemTime(new Date('2026-09-23T08:00:00.000Z'));

    // A workflow this bundle has never heard of, queued by a newer one (a cached shell after a
    // deploy). 4.5.4 used `attendance.checkIn` here as its stand-in for a future kind; 4.5.5
    // implemented that one, so the stand-in has to be something genuinely ahead of this build.
    const future = (await enqueueMutation({
      scope,
      kind: 'animal.create',
      targetId: 'a-1',
      occurredAt: '2026-09-23T06:00:00.000Z',
      payload: { tagNumber: 'C-001' },
    })).item!;

    await requestFlush();

    expect(syncApi.applyMutations).not.toHaveBeenCalled();
    const stored = (await getOutboxItem(scope, future.mutationId))!;
    expect(stored.status).toBe('pending');
    expect(stored.attempts).toBe(1);
    expect(stored.lastError).toContain('Update the app');
  });

  it('sends nothing for another account queue, even with items waiting', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    vi.setSystemTime(new Date('2026-09-23T08:00:00.000Z'));

    await enqueueWeight('a-1', 412, '2026-09-23T06:00:00.000Z');

    // A different user signs in on the same device: their pass must not touch the first
    // account's queue, and the first account's items must still be there afterwards.
    useAuthStore.setState({ user: { ...user, userId: 'user-2', accountId: 'acct-2' }, isAuthenticated: true });
    await requestFlush();
    expect(syncApi.applyMutations).not.toHaveBeenCalled();
    expect(await pendingIds()).toHaveLength(1);

    useAuthStore.setState({ user, isAuthenticated: true });
    vi.mocked(syncApi.applyMutations).mockImplementation(async (_farmId, items) => accepted(items));
    await requestFlush();

    expect(syncCalls()).toHaveLength(1);
    expect(await getCounts(account)).toMatchObject({ pending: 0, applied: 1 });
  });

  // Long by necessity: it queues 450 items, flushes them in three batches, and applies each
  // result to the store one transaction at a time.
  it('batches a week-old queue into paced requests and keeps every item', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    // A week offline, then the connection comes back.
    const weekAgo = new Date('2026-09-16T06:00:00.000Z');
    vi.setSystemTime(weekAgo);

    const total = 450;
    const queued: OutboxItem[] = [];
    for (let index = 0; index < total; index++) {
      queued.push((await enqueueWeight(`a-${index}`, 400 + (index % 50), weekAgo.toISOString()))!);
    }

    vi.setSystemTime(new Date('2026-09-23T06:00:00.000Z'));

    const callTimes: number[] = [];
    vi.mocked(syncApi.applyMutations).mockImplementation(async (_farmId, items) => {
      callTimes.push(Date.now());
      return accepted(items);
    });

    await settle(requestFlush());

    // ceil(450 / 200): three requests, not 450.
    expect(syncCalls()).toHaveLength(3);
    expect(syncCalls().map(([, items]) => items.length)).toEqual([200, 200, 50]);
    for (const [, items] of syncCalls()) expect(items.length).toBeLessThanOrEqual(MAX_ITEMS_PER_REQUEST);

    // Paced, never a burst — and three requests is nowhere near the 300/minute the limiter
    // allows, which is what makes a long queue safe to send in one pass.
    expect(callTimes[1] - callTimes[0]).toBeGreaterThanOrEqual(BATCH_SPACING_MS);
    expect(callTimes[2] - callTimes[1]).toBeGreaterThanOrEqual(BATCH_SPACING_MS);

    // Nothing dropped, nothing duplicated.
    const sentIds = sentMutationIds();
    expect(sentIds).toHaveLength(total);
    expect(new Set(sentIds).size).toBe(total);
    expect(new Set(sentIds)).toEqual(new Set(queued.map((item) => item.mutationId)));
    expect(await getCounts(account)).toMatchObject({ pending: 0, applied: total, quarantined: 0 });

    // Every item kept the device's clock, not the upload time.
    for (const [, items] of syncCalls()) {
      for (const envelope of items) {
        expect((envelope.payload as WeightRecordPayload).recordedAt).toBe(weekAgo.toISOString());
      }
    }
  }, 60_000);

  it('survives an app kill with the whole queue and flushes it exactly once', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    vi.setSystemTime(new Date('2026-09-23T08:00:00.000Z'));

    const first = (await enqueueWeight('a-1', 412, '2026-09-23T06:00:00.000Z'))!;
    const second = (await enqueueWeight('a-2', 388, '2026-09-23T06:15:00.000Z'))!;
    const third = (await enqueueWeight('a-3', 500, '2026-09-23T06:30:00.000Z'))!;

    // The app is killed while offline: the connection handle and every in-memory structure
    // go away, exactly as they do on a real relaunch. Only IndexedDB survives.
    resetOfflineDbConnection();
    resetSyncEngineForTests();
    configure();
    useOfflineStore.setState({ isOnline: false });

    vi.mocked(syncApi.applyMutations).mockImplementation(async (_farmId, items) => accepted(items));
    await requestFlush();
    expect(syncApi.applyMutations).not.toHaveBeenCalled();

    // Reconnect: the queue is still there, complete and in order, and it goes once.
    useOfflineStore.setState({ isOnline: true });
    await requestFlush();

    expect(syncCalls()).toHaveLength(1);
    expect(syncCalls()[0][1].map((envelope) => envelope.clientMutationId)).toEqual([
      first.mutationId,
      second.mutationId,
      third.mutationId,
    ]);
    expect(syncCalls()[0][1].map((envelope) => (envelope.payload as WeightRecordPayload).recordedAt)).toEqual([
      '2026-09-23T06:00:00.000Z',
      '2026-09-23T06:15:00.000Z',
      '2026-09-23T06:30:00.000Z',
    ]);

    // A second trigger after a clean pass sends nothing again: the items left `pending`.
    await requestFlush();
    expect(syncCalls()).toHaveLength(1);
    expect(await getCounts(account)).toMatchObject({ pending: 0, applied: 3 });
  });

  it('re-sends the same mutation id after a failure, so the server can dedupe it', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    vi.setSystemTime(new Date('2026-09-23T08:00:00.000Z'));

    const item = (await enqueueWeight('a-1', 412, '2026-09-23T06:00:00.000Z'))!;
    vi.mocked(syncApi.applyMutations).mockImplementationOnce(async () => {
      // An ambiguous failure: the request may or may not have been applied server-side.
      throw httpError(500, { error: 'Server error' });
    });
    vi.mocked(syncApi.applyMutations).mockImplementation(async (_farmId, items) => accepted(items));

    await requestFlush();
    expect(syncCalls()).toHaveLength(1);

    // The retry is the same mutation, not a new one — which is what makes the server's
    // idempotency key bind when the first attempt may or may not have been applied.
    await until(() => syncCalls().length >= 2, 60_000);

    expect(syncCalls().length).toBeGreaterThanOrEqual(2);
    for (const [, items] of syncCalls()) {
      expect(items[0].clientMutationId).toBe(item.mutationId);
    }
  });

  it('does not poll: one boot pass, then silence', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    vi.setSystemTime(new Date('2026-09-23T08:00:00.000Z'));

    await enqueueWeight('a-1', 412, '2026-09-23T06:00:00.000Z');
    vi.mocked(syncApi.applyMutations).mockImplementation(async (_farmId, items) => accepted(items));

    const teardown = initSyncEngine();
    await advance(10 * 60_000);

    // The boot pass sent the queue; nothing else ran for ten minutes. There is no interval
    // anywhere in the engine — every other flush is an event.
    expect(syncCalls()).toHaveLength(1);
    teardown();
  });

  it('flushes when a request succeeds or the token is refreshed', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    vi.setSystemTime(new Date('2026-09-23T08:00:00.000Z'));

    const teardown = initSyncEngine();
    // The engine registers a trigger rather than importing one: this is the callback axios
    // would call after a successful request or a token refresh.
    const trigger = vi.mocked(setSyncTriggerHandler).mock.calls[0][0] as (reason: string) => void;

    await enqueueWeight('a-1', 412, '2026-09-23T06:00:00.000Z');
    vi.mocked(syncApi.applyMutations).mockImplementation(async (_farmId, items) => accepted(items));

    trigger('request-succeeded');
    // The trigger does not return the pass it started; requesting another flush returns the
    // one already running (or starts a no-op pass if it finished, which sends nothing).
    await requestFlush();
    await until(() => syncCalls().length >= 1);

    expect(syncCalls()).toHaveLength(1);
    expect(await getCounts(account)).toMatchObject({ pending: 0, applied: 1 });

    teardown();
  });

  /*
   * The offline-write window (5.6) and the flush have to agree about what "we reached the
   * server" means.
   *
   * The window exists so a device stops accepting offline work before its refresh token can
   * expire with the queue undeliverable. A flush the server accepted is the strongest possible
   * evidence of the opposite, so it resets the window — but only an *authenticated* answer
   * counts: a transport failure reached nothing, and a 401 means the session cannot deliver,
   * which is precisely what the window is there to flag. Without the reset a device could come
   * back online, drain its whole queue, and still refuse the next record.
   */
  it('advances the offline write window once a flush reaches the server', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    vi.setSystemTime(new Date('2026-09-23T08:00:00.000Z'));

    // Work recorded while the device could still deliver it...
    const item = await enqueueWeight('a-1', 412, '2026-09-23T06:00:00.000Z');

    // ...then eight days without a reachable API. The window is shut and the next record is
    // refused, which is the state a user hits after a long spell offline.
    await touchSessionMarker(account, '2026-09-15T08:00:00.000Z');
    useOfflineStore.setState({ isOnline: false });

    expect((await currentSessionState(account)).status).toBe('blocked');

    const refused = await enqueueMutation<WeightRecordPayload>({
      scope,
      kind: WEIGHT_RECORD,
      targetId: 'a-2',
      occurredAt: '2026-09-23T07:00:00.000Z',
      payload: weightPayload('a-2', 399, '2026-09-23T07:00:00.000Z'),
    });
    expect(refused.item).toBeNull();
    expect(refused.refusal).toBe('session-stale');
    expect(await pendingIds()).toEqual([item.mutationId]);

    // The connection comes back and the queue drains. That is the event that proves the API is
    // reachable again, so it is the event that reopens the window.
    useOfflineStore.setState({ isOnline: true });
    vi.mocked(syncApi.applyMutations).mockImplementation(async (_farmId, envelopes) => accepted(envelopes));

    await requestFlush();

    expect(await pendingIds()).toEqual([]);
    const marker = await getSessionMarker(account);
    expect(Date.parse(marker!.lastServerContactAt))
      .toBeGreaterThan(Date.parse('2026-09-15T08:00:00.000Z'));
    expect(await currentSessionState(account)).toMatchObject({ status: 'fresh', writeAllowed: true });

    // And work can be recorded again, which is the whole point of a window being a window.
    const queuedAgain = await enqueueMutation<WeightRecordPayload>({
      scope,
      kind: WEIGHT_RECORD,
      targetId: 'a-2',
      occurredAt: '2026-09-23T07:30:00.000Z',
      payload: weightPayload('a-2', 399, '2026-09-23T07:30:00.000Z'),
    });
    expect(queuedAgain.item).not.toBeNull();
  });

  it('does advance the window when the server answers by refusing one record', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    vi.setSystemTime(new Date('2026-09-23T08:00:00.000Z'));

    await enqueueWeight('a-1', 412, '2026-09-23T06:00:00.000Z');
    await touchSessionMarker(account, '2026-09-15T08:00:00.000Z');

    // A per-item 4xx: the row is quarantined with the server's own words, and the fact that
    // matters here is that the server *answered* — the session is alive and the API is up.
    vi.mocked(syncApi.applyMutations).mockImplementation(async (_farmId, envelopes) =>
      rejected(envelopes, [0], 'Weight must be greater than zero'));

    await requestFlush();

    expect(await getCounts(account)).toMatchObject({ pending: 0, quarantined: 1 });
    const marker = await getSessionMarker(account);
    expect(Date.parse(marker!.lastServerContactAt))
      .toBeGreaterThan(Date.parse('2026-09-15T08:00:00.000Z'));
  });

  it('does not advance the window when the connection never reached the server', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    vi.setSystemTime(new Date('2026-09-23T08:00:00.000Z'));

    await enqueueWeight('a-1', 412, '2026-09-23T06:00:00.000Z');
    await touchSessionMarker(account, '2026-09-15T08:00:00.000Z');

    // No response object at all: the request never arrived. Advancing the window here would
    // let a device that cannot reach the API keep accepting records forever, which is the one
    // failure the window exists to prevent.
    vi.mocked(syncApi.applyMutations).mockRejectedValue(
      Object.assign(new Error('Network Error'), { isAxiosError: true, config: { headers: {} } }),
    );

    await requestFlush();

    expect(await pendingIds()).toHaveLength(1);
    expect((await getSessionMarker(account))!.lastServerContactAt).toBe('2026-09-15T08:00:00.000Z');
    expect((await currentSessionState(account)).status).toBe('blocked');
  });

  it('does not advance the window when the session cannot deliver (401)', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    vi.setSystemTime(new Date('2026-09-23T08:00:00.000Z'));

    await enqueueWeight('a-1', 412, '2026-09-23T06:00:00.000Z');
    await touchSessionMarker(account, '2026-09-15T08:00:00.000Z');

    // The server answered — but with a dead session, and no refresh to recover it. That is
    // exactly the state the window is there to flag, so it must not be cleared by it.
    vi.mocked(syncApi.applyMutations).mockRejectedValue(httpError(401));
    vi.mocked(ensureFreshAccessToken).mockRejectedValue(new Error('refresh refused'));

    await requestFlush();

    expect(await pendingIds()).toHaveLength(1);
    expect((await getSessionMarker(account))!.lastServerContactAt).toBe('2026-09-15T08:00:00.000Z');
    expect(useSyncStore.getState().sessionExpired).toBe(true);
  });

  /*
   * A full offline session, which is what the device actually carries: several workflows, more
   * than one farm, queued with the network gone and flushed on reconnect.
   *
   * The per-workflow tests each prove their own payload; what this one adds is the *mix* — that
   * the queue is one account-wide list of heterogeneous work, that the flush honours the
   * per-farm sequentiality across it, and that one reconnect delivers every effect exactly
   * once with the times the device captured rather than the time it reconnected.
   */
  it('flushes a mixed offline session across farms, once, with the device times', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    vi.setSystemTime(new Date('2026-09-23T08:00:00.000Z'));

    const second: OfflineScope = { accountId: account, farmId: 'farm-b' };
    useOfflineStore.setState({ isOnline: false });

    // Queued one simulated second apart, so the assertions describe the queue's oldest-first
    // rule rather than its mutation-id tie-break.
    const weight = await enqueueWeight('a-1', 412, '2026-09-23T06:00:00.000Z');
    vi.setSystemTime(new Date(Date.now() + 1_000));
    const completion = (await enqueueMutation<TaskCompletionPayload>({
      scope,
      kind: TASK_COMPLETE,
      targetId: 't-1',
      occurredAt: '2026-09-23T06:05:00.000Z',
      payload: { taskId: 't-1', completionNotes: 'Fence repaired', occurredAt: '2026-09-23T06:05:00.000Z' },
    })).item!;
    vi.setSystemTime(new Date(Date.now() + 1_000));
    const checkIn = (await enqueueMutation<AttendanceMutationPayload>({
      scope: second,
      kind: ATTENDANCE_CHECK_IN,
      targetId: 'e-1',
      occurredAt: '2026-09-23T06:10:00.000Z',
      payload: { employeeId: 'e-1', occurredAt: '2026-09-23T06:10:00.000Z' },
    })).item!;

    expect(await pendingIds()).toHaveLength(3);

    // One reconnect.
    useOfflineStore.setState({ isOnline: true });
    vi.mocked(syncApi.applyMutations).mockImplementation(async (_farmId, envelopes) => accepted(envelopes));

    await requestFlush();

    // A farm's work goes to that farm's context, and the farms are flushed in turn rather than
    // in parallel — the same rule that keeps a day's order meaningful.
    expect(syncCalls().map(([farmId]) => farmId)).toEqual(['farm-a', 'farm-b']);
    const firstBatch = syncCalls()[0][1];
    expect(firstBatch.map((envelope) => envelope.operation)).toEqual(['weight.record', 'task.complete']);
    expect(syncCalls()[1][1].map((envelope) => envelope.operation)).toEqual(['attendance.checkIn']);

    // Every envelope carries the device's capture time, and the ids the queue minted.
    expect(firstBatch.map((envelope) => envelope.clientMutationId)).toEqual([
      weight.mutationId,
      completion.mutationId,
    ]);
    expect((firstBatch[0].payload as WeightRecordPayload).recordedAt).toBe('2026-09-23T06:00:00.000Z');
    expect((firstBatch[1].payload as TaskCompletionPayload).occurredAt).toBe('2026-09-23T06:05:00.000Z');
    expect((syncCalls()[1][1][0].payload as AttendanceMutationPayload).occurredAt).toBe('2026-09-23T06:10:00.000Z');

    // Three effects applied, nothing left, and a second pass adds nothing: the queue is empty
    // and the server has already seen every id.
    expect(await getCounts(account)).toMatchObject({ pending: 0, applied: 3 });
    expect(sentMutationIds()).toEqual([weight.mutationId, completion.mutationId, checkIn.mutationId]);

    await requestFlush();
    expect(syncCalls()).toHaveLength(2);
  });

  it('honours a trigger that arrives while a pass is already running', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    vi.setSystemTime(new Date('2026-09-23T08:00:00.000Z'));

    const first = (await enqueueWeight('a-1', 412, '2026-09-23T06:00:00.000Z'))!;

    // The first request is held open so a second weight can be queued *during* the pass. That
    // is the real race: a page's success trigger lands while a flush is midway through its
    // batches, and the item it carries has no later trigger of its own to rely on.
    let release!: () => void;
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    let calls = 0;
    vi.mocked(syncApi.applyMutations).mockImplementation(async (_farmId, items) => {
      calls += 1;
      if (calls === 1) await held;
      return accepted(items);
    });

    const pass = requestFlush();
    await until(() => syncCalls().length === 1);

    const second = (await enqueueWeight('a-2', 418, '2026-09-23T06:05:00.000Z'))!;
    // Mid-pass: the trigger coalesces into the running pass, which must run once more at the
    // end — a pass that simply drops it leaves `second` parked until some unrelated event.
    const coalesced = requestFlush();
    release();

    await settle(pass);
    await settle(coalesced);

    expect(await pendingIds()).toEqual([]);
    expect(sentMutationIds()).toEqual([first.mutationId, second.mutationId]);
    expect(await getCounts(account)).toMatchObject({ pending: 0, applied: 2 });
  });
});
