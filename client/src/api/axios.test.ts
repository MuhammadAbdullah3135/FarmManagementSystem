import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import axios from 'axios';
import api, {
  isFarmAccessDenied,
  setFarmAccessDeniedHandler,
  setSyncTriggerHandler,
} from './axios';

afterEach(() => {
  // Tests that assert the login redirect replace `window` because jsdom's Location
  // properties are non-configurable and cannot be spied on directly.
  vi.unstubAllGlobals();
});

/*
 * The mid-session revocation reaction must be keyed to the farm-context denial
 * body specifically. A role-based 403 ("Only farm owners and managers can …")
 * means the user is still a member and must NOT lose their selected farm.
 */

const failWith = (status: number, data: unknown) => {
  api.defaults.adapter = async (config) => {
    const error = new Error('Request failed') as Error & {
      config?: unknown;
      response?: unknown;
    };
    error.config = config;
    error.response = { status, data, config, headers: {}, statusText: '' };
    throw error;
  };
};

/** Replaces `window` just far enough to observe `location.assign`; jsdom's is non-configurable. */
const stubRedirect = () => {
  const assign = vi.fn();
  const stubbedWindow = Object.create(window);
  Object.defineProperty(stubbedWindow, 'location', {
    value: { assign },
    configurable: true,
  });
  vi.stubGlobal('window', stubbedWindow);
  return assign;
};

describe('isFarmAccessDenied', () => {
  it('recognises the farm-context denial body', () => {
    expect(isFarmAccessDenied({
      response: { status: 403, data: { error: 'Access denied to this farm' } },
    })).toBe(true);
  });

  it('ignores role-based 403s and every other failure', () => {
    expect(isFarmAccessDenied({
      response: { status: 403, data: { error: 'Only farm owners and managers can update farm details' } },
    })).toBe(false);

    // Forbid() carries no body at all.
    expect(isFarmAccessDenied({ response: { status: 403, data: '' } })).toBe(false);
    expect(isFarmAccessDenied({ response: { status: 403, data: { message: 'Access denied to this farm' } } })).toBe(false);
    expect(isFarmAccessDenied({ response: { status: 401, data: { error: 'Access denied to this farm' } } })).toBe(false);
    expect(isFarmAccessDenied({ response: { status: 500 } })).toBe(false);
    expect(isFarmAccessDenied(undefined)).toBe(false);
  });
});

describe('farm-access-denied interceptor', () => {
  beforeEach(() => {
    localStorage.clear();
    setFarmAccessDeniedHandler(null);
  });

  it('invokes the handler once for the farm-context denial', async () => {
    const handler = vi.fn();
    setFarmAccessDeniedHandler(handler);
    failWith(403, { error: 'Access denied to this farm' });

    await expect(api.get('/farm/abc/members')).rejects.toBeTruthy();

    expect(handler).toHaveBeenCalledTimes(1);
  });

  it('does not invoke the handler for a role-based 403', async () => {
    const handler = vi.fn();
    setFarmAccessDeniedHandler(handler);
    failWith(403, { error: 'Only farm owners and managers can remove members' });

    await expect(api.delete('/farm/abc/members/def')).rejects.toBeTruthy();

    expect(handler).not.toHaveBeenCalled();
  });

  it('does not invoke the handler for a bodyless 403 or a 401', async () => {
    const handler = vi.fn();
    setFarmAccessDeniedHandler(handler);

    failWith(403, '');
    await expect(api.post('/farm/abc/invitations')).rejects.toBeTruthy();

    failWith(401, {});
    await expect(api.get('/farm/abc/members')).rejects.toBeTruthy();

    expect(handler).not.toHaveBeenCalled();
  });

  it('does not invoke the handler when none is registered', async () => {
    failWith(403, { error: 'Access denied to this farm' });

    // No handler registered: must reject cleanly rather than throw.
    await expect(api.get('/farm/abc/members')).rejects.toBeTruthy();
  });
});

/**
 * The interceptors' two 4.5.4 responsibilities: a request that names its own farm keeps it
 * (the flush walks farms that are not the active one), and a success tells the sync engine the
 * API is reachable without the engine polling for it.
 */
