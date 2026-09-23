import { useCallback, useEffect, useRef, useState } from 'react';
import {
  getCollection,
  getMeta,
  mergeCollection,
  replaceCollection,
  type OfflineScope,
} from './db';
import { useOfflineStore } from './connectivity';
import { useAuthStore } from '../stores/authStore';
import { useFarmStore } from '../stores/farmStore';

/**
 * The read-only lists allowed to be cached, and the query variants each may hold.
 *
 * This is deliberately an enumerated registry rather than a free-form `collection`
 * string: a hook that would happily cache any endpoint is one call site away from quietly
 * mirroring the whole API onto the device. Adding a collection here is a decision, not a
 * side effect — and in dev an unregistered pair throws rather than silently caching.
 *
 * 4.5.2 scoped this to three read-only work lists (weight-check status, the first page of the
 * task list, employees). 4.5.4 adds one more: the farm's animal lookup (`animals@lookup`),
 * because offline weight recording needs to name an animal the device actually holds — the
 * architecture's answer to "cache-only animal lookup" is enforced by this being the only
 * source a picker may read, not by asking the form to behave.
 *
 * 4.5.5 adds `attendance@firstPage`: the default (today) view of the register. Attendance is
 * one of the three offline targets in the approved architecture, so its work list has to be
 * readable with no signal — a page that can queue a check-in but cannot show who is already in
 * would make the user guess. Only the default view is stored; a filtered or later page is a
 * live query, exactly as the task list's are.
 */
export const CACHED_COLLECTIONS = {
  weightCheckStatus: ['all'],
  tasks: ['firstPage'],
  employees: ['listPage1', 'options'],
  animals: ['lookup'],
  attendance: ['firstPage'],
} as const;

export type CachedCollectionName = keyof typeof CACHED_COLLECTIONS;

/** The store's collection key for one query shape: `'<collection>@<variant>'`. */
export function collectionKey(collection: string, variant: string): string {
  return `${collection}@${variant}`;
}

const verifiedVariants = new Set<string>();

/**
 * Development-time guard. Unknown pairs throw in dev so a mistake surfaces in the
 * suite; in production they degrade to an ordinary scoped collection — safe, because
 * every read and write below still carries the full (accountId, farmId) scope.
 */
function assertRegisteredVariant(collection: string, variant: string): void {
  const key = collectionKey(collection, variant);
  if (verifiedVariants.has(key)) return;

  const allowed = (CACHED_COLLECTIONS as Record<string, readonly string[]>)[collection];
  if (allowed?.includes(variant)) {
    verifiedVariants.add(key);
    return;
  }

  if (import.meta.env?.DEV) {
    throw new Error(
      `useCachedQuery: '${key}' is not a registered cached variant. Cached collections are an `
        + 'approved, enumerated surface (Phase 5.2) — add it to CACHED_COLLECTIONS deliberately, '
        + 'or fetch it without the hook.',
    );
  }
}

/**
 * A plain read of a cached collection, without a component lifecycle attached.
 *
 * For callers that need rows belonging to a scope they are not *viewing* — the sync screen
 * resolves animal tags for queued items from other farms (the queue is account-wide, the page
 * is farm-independent). It stays inside this module so the registry check, and therefore the
 * "only approved collections are cached" rule, covers this path too.
 */
export async function readCachedCollection<TRow>(
  scope: OfflineScope,
  collection: CachedCollectionName,
  variant: string,
): Promise<TRow[]> {
  assertRegisteredVariant(collection, variant);
  const records = await getCollection<TRow>(scope, collectionKey(collection, variant));
  return records.map((record) => record.data);
}

/**
 * What a delta read says beyond its rows.
 *
 * `deletedIds` is the part that cannot be inferred: a delta that simply omits a row is
 * indistinguishable from a delta that has nothing to say about it, so the server states the
 * removals. `requiresFullSync` is the honest "I cannot answer that cursor" — the caller then
 * reads the collection in full.
 */
export interface CachedQueryDelta {
  deletedIds: string[];
  requiresFullSync: boolean;
}

