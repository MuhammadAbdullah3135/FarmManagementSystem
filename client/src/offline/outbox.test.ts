import { beforeEach, describe, expect, it, vi } from 'vitest';
import 'fake-indexeddb/auto';
import { IDBFactory } from 'fake-indexeddb';
import {
  activeScope,
  enqueueMutation,
  getCounts,
  listPendingMutations,
  markApplied,
  markDismissed,
  markQuarantined,
  newMutationId,
  purgeOldApplied,
  recordAttempt,
  requeueMutation,
} from './outbox';
import { deleteOutboxItem, getOutboxItem, getSessionMarker, resetOfflineDbConnection } from './db';
import {
  MAX_QUEUED_ITEMS,
  OFFLINE_SESSION_BLOCK_DAYS,
  OFFLINE_SESSION_WARN_DAYS,
  QUEUE_WARN_REMAINING,
  sessionStateFrom,
} from './offlinePolicy';
import { WEIGHT_RECORD, type WeightRecordPayload } from './mutationKinds';
import { useAuthStore } from '../stores/authStore';
import { useFarmStore } from '../stores/farmStore';
import type { Farm, User } from '../types';

const account = 'acct-1';
const farmA = { accountId: account, farmId: 'farm-a' };
const farmB = { accountId: account, farmId: 'farm-b' };

const payload = (animalId: string, weightKg: number): WeightRecordPayload => ({
  animalId,
  weightKg,
  recordedAt: '2026-09-23T06:15:00.000Z',
  notes: null,
});

/// The queue's own helper: every existing test here is about a *queued* item, so an unexpected
/// refusal should stop the test rather than silently change what it asserts.
const enqueue = async (scope = farmA, animalId = 'a-1', weightKg = 412, mutationId?: string) => {
  const result = await enqueueMutation<WeightRecordPayload>({
    scope,
    kind: WEIGHT_RECORD,
    targetId: animalId,
    occurredAt: '2026-09-23T06:15:00.000Z',
    payload: payload(animalId, weightKg),
    mutationId,
  });

  if (!result.item) throw new Error(`enqueue refused: ${result.refusal ?? 'unknown'}`);
  return result.item;
};

