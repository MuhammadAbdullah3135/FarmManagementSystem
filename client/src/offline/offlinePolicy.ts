/**
 * The two limits the device works to, and the wording that goes with them.
 *
 * <para>
 * Both exist because of the same failure: work recorded offline that can never reach the
 * server. One is about the *session* (a refresh token has a ceiling of 30 days, so a device
 * that has not reached the API for a week is close enough to that ceiling that continuing to
 * accept offline writes is storing records that may never be sent). The other is about the
 * *device* (a queue with no cap eventually fills the origin's storage quota, and a full quota
 * is how the *cache* gets evicted too).
 * </para>
 *
 * <para>
 * The numbers and the sentences live here together on purpose. A limit the user cannot act on
 * is a refusal with no way forward, so every threshold has the sentence that explains it and
 * says what to do.
 * </para>
 */

/** Days without reaching the API after which the device starts saying so. */
export const OFFLINE_SESSION_WARN_DAYS = 5;

/**
 * Days without reaching the API after which new offline writes are refused.
 *
 * Seven rather than thirty: the point is to stop *before* the refresh token dies, so the
 * records the device already holds can still be delivered. Warning at five leaves a working
 * day to notice.
 */
export const OFFLINE_SESSION_BLOCK_DAYS = 7;

/** How many unsynced items the device will hold. */
export const MAX_QUEUED_ITEMS = 5000;

/** How many items are left when the user is warned that the cap is approaching. */
export const QUEUE_WARN_REMAINING = 500;

export type OfflineSessionStatus = 'fresh' | 'warning' | 'blocked';

export interface OfflineSessionState {
  status: OfflineSessionStatus;
  /**
   * Whole days since the account last reached the API, or null when this device has no record
   * of it ever doing so (a device that has never been online has nothing to be behind on —
   * reaching the API is what signs a user in in the first place).
   */
  daysSinceServerContact: number | null;
  lastServerContactAt: string | null;
  /** The single question a write path asks. */
  writeAllowed: boolean;
}

const DAY_MS = 24 * 60 * 60 * 1000;

/** The session's state, from the marker's timestamp. Pure, so the policy is testable alone. */
export function sessionStateFrom(
  lastServerContactAt: string | null,
  now: number = Date.now(),
): OfflineSessionState {
  if (!lastServerContactAt) {
    return {
      status: 'fresh',
      daysSinceServerContact: null,
      lastServerContactAt: null,
      writeAllowed: true,
    };
  }

  const contactedAt = Date.parse(lastServerContactAt);
  if (Number.isNaN(contactedAt)) {
    // An unreadable timestamp is treated as "no record", not as "infinitely old": refusing to
    // record on a device whose marker is corrupt would strand a worker for a storage bug.
    return {
      status: 'fresh',
      daysSinceServerContact: null,
      lastServerContactAt: null,
      writeAllowed: true,
    };
  }

  const days = Math.floor(Math.max(0, now - contactedAt) / DAY_MS);
  const status: OfflineSessionStatus = days >= OFFLINE_SESSION_BLOCK_DAYS
    ? 'blocked'
    : days >= OFFLINE_SESSION_WARN_DAYS
      ? 'warning'
      : 'fresh';

  return {
    status,
    daysSinceServerContact: days,
    lastServerContactAt,
    writeAllowed: status !== 'blocked',
  };
}

export interface QueueCapacityState {
  pendingCount: number;
  remaining: number;
  /** True when the queue is close enough to the cap to say so. */
  warning: boolean;
  full: boolean;
}

export function queueCapacityFrom(pendingCount: number): QueueCapacityState {
  const remaining = Math.max(0, MAX_QUEUED_ITEMS - pendingCount);
  const full = pendingCount >= MAX_QUEUED_ITEMS;

  return {
    pendingCount,
    remaining,
    warning: !full && remaining <= QUEUE_WARN_REMAINING,
    full,
  };
}

/** The sentence for a session that is close to the refresh token's ceiling. */
export function sessionWarningMessage(days: number): string {
  return `This device has not reached the server in ${days} days. Unsent records can only be `
    + 'delivered while the session lasts — connect and sync soon.';
}

/** The sentence for a session that has passed it: a refusal, with the way out. */
export function sessionBlockedMessage(days: number): string {
  return `This device has not reached the server in ${days} days, so offline records can no `
    + 'longer be delivered reliably. Connect to the internet and sync before recording more.';
}

/** The sentence for a queue that is nearly full. */
export function queueWarningMessage(remaining: number): string {
  return `This device is holding almost as many unsynced records as it can (${remaining} left). `
    + 'Sync to make room.';
}

/** The sentence for a queue that is full. */
export function queueFullMessage(): string {
  return `This device is holding ${MAX_QUEUED_ITEMS.toLocaleString()} unsynced records, its `
    + 'limit. Sync before recording more.';
}
