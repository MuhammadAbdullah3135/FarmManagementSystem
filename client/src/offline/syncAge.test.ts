import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { formatSyncAge } from './syncAge';

describe('formatSyncAge', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-09-23T12:00:00.000Z'));
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('reports never for missing or unparseable values', () => {
    expect(formatSyncAge(null)).toBe('never');
    expect(formatSyncAge('')).toBe('never');
    expect(formatSyncAge('not a date')).toBe('never');
  });

  it('rounds nothing up into looking fresher than it is', () => {
    // 59 seconds is still "just now"; a minute exactly is already "1 minute ago".
    expect(formatSyncAge(new Date(Date.now() - 59_000).toISOString())).toBe('just now');
    expect(formatSyncAge(new Date(Date.now() - 60_000).toISOString())).toBe('1 minute ago');
    expect(formatSyncAge(new Date(Date.now() - 2 * 60_000).toISOString())).toBe('2 minutes ago');
    expect(formatSyncAge(new Date(Date.now() - 60 * 60_000).toISOString())).toBe('1 hour ago');
    expect(formatSyncAge(new Date(Date.now() - 5 * 60 * 60_000).toISOString())).toBe('5 hours ago');
    expect(formatSyncAge(new Date(Date.now() - 2 * 24 * 60 * 60_000).toISOString())).toBe('2 days ago');
    expect(formatSyncAge(new Date(Date.now() - 25 * 60 * 60_000).toISOString())).toBe('1 day ago');
  });
});
