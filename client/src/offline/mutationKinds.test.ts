import { describe, expect, it } from 'vitest';
import {
  ATTENDANCE_CHECK_IN,
  ATTENDANCE_CHECK_OUT,
  MUTATION_KINDS,
  OUTBOX_STATUS_COLORS,
  OUTBOX_STATUS_LABELS,
  TASK_COMPLETE,
  WEIGHT_RECORD,
  describeItem,
  getMutationKind,
  kindLabel,
} from './mutationKinds';
import type { OutboxItem, OutboxStatus } from './db';

const item = <TPayload>(kind: string, payload: TPayload, targetId = 'target-1'): OutboxItem<TPayload> => ({
  accountId: 'acct-1',
  farmId: 'farm-a',
  mutationId: 'm-1',
  kind,
  targetId,
  occurredAt: '2026-09-23T06:00:00.000Z',
  queuedAt: '2026-09-23T06:00:00.000Z',
  payload,
  status: 'pending',
  attempts: 0,
  lastError: null,
  serverMessage: null,
  appliedAt: null,
  appliedEntityId: null,
  dismissedReason: null,
});

const envelopeOf = (kind: string, payload: unknown) => getMutationKind(kind)!.toEnvelope(item(kind, payload));

/**
 * The wire shape, per workflow, against the server's own binding types.
 *
 * These are not cosmetic assertions: the sync endpoint reads each payload into
 * `WeightRecordMutation` / `AttendanceMutation` / `TaskCompletionMutation`, and a renamed or
 * dropped key binds as null rather than failing — a check-in with no employee id, a completion
 * with no task. The exact key set is the only thing that stands between a typo and a queued
 * record the server cannot place.
 */
describe('mutation kinds', () => {
  it('registers every workflow the queue may carry, under the server\'s own operation strings', () => {
    expect(Object.keys(MUTATION_KINDS).sort()).toEqual([
      'attendance.checkIn',
      'attendance.checkOut',
      'task.complete',
      'weight.record',
    ]);

    // The kind and the operation are the same word, which is what makes a mismatch visible in
    // the store instead of hidden behind an alias table. `SyncOperations` declares these four.
    expect(envelopeOf(WEIGHT_RECORD, { animalId: 'a-1', weightKg: 412, recordedAt: 'x' }).operation)
      .toBe('weight.record');
    expect(envelopeOf(ATTENDANCE_CHECK_IN, { employeeId: 'e-1', occurredAt: 'x' }).operation)
      .toBe('attendance.checkIn');
    expect(envelopeOf(ATTENDANCE_CHECK_OUT, { employeeId: 'e-1', occurredAt: 'x' }).operation)
      .toBe('attendance.checkOut');
    expect(envelopeOf(TASK_COMPLETE, { taskId: 't-1', occurredAt: 'x' }).operation)
      .toBe('task.complete');
  });

  it('sends a weight exactly as WeightRecordMutation binds it', () => {
    const envelope = envelopeOf(WEIGHT_RECORD, {
      animalId: 'a-1',
      weightKg: 412,
      recordedAt: '2026-09-23T06:00:00.000Z',
      notes: 'After the morning feed',
    });

    expect(envelope.clientMutationId).toBe('m-1');
    expect(envelope.payload).toEqual({
      animalId: 'a-1',
      weightKg: 412,
      recordedAt: '2026-09-23T06:00:00.000Z',
      notes: 'After the morning feed',
    });
    expect(Object.keys(envelope.payload as object).sort()).toEqual([
      'animalId', 'notes', 'recordedAt', 'weightKg',
    ]);
  });

  it('sends a check-in and a check-out as AttendanceMutation binds them', () => {
    const payload = { employeeId: 'e-1', occurredAt: '2026-09-23T06:00:00.000Z' };

    for (const kind of [ATTENDANCE_CHECK_IN, ATTENDANCE_CHECK_OUT]) {
      const envelope = envelopeOf(kind, payload);
      expect(envelope.payload).toEqual(payload);
      expect(Object.keys(envelope.payload as object).sort()).toEqual(['employeeId', 'occurredAt']);
    }
  });

  it('sends a completion as TaskCompletionMutation binds it, with an absent note as null', () => {
    const withNotes = envelopeOf(TASK_COMPLETE, {
      taskId: 't-1',
      completionNotes: 'Fence repaired',
      occurredAt: '2026-09-23T06:00:00.000Z',
    });

    expect(withNotes.payload).toEqual({
      taskId: 't-1',
      completionNotes: 'Fence repaired',
      occurredAt: '2026-09-23T06:00:00.000Z',
    });
    expect(Object.keys(withNotes.payload as object).sort()).toEqual([
      'completionNotes', 'occurredAt', 'taskId',
    ]);

    // Absent is sent as an explicit null rather than omitted: the server's model binder reads
    // the property either way, and a null is what the completion conflict rule compares against
    // the notes already on record.
    const withoutNotes = envelopeOf(TASK_COMPLETE, { taskId: 't-1', occurredAt: 'x' });
    expect(withoutNotes.payload).toEqual({ taskId: 't-1', completionNotes: null, occurredAt: 'x' });
  });

  it('derives the target and the device time from the payload, not from the item', () => {
    const attendance = getMutationKind(ATTENDANCE_CHECK_IN)!;
    const stored = item(ATTENDANCE_CHECK_IN, {
      employeeId: 'e-9',
      occurredAt: '2026-09-23T05:00:00.000Z',
    }, 'wrong-target');

    expect(attendance.targetIdOf(stored.payload)).toBe('e-9');
    expect(attendance.occurredAtOf(stored.payload)).toBe('2026-09-23T05:00:00.000Z');
    expect(getMutationKind(TASK_COMPLETE)!.targetIdOf({ taskId: 't-2', occurredAt: 'x' })).toBe('t-2');
  });

  it('names each workflow for the queue UI, using the target label where there is one', () => {
    expect(kindLabel(ATTENDANCE_CHECK_IN)).toBe('Check-in');
    expect(kindLabel(ATTENDANCE_CHECK_OUT)).toBe('Check-out');
    expect(kindLabel(TASK_COMPLETE)).toBe('Task completion');

    // In the real queue the item's stored target id *is* the employee id (that is what
    // `enqueueMutation` is given), which is what the no-label fallback falls back to.
    const checkIn = item(ATTENDANCE_CHECK_IN, { employeeId: 'e-1', occurredAt: 'x' }, 'e-1');
    expect(describeItem(checkIn as OutboxItem, 'Grace Otieno')).toBe('Checked in · Grace Otieno');
    // With no cached label the id stands in: accurate, if less friendly.
    expect(describeItem(checkIn as OutboxItem)).toBe('Checked in · e-1');

    const completion = item(TASK_COMPLETE, { taskId: 't-1', occurredAt: 'x' });
    expect(describeItem(completion as OutboxItem, 'Repair the fence')).toBe('Completed · Repair the fence');
  });

  it('falls back to the stored kind for an item this build does not know', () => {
    const future = item('animal.create', { tagNumber: 'C-001' });

    expect(getMutationKind('animal.create')).toBeNull();
    expect(describeItem(future as OutboxItem)).toBe('animal.create');
    expect(kindLabel('animal.create')).toBe('animal.create');
  });

  it('has wording and a colour for every status the queue can be in', () => {
    const statuses: OutboxStatus[] = ['pending', 'applied', 'quarantined', 'dismissed'];

    for (const status of statuses) {
      expect(OUTBOX_STATUS_LABELS[status]).toBeTruthy();
      expect(OUTBOX_STATUS_COLORS[status]).toBeTruthy();
    }
  });
});
