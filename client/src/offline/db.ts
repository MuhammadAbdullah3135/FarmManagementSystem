/**
 * The device's offline store (IndexedDB).
 *
 * This is the platform layer for offline work: a versioned database, its schema
 * migration path, the cached read layer (4.5.2) and the write queue (4.5.4). Both
 * store the same way — every key carries the compound `(accountId, farmId, …)` path —
 * and both are reached through a typed module above this one (`cachedQuery`/`outbox`),
 * which is what keeps "a read cannot cross a farm boundary" a property of the shape
 * rather than of the caller's care.
 *
 * Four rules this module exists to enforce:
 *
 * 1. **Every key carries both `accountId` and `farmId`.** The app supports multiple
 *    farms per account and multiple accounts per device, so "the cached task list"
 *    is never a single global value. Reads always take an explicit scope, which is
 *    what makes it structurally impossible to serve one farm's rows for another.
 *    (The session marker is the one record keyed by account alone — see `SessionMarker`
 *    — which is what makes the offline-write policy a property of the session rather
 *    than of a farm the user may have switched away from.)
 * 2. **No credentials are stored here.** Access and refresh tokens stay in
 *    `localStorage`, where the axios layer already reads them; this store holds only
 *    farm data that the API already returned to the signed-in user.
 * 3. **Missing or broken storage degrades to "no cache", never to an error.** Old
 *    Android WebViews, private-browsing modes and jsdom all reach this code, and an
 *    app that works without a cache is strictly better than one that throws.
 */

export const OFFLINE_DB_NAME = 'fms-offline';

/** Bump this and add a step in `upgradeOfflineSchema` when a store changes shape. */
export const OFFLINE_DB_VERSION = 3;

/** One record per account: when that account last reached the API. See `SessionMarker`. */
export const SESSION_STORE = 'session';

/** Cached API responses, one record per (account, farm, collection, id). */
export const CACHE_STORE = 'cache';

/** Per-collection bookkeeping: when it was last refreshed, and how many rows it holds. */
export const META_STORE = 'syncMeta';

/**
 * The write queue (4.5.4): one record per queued mutation, per (account, farm, mutation).
 *
 * Separate from the read cache on purpose, because its lifetime is different: the cache
 * is disposable (the API can hand it back), while a queued item is the *only* copy of a
 * field worker's measurement until the server has accepted it. Signing out therefore
 * clears the cache and keeps the queue (see `clearCachedData` vs `clearQueue`).
 */
export const QUEUE_STORE = 'outbox';

export interface OfflineScope {
  accountId: string;
  farmId: string;
}

export interface CachedRecord<T = unknown> extends OfflineScope {
  /** Logical collection, e.g. `animals`, `tasks`, `weightCheckStatus`. */
  collection: string;
  /** The row's own id (a GUID string from the API). */
  id: string;
  data: T;
  /** When this row was fetched from the API, so the UI can say how stale it is. */
  fetchedAt: string;
}

export interface CollectionMeta extends OfflineScope {
  collection: string;
  lastSyncedAt: string;
  recordCount: number;
  /**
   * The server's own timestamp for the last read of this collection — what a delta sends back
   * as `updatedSince` (5.6).
   *
   * Deliberately *not* `lastSyncedAt`: that one is the device's clock, and a device whose clock
   * is wrong would then skip rows it never received. `null` means "no cursor yet", which is
   * exactly the state a full read leaves it in.
   */
  cursor: string | null;
}

/**
 * When one account last talked to the API, whatever it was asking for.
 *
 * <para>
 * This is the clock behind the offline-write policy: past a few days without reaching the
 * server, a device is warned, and past a week it stops accepting new offline work and asks for
 * a sync. The alternative it exists to prevent is the one users actually hit — recording all
 * day, then discovering at sync time that the refresh token expired and the queue cannot be
 * delivered at all.
 * </para>
 *
 * <para>
 * One per account, and deliberately outside the read cache: clearing cached data must not
 * reset this, or clearing it would be a way to keep writing offline past the window that
 * protects the queue. Signing out does not reset it either — the queue survives sign-out, so
 * the reason the queue is in danger survives with it.
 * </para>
 */
