import axios from 'axios';

declare module 'axios' {
  /**
   * Marks a request as queue traffic (the sync flush). Its success must not be treated as
   * "some other request proved the API is reachable", which would re-trigger the flush that
   * sent it and let a busy queue schedule itself in a loop.
   */
  export interface AxiosRequestConfig {
    syncRequest?: boolean;
  }
}

const API_BASE = import.meta.env.VITE_API_URL ?? '/api';

/**
 * The exact body FarmContextMiddleware returns when the caller is not — or is no
 * longer — a member of the farm they addressed.
 *
 * Matching this precise payload rather than the status code alone is what keeps
 * role-based 403s (e.g. "Only farm owners and managers can …") from being
 * mistaken for a revoked membership, which would wrongly drop the user's farm.
 */
const FARM_ACCESS_DENIED_ERROR = 'Access denied to this farm';

type FarmAccessDeniedHandler = () => void;

let farmAccessDeniedHandler: FarmAccessDeniedHandler | null = null;

/**
 * Registers what happens when the active farm's membership disappears.
 *
 * A registered callback rather than an import of the farm store: the store
 * imports this module, so importing it back would create a cycle. The layout
 * owns the reaction (clear the farm, refetch, notify).
 */
export const setFarmAccessDeniedHandler = (handler: FarmAccessDeniedHandler | null) => {
  farmAccessDeniedHandler = handler;
};

/** True only for the farm-context denial, never for an unrelated 403. */
export const isFarmAccessDenied = (error: unknown): boolean => {
  const response = (error as { response?: { status?: number; data?: unknown } })?.response;
  if (response?.status !== 403) return false;

  const data = response.data as { error?: unknown } | undefined;
  return typeof data?.error === 'string' && data.error === FARM_ACCESS_DENIED_ERROR;
};

/**
 * Why a flush is worth attempting: something just proved the API is reachable.
 *
 * `request-succeeded` is every ordinary successful call — the piggyback that makes "the app
 * has a connection" self-evident without a poller. `token-refreshed` is the session being
 * renewed, which is the moment a queue that had stalled on an expired token becomes
 * sendable again.
 */
export type SyncTriggerReason = 'request-succeeded' | 'token-refreshed';

let syncTriggerHandler: ((reason: SyncTriggerReason) => void) | null = null;

/**
 * Registers what happens when a request succeeded or the session was refreshed.
 *
 * A registered callback rather than an import of the sync engine, for the same reason
 * `setFarmAccessDeniedHandler` exists: the engine imports this module, so importing it back
 * would be a cycle. The engine owns the decision (it may be offline, already flushing, or
 * inside a backoff window).
 */
export const setSyncTriggerHandler = (
  handler: ((reason: SyncTriggerReason) => void) | null,
): void => {
  syncTriggerHandler = handler;
};

const notifySyncTrigger = (reason: SyncTriggerReason) => {
  try {
    syncTriggerHandler?.(reason);
  } catch {
    // A trigger is opportunistic: whatever went wrong there must never fail the request that
    // produced it.
  }
};

const api = axios.create({
  baseURL: API_BASE,
  headers: {
    'Content-Type': 'application/json',
  },
});

/**
 * The in-flight refresh, shared by every request that 401s while it runs.
 *
 * Pages fire several requests at once, so an expired access token used to produce one
 * /auth/refresh per request (six calls for five concurrent requests, measured against the
 * deployed API). That is wasteful, and it turns a strict single-use refresh token into a
 * logout loop. One call, everyone waits on it.
 */
let refreshInFlight: Promise<string> | null = null;

const refreshAccessToken = (): Promise<string> => {
  if (!refreshInFlight) {
    refreshInFlight = (async () => {
      const refreshToken = localStorage.getItem('refreshToken');
      if (!refreshToken) {
        throw new Error('No refresh token stored');
      }

      // Plain axios, not `api`: going through the instance would recurse into this interceptor.
      const response = await axios.post(`${API_BASE}/auth/refresh`, { refreshToken });
      const { accessToken, refreshToken: newRefreshToken } = response.data;
      localStorage.setItem('accessToken', accessToken);
      localStorage.setItem('refreshToken', newRefreshToken);

      // Once, after a successful refresh: a queue that was waiting on an expired access
      // token can go now. This is the only place a refresh announces itself, so an item
      // queued behind one is not stuck until the user happens to open a page.
      notifySyncTrigger('token-refreshed');
      return accessToken as string;
    })().finally(() => {
      refreshInFlight = null;
    });
  }

  return refreshInFlight;
};

/**
 * Renews the access token, sharing the single in-flight refresh with every 401ing request.
 *
 * Exported for the sync engine, which refreshes *proactively* when the token is nearly
 * expired instead of waiting for a 401: a queue that has been offline for a week should not
 * spend its first round trip discovering that its token died overnight.
 */
export const ensureFreshAccessToken = (): Promise<string> => refreshAccessToken();

/** Drops the session and sends the user to the login screen. */
const endSession = () => {
  localStorage.removeItem('accessToken');
  localStorage.removeItem('refreshToken');
  localStorage.removeItem('user');
  localStorage.removeItem('auth-storage');
  localStorage.removeItem('farm-storage');
  localStorage.removeItem('activeFarmId');
  window.location.href = `${import.meta.env.BASE_URL}login`;
};

api.interceptors.request.use((config) => {
  // Never attach auth headers to auth endpoints — stale tokens/farm IDs
  // from a previous session cause 403 "Access denied to this farm" on
  // login/register/reset/refresh before the endpoint can even run.
  const url = config.url ?? '';
  if (url.includes('/auth/')) {
    return config;
  }

  const token = localStorage.getItem('accessToken');
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }

  // A request that names its own farm keeps it. The sync flush walks every farm in the
  // account, so stamping the *active* farm on its batches would be refused by
  // FarmContextMiddleware (header and route must agree), and the user's active farm has no
  // bearing on which farm a queued item belongs to. Every other request still gets the
  // active farm, exactly as before.
  const farmId = localStorage.getItem('activeFarmId');
  if (farmId && !config.headers['X-Farm-Id']) {
    config.headers['X-Farm-Id'] = farmId;
  }

  return config;
});

api.interceptors.response.use(
  (response) => {
    // The reachability signal the queue piggybacks on. Not a poller: it costs nothing extra
    // and it is only ever true when some other part of the app has just talked to the API.
    if (!response.config?.syncRequest) {
      notifySyncTrigger('request-succeeded');
    }
    return response;
  },
  async (error) => {
    const originalRequest = error.config;

    if (error.response?.status === 401 && originalRequest && !originalRequest._retry) {
      originalRequest._retry = true;

      if (localStorage.getItem('refreshToken')) {
        try {
          const accessToken = await refreshAccessToken();
          originalRequest.headers.Authorization = `Bearer ${accessToken}`;
          return api(originalRequest);
        } catch {
          endSession();
        }
      }
    }

    // Farm membership revoked (or otherwise lost) mid-session. Handled once per
    // request so a burst of parallel calls cannot trigger the reaction repeatedly.
    if (
      isFarmAccessDenied(error) &&
      originalRequest &&
      !originalRequest._farmAccessHandled
    ) {
      originalRequest._farmAccessHandled = true;
      farmAccessDeniedHandler?.();
    }

    return Promise.reject(error);
  }
);

export default api;