describe('outbox', () => {
  beforeEach(() => {
    (globalThis as { indexedDB: IDBFactory }).indexedDB = new IDBFactory();
    resetOfflineDbConnection();
    useAuthStore.setState({ user: null, isAuthenticated: false });
    useFarmStore.setState({ farms: [], activeFarm: null, isLoading: false, error: null });
  });

  it('queues a weight with the device time and the animal it is about', async () => {
    const item = (await enqueue())!;

    expect(item.status).toBe('pending');
    expect(item.attempts).toBe(0);
    expect(item.kind).toBe(WEIGHT_RECORD);
    expect(item.targetId).toBe('a-1');
    // The device's capture time, not the time it will be uploaded — that is the number the
    // whole offline story depends on.
    expect(item.occurredAt).toBe('2026-09-23T06:15:00.000Z');
    expect(item.payload.recordedAt).toBe('2026-09-23T06:15:00.000Z');
    expect((await getOutboxItem<WeightRecordPayload>(farmA, item.mutationId))!.payload.weightKg).toBe(412);
  });

  it('leaves the payload and the identity untouched through every outcome', async () => {
    const item = (await enqueue())!;

    await recordAttempt(farmA, item.mutationId, 'Server error');
    await markQuarantined(farmA, item.mutationId, 'Weight must be greater than zero');
    await requeueMutation(farmA, item.mutationId);
    await markDismissed(farmA, item.mutationId, 'weighed the wrong animal');

    const stored = (await getOutboxItem<WeightRecordPayload>(farmA, item.mutationId))!;
    expect(stored.status).toBe('dismissed');
    expect(stored.dismissedReason).toBe('weighed the wrong animal');
    // The half the server's idempotency guarantee is anchored to never moved.
    expect(stored.mutationId).toBe(item.mutationId);
    expect(stored.payload).toEqual(item.payload);
    expect(stored.occurredAt).toBe(item.occurredAt);
    expect(stored.queuedAt).toBe(item.queuedAt);
    expect(stored.kind).toBe(item.kind);
    expect(stored.targetId).toBe(item.targetId);
  });

  it('counts a refusal as an attempt and keeps the server wording', async () => {
    const item = (await enqueue())!;

    await recordAttempt(farmA, item.mutationId, 'No connection');
    const afterAttempt = (await getOutboxItem(farmA, item.mutationId))!;
    expect(afterAttempt.status).toBe('pending');
    expect(afterAttempt.attempts).toBe(1);
    expect(afterAttempt.lastError).toBe('No connection');

    await markQuarantined(farmA, item.mutationId, 'Weight must be greater than zero');
    const afterRefusal = (await getOutboxItem(farmA, item.mutationId))!;
    expect(afterRefusal.status).toBe('quarantined');
    expect(afterRefusal.attempts).toBe(2);
    expect(afterRefusal.serverMessage).toBe('Weight must be greater than zero');
    expect(afterRefusal.lastError).toBeNull();
  });

  it('records what the server created when an item is applied', async () => {
    const item = (await enqueue())!;

    await markApplied(farmA, item.mutationId, { entityId: 'w-9', appliedAt: '2026-09-23T07:00:00.000Z' });

    const stored = (await getOutboxItem(farmA, item.mutationId))!;
    expect(stored.status).toBe('applied');
    expect(stored.appliedEntityId).toBe('w-9');
    expect(stored.appliedAt).toBe('2026-09-23T07:00:00.000Z');
    expect(stored.lastError).toBeNull();
  });

  it('keeps a superseded item as done, with the server explanation', async () => {
    const item = (await enqueue())!;

    await markApplied(farmA, item.mutationId, {
      message: 'Employee already has an attendance record for today',
    });

    const stored = (await getOutboxItem(farmA, item.mutationId))!;
    expect(stored.status).toBe('applied');
    expect(stored.serverMessage).toBe('Employee already has an attendance record for today');
  });

  it('never queues across a farm or an account boundary', async () => {
    await enqueue(farmA, 'a-1');
    await enqueue(farmB, 'a-2');
    await enqueue({ accountId: 'acct-2', farmId: 'farm-a' }, 'a-3');

    expect(await listPendingMutations(account)).toHaveLength(2);
    expect(await listPendingMutations('acct-2')).toHaveLength(1);
    expect((await getCounts(account)).pending).toBe(2);
    expect((await getCounts('acct-2')).pending).toBe(1);
  });

  it('orders pending items oldest first, across farms, and names the oldest', async () => {
    // Only `Date` is faked: the queue's ordering key is a timestamp, and leaving the timers
    // real keeps fake-indexeddb's own scheduling untouched.
    vi.useFakeTimers({ toFake: ['Date'] });
    try {
      vi.setSystemTime(new Date('2026-09-23T06:00:00.000Z'));
      const first = (await enqueue(farmA, 'a-1', 100))!;
      vi.setSystemTime(new Date('2026-09-23T06:05:00.000Z'));
      const second = (await enqueue(farmB, 'a-3', 300))!;
      vi.setSystemTime(new Date('2026-09-23T06:10:00.000Z'));
      const third = (await enqueue(farmA, 'a-2', 200))!;

      const pending = await listPendingMutations(account);
      expect(pending.map((item) => item.mutationId)).toEqual([
        first.mutationId,
        second.mutationId,
        third.mutationId,
      ]);
      expect((await getCounts(account)).oldestQueuedAt).toBe(first.queuedAt);
    } finally {
      vi.useRealTimers();
    }
  });

  it('counts each status separately for the badge', async () => {
    const pending = (await enqueue(farmA, 'a-1'))!;
    const refused = (await enqueue(farmA, 'a-2'))!;
    const gone = (await enqueue(farmA, 'a-3'))!;
    const applied = (await enqueue(farmA, 'a-4'))!;

    await markQuarantined(farmA, refused.mutationId, 'Weight must be greater than zero');
    await markDismissed(farmA, gone.mutationId, 'duplicate entry');
    await markApplied(farmA, applied.mutationId, { entityId: 'w-1' });

    expect(await getCounts(account)).toEqual({
      pending: 1,
      quarantined: 1,
      dismissed: 1,
      applied: 1,
      oldestQueuedAt: pending.queuedAt,
    });
  });

  it('reaps applied items after the retention window and nothing else', async () => {
    const stale = (await enqueue(farmA, 'a-1'))!;
    const fresh = (await enqueue(farmA, 'a-2'))!;
    const refused = (await enqueue(farmA, 'a-3'))!;

    const twoDaysAgo = new Date(Date.now() - 48 * 60 * 60 * 1000).toISOString();
    await markApplied(farmA, stale.mutationId, { appliedAt: twoDaysAgo });
    await markApplied(farmA, fresh.mutationId, {});
    await markQuarantined(farmA, refused.mutationId, 'nope');

    expect(await purgeOldApplied()).toBe(1);
    expect(await getOutboxItem(farmA, stale.mutationId)).toBeNull();
    expect(await getOutboxItem(farmA, fresh.mutationId)).not.toBeNull();
    // A refusal is never reaped: it is waiting for a person, not for time.
    expect(await getOutboxItem(farmA, refused.mutationId)).not.toBeNull();
  });

  it('removes an item only by its full key', async () => {
    const item = (await enqueue(farmA, 'a-1'))!;
    await enqueue(farmB, 'a-1');

    await deleteOutboxItem(farmA, item.mutationId);

    expect(await getOutboxItem(farmA, item.mutationId)).toBeNull();
  });

  it('mints distinct v4 ids even without a secure-context randomUUID', async () => {
    const ids = new Set([newMutationId(), newMutationId(), newMutationId()]);

    expect(ids.size).toBe(3);
    for (const id of ids) {
      expect(id).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
    }
  });

  it('resolves the active scope from the session, and nothing when signed out', () => {
    expect(activeScope()).toBeNull();

    const user: User = {
      userId: 'user-1',
      accountId: account,
      email: 'owner@example.com',
      firstName: 'Owner',
      lastName: 'Person',
      roles: ['FarmManager'],
    };
    const farm: Farm = {
      id: 'farm-a',
      name: 'Farm A',
      isActive: true,
      createdAt: '2026-01-01T00:00:00Z',
      userFarmRole: 'FarmManager',
    };

    useAuthStore.setState({ user, isAuthenticated: true });
    expect(activeScope()).toBeNull();

    useFarmStore.setState({ farms: [farm], activeFarm: farm, isLoading: false, error: null });
    expect(activeScope()).toEqual(farmA);
  });
});