export interface SessionMarker {
  accountId: string;
  lastServerContactAt: string;
}

export interface OfflineStorageStats {
  recordCount: number;
  collectionCount: number;
  /** Most recent `lastSyncedAt` across every collection, or null when nothing is cached. */
  lastSyncedAt: string | null;
}

/**
 * Where one queued mutation stands.
 *
 * `pending` is the only status the flush sends. `applied` means the server accepted it
 * (or reported its intent already satisfied); `quarantined` means the server refused it
 * and its message is kept for the user; `dismissed` means a user explicitly gave up on a
 * quarantined item *with a reason*, and the record is retained rather than deleted — an
 * item is never silently dropped.
 */
export type OutboxStatus = 'pending' | 'applied' | 'quarantined' | 'dismissed';

/**
 * One queued mutation.
 *
 * The first nine fields are written once, at enqueue, and never change — see
 * `updateOutboxItem`, which can only ever touch the outcome half. That split is what makes
 * "append-only" a property of the code rather than a convention: a mutation id, its
 * payload and its device timestamp are the things the server's idempotency guarantee is
 * anchored to, so nothing downstream may rewrite them.
 */
export interface OutboxItem<TPayload = unknown> extends OfflineScope {
  /** The device's id for this mutation. Repeating it is what makes a retry safe. */
  mutationId: string;
  /** The workflow to apply, e.g. `weight.record` — the server's own operation string. */
  kind: string;
  /** What the mutation is about (for a weight, the animal). */
  targetId: string;
  /** When the change happened on the device (UTC). Not when it was uploaded. */
  occurredAt: string;
  /** When it entered the queue; the flush's oldest-first ordering key. */
  queuedAt: string;
  payload: TPayload;

  // ── outcome: the only half that is ever written after enqueue ──
  status: OutboxStatus;
  attempts: number;
  /** Transport-level reason from the last attempt (a 5xx, a network failure). */
  lastError: string | null;
  /** The server's own message, verbatim, when it refused the item. */
  serverMessage: string | null;
  appliedAt: string | null;
  /** The row the server created or changed, from the item's result. */
  appliedEntityId: string | null;
  dismissedReason: string | null;
}

/** What a user-facing count needs: how much is waiting, and how old the oldest is. */
export interface OutboxCounts {
  pending: number;
  quarantined: number;
  dismissed: number;
  applied: number;
  /** `queuedAt` of the oldest pending item, or null when nothing is pending. */
  oldestQueuedAt: string | null;
}

function request<T>(req: IDBRequest<T>): Promise<T> {
  return new Promise((resolve, reject) => {
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error ?? new Error('IndexedDB request failed'));
  });
}

function transactionDone(tx: IDBTransaction): Promise<void> {
  return new Promise((resolve, reject) => {
    tx.oncomplete = () => resolve();
    tx.onerror = () => reject(tx.error ?? new Error('IndexedDB transaction failed'));
    tx.onabort = () => reject(tx.error ?? new Error('IndexedDB transaction aborted'));
  });
}

/**
 * Creates the stores each version needs.
 *
 * Written as explicit per-version steps rather than "create everything, ignore
 * errors": version 2 adds the outbox and existing devices keep the rows version 1
 * cached instead of starting empty. (A device that has never opened the app runs both
 * steps in order, which is why they must not depend on each other.)
 */
