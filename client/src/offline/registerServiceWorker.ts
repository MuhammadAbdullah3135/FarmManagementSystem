/**
 * Registers the offline shell's service worker.
 *
 * Three rules, all of them about not making things worse:
 *
 * - **A build-only concern.** `sw.js` is emitted by `tools/offline/vite-plugin-offline.ts`
 *   during `vite build`; in dev there is nothing to register and a worker would fight
 *   HMR.
 * - **Failure is silent by design.** An app with no service worker still works
 *   perfectly online; an app that throws on boot because a worker could not register
 *   does not. Nothing here rejects.
 * - **No forced reload.** The generated worker calls `skipWaiting` and `clients.claim`,
 *   so it takes control promptly. Because every asset name is content-hashed, a page in
 *   flight keeps fetching the files it already loaded, so there is no reason to
 *   interrupt the user with a reload.
 */

export interface RegisterOfflineShellOptions {
  /** Defaults to a real production build: `sw.js` only exists there. */
  enabled?: boolean;
  /** Defaults to `navigator.serviceWorker`; injected in tests. */
  container?: ServiceWorkerContainer | null;
  /** Defaults to Vite's base URL, so the worker is scoped to the app's path. */
  baseUrl?: string;
}

export async function registerOfflineShell(
  options: RegisterOfflineShellOptions = {},
): Promise<ServiceWorkerRegistration | null> {
  const enabled = options.enabled ?? Boolean(import.meta.env?.PROD);
  if (!enabled) return null;

  const container =
    options.container ?? (typeof navigator !== 'undefined' ? navigator.serviceWorker : undefined);
  if (!container || typeof container.register !== 'function') return null;

  const baseUrl = options.baseUrl ?? import.meta.env?.BASE_URL ?? '/';

  try {
    return await container.register(`${baseUrl}sw.js`, { scope: baseUrl });
  } catch {
    return null;
  }
}
