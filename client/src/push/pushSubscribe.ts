/**
 * Subscribing this browser to the server's push channel.
 *
 * <p>
 * Two halves that fail independently, and the module keeps them apart. The *browser* half
 * is permission plus a `PushSubscription`; the *server* half is a row that remembers where
 * to deliver. Both have to happen, in that order, and each can be refused on its own — so
 * every outcome here names which one refused, because "push is on" said while the server
 * holds nothing is the one lie this screen must not tell.
 * </p>
 *
 * <p>
 * Everything the browser owns is behind {@link PushBrowser}, so the whole flow is testable
 * without a service worker, a permission prompt, or a push service. That injection is the
 * same shape `offline/registerServiceWorker` uses, for the same reason.
 * </p>
 *
 * <p>
 * Android's WebView — the shell the farm app ships in — implements neither the Push API nor
 * a notification permission, so this reports `unsupported` there rather than offering a
 * switch that cannot work. The mobile wrapper reaching a phone is a separate transport
 * (FCM, which needs credentials this repository does not hold); it is not a variation of
 * this one.
 * </p>
 */

import type { RegisterPushSubscriptionRequest } from '../api/notifications';

/** What the browser can do, reduced to the questions this feature asks it. */
export interface PushBrowser {
  /** False when the Push API, a service worker, or notifications are missing entirely. */
  isSupported(): boolean;
  permission(): NotificationPermission | 'unsupported';
  requestPermission(): Promise<NotificationPermission>;
  /** The endpoint of this browser's current subscription, or null if there is none. */
  currentEndpoint(): Promise<string | null>;
  /** Subscribes with the deployment's VAPID key, or throws. */
  subscribe(vapidPublicKey: string): Promise<RegisterPushSubscriptionRequest>;
  /** Drops the browser's own subscription. Returns whether one was removed. */
  unsubscribe(): Promise<boolean>;
}

/** What the push card can be showing. The page owns the words for each. */
export type PushState =
  | 'unsupported'
  | 'server-not-configured'
  | 'denied'
  | 'subscribed'
  | 'available';

export type EnablePushOutcome =
  | { status: 'subscribed'; subscriptionId: string }
  | { status: 'unsupported' }
  | { status: 'denied' }
  | { status: 'not-configured' }
  | { status: 'failed'; error: string };

/** The real browser, wrapped so nothing else has to touch `navigator` directly. */
export const browserPush: PushBrowser = {
  isSupported: () =>
    typeof navigator !== 'undefined'
    && 'serviceWorker' in navigator
    && typeof window !== 'undefined'
    && 'PushManager' in window
    && typeof Notification !== 'undefined',

  permission: () => (typeof Notification === 'undefined' ? 'unsupported' : Notification.permission),

  requestPermission: () => Notification.requestPermission(),

  currentEndpoint: async () => {
    const registration = await navigator.serviceWorker.ready;
    const subscription = await registration.pushManager.getSubscription();

    return subscription?.endpoint ?? null;
  },

  subscribe: async (vapidPublicKey) => {
    const registration = await navigator.serviceWorker.ready;

    // The key is base64url on the wire and bytes to the browser: `applicationServerKey`
    // takes the raw point, and it is what binds this subscription to our VAPID identity
    // (a subscription made with another key is one our pushes would be rejected for).
    const subscription = await registration.pushManager.subscribe({
      userVisibleOnly: true,
      applicationServerKey: base64UrlToBytes(vapidPublicKey),
    });

    return toRequest(subscription.toJSON());
  },

  unsubscribe: async () => {
    const registration = await navigator.serviceWorker.ready;
    const subscription = await registration.pushManager.getSubscription();

    return subscription ? subscription.unsubscribe() : false;
  },
};

/** Merges test doubles over the real browser. */
export const pushBrowserWith = (overrides: Partial<PushBrowser> = {}): PushBrowser => ({
  ...browserPush,
  ...overrides,
});

/**
 * Which state the card is in.
 *
 * Ordered deliberately: a browser that cannot push says so before anything about this
 * deployment matters, and a deployment that cannot push says so before the permission —
 * because granting it would achieve nothing, and asking for it anyway is the mistake this
 * ordering prevents.
 */
export function pushState(input: {
  vapidPublicKey: string | null;
  permission: NotificationPermission | 'unsupported';
  endpoint: string | null;
}): PushState {
  if (input.permission === 'unsupported') return 'unsupported';
  if (!input.vapidPublicKey) return 'server-not-configured';
  if (input.permission === 'denied') return 'denied';
  if (input.endpoint) return 'subscribed';

  return 'available';
}

/**
 * Turns push on for this browser: permission, then a subscription, then the server row.
 *
 * The order matters in one direction only — a subscription registered before the server
 * knows about it would deliver nothing and look subscribed — and each refusal is reported
 * as itself, so the page can say "your browser blocked it" and "this server has no push
 * keys" differently. The last step failing leaves the browser subscribed and the server
 * unaware; that is reported as a failure rather than as success, and the next attempt
 * refreshes the same endpoint.
 */