export function upgradeOfflineSchema(db: IDBDatabase, oldVersion: number): void {
  // Each step is guarded by `contains` as well as by `oldVersion`. A step that throws would
  // abort the whole upgrade transaction, and a failed upgrade means `getOfflineDb` resolves to
  // null — i.e. the device silently loses its cache *and* its queue because one store was
  // already present. Idempotent steps cost nothing and cannot do that.
  if (oldVersion < 1) {
    if (!db.objectStoreNames.contains(CACHE_STORE)) {
      const cache = db.createObjectStore(CACHE_STORE, {
        keyPath: ['accountId', 'farmId', 'collection', 'id'],
      });
      cache.createIndex('byScope', ['accountId', 'farmId', 'collection'], { unique: false });
    }

    if (!db.objectStoreNames.contains(META_STORE)) {
      const meta = db.createObjectStore(META_STORE, {
        keyPath: ['accountId', 'farmId', 'collection'],
      });
      meta.createIndex('byAccountFarm', ['accountId', 'farmId'], { unique: false });
    }
  }

  if (oldVersion < 2 && !db.objectStoreNames.contains(QUEUE_STORE)) {
    const queue = db.createObjectStore(QUEUE_STORE, {
      keyPath: ['accountId', 'farmId', 'mutationId'],
    });
    // One farm's queue (the sync screen and a page's own pending rows).
    queue.createIndex('byScope', ['accountId', 'farmId'], { unique: false });
    // The flush's query: everything pending for this account, across every farm.
    queue.createIndex('byAccountStatus', ['accountId', 'status'], { unique: false });
  }

  if (oldVersion < 3 && !db.objectStoreNames.contains(SESSION_STORE)) {
    // Keyed by account alone: the policy is about the session's age, and a session belongs to
    // an account whether or not it has a farm selected.
    db.createObjectStore(SESSION_STORE, { keyPath: ['accountId'] });
  }
}

export function isOfflineStorageAvailable(): boolean {
  try {
    return typeof globalThis.indexedDB !== 'undefined' && globalThis.indexedDB !== null;
  } catch {
    // Some locked-down WebView configurations throw on property access.
    return false;
  }
}

let openPromise: Promise<IDBDatabase | null> | null = null;

/**
 * Opens (and on first run, creates) the database. Resolves to null when storage is
 * unavailable or unusable, which every caller below treats as an empty cache.
 */
export function getOfflineDb(): Promise<IDBDatabase | null> {
  if (!isOfflineStorageAvailable()) return Promise.resolve(null);
  if (openPromise) return openPromise;

  openPromise = new Promise<IDBDatabase | null>((resolve) => {
    let settled = false;
    const finish = (value: IDBDatabase | null) => {
      if (settled) return;
      settled = true;
      clearTimeout(timeout);
      resolve(value);
    };

    // A hung open (a quota prompt, a blocked upgrade) must not hang the app forever:
    // fall back to "no cache" and let the next call try again.
    const timeout = setTimeout(() => {
      openPromise = null;
      finish(null);
    }, 3000);

    try {
      const req = globalThis.indexedDB.open(OFFLINE_DB_NAME, OFFLINE_DB_VERSION);

      req.onupgradeneeded = (event) => {
        upgradeOfflineSchema(req.result, (event as IDBVersionChangeEvent).oldVersion ?? 0);
      };

      req.onsuccess = () => {
        const db = req.result;
        // Another context is upgrading the schema: release our handle so it can
        // finish, and let the next call reopen on the new version.
        db.onversionchange = () => {
          db.close();
          openPromise = null;
        };
        finish(db);
      };

      req.onerror = () => finish(null);
      req.onblocked = () => {
        openPromise = null;
        finish(null);
      };
    } catch {
      openPromise = null;
      finish(null);
    }
  });

  return openPromise;
}

/** Test seam: forget the cached connection so a fresh factory can be picked up. */
export function resetOfflineDbConnection(): void {
  openPromise = null;
}

/**
 * Runs `work` inside one transaction over the named stores.
 *
 * Every store the work touches must be listed here: a transaction's scope is fixed at
 * creation, and `tx.objectStore(name)` throws `NotFoundError` for anything outside it.
 * That trap is why this takes a list rather than a single name — the row and its
 * bookkeeping write are one atomic step (see `replaceCollection`), so both stores have
 * to be in scope together.
 */