/**
 * The two limits 5.6 puts on the queue's one writer.
 *
 * They are asserted at `enqueueMutation` because that is the only place a mutation can enter
 * the queue: a page cannot forget to ask, and a workflow added later inherits both checks
 * without knowing they exist.
 */
describe('the offline write window', () => {
  beforeEach(() => {
    (globalThis as { indexedDB: IDBFactory }).indexedDB = new IDBFactory();
    resetOfflineDbConnection();
  });

  const daysAgo = (days: number) => new Date(Date.now() - days * 24 * 60 * 60 * 1000).toISOString();

  it('accepts a write and says nothing while the session is fresh', async () => {
    const result = await enqueueMutation<WeightRecordPayload>({
      scope: farmA,
      kind: WEIGHT_RECORD,
      targetId: 'a-1',
      occurredAt: '2026-09-23T06:15:00.000Z',
      payload: payload('a-1', 412),
      mutationId: 'm-1',
    }, { sessionState: sessionStateFrom(daysAgo(0)) });

    expect(result.item?.status).toBe('pending');
    expect(result.refusal).toBeNull();
    expect(result.warning).toBeNull();
  });

  it('warns before the cutoff but still accepts the write', async () => {
    const result = await enqueueMutation<WeightRecordPayload>({
      scope: farmA,
      kind: WEIGHT_RECORD,
      targetId: 'a-1',
      occurredAt: '2026-09-23T06:15:00.000Z',
      payload: payload('a-1', 412),
      mutationId: 'm-1',
    }, { sessionState: sessionStateFrom(daysAgo(OFFLINE_SESSION_WARN_DAYS)) });

    // A warning that refused the write would strand the shift it is warning about.
    expect(result.item).not.toBeNull();
    expect(result.refusal).toBeNull();
    expect(result.warning).toBeNull();

    // The warning is the banner's job, so the store must agree with the refusal that would
    // come two days later.
    expect((await getSessionMarker(farmA.accountId))).toBeNull();
  });

  it('refuses a write at the cutoff, saving nothing', async () => {
    const result = await enqueueMutation<WeightRecordPayload>({
      scope: farmA,
      kind: WEIGHT_RECORD,
      targetId: 'a-1',
      occurredAt: '2026-09-23T06:15:00.000Z',
      payload: payload('a-1', 412),
      mutationId: 'm-1',
    }, { sessionState: sessionStateFrom(daysAgo(OFFLINE_SESSION_BLOCK_DAYS)) });

    expect(result.item).toBeNull();
    expect(result.refusal).toBe('session-stale');
    expect(result.message).toContain('sync');
    // Nothing was stored: a refusal that still queued the record would be the failure this
    // whole policy exists to prevent.
    expect(await getCounts(farmA.accountId)).toMatchObject({ pending: 0 });
  });

  it('refuses at the cap, and warns while there is still room', async () => {
    const full = await enqueueMutation<WeightRecordPayload>({
      scope: farmA,
      kind: WEIGHT_RECORD,
      targetId: 'a-1',
      occurredAt: '2026-09-23T06:15:00.000Z',
      payload: payload('a-1', 412),
      mutationId: 'm-1',
    }, { sessionState: sessionStateFrom(null), pendingCount: MAX_QUEUED_ITEMS });

    expect(full.item).toBeNull();
    expect(full.refusal).toBe('queue-full');
    expect(full.message).toContain('Sync');

    const nearlyFull = await enqueueMutation<WeightRecordPayload>({
      scope: farmA,
      kind: WEIGHT_RECORD,
      targetId: 'a-1',
      occurredAt: '2026-09-23T06:15:00.000Z',
      payload: payload('a-1', 412),
      mutationId: 'm-2',
    }, { sessionState: sessionStateFrom(null), pendingCount: MAX_QUEUED_ITEMS - QUEUE_WARN_REMAINING });

    expect(nearlyFull.item).not.toBeNull();
    expect(nearlyFull.refusal).toBeNull();
    expect(nearlyFull.warning).toContain('Sync');
  });

  it('counts the queue itself when the caller does not say how full it is', async () => {
    // The count a page would pass is one farm's view; the cap is account-wide, so the store is
    // the authority unless a test deliberately stands in for it.
    for (let i = 0; i < 3; i++) await enqueue(farmB, 'a-1', 400 + i);

    const result = await enqueueMutation<WeightRecordPayload>({
      scope: farmA,
      kind: WEIGHT_RECORD,
      targetId: 'a-1',
      occurredAt: '2026-09-23T06:15:00.000Z',
      payload: payload('a-1', 412),
      mutationId: 'm-9',
    }, { sessionState: sessionStateFrom(null) });

    expect(result.item).not.toBeNull();
    expect(await getCounts(farmA.accountId)).toMatchObject({ pending: 4 });
  });
});
