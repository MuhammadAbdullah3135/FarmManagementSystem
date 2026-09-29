/**
 * Reading the rows a refused change names.
 *
 * A 409 that only counts what blocks it ("still used by 2 gestation records") sends the
 * reader hunting. The delete endpoints therefore attach the blocking rows themselves to the
 * problem body (`blockers[]` — id, kind, label). This is where a caller turns a response into
 * that list, or `[]` for every refusal that has none: plain-string conflicts, older servers,
 * and refusals about something other than rows.
 *
 * The parsing is defensive because a message toast is the one place a malformed body must not
 * crash — a `detail` that arrives as a number, or a `blockers` that is an object, degrades to
 * the English sentence rather than an exception.
 */

export interface Blocker {
  /** The id of the row in the way — routable. */
  id: string;
  /** What the row is, as the domain names it: "gestationRecord". */
  kind: string;
  /** Enough to recognise it at a glance ("005 · expected 2026-11-30"). */
  label: string;
}

const asString = (value: unknown): string | null =>
  typeof value === 'string' && value.trim() ? value : null;

const asBlocker = (raw: unknown): Blocker | null => {
  if (!raw || typeof raw !== 'object') return null;
  const record = raw as Record<string, unknown>;
  const id = asString(record.id);
  const kind = asString(record.kind);
  const label = asString(record.label);
  return id && kind && label ? { id, kind, label } : null;
};

/**
 * The blockers a conflict response carries, or `[]` when it names none.
 *
 * Accepts the axios error shape (`err.response.data`) or an already-unwrapped body, so a
 * caller can pass whatever its catch block is holding.
 */
export const blockersOf = (errorOrBody: unknown): Blocker[] => {
  const response = (errorOrBody as { response?: { data?: unknown } } | null)?.response;
  const body = (response?.data ?? errorOrBody) as unknown;

  if (!body || typeof body !== 'object') return [];
  const blockers = (body as Record<string, unknown>).blockers;
  if (!Array.isArray(blockers)) return [];

  return blockers
    .map(asBlocker)
    .filter((blocker): blocker is Blocker => blocker !== null);
};