async function withTransaction<T>(
  storeNames: string[],
  mode: IDBTransactionMode,
  work: (tx: IDBTransaction) => Promise<T>,
): Promise<T | null> {
  const db = await getOfflineDb();
  if (!db) return null;

  try {
    const tx = db.transaction(storeNames, mode);
    const result = await work(tx);
    await transactionDone(tx);
    return result;
  } catch (error) {
    // A failed write must not break the page that triggered it — but it must not vanish
    // either: a silently dead cache is invisible until someone is offline and needs it.
    // Development builds say so out loud; production keeps the user's page working.
    if (import.meta.env?.DEV) {
      console.warn('[offline] IndexedDB operation failed; continuing without the cache', error);
    }
    return null;
  }
}

export async function putRecords<T>(records: CachedRecord<T>[]): Promise<void> {
  if (records.length === 0) return;
  await withTransaction([CACHE_STORE], 'readwrite', async (tx) => {
    const store = tx.objectStore(CACHE_STORE);
    for (const record of records) store.put(record);
  });
}

/**
 * Replaces a whole collection in one transaction, so a refresh can never leave a
 * half-updated list behind for the UI to render.
 */
export async function replaceCollection<T>(
  scope: OfflineScope,
  collection: string,
  rows: { id: string; data: T }[],
  options: { fetchedAt?: string; cursor?: string | null } = {},
): Promise<CollectionMeta | null> {
  const fetchedAt = options.fetchedAt ?? new Date().toISOString();

  return withTransaction<CollectionMeta>([CACHE_STORE, META_STORE], 'readwrite', async (tx) => {
    const store = tx.objectStore(CACHE_STORE);
    await deleteCollectionInStore(store, scope, collection);

    for (const row of rows) {
      store.put({
        accountId: scope.accountId,
        farmId: scope.farmId,
        collection,
        id: row.id,
        data: row.data,
        fetchedAt,
      } satisfies CachedRecord<T>);
    }

    const meta: CollectionMeta = {
      accountId: scope.accountId,
      farmId: scope.farmId,
      collection,
      lastSyncedAt: fetchedAt,
      recordCount: rows.length,
      cursor: options.cursor ?? null,
    };

    // Same transaction as the rows: the count and the rows it describes can never
    // disagree, even if the app is killed mid-refresh.
    const metaStore = tx.objectStore(META_STORE);
    metaStore.put(meta);

    return meta;
  });
}

/**
 * Applies a delta to a stored collection: upsert what changed, drop what the server says is
 * gone, leave everything else exactly as it is.
 *
 * <para>
 * The difference from `replaceCollection` is the whole point of a delta: a device that has
 * been offline for a week should send back a handful of changed rows rather than the entire
 * collection, and the rows it keeps must survive untouched — including rows the delta does not
 * mention, which is every row that did not change.
 * </para>
 *
 * <para>
 * The deletions are applied before the cursor is stored, in one transaction: a tombstone that
 * arrived but was not applied because the app died between two writes would be lost forever,
 * since the next delta starts from the cursor that would already have passed it.
 * </para>
 */
export async function mergeCollection<T>(
  scope: OfflineScope,
  collection: string,
  delta: { rows: { id: string; data: T }[]; deletedIds: string[]; cursor: string },
  options: { fetchedAt?: string } = {},
): Promise<CollectionMeta | null> {
  const fetchedAt = options.fetchedAt ?? new Date().toISOString();

  return withTransaction<CollectionMeta>([CACHE_STORE, META_STORE], 'readwrite', async (tx) => {
    const store = tx.objectStore(CACHE_STORE);

    for (const id of delta.deletedIds) {
      store.delete([scope.accountId, scope.farmId, collection, id]);
    }

    for (const row of delta.rows) {
      store.put({
        accountId: scope.accountId,
        farmId: scope.farmId,
        collection,
        id: row.id,
        data: row.data,
        fetchedAt,
      } satisfies CachedRecord<T>);
    }

    // Read inside the same transaction so the count describes the rows this merge produced,
    // not the rows some concurrent write left behind.
    const recordCount = await request<number>(
      store.index('byScope').count([scope.accountId, scope.farmId, collection]),
    );

    const meta: CollectionMeta = {
      accountId: scope.accountId,
      farmId: scope.farmId,
      collection,
      lastSyncedAt: fetchedAt,
      recordCount,
      cursor: delta.cursor,
    };

    tx.objectStore(META_STORE).put(meta);

    return meta;
  });
}