export interface CachedQueryFetcherResult<TRow> {
  /** The rows to show and (when cacheable) to store. */
  rows: TRow[];
  /** Total matching rows on the server, for pagination. Cached views report the stored count. */
  total: number;
  /**
   * The server's own cursor for this read, stored so the *next* read can be a delta.
   *
   * A full read has to report it too, and that is the whole reason it is not a field of
   * `delta`: the reply to "give me everything" is what a device has to remember in order to
   * ever ask "what changed since then". A cursor the device invents would be compared against
   * the server's clock, which is how a client skips rows it never received.
   */
  cursor?: string | null;
  /**
   * Present when this response is a change set to merge rather than a full read. Absent means
   * the response is the collection, which replaces what is stored.
   */
  delta?: CachedQueryDelta;
}

export interface UseCachedQueryOptions<TRow> {
  collection: CachedCollectionName;
  /** Which query shape of that collection, e.g. 'firstPage' / 'options'. */
  variant: string;
  /**
   * Performs the request. Read through a ref, so an inline closure is fine.
   *
   * Receives the stored cursor for this collection when the view can be deltaed (`null` on the
   * first read of a collection, and always `null` for a view that does not support deltas), so
   * a fetcher never has to reach into storage to find out where it left off.
   */
  fetcher: (cursor: string | null) => Promise<CachedQueryFetcherResult<TRow>>;

  /**
   * Whether this view's endpoint understands `updatedSince` and reports deletions. Only the
   * collections the architecture allows a device to rely on do; anything else reads in full,
   * every time.
   */
  supportsDelta?: boolean;
  /**
   * Summarises everything the fetcher depends on (page, filters, search). A change
   * re-runs the query — the fetcher itself cannot be an effect dependency without
   * re-running on every render.
   */
  paramsKey?: string;
  /**
   * Whether the *current* parameters are the cacheable default view. False for
   * searches, later pages and filtered views: they neither read nor write the store.
   */
  cacheable?: boolean;
  /** Skips the query entirely (e.g. a view that is not open). */
  enabled?: boolean;
  /** Derives the stored row id; defaults to `row.id`. */
  getRowId?: (row: TRow) => string;
  /** Reports a failed request; pages own the presentation of errors. */
  onError?: (error: unknown) => void;
}

export interface CachedQueryResult<TRow> {
  rows: TRow[];
  total: number;
  /** When these rows were last read from the API, or null when the view is not cached. */
  lastSyncedAt: string | null;
  /** True while the rows on screen came from the store rather than the network. */
  fromCache: boolean;
  isLoading: boolean;
  /** Re-runs the current query (and re-stamps the cache when the view is cacheable). */
  refresh: () => void;
}

interface QueryBundle<TRow> {
  /** Identifies the exact view these rows belong to; see `viewKey` below. */
  viewKey: string;
  rows: TRow[];
  total: number;
  lastSyncedAt: string | null;
  fromCache: boolean;
}

/**
 * Serves a read-only list from the device's cache, then from the network.
 *
 * Behaviour, in the order it matters offline:
 *
 * - **Offline**: the store is the only source. The fetcher is never called, so an
 *   offline view cannot accidentally depend on a request that will fail.
 * - **Online**: stale-while-revalidate — the cached rows paint first (a field device
 *   should never stare at a spinner for data it already has), then the request runs
 *   and replaces them, re-stamping the collection's `lastSyncedAt`. Since 5.6 the
 *   request is a *delta* when the collection supports one and the store holds a
 *   cursor: only what changed comes back, and rows that did not change stay as they
 *   were. Deletions arrive as ids and are removed from the store, because a delta
 *   cannot express a removal by omission.
 * - **Scope changes**: rows carry the identity of the view they were read for — the
 *   account, the farm, the query and its parameters. Only rows whose identity still
 *   matches the *current* view are ever rendered, and that check happens during
 *   render. Switching farms therefore cannot show the previous farm's rows for even
 *   one frame while the new scope's cache read is in flight.
 *
 * This is a read path only: it never queues a write and never mutates server state.
 */
