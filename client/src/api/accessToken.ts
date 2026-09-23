/**
 * When the stored access token expires.
 *
 * The one thing the offline flush needs from the token itself: the expiry is inside the JWT,
 * and the app otherwise never looks — every expired token is discovered the expensive way, by
 * a request 401ing. A queue that has been sitting on a device for a week should not spend its
 * first round trip that way, so the engine checks this first and renews ahead of time.
 *
 * Read-only and best-effort by design: a token that cannot be parsed is treated as "unknown",
 * which means the flush proceeds and lets the 401 path handle it. Failing closed here would
 * turn a decode quirk into a queue that never sends.
 */

/** Refresh this long before expiry, so a flush cannot start on a token about to die. */
export const TOKEN_REFRESH_WINDOW_MS = 2 * 60 * 1000;

/** The `exp` claim of the stored access token, in epoch milliseconds, or null if unknown. */
export function readAccessTokenExpiry(): number | null {
  let token: string | null = null;
  try {
    token = localStorage.getItem('accessToken');
  } catch {
    return null;
  }
  if (!token) return null;

  const payload = token.split('.')[1];
  if (!payload) return null;

  try {
    // JWT payloads are base64url; `atob` needs the standard alphabet and padding.
    const base64 = payload.replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '=');
    const claims = JSON.parse(globalThis.atob(padded)) as { exp?: unknown };
    return typeof claims.exp === 'number' ? claims.exp * 1000 : null;
  } catch {
    return null;
  }
}

/**
 * True when the token expires within `windowMs` (or already has).
 *
 * An unknown expiry answers false: the caller then proceeds and the 401 path catches it, which
 * is the behaviour the app had before this helper existed.
 */
export function isAccessTokenExpiringWithin(windowMs: number): boolean {
  const expiry = readAccessTokenExpiry();
  if (expiry === null) return false;
  return expiry - Date.now() <= windowMs;
}