export async function getRecord<T>(
  scope: OfflineScope,
  collection: string,
  id: string,
): Promise<CachedRecord<T> | null> {
  const result = await withTransaction<CachedRecord<T> | undefined>([CACHE_STORE], 'readonly', (tx) =>
    request<CachedRecord<T> | undefined>(
      tx.objectStore(CACHE_STORE).get([scope.accountId, scope.farmId, collection, id]) as IDBRequest<
        CachedRecord<T> | undefined
      >,
    ),
  );
  return result ?? null;
}

export async function getCollection<T>(
  scope: OfflineScope,
  collection: string,
): Promise<CachedRecord<T>[]> {
  const result = await withTransaction<CachedRecord<T>[]>([CACHE_STORE], 'readonly', (tx) =>
    request<CachedRecord<T>[]>(
      tx.objectStore(CACHE_STORE).index('byScope').getAll([
        scope.accountId,
        scope.farmId,
        collection,
      ]) as IDBRequest<CachedRecord<T>[]>,
    ),
  );
  return result ?? [];
}

export async function deleteRecord(
  scope: OfflineScope,
  collection: string,
  id: string,
): Promise<void> {
  await withTransaction([CACHE_STORE], 'readwrite', async (tx) => {
    tx.objectStore(CACHE_STORE).delete([scope.accountId, scope.farmId, collection, id]);
  });
}

export async function getMeta(
  scope: OfflineScope,
  collection: string,
): Promise<CollectionMeta | null> {
  const result = await withTransaction<CollectionMeta | undefined>([META_STORE], 'readonly', (tx) =>
    request<CollectionMeta | undefined>(
      tx.objectStore(META_STORE).get([scope.accountId, scope.farmId, collection]) as IDBRequest<
        CollectionMeta | undefined
      >,
    ),
  );
  return result ?? null;
}

/** Every collection's bookkeeping for a scope — what the banner's freshness label reads. */
export async function getScopeMeta(scope: OfflineScope): Promise<CollectionMeta[]> {
  const result = await withTransaction<CollectionMeta[]>([META_STORE], 'readonly', (tx) =>
    request<CollectionMeta[]>(
      tx.objectStore(META_STORE).index('byAccountFarm').getAll([
        scope.accountId,
        scope.farmId,
      ]) as IDBRequest<CollectionMeta[]>,
    ),
  );
  return result ?? [];
}

export async function getStats(): Promise<OfflineStorageStats> {
  const result = await withTransaction<{ records: number; metas: CollectionMeta[] }>(
    [CACHE_STORE, META_STORE],
    'readonly',
    async (tx) => {
      const metas = await request<CollectionMeta[]>(
        tx.objectStore(META_STORE).getAll() as IDBRequest<CollectionMeta[]>,
      );
      const records = await request<number>(tx.objectStore(CACHE_STORE).count());
      return { records, metas };
    },
  );

  if (!result) return { recordCount: 0, collectionCount: 0, lastSyncedAt: null };

  const lastSyncedAt = result.metas.reduce<string | null>(
    (latest, meta) => (!latest || meta.lastSyncedAt > latest ? meta.lastSyncedAt : latest),
    null,
  );

  return {
    recordCount: result.records,
    collectionCount: result.metas.length,
    lastSyncedAt,
  };
}

async function deleteCollectionInStore(
  store: IDBObjectStore,
  scope: OfflineScope,
  collection: string,
): Promise<void> {
  const keys = await request<IDBValidKey[]>(
    store.index('byScope').getAllKeys([scope.accountId, scope.farmId, collection]),
  );
  for (const key of keys) store.delete(key);
}

// ── the session marker ──────────────────────────────────────

