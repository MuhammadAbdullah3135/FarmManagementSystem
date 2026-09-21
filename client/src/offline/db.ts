/**
 * The device's offline store (IndexedDB).
 *
 * This is the platform layer for offline work: a versioned database, its schema
 * migration path, and the reads/writes the cached read layer (4.5.2) and the write
 * queue (4.5.4) will both use. Nothing in 4.5.1 reads cached *farm* data yet beyond
 * the banner's counts — wiring pages to it is 4.5.2, and the outbox store arrives in
 * 4.5.4 with a version bump, so the migration step below is exercised for real
 * rather than being decorative.
 *
 * Three rules this module exists to enforce:
 *
 * 1. **Every key carries both `accountId` and `farmId`.** The app supports multiple
 *    farms per account and multiple accounts per device, so "the cached task list"
 *    is never a single global value. Reads always take an explicit scope, which is
 *    what makes it structurally impossible to serve one farm's rows for another.
 * 2. **No credentials are stored here.** Access and refresh tokens stay in
 *    `localStorage`, where the axios layer already reads them; this store holds only
 *    farm data that the API already returned to the signed-in user.
 * 3. **Missing or broken storage degrades to "no cache", never to an error.** Old
 *    Android WebViews, private-browsing modes and jsdom all reach this code, and an
 *    app that works without a cache is strictly better than one that throws.
 */

export const OFFLINE_DB_NAME = 'fms-offline';

/** Bump this and add a step in `upgradeOfflineSchema` when a store changes shape. */
export const OFFLINE_DB_VERSION = 1;

/** Cached API responses, one record per (account, farm, collection, id). */
export const CACHE_STORE = 'cache';

/** Per-collection bookkeeping: when it was last refreshed, and how many rows it holds. */
export const META_STORE = 'syncMeta';

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
}

export interface OfflineStorageStats {
  recordCount: number;
  collectionCount: number;
  /** Most recent `lastSyncedAt` across every collection, or null when nothing is cached. */
  lastSyncedAt: string | null;
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
 * Creates the stores this version needs.
 *
 * Written as explicit per-version steps rather than "create everything, ignore
 * errors": when 4.5.4 adds the outbox, this function gains a `oldVersion < 2` branch
 * and existing devices keep their cached rows instead of starting empty.
 */
export function upgradeOfflineSchema(db: IDBDatabase, oldVersion: number): void {
  if (oldVersion < 1) {
    const cache = db.createObjectStore(CACHE_STORE, {
      keyPath: ['accountId', 'farmId', 'collection', 'id'],
    });
    cache.createIndex('byScope', ['accountId', 'farmId', 'collection'], { unique: false });

    const meta = db.createObjectStore(META_STORE, {
      keyPath: ['accountId', 'farmId', 'collection'],
    });
    meta.createIndex('byAccountFarm', ['accountId', 'farmId'], { unique: false });
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
  options: { fetchedAt?: string } = {},
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
    };

    // Same transaction as the rows: the count and the rows it describes can never
    // disagree, even if the app is killed mid-refresh.
    const metaStore = tx.objectStore(META_STORE);
    metaStore.put(meta);

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

/** Drops everything belonging to one account, leaving other accounts on the device alone. */
export async function clearAccount(accountId: string): Promise<void> {
  await withTransaction([CACHE_STORE, META_STORE], 'readwrite', async (tx) => {
    await deleteWhere(tx.objectStore(CACHE_STORE), (record) => record.accountId === accountId);
    await deleteWhere(tx.objectStore(META_STORE), (meta) => meta.accountId === accountId);
  });
}

/**
 * Empties the store entirely. Called on sign-out: the next user of the device must
 * not be able to read the previous user's farm data out of the cache.
 */
export async function clearAll(): Promise<void> {
  await withTransaction([CACHE_STORE, META_STORE], 'readwrite', async (tx) => {
    await request(tx.objectStore(CACHE_STORE).clear());
    await request(tx.objectStore(META_STORE).clear());
  });
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
