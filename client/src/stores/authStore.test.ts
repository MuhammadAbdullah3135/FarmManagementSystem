import { readFileSync } from 'node:fs';
import path from 'node:path';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import api from '../api/axios';
import type { AuthResponse } from '../types';

vi.mock('../api/axios', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}));

import { useAuthStore } from './authStore';

const post = vi.mocked(api.post);

const responseWith = (roles: string[]): AuthResponse => ({
  accessToken: 'access',
  refreshToken: 'refresh',
  userId: 'user-1',
  accountId: 'account-1',
  email: 'owner@example.com',
  firstName: 'Owner',
  lastName: 'Person',
  roles,
});

describe('authStore roles', () => {
  beforeEach(() => {
    post.mockReset();
    localStorage.clear();
    useAuthStore.setState({ user: null, isAuthenticated: false, isLoading: false, error: null });
  });

  it('stores the roles returned by the login response', async () => {
    post.mockResolvedValue({ data: responseWith(['Viewer']) });

    await useAuthStore.getState().login({ email: 'owner@example.com', password: 'secret' });

    expect(useAuthStore.getState().user?.roles).toEqual(['Viewer']);
  });

  it('stores the roles returned by the register response', async () => {
    post.mockResolvedValue({ data: responseWith(['SystemOwner']) });

    await useAuthStore.getState().register({
      email: 'owner@example.com',
      password: 'secret',
      firstName: 'Owner',
      lastName: 'Person',
      accountName: 'My Farm',
    });

    expect(useAuthStore.getState().user?.roles).toEqual(['SystemOwner']);
  });

  it('falls back to no roles when the response omits them', async () => {
    const { roles: _omitted, ...withoutRoles } = responseWith([]);
    post.mockResolvedValue({ data: withoutRoles });

    await useAuthStore.getState().login({ email: 'owner@example.com', password: 'secret' });

    expect(useAuthStore.getState().user?.roles).toEqual([]);
  });
});

/*
 * The roles used to be hardcoded in the auth store ([] on login, ['SystemOwner']
 * on register) while the API had the real list. This guard fails if either
 * literal comes back, because a behavioural test can only catch the paths it
 * exercises.
 */
describe('authStore source', () => {
  it('no longer hardcodes roles in the auth flow', () => {
    const source = readFileSync(path.resolve(process.cwd(), 'src/stores/authStore.ts'), 'utf8');

    expect(source).not.toMatch(/roles:\s*\[\s*\]/);
    expect(source).not.toMatch(/roles:\s*\[\s*['"]SystemOwner['"]\s*\]/);
  });
});