export function useCachedQuery<TRow>(options: UseCachedQueryOptions<TRow>): CachedQueryResult<TRow> {
  const {
    collection,
    variant,
    fetcher,
    supportsDelta = false,
    paramsKey = variant,
    cacheable = true,
    enabled = true,
    getRowId,
    onError,
  } = options;

  assertRegisteredVariant(collection, variant);

  const key = collectionKey(collection, variant);
  const accountId = useAuthStore((state) => state.user?.accountId ?? null);
  const farmId = useFarmStore((state) => state.activeFarm?.id ?? null);
  const scopeKey = accountId && farmId ? `${accountId}|${farmId}` : null;
  const viewKey = `${scopeKey ?? ''}|${key}|${paramsKey}|${cacheable ? 'cache' : 'live'}`;

  const [bundle, setBundle] = useState<QueryBundle<TRow> | null>(null);
  const [fetching, setFetching] = useState(false);

  /**
   * The latest options, readable from async work without becoming effect dependencies.
   *
   * Synced in an effect rather than during render: assigning to a ref while rendering is
   * unsafe under concurrent rendering (React can render without committing), and the
   * linter in this repo rejects it outright. The effect below is declared before the
   * data effect, so by the time the data effect runs the refs hold this render's values.
   */
  const latest = useRef({ fetcher, getRowId, onError, cacheable, supportsDelta, viewKey });
  const scopeRef = useRef<{ accountId: string; farmId: string } | null>(
    accountId && farmId ? { accountId, farmId } : null,
  );

  useEffect(() => {
    latest.current = { fetcher, getRowId, onError, cacheable, supportsDelta, viewKey };
    scopeRef.current = accountId && farmId ? { accountId, farmId } : null;
  });

  const runFetch = useCallback(async () => {
    const scope = scopeRef.current;
    if (!scope) return;
    if (!useOfflineStore.getState().isOnline) return;

    const startedAs = latest.current.viewKey;
    setFetching(true);
    try {
      // A delta only makes sense for a cacheable view: a live, filtered or later page has
      // nothing stored to merge into, and its rows were never the cached ones anyway.
      const canDelta = latest.current.supportsDelta && latest.current.cacheable;
      const storedMeta = canDelta ? await getMeta(scope, key) : null;
      if (latest.current.viewKey !== startedAs) return;
      const storedCursor = canDelta ? storedMeta?.cursor ?? null : null;

      // Which cursor the response actually belongs to. A full re-read replaces the stored
      // collection; only a request that carried a cursor may be merged into it, or rows the
      // server never mentioned again would survive as if they still existed.
      let askedWith = storedCursor;
      let result = await latest.current.fetcher(storedCursor);

      // "I cannot answer that cursor" is not a failure — it is the server asking to be asked
      // in full. Asked once: a second refusal means the request itself is the problem, and
      // re-asking cannot fix that.
      if (result.delta?.requiresFullSync) {
        askedWith = null;
        result = await latest.current.fetcher(null);

        if (result.delta?.requiresFullSync) {
          // Nothing trustworthy came back, and an empty answer must never be written over a
          // device's only copy of the collection.
          latest.current.onError?.(new Error('The server could not provide this list in full.'));
          return;
        }
      }

      // The view may have moved on (another page, another farm) while the request was in
      // flight. Applying the response then would show one view's rows under another.
      if (latest.current.viewKey !== startedAs) return;

      const shouldCache = latest.current.cacheable;
      // One timestamp for both the label and the stored meta: the view says "synced just
      // now" while the store holds a time a millisecond later, and the two must agree.
      const syncedAt = shouldCache ? new Date().toISOString() : null;

      if (shouldCache && result.delta && askedWith !== null) {
        // A delta: apply it to what is stored, then show the store — not the delta's handful of
        // rows, which would paint as if the collection had shrunk to what just changed.
        await mergeCollection<TRow>(
          scope,
          key,
          {
            rows: result.rows.map((row) => ({
              id: latest.current.getRowId ? latest.current.getRowId(row) : (row as { id: string }).id,
              data: row,
            })),
            deletedIds: result.delta.deletedIds,
            // A response without a cursor leaves the stored one alone: re-asking from the same
            // position is safe, whereas inventing a later one would skip whatever it covered.
            cursor: result.cursor ?? askedWith,
          },
          { fetchedAt: syncedAt ?? undefined },
        );

        const records = await getCollection<TRow>(scope, key);
        if (latest.current.viewKey !== startedAs) return;

        setBundle({
          viewKey: startedAs,
          rows: records.map((record) => record.data),
          total: records.length,
          lastSyncedAt: syncedAt,
          fromCache: false,
        });

        void useOfflineStore.getState().refreshStats();
        return;
      }

      setBundle({
        viewKey: startedAs,
        rows: result.rows,
        total: result.total,
        // Only a cacheable view advertises a sync time: a filtered view's rows are live
        // and must not be labelled as saved on the device.
        lastSyncedAt: syncedAt,
        fromCache: false,
      });

      if (shouldCache) {
        await replaceCollection(
          scope,
          key,
          result.rows.map((row) => ({
            id: latest.current.getRowId ? latest.current.getRowId(row) : (row as { id: string }).id,
            data: row,
          })),
          {
            fetchedAt: syncedAt ?? undefined,
            // A full read states the cursor the next read will use, so the very first sync of a
            // collection leaves it able to be deltaed rather than always re-reading in full.
            cursor: result.cursor ?? null,
          },
        );

        // The banner counts what the device holds; it must follow what was just stored.
        void useOfflineStore.getState().refreshStats();
      }
    } catch (error) {
      latest.current.onError?.(error);
    } finally {
      setFetching(false);
    }
  }, [key]);

  useEffect(() => {
    if (!enabled) return;
    if (!scopeKey) return;

    let cancelled = false;
    const scope = scopeRef.current;
    const forView = viewKey;

    void (async () => {
      // Uncacheable views never read the store: a stale row set is not the answer to
      // "page 2 with this filter", and the bundle they would replace belongs to a
      // different view anyway (so it is already hidden by the render guard above).
      if (cacheable && scope) {
        const [records, meta] = await Promise.all([
          getCollection<TRow>(scope, key),
          getMeta(scope, key),
        ]);
        if (cancelled) return;

        // Re-check after the async read: a farm switch during it must not paint the
        // previous farm's rows under the new farm.
        if (latest.current.viewKey !== forView) return;

        setBundle({
          viewKey: forView,
          rows: records.map((record) => record.data),
          total: records.length,
          lastSyncedAt: meta?.lastSyncedAt ?? null,
          fromCache: records.length > 0,
        });
      }

      void runFetch();
    })();

    return () => {
      cancelled = true;
    };
  }, [scopeKey, key, enabled, cacheable, paramsKey, viewKey, runFetch]);

  // The render-time view guard: only a bundle that belongs to the *current* view is ever
  // visible. This is what makes "never shows another farm's rows" a property of the
  // structure rather than of cleanup timing.
  const active = bundle && bundle.viewKey === viewKey ? bundle : null;

  const awaitingCacheRead = enabled && cacheable && Boolean(scopeKey) && active === null && !fetching;

  const refresh = useCallback(() => {
    void runFetch();
  }, [runFetch]);

  return {
    rows: active?.rows ?? [],
    total: active?.total ?? 0,
    lastSyncedAt: active?.lastSyncedAt ?? null,
    fromCache: active?.fromCache ?? false,
    // "Loading" means "nothing to show yet", not "a request is in flight". A view that already
    // has rows — from the store or from an earlier read — stays usable while it revalidates,
    // which is the whole point of painting the cache first. Callers put this on a table's
    // spinner, and a spinner blur makes the rows inert; making a revalidation look like a load
    // is how a field device becomes unclickable every time it refreshes.
    isLoading: enabled && active === null && (fetching || awaitingCacheRead),
    refresh,
  };
}