describe('sync support in the interceptors', () => {
  interface CapturedConfig {
    headers: Record<string, unknown>;
  }

  let captured: CapturedConfig | null = null;

  const captureAdapter = () => {
    api.defaults.adapter = async (config) => {
      captured = { headers: config.headers as unknown as Record<string, unknown> };
      return { data: {}, status: 200, statusText: '', headers: {}, config };
    };
  };

  beforeEach(() => {
    localStorage.clear();
    captured = null;
    setFarmAccessDeniedHandler(null);
    setSyncTriggerHandler(null);
    captureAdapter();
  });

  it('stamps the active farm on an ordinary request', async () => {
    localStorage.setItem('activeFarmId', 'farm-a');

    await api.get('/farm/farm-a/animals');

    expect(captured!.headers['X-Farm-Id']).toBe('farm-a');
  });

  it('leaves a farm the request named itself alone', async () => {
    localStorage.setItem('activeFarmId', 'farm-a');

    // The flush sends a farm the user is not currently viewing; overwriting the header would
    // make FarmContextMiddleware refuse the batch (header and route must agree).
    await api.post('/farm/farm-b/sync/mutations', { items: [] }, { headers: { 'X-Farm-Id': 'farm-b' } });

    expect(captured!.headers['X-Farm-Id']).toBe('farm-b');
  });

  it('tells the sync engine when a request succeeded', async () => {
    const trigger = vi.fn();
    setSyncTriggerHandler(trigger);

    await api.get('/farm/farm-a/animals');

    expect(trigger).toHaveBeenCalledWith('request-succeeded');
  });

  it('does not let queue traffic trigger the flush that sent it', async () => {
    const trigger = vi.fn();
    setSyncTriggerHandler(trigger);

    await api.post('/farm/farm-a/sync/mutations', { items: [] }, { syncRequest: true });

    expect(trigger).not.toHaveBeenCalled();
  });

  it('tells the sync engine once after a successful token refresh', async () => {
    localStorage.setItem('accessToken', 'a1');
    localStorage.setItem('refreshToken', 'r1');
    const trigger = vi.fn();
    setSyncTriggerHandler(trigger);
    const refresh = vi
      .spyOn(axios, 'post')
      .mockResolvedValue({ data: { accessToken: 'a2', refreshToken: 'r2' } } as never);

    let calls = 0;
    api.defaults.adapter = async (config) => {
      calls += 1;
      if (calls === 1) {
        const error = new Error('Request failed') as Error & { config?: unknown; response?: unknown };
        error.config = config;
        error.response = { status: 401, data: '', config, headers: {}, statusText: '' };
        throw error;
      }
      captured = { headers: config.headers as unknown as Record<string, unknown> };
      return { data: {}, status: 200, statusText: '', headers: {}, config };
    };

    await api.get('/farm/farm-a/animals');

    expect(refresh).toHaveBeenCalledWith(expect.stringContaining('/auth/refresh'), { refreshToken: 'r1' });
    expect(trigger).toHaveBeenCalledWith('token-refreshed');
    // The renewed token was used for the retry, which then succeeded.
    expect(captured!.headers.Authorization).toBe('Bearer a2');
    expect(trigger).toHaveBeenCalledWith('request-succeeded');

    refresh.mockRestore();
  });
});

/*
 * Auth endpoints are not all public. The public four must not inherit a stale token, while
 * /auth/me, /auth/demo-reset, and /auth/revoke are authenticated and must send the bearer.
 * Skipping the whole /auth/ prefix turned every profile read and demo reset into a 401 ->
 * refresh -> retry cycle, and a rapid second reset could race refresh-token rotation.
 */
