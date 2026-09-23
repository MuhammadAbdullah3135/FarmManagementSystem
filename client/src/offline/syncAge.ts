/**
 * How long ago data was synced, in the words every cached surface uses.
 *
 * One formatter, two callers — the offline banner's "last synced" line and the
 * per-view freshness label — because those two places must never disagree about
 * how old the device's data is. This is presentation only: it never decides
 * whether data is usable, and it never rounds *down* into looking fresher than
 * it is ("just now" means under a minute, not "probably fine").
 */
export function formatSyncAge(value: string | null): string {
  if (!value) return 'never';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return 'never';

  const minutes = Math.floor((Date.now() - date.getTime()) / 60000);
  if (minutes < 1) return 'just now';
  if (minutes < 60) return `${minutes} minute${minutes === 1 ? '' : 's'} ago`;

  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} hour${hours === 1 ? '' : 's'} ago`;

  const days = Math.floor(hours / 24);
  return `${days} day${days === 1 ? '' : 's'} ago`;
}