/** When this account last reached the API, or null if it never has on this device. */
export async function getSessionMarker(accountId: string): Promise<SessionMarker | null> {
  const result = await withTransaction<SessionMarker | undefined>(
    [SESSION_STORE],
    'readonly',
    (tx) =>
      request<SessionMarker | undefined>(
        tx.objectStore(SESSION_STORE).get([accountId]) as IDBRequest<SessionMarker | undefined>,
      ),
  );
  return result ?? null;
}

/**
 * Records that this account just reached the API.
 *
 * Monotonic: an older timestamp never replaces a newer one, so an out-of-order write (two
 * tabs, a queued write landing after a fresh one) cannot move the offline-write window
 * backwards and let a stale session keep accepting work.
 */
export async function touchSessionMarker(
  accountId: string,
  at: string = new Date().toISOString(),
): Promise<SessionMarker | null> {
  return withTransaction<SessionMarker>([SESSION_STORE], 'readwrite', async (tx) => {
    const store = tx.objectStore(SESSION_STORE);
    const existing = await request<SessionMarker | undefined>(
      store.get([accountId]) as IDBRequest<SessionMarker | undefined>,
    );

    if (existing && existing.lastServerContactAt >= at) return existing;

    const marker: SessionMarker = { accountId, lastServerContactAt: at };
    store.put(marker);
    return marker;
  });
}

export async function deleteCollection(scope: OfflineScope, collection: string): Promise<void> {
  await withTransaction([CACHE_STORE, META_STORE], 'readwrite', async (tx) => {
    await deleteCollectionInStore(tx.objectStore(CACHE_STORE), scope, collection);
    tx.objectStore(META_STORE).delete([scope.accountId, scope.farmId, collection]);
  });
}

/**
 * Drops one farm's cached data, leaving other farms (and the account's own entries)
 * intact. This is what runs when the API says membership to a farm is gone: the
 * device should not keep serving rows it can no longer authorise.
 *
 * Implemented by walking the store and filtering on the scope rather than by key
 * range: the store is deliberately small (cache-by-use, never a mirror), and an
 * explicit comparison cannot get the bounds wrong the way a constructed
 * `IDBKeyRange.bound` over a four-part compound key can.
 */
export async function clearFarm(scope: OfflineScope): Promise<void> {
  await withTransaction([CACHE_STORE, META_STORE], 'readwrite', async (tx) => {
    await deleteWhere(tx.objectStore(CACHE_STORE), (record) =>
      record.accountId === scope.accountId && record.farmId === scope.farmId);
    await deleteWhere(tx.objectStore(META_STORE), (meta) =>
      meta.accountId === scope.accountId && meta.farmId === scope.farmId);
  });
}

/**
 * Drops everything belonging to one account, leaving other accounts on the device alone.
 *
 * Cache only, deliberately: the read cache is disposable, a queued item is not. The queue
 * becomes flushable again when that account signs back in.
 */
export async function clearAccount(accountId: string): Promise<void> {
  await withTransaction([CACHE_STORE, META_STORE], 'readwrite', async (tx) => {
    await deleteWhere(tx.objectStore(CACHE_STORE), (record) => record.accountId === accountId);
    await deleteWhere(tx.objectStore(META_STORE), (meta) => meta.accountId === accountId);
  });
}

/**
 * Empties the read cache (rows and their bookkeeping). This is what sign-out does: the
 * next user of the device must not be able to read the previous user's farm data out of
 * the cache.
 *
 * The session marker is *not* cleared: it says how long the account has been without the
 * API, and clearing it would make "clear offline data" a way around the offline-write
 * window rather than a way to reclaim space.
 *
 * The write queue is **not** touched — a measurement that exists only on this device is
 * not the previous user's data to destroy, and a later session's flush can still deliver
 * it (the server re-checks farm membership at sync time). 4.5.4's sign-out warning is what
 * tells the user how many items are still waiting, so this is never silent.
 */
export async function clearCachedData(): Promise<void> {
  await withTransaction([CACHE_STORE, META_STORE], 'readwrite', async (tx) => {
    await request(tx.objectStore(CACHE_STORE).clear());
    await request(tx.objectStore(META_STORE).clear());
  });
}

