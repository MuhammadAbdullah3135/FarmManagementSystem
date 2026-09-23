import api from './axios';
import type { MutationEnvelope } from '../offline/mutationKinds';

/**
 * The sync endpoint: the one server surface queued work is applied through.
 *
 * Two details are load-bearing rather than decorative:
 *
 * - **The farm id is in the path *and* the header.** `FarmContextMiddleware` refuses a
 *   request whose `X-Farm-Id` disagrees with the route, and the request interceptor would
 *   otherwise stamp the *active* farm — so a flush that is working through a farm the user
 *   is not currently viewing would be refused. The header is set explicitly here, per batch.
 * - **`syncRequest: true`** marks it as queue traffic, so its own success cannot re-trigger
 *   the flush that sent it (see the response interceptor in `axios.ts`).
 */

export type SyncMutationOutcome = 'Accepted' | 'Superseded' | 'Rejected';

export interface SyncMutationItemResult {
  index: number;
  clientMutationId: string;
  outcome: SyncMutationOutcome;
  /** The server's own message, verbatim. Null when accepted. */
  message: string | null;
  targetEntityId: string | null;
  result: unknown | null;
}

export interface SyncMutationResult {
  requestedCount: number;
  successCount: number;
  acceptedCount: number;
  supersededCount: number;
  rejectedCount: number;
  items: SyncMutationItemResult[];
  /** The import pipeline's failure shape, one entry per rejected item. */
  failures: { index: number; message: string }[];
}

export const syncApi = {
  applyMutations: (farmId: string, items: MutationEnvelope[]) =>
    api.post<SyncMutationResult>(
      `/farm/${farmId}/sync/mutations`,
      { items },
      { headers: { 'X-Farm-Id': farmId }, syncRequest: true },
    ),
};
