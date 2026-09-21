import { beforeEach, describe, expect, it, vi } from 'vitest';
import api, { isFarmAccessDenied, setFarmAccessDeniedHandler } from './axios';

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
