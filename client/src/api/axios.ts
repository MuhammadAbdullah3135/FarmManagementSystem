import axios from 'axios';

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
      return accessToken as string;
    })().finally(() => {
      refreshInFlight = null;
    });
  }

  return refreshInFlight;
};

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

  const farmId = localStorage.getItem('activeFarmId');
  if (farmId) {
    config.headers['X-Farm-Id'] = farmId;
  }

  return config;
});

api.interceptors.response.use(
  (response) => response,
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
