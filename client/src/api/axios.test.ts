import { beforeEach, describe, expect, it, vi } from 'vitest';
import axios from 'axios';
import api, {
  isFarmAccessDenied,
  setFarmAccessDeniedHandler,
  setSyncTriggerHandler,
} from './axios';

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