export async function enablePush(options: {
  vapidPublicKey: string | null;
  register: (body: RegisterPushSubscriptionRequest) => Promise<{ id: string }>;
  browser?: Partial<PushBrowser>;
  deviceLabel?: string;
}): Promise<EnablePushOutcome> {
  const browser = pushBrowserWith(options.browser);

  if (!browser.isSupported()) return { status: 'unsupported' };
  if (!options.vapidPublicKey) return { status: 'not-configured' };

  let permission = browser.permission();
  if (permission === 'default') {
    permission = await browser.requestPermission();
  }

  if (permission !== 'granted') return { status: 'denied' };

  try {
    const subscription = await browser.subscribe(options.vapidPublicKey);

    const registered = await options.register({
      ...subscription,
      deviceLabel: options.deviceLabel ?? deviceLabelFrom(
        typeof navigator === 'undefined' ? '' : navigator.userAgent,
      ),
    });

    return { status: 'subscribed', subscriptionId: registered.id };
  } catch (error) {
    return { status: 'failed', error: error instanceof Error ? error.message : String(error) };
  }
}

/**
 * Turns push off: the server's rows first, then the browser's subscription.
 *
 * That order is the opposite of enabling, and deliberately. If the local unsubscribe
 * succeeded but the server still held the row, every later dispatch would spend a request on
 * an endpoint the browser has discarded; the reverse leaves a subscription the browser will
 * refuse to duplicate and which the next enable re-registers.
 */
export async function disablePush(options: {
  subscriptionIds: string[];
  unregister: (id: string) => Promise<unknown>;
  browser?: Partial<PushBrowser>;
}): Promise<{ removed: number; stoppedLocally: boolean }> {
  let removed = 0;

  for (const id of options.subscriptionIds) {
    try {
      await options.unregister(id);
      removed += 1;
    } catch {
      // Reported through the count rather than thrown: one stale row must not stop the rest,
      // and the browser's own subscription is still dropped below.
    }
  }

  const browser = pushBrowserWith(options.browser);

  let stoppedLocally = false;
  if (browser.isSupported()) {
    try {
      stoppedLocally = await browser.unsubscribe();
    } catch {
      stoppedLocally = false;
    }
  }

  return { removed, stoppedLocally };
}

/**
 * A short label for the device list, derived from the user agent.
 *
 * Deliberately crude and deliberately short: the user sees it in a list of their own
 * devices, where "Chrome on Android" is the whole of what they need and a full user-agent
 * string is a fingerprint they did not ask to publish. The server owns the column's length;
 * nothing here pretends to be a parser.
 */
export function deviceLabelFrom(userAgent: string): string {
  const ua = userAgent || '';

  const browser = /Edg\//.test(ua) ? 'Edge'
    : /OPR\//.test(ua) ? 'Opera'
    : /Firefox\//.test(ua) ? 'Firefox'
    : /Chrome\//.test(ua) ? 'Chrome'
    : /Safari\//.test(ua) ? 'Safari'
    : 'Browser';

  const platform = /Android/.test(ua) ? 'Android'
    : /iPhone|iPad|iPod/.test(ua) ? 'iOS'
    : /Windows/.test(ua) ? 'Windows'
    : /Mac OS X/.test(ua) ? 'macOS'
    : /Linux/.test(ua) ? 'Linux'
    : '';

  return platform ? `${browser} on ${platform}` : browser;
}

/**
 * base64url → the bytes `applicationServerKey` expects.
 *
 * Padding is tolerated because it is optional in the alphabet and browsers differ; a value
 * that does not decode throws, which is the honest outcome for a key the server sent wrong.
 */
export function base64UrlToBytes(value: string): Uint8Array<ArrayBuffer> {
  const normalized = value.trim().replace(/-/g, '+').replace(/_/g, '/');
  const padded = normalized + '='.repeat((4 - (normalized.length % 4)) % 4);
  const binary = atob(padded);

  // Over its own ArrayBuffer rather than a sized view, so the result is a `BufferSource` that
  // `applicationServerKey` (and Web Crypto) will accept without a cast.
  const bytes = new Uint8Array(new ArrayBuffer(binary.length));
  for (let index = 0; index < binary.length; index++) {
    bytes[index] = binary.charCodeAt(index);
  }

  return bytes;
}

/** The JSON a registration request carries, from whatever the browser's subscription exposes. */
function toRequest(json: PushSubscriptionJSON): RegisterPushSubscriptionRequest {
  const p256dh = json.keys?.p256dh;
  const auth = json.keys?.auth;

  if (!json.endpoint || !p256dh || !auth) {
    throw new Error('The browser returned a subscription without its endpoint or keys.');
  }

  return { endpoint: json.endpoint, p256dh, auth };
}