/**
 * Drops the write queue. Only the user-facing "clear offline data" action does this, and
 * only after a confirmation that names how many unsynced records it discards.
 */
export async function clearQueue(): Promise<void> {
  await withTransaction([QUEUE_STORE], 'readwrite', async (tx) => {
    await request(tx.objectStore(QUEUE_STORE).clear());
  });
}

// ── the write queue ─────────────────────────────────────────

function outboxKey(scope: OfflineScope, mutationId: string): IDBValidKey {
  return [scope.accountId, scope.farmId, mutationId];
}

/** Adds one queued mutation. False means storage is unavailable and nothing was queued. */
export async function putOutboxItem(item: OutboxItem): Promise<boolean> {
  const result = await withTransaction<boolean>([QUEUE_STORE], 'readwrite', async (tx) => {
    tx.objectStore(QUEUE_STORE).put(item);
    return true;
  });
  return result === true;
}

export async function getOutboxItem<TPayload>(
  scope: OfflineScope,
  mutationId: string,
): Promise<OutboxItem<TPayload> | null> {
  const result = await withTransaction<OutboxItem<TPayload> | undefined>(
    [QUEUE_STORE],
    'readonly',
    (tx) =>
      request<OutboxItem<TPayload> | undefined>(
        tx.objectStore(QUEUE_STORE).get(outboxKey(scope, mutationId)) as IDBRequest<
          OutboxItem<TPayload> | undefined
        >,
      ),
  );
  return result ?? null;
}

/** One farm's queue, oldest first. */
export async function listOutboxForScope<TPayload>(
  scope: OfflineScope,
): Promise<OutboxItem<TPayload>[]> {
  const result = await withTransaction<OutboxItem<TPayload>[]>([QUEUE_STORE], 'readonly', (tx) =>
    request<OutboxItem<TPayload>[]>(
      tx.objectStore(QUEUE_STORE).index('byScope').getAll([
        scope.accountId,
        scope.farmId,
      ]) as IDBRequest<OutboxItem<TPayload>[]>,
    ),
  );
  return sortByQueuedAt(result ?? []);
}

/** Every queued item for one account, oldest first, whatever its status. */
export async function listOutboxForAccount<TPayload>(
  accountId: string,
): Promise<OutboxItem<TPayload>[]> {
  const result = await withTransaction<OutboxItem<TPayload>[]>([QUEUE_STORE], 'readonly', (tx) =>
    request<OutboxItem<TPayload>[]>(
      tx.objectStore(QUEUE_STORE).getAll() as IDBRequest<OutboxItem<TPayload>[]>,
    ),
  );
  return sortByQueuedAt((result ?? []).filter((item) => item.accountId === accountId));
}

/**
 * Everything queued for one account in one status, across every farm, oldest first.
 *
 * The exact compound key keeps this index lookup free of the range-bound subtleties an
 * "every status for this account" query would need, which is why the flush asks for
 * `pending` rather than filtering a wider read afterwards.
 */
export async function listOutboxByStatus<TPayload>(
  accountId: string,
  status: OutboxStatus,
): Promise<OutboxItem<TPayload>[]> {
  const result = await withTransaction<OutboxItem<TPayload>[]>([QUEUE_STORE], 'readonly', (tx) =>
    request<OutboxItem<TPayload>[]>(
      tx.objectStore(QUEUE_STORE).index('byAccountStatus').getAll([
        accountId,
        status,
      ]) as IDBRequest<OutboxItem<TPayload>[]>,
    ),
  );
  return sortByQueuedAt(result ?? []);
}

/**
 * Rewrites only the *outcome* half of a queued item, inside one transaction.
 *
 * The read and the write share a transaction so two passes cannot interleave into a lost
 * update, and the immutable half is copied from the stored record without being passed to
 * the caller. That is the append-only rule enforced by construction: `payload`, `kind`,
 * `targetId`, `occurredAt` and `queuedAt` are unreachable after enqueue, and they are
 * exactly what the server's idempotency guarantee is anchored to.
 */
