import { describe, expect, it, vi } from 'vitest';
import { registerOfflineShell } from './registerServiceWorker';

const containerWith = (register: ReturnType<typeof vi.fn>) =>
  ({ register } as unknown as ServiceWorkerContainer);

describe('registerOfflineShell', () => {
  it('does nothing when offline support is not enabled', async () => {
    const register = vi.fn();

    const result = await registerOfflineShell({
      enabled: false,
      container: containerWith(register),
      baseUrl: '/FarmManagementSystem/',
    });

    expect(result).toBeNull();
    expect(register).not.toHaveBeenCalled();
  });

  it('does nothing when the environment has no service worker support', async () => {
    const result = await registerOfflineShell({ enabled: true, container: null });

    expect(result).toBeNull();
  });

  it('registers the worker inside the app base path', async () => {
    const registration = { scope: '/FarmManagementSystem/' } as ServiceWorkerRegistration;
    const register = vi.fn().mockResolvedValue(registration);

    const result = await registerOfflineShell({
      enabled: true,
      container: containerWith(register),
      baseUrl: '/FarmManagementSystem/',
    });

    // The scope matters: the worker must not claim the whole origin, and it must sit
    // where the Pages deployment actually serves the app.
    expect(register).toHaveBeenCalledWith('/FarmManagementSystem/sw.js', {
      scope: '/FarmManagementSystem/',
    });
    expect(result).toBe(registration);
  });

  it('resolves to null instead of throwing when registration fails', async () => {
    const register = vi.fn().mockRejectedValue(new Error('insecure context'));

    await expect(
      registerOfflineShell({ enabled: true, container: containerWith(register) }),
    ).resolves.toBeNull();
  });
});