describe('auth token attachment', () => {
  interface CapturedConfig {
    url?: string;
    headers: Record<string, unknown>;
  }

  let captured: CapturedConfig[] = [];

  beforeEach(() => {
    localStorage.clear();
    captured = [];
    api.defaults.adapter = async (config) => {
      captured.push({ url: config.url, headers: config.headers as unknown as Record<string, unknown> });
      return { data: {}, status: 200, statusText: '', headers: {}, config };
    };
  });

  it('skips the bearer token on the public auth endpoints', async () => {
    localStorage.setItem('accessToken', 'a1');
    localStorage.setItem('activeFarmId', 'farm-a');

    await api.post('/auth/login', {});
    await api.post('/auth/register', {});
    await api.post('/auth/refresh', { refreshToken: 'r1' });
    await api.post('/auth/demo-login', {});

    for (const request of captured) {
      expect(request.headers.Authorization).toBeUndefined();
      // A stale farm id must not leak to an endpoint that has nothing to do with a farm.
      expect(request.headers['X-Farm-Id']).toBeUndefined();
    }
  });

  it('sends the bearer token on authenticated auth endpoints', async () => {
    localStorage.setItem('accessToken', 'a1');
    localStorage.setItem('activeFarmId', 'farm-a');

    await api.get('/auth/me');
    await api.post('/auth/demo-reset');
    await api.post('/auth/revoke', { refreshToken: 'r1' });

    for (const request of captured) {
      expect(request.headers.Authorization).toBe('Bearer a1');
      // These are account-scoped, so the active farm must not be stamped on them.
      expect(request.headers['X-Farm-Id']).toBeUndefined();
    }
  });

  it('shares one refresh across concurrent 401s and retries each with the new token', async () => {
    localStorage.setItem('accessToken', 'stale');
    localStorage.setItem('refreshToken', 'r1');

    const retried: string[] = [];
    let attempts = 0;
    api.defaults.adapter = async (config) => {
      const authorization = (config.headers as unknown as Record<string, unknown>).Authorization;
      if (authorization === 'Bearer a2') {
        retried.push(config.url ?? '');
        return { data: {}, status: 200, statusText: '', headers: {}, config };
      }

      attempts += 1;
      const error = new Error('Request failed') as Error & { config?: unknown; response?: unknown };
      error.config = config;
      error.response = { status: 401, data: '', config, headers: {}, statusText: '' };
      throw error;
    };

    let resolveRefresh!: (value: unknown) => void;
    const refreshPromise = new Promise((resolve) => {
      resolveRefresh = resolve;
    });
    const refresh = vi.spyOn(axios, 'post').mockReturnValue(refreshPromise as never);

    const first = api.get('/farm/farm-a/animals');
    const second = api.get('/farm/farm-a/health');

    await vi.waitFor(() => expect(attempts).toBe(2));
    expect(refresh).toHaveBeenCalledTimes(1);

    resolveRefresh({ data: { accessToken: 'a2', refreshToken: 'r2' } });
    await Promise.all([first, second]);

    expect(refresh).toHaveBeenCalledTimes(1);
    expect(retried).toEqual(expect.arrayContaining(['/farm/farm-a/animals', '/farm/farm-a/health']));
    expect(localStorage.getItem('accessToken')).toBe('a2');
    expect(localStorage.getItem('refreshToken')).toBe('r2');

    refresh.mockRestore();
  });

  it('clears the session once when concurrent 401s share a failed refresh', async () => {
    localStorage.setItem('accessToken', 'stale');
    localStorage.setItem('refreshToken', 'r1');
    localStorage.setItem('user', JSON.stringify({ email: 'Test@apk.com' }));

    failWith(401, {});
    const refresh = vi.spyOn(axios, 'post').mockRejectedValue({
      response: { status: 401, data: '', headers: {}, config: {}, statusText: '' },
    });
    const assign = stubRedirect();
    const removeItem = vi.spyOn(Storage.prototype, 'removeItem');

    const first = api.get('/farm/farm-a/animals');
    const second = api.get('/farm/farm-a/health');
    await Promise.allSettled([first, second]);

    // The refresh itself ran once, so the terminal failure cleared the session once.
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(removeItem.mock.calls.filter(([key]) => key === 'accessToken')).toHaveLength(1);
    expect(localStorage.getItem('accessToken')).toBeNull();
    expect(localStorage.getItem('refreshToken')).toBeNull();
    expect(localStorage.getItem('user')).toBeNull();
    expect(assign).toHaveBeenCalledTimes(1);
    expect(assign.mock.calls[0][0]).toContain('login');

    removeItem.mockRestore();
    refresh.mockRestore();
  });
});

/*
 * Backgrounding the Android WebView kills its sockets, so the next call 401s and the
 * refresh POST fails with a network error — no HTTP response at all. A network failure says
 * nothing about whether the refresh token is still valid, so the session must survive; only
 * a server answer ("this token is dead") may end it. Ending it on a network error used to
 * log the user out every time they switched away from the app and back.
 */
describe('401 with a failing refresh call', () => {
  beforeEach(() => {
    localStorage.clear();
    setFarmAccessDeniedHandler(null);
    setSyncTriggerHandler(null);
  });

  it('keeps the session when the refresh call fails with a network error', async () => {
    localStorage.setItem('accessToken', 'stale');
    localStorage.setItem('refreshToken', 'r1');

    failWith(401, {});
    const refresh = vi.spyOn(axios, 'post').mockRejectedValue(new Error('Network Error'));

    await expect(api.get('/farm/farm-a/animals')).rejects.toBeTruthy();

    // The refresh was attempted and failed without a response — and nothing was wiped.
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(refresh).toHaveBeenCalledWith(expect.stringContaining('/auth/refresh'), { refreshToken: 'r1' });
    expect(localStorage.getItem('refreshToken')).toBe('r1');
    expect(localStorage.getItem('accessToken')).toBe('stale');

    refresh.mockRestore();
  });

  it('ends the session when the server rejects the refresh token', async () => {
    localStorage.setItem('accessToken', 'stale');
    localStorage.setItem('refreshToken', 'r1');
    localStorage.setItem('user', JSON.stringify({ email: 'Test@apk.com' }));

    failWith(401, {});
    const refresh = vi.spyOn(axios, 'post').mockRejectedValue({
      response: { status: 401, data: '', headers: {}, config: {}, statusText: '' },
    });
    const assign = stubRedirect();

    await expect(api.get('/farm/farm-a/animals')).rejects.toBeTruthy();

    // A server rejection of the refresh is the one proof the session itself is dead.
    expect(localStorage.getItem('refreshToken')).toBeNull();
    expect(localStorage.getItem('accessToken')).toBeNull();
    expect(localStorage.getItem('user')).toBeNull();
    expect(assign).toHaveBeenCalledTimes(1);

    refresh.mockRestore();
  });
});
