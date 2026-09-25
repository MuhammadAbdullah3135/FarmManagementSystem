/**
 * The write workflows the queue may carry, and how each one becomes a server item.
 *
 * The `kind` strings are deliberately the server's own operation strings
 * (`SyncOperations.WeightRecord` = `weight.record`), so a queued item and a sync request use
 * the same word for the same workflow, and a mismatch is visible in the store rather than
 * hidden behind a client-side alias table.
 *
 * Three workflows are queued: weight recording (4.5.4), and attendance check-in/check-out and
 * task completion (4.5.5). Adding one here is a decision, and it is the only place a new kind
 * has to be declared — the flush, the quarantine list and the sync screen all read this
 * registry instead of switching on `kind` themselves.
 *
 * The four kinds cover the three shapes the architecture named: append-only (a weight), a
 * keyed row whose content is the device's (an employee's day), and a state transition (a
 * completion). What differs between them is entirely the server's conflict policy; nothing
 * here decides an outcome.
 */

import type { OutboxItem, OutboxStatus } from './db';

export interface WeightRecordPayload {
  animalId: string;
  weightKg: number;
  /** ISO 8601 UTC — the device's capture time, sent as the record's `recordedAt`. */
  recordedAt: string;
  notes?: string | null;
}

/**
 * Check-in and check-out share a payload: the server's `AttendanceMutation` is `{ employeeId,
 * occurredAt }` for both, because both are "this employee, at this time, that day".
 */
export interface AttendanceMutationPayload {
  employeeId: string;
  /** ISO 8601 UTC — when the device saw the employee arrive or leave. */
  occurredAt: string;
}

export interface TaskCompletionPayload {
  taskId: string;
  completionNotes?: string | null;
  /** ISO 8601 UTC — when the work was actually finished. */
  occurredAt: string;
}

/** One item as the sync endpoint's `items[]` expects it. */
export interface MutationEnvelope<TPayload = unknown> {
  operation: string;
  clientMutationId: string;
  payload: TPayload;
}

export interface MutationKindDefinition<TPayload> {
  /** The kind stored on the item and the operation sent to the server. */
  kind: string;
  /** A short noun for the sync screen's group headings and toasts. */
  label: string;
  /** Builds the wire item. The payload goes out exactly as it was stored. */
  toEnvelope: (item: OutboxItem<TPayload>) => MutationEnvelope<TPayload>;
  /** The entity the mutation is about, derived from the payload at enqueue. */
  targetIdOf: (payload: TPayload) => string;
  /** The device capture time, derived from the payload at enqueue. */
  occurredAtOf: (payload: TPayload) => string;
  /** One line for the queue UI, optionally naming the target. */
  summarize: (item: OutboxItem<TPayload>, targetLabel?: string | null) => string;
}

const WEIGHT_RECORD_KIND = 'weight.record';

const weightRecord: MutationKindDefinition<WeightRecordPayload> = {
  kind: WEIGHT_RECORD_KIND,
  label: 'Weight',

  toEnvelope: (item) => ({
    operation: WEIGHT_RECORD_KIND,
    clientMutationId: item.mutationId,
    // Unwrapped from the stored payload rather than rebuilt from the item's other fields:
    // what the server receives is what the device recorded, with no second derivation that
    // could disagree with it.
    payload: {
      animalId: item.payload.animalId,
      weightKg: item.payload.weightKg,
      recordedAt: item.payload.recordedAt,
      notes: item.payload.notes ?? null,
    },
  }),

  targetIdOf: (payload) => payload.animalId,
  occurredAtOf: (payload) => payload.recordedAt,

  summarize: (item, targetLabel) => {
    const target = targetLabel ?? item.targetId;
    const weight = `${item.payload.weightKg} kg`;
    return `${weight} · ${target}`;
  },
};

const ATTENDANCE_CHECK_IN_KIND = 'attendance.checkIn';
const ATTENDANCE_CHECK_OUT_KIND = 'attendance.checkOut';
const TASK_COMPLETE_KIND = 'task.complete';

/**
 * The two attendance kinds differ only in the operation string and the wording, so they are
 * built from one definition rather than duplicated: a check-out that forgot to send
 * `employeeId` is exactly the kind of drift a second copy invites.
 */
const attendanceKind = (
  kind: string,
  label: string,
  verb: string,
): MutationKindDefinition<AttendanceMutationPayload> => ({
  kind,
  label,

  toEnvelope: (item) => ({
    operation: kind,
    clientMutationId: item.mutationId,
    payload: {
      employeeId: item.payload.employeeId,
      occurredAt: item.payload.occurredAt,
    },
  }),

  targetIdOf: (payload) => payload.employeeId,
  occurredAtOf: (payload) => payload.occurredAt,

  summarize: (item, targetLabel) => `${verb} · ${targetLabel ?? item.targetId}`,
});

const attendanceCheckIn = attendanceKind(ATTENDANCE_CHECK_IN_KIND, 'Check-in', 'Checked in');
const attendanceCheckOut = attendanceKind(ATTENDANCE_CHECK_OUT_KIND, 'Check-out', 'Checked out');

const taskComplete: MutationKindDefinition<TaskCompletionPayload> = {
  kind: TASK_COMPLETE_KIND,
  label: 'Task completion',

  toEnvelope: (item) => ({
    operation: TASK_COMPLETE_KIND,
    clientMutationId: item.mutationId,
    payload: {
      taskId: item.payload.taskId,
      completionNotes: item.payload.completionNotes ?? null,
      occurredAt: item.payload.occurredAt,
    },
  }),

  targetIdOf: (payload) => payload.taskId,
  occurredAtOf: (payload) => payload.occurredAt,

  summarize: (item, targetLabel) => `Completed · ${targetLabel ?? item.targetId}`,
};

export const MUTATION_KINDS: Record<string, MutationKindDefinition<any>> = {
  [WEIGHT_RECORD_KIND]: weightRecord,
  [ATTENDANCE_CHECK_IN_KIND]: attendanceCheckIn,
  [ATTENDANCE_CHECK_OUT_KIND]: attendanceCheckOut,
  [TASK_COMPLETE_KIND]: taskComplete,
};

/** The kinds, for call sites that must not misspell them. */
export const WEIGHT_RECORD = WEIGHT_RECORD_KIND;
export const ATTENDANCE_CHECK_IN = ATTENDANCE_CHECK_IN_KIND;
export const ATTENDANCE_CHECK_OUT = ATTENDANCE_CHECK_OUT_KIND;
export const TASK_COMPLETE = TASK_COMPLETE_KIND;

export function getMutationKind(kind: string): MutationKindDefinition<any> | null {
  return MUTATION_KINDS[kind] ?? null;
}

/** Status wording, in one place so the badge, the screen and the pages agree. */
// The status labels are translated at render time; `i18n/vocabulary.ts` owns the
// value → key table, since the same four words appear on the sync screens and in the
// outbox table. Colours stay here: a colour is not a language.

export const OUTBOX_STATUS_COLORS: Record<OutboxStatus, string> = {
  pending: 'blue',
  applied: 'green',
  quarantined: 'red',
  dismissed: 'default',
};

/** Kind-scoped lookups, tolerant of an item whose kind this build does not know. */
export function describeItem(item: OutboxItem, targetLabel?: string | null): string {
  const definition = getMutationKind(item.kind);
  return definition ? definition.summarize(item as OutboxItem<any>, targetLabel) : item.kind;
}

export function kindLabel(kind: string): string {
  return getMutationKind(kind)?.label ?? kind;
}