export async function updateOutboxItem<TPayload>(
  scope: OfflineScope,
  mutationId: string,
  update: (item: OutboxItem<TPayload>) => Pick<
    OutboxItem<TPayload>,
    | 'status'
    | 'attempts'
    | 'lastError'
    | 'serverMessage'
    | 'appliedAt'
    | 'appliedEntityId'
    | 'dismissedReason'
  >,
): Promise<OutboxItem<TPayload> | null> {
  const result = await withTransaction<OutboxItem<TPayload> | null>(
    [QUEUE_STORE],
    'readwrite',
    async (tx) => {
      const store = tx.objectStore(QUEUE_STORE);
      const key = outboxKey(scope, mutationId);
      const existing = await request<OutboxItem<TPayload> | undefined>(
        store.get(key) as IDBRequest<OutboxItem<TPayload> | undefined>,
      );
      if (!existing) return null;

      const updated: OutboxItem<TPayload> = { ...existing, ...update(existing) };
      store.put(updated);
      return updated;
    },
  );
  return result ?? null;
}

export async function deleteOutboxItem(scope: OfflineScope, mutationId: string): Promise<void> {
  await withTransaction([QUEUE_STORE], 'readwrite', async (tx) => {
    tx.objectStore(QUEUE_STORE).delete(outboxKey(scope, mutationId));
  });
}

/**
 * What the badge and the sync screen read. Counted by walking the store rather than by
 * four index queries: the queue is hundreds of small records, not a mirror of a table, and
 * one pass cannot disagree with itself the way four independent counts can.
 */
export async function getOutboxCounts(accountId: string): Promise<OutboxCounts> {
  const items = await listOutboxForAccount(accountId);
  const pending = items.filter((item) => item.status === 'pending');

  return {
    pending: pending.length,
    quarantined: items.filter((item) => item.status === 'quarantined').length,
    dismissed: items.filter((item) => item.status === 'dismissed').length,
    applied: items.filter((item) => item.status === 'applied').length,
    oldestQueuedAt: pending.length > 0 ? pending[0].queuedAt : null,
  };
}

/**
 * Drops applied items recorded before `cutoff`.
 *
 * Applied items are kept for a day rather than deleted the instant the server accepts
 * them, so the sync screen can show what just went through and a page can swap its
 * optimistic row for the server's. After that they are cruft — the row itself lives on the
 * server — and are reaped.
 */
export async function purgeAppliedBefore(cutoffIso: string): Promise<number> {
  const result = await withTransaction<number>([QUEUE_STORE], 'readwrite', async (tx) => {
    const store = tx.objectStore(QUEUE_STORE);
    const all = await request<OutboxItem[]>(store.getAll() as IDBRequest<OutboxItem[]>);
    let removed = 0;
    for (const item of all) {
      if (item.status === 'applied' && item.appliedAt && item.appliedAt < cutoffIso) {
        store.delete([item.accountId, item.farmId, item.mutationId]);
        removed += 1;
      }
    }
    return removed;
  });
  return result ?? 0;
}

function sortByQueuedAt<TPayload>(items: OutboxItem<TPayload>[]): OutboxItem<TPayload>[] {
  // `queuedAt` is an ISO string, so lexicographic order is chronological order. The
  // mutation id breaks ties so two items queued in the same millisecond still flush in a
  // stable order across passes — a device must not reorder its own history between tries.
  return [...items].sort((a, b) =>
    a.queuedAt === b.queuedAt
      ? a.mutationId.localeCompare(b.mutationId)
      : a.queuedAt.localeCompare(b.queuedAt),
  );
}

async function deleteWhere(
  store: IDBObjectStore,
  predicate: (record: { accountId: string; farmId: string }) => boolean,
): Promise<void> {
  const keys = await request<IDBValidKey[]>(store.getAllKeys());
  const values = await request<Array<{ accountId: string; farmId: string }>>(
    store.getAll() as IDBRequest<Array<{ accountId: string; farmId: string }>>,
  );

  for (let index = 0; index < values.length; index++) {
    if (predicate(values[index])) store.delete(keys[index]);
  }
}
