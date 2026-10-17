import api from './axios';
import type { DemoLoginResponse } from '../types';

/** Credential-free demo sign-in. */
export function demoLogin() {
  return api.post<DemoLoginResponse>('/auth/demo-login', {});
}

/** Restores the shared demo data to its seed state. Only reachable by the demo user (SystemOwner of the demo farm). */
export function resetDemo() {
  return api.post('/auth/demo-reset');
}
