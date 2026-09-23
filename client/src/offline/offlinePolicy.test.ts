import { describe, expect, it } from 'vitest';
import {
  MAX_QUEUED_ITEMS,
  OFFLINE_SESSION_BLOCK_DAYS,
  OFFLINE_SESSION_WARN_DAYS,
  QUEUE_WARN_REMAINING,
  queueCapacityFrom,
  queueFullMessage,
  queueWarningMessage,
  sessionBlockedMessage,
  sessionStateFrom,
  sessionWarningMessage,
} from './offlinePolicy';

const DAY_MS = 24 * 60 * 60 * 1000;
const now = Date.parse('2026-09-23T08:00:00.000Z');
const daysAgo = (days: number) => new Date(now - days * DAY_MS).toISOString();

/**
 * The two limits, asserted as the numbers the user actually meets: which day new offline work
 * stops being accepted, and how many records the device will hold before it says no.
 */
describe('offline write window', () => {
  it('accepts writes on a fresh session and on a device that has never reached the API', () => {
    // No marker at all is a device that has never been online — reaching the API is what signs a
    // user in, so there is nothing recorded to be behind on.
    expect(sessionStateFrom(null, now)).toMatchObject({
      status: 'fresh',
      daysSinceServerContact: null,
      writeAllowed: true,
    });

    const today = sessionStateFrom(daysAgo(0), now);
    expect(today.status).toBe('fresh');
    expect(today.writeAllowed).toBe(true);
    expect(today.daysSinceServerContact).toBe(0);
  });

  it('warns before it refuses, and refuses at the cutoff rather than at the token expiry', () => {
    const warnDay = sessionStateFrom(daysAgo(OFFLINE_SESSION_WARN_DAYS), now);
    expect(warnDay.status).toBe('warning');
    // A warning still accepts work: the point is to leave time to sync, not to strand a shift.
    expect(warnDay.writeAllowed).toBe(true);

    const lastAccepted = sessionStateFrom(daysAgo(OFFLINE_SESSION_BLOCK_DAYS - 1), now);
    expect(lastAccepted.status).toBe('warning');

    const blocked = sessionStateFrom(daysAgo(OFFLINE_SESSION_BLOCK_DAYS), now);
    expect(blocked.status).toBe('blocked');
    expect(blocked.writeAllowed).toBe(false);
    expect(blocked.daysSinceServerContact).toBe(OFFLINE_SESSION_BLOCK_DAYS);

    // Seven days of refusal under a thirty-day refresh token: the stop has to come well before
    // the ceiling, or the records already queued could not be delivered at all.
    expect(OFFLINE_SESSION_BLOCK_DAYS).toBeLessThan(30);
  });

  it('degrades an unreadable timestamp to "no record" rather than to a refusal', () => {
    const state = sessionStateFrom('not a timestamp', now);

    // Refusing to record because a storage value is corrupt would strand a worker for a bug.
    expect(state.status).toBe('fresh');
    expect(state.writeAllowed).toBe(true);
    expect(state.daysSinceServerContact).toBeNull();
  });

  it('never treats a future timestamp as an old session', () => {
    const state = sessionStateFrom(new Date(now + 3 * DAY_MS).toISOString(), now);

    expect(state.status).toBe('fresh');
    expect(state.daysSinceServerContact).toBe(0);
  });

  it('says what to do, not just that something is wrong', () => {
    expect(sessionWarningMessage(5)).toContain('5 days');
    expect(sessionWarningMessage(5)).toContain('sync');
    expect(sessionBlockedMessage(8)).toContain('8 days');
    expect(sessionBlockedMessage(8)).toContain('sync');
  });
});

describe('queue capacity', () => {
  it('neither warns nor refuses while there is room', () => {
    const state = queueCapacityFrom(10);

    expect(state).toMatchObject({ full: false, warning: false, remaining: MAX_QUEUED_ITEMS - 10 });
  });

  it('warns well before the cap, and still accepts the write', () => {
    const state = queueCapacityFrom(MAX_QUEUED_ITEMS - QUEUE_WARN_REMAINING);

    expect(state.warning).toBe(true);
    expect(state.full).toBe(false);
    expect(state.remaining).toBe(QUEUE_WARN_REMAINING);
    expect(queueWarningMessage(state.remaining)).toContain(String(QUEUE_WARN_REMAINING));
  });

  it('refuses at the cap, and says the number rather than "storage failed"', () => {
    const state = queueCapacityFrom(MAX_QUEUED_ITEMS);

    expect(state).toMatchObject({ full: true, warning: false, remaining: 0 });
    expect(queueFullMessage()).toContain('5,000');
    expect(queueFullMessage()).toContain('Sync');
  });
});
