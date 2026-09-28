import { useCallback, useMemo } from 'react';
import { lookupsApi, type AnimalLookupRow } from '../api/attendance';
import { useCachedQuery } from './cachedQuery';
import { useOfflineStore } from './connectivity';
import { animalLabel, resolveScan, type ScanOutcome } from './scanResolve';

/**
 * The farm's animals, served from the device when there is no connection.
 *
 * <para>
 * This is the `animals@lookup` collection 5.4 introduced: the first page of the farm's
 * animals, cached under the (account, farm) scope, and the *only* place a scanned tag is
 * allowed to resolve an identity from. Nothing here invents an animal — the code names an id,
 * and the id either matches a row this device already holds for this farm or it does not.
 * That is what makes scanning safe offline **and** what stops another farm's label from
 * opening somebody else's record: a stranger's id matches no row in this scope, and there is
 * no fallback that would look it up across a farm boundary.
 * </para>
 *
 * <para>
 * The honest limitation, stated here because it decides the wording a user sees: the lookup
 * holds a bounded first page (100 animals, as the form lookup always has). A tag outside that
 * page cannot resolve *offline*. It still opens online — the scan flow verifies an unknown id
 * against the API before giving up — so what the device holds is a fast path, not the
 * authority.
 * </para>
 */
export interface AnimalLookup {
  /** The animal rows this scope holds, from cache first, then from the API when online. */
  rows: AnimalLookupRow[];
  /** When those rows were last read from the API, for the freshness label. */
  lastSyncedAt: string | null;
  /** True while the rows on screen came from the store rather than the network. */
  fromCache: boolean;
  isLoading: boolean;
  /** Offline with nothing stored: the one state a picker or a scan cannot recover from. */
  isEmptyOffline: boolean;
  /** Resolves a scanned code against these rows. */
  scan: (scanned: string | null | undefined) => ScanOutcome;
  /** The row for one animal id, or undefined when this scope does not hold it. */
  findById: (animalId: string | null | undefined) => AnimalLookupRow | undefined;
  /** How one animal is written on screen, or null when it is not held. */
  labelFor: (animalId: string | null | undefined) => string | null;
  refresh: () => void;
}

const sameId = (row: AnimalLookupRow, wanted: string) => row.id.toLowerCase() === wanted;

export interface UseAnimalLookupOptions {
  /**
   * Whether to read the lookup at all. Default true.
   *
   * A view that only needs the rows when something else has already failed — the animal page,
   * whose lookup is its offline fallback — passes false until that happens, so a page that
   * loaded normally does not quietly read a hundred more animals behind it.
   */
  enabled?: boolean;
}

export function useAnimalLookup({ enabled = true }: UseAnimalLookupOptions = {}): AnimalLookup {
  const isOnline = useOfflineStore((state) => state.isOnline);

  const query = useCachedQuery<AnimalLookupRow>({
    collection: 'animals',
    variant: 'lookup',
    enabled,
    fetcher: async () => {
      const response = await lookupsApi.animals();
      return { rows: response.data.items ?? [], total: response.data.totalCount };
    },
    // A failed lookup must not interrupt the page: the cached rows are what a scan needs, and
    // an empty one is reported in words the user can act on.
    onError: () => undefined,
  });

  const { rows, lastSyncedAt, fromCache, isLoading, refresh } = query;

  const findById = useCallback(
    (animalId: string | null | undefined) =>
      animalId ? rows.find((row) => sameId(row, animalId)) : undefined,
    [rows],
  );

  // The whole of "scanning": the pure resolver bound to the rows this scope holds. The
  // parsing grammar and the resolution rule live in `scanResolve.ts` so they can be read and
  // tested without a store, a camera or a network in the room.
  const scan = useCallback(
    (scanned: string | null | undefined): ScanOutcome => resolveScan(scanned, rows),
    [rows],
  );

  const labelFor = useCallback(
    (animalId: string | null | undefined) => {
      const row = findById(animalId);
      return row ? animalLabel(row) : null;
    },
    [findById],
  );

  return useMemo(
    () => ({
      rows,
      lastSyncedAt,
      fromCache,
      isLoading,
      isEmptyOffline: !isOnline && rows.length === 0,
      scan,
      findById,
      labelFor,
      refresh,
    }),
    [rows, lastSyncedAt, fromCache, isLoading, isOnline, scan, findById, labelFor, refresh],
  );
}
