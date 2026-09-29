import { describe, expect, it, vi } from 'vitest';
import {
  base64UrlToBytes,
  disablePush,
  deviceLabelFrom,
  enablePush,
  pushState,
} from './pushSubscribe';
import type { PushBrowser } from './pushSubscribe';

/**
 * A browser that does the right thing, with anything a test cares about overridable.
 *
 * The real implementation is four calls into `navigator.serviceWorker` and `Notification`;
 * these tests are about the *order* and the *outcomes* — permission before subscription,
 * subscription before registration, and every refusal reported as itself.
 */
const browser = (overrides: Partial<PushBrowser> = {}): PushBrowser => ({
  isSupported: () => true,
  permission: () => 'granted',
  requestPermission: async () => 'granted',
  currentEndpoint: async () => null,
  subscribe: async () => ({
    endpoint: 'https://push.example.net/abc',
    p256dh: 'p256dh-value',
    auth: 'auth-value',
  }),
  unsubscribe: async () => true,
  ...overrides,
});

describe('enablePush', () => {
  it('asks the browser first and the server second, and reports the row it got', async () => {
    const register = vi.fn(async () => ({ id: 'subscription-1' }));
    const subscribe = vi.fn(async () => ({
      endpoint: 'https://push.example.net/abc',
      p256dh: 'p256dh-value',
      auth: 'auth-value',
    }));

    const outcome = await enablePush({
      vapidPublicKey: 'BExampleKey',
      register,
      // Built from the helper rather than a bare override: the real `isSupported()` reads
      // `navigator` and `window`, which is what a browser test double is standing in for.
      browser: browser({ subscribe }),
      deviceLabel: 'Chrome on Android',
    });

    expect(outcome).toEqual({ status: 'subscribed', subscriptionId: 'subscription-1' });
    expect(subscribe).toHaveBeenCalledWith('BExampleKey');

    // The server is told exactly what the browser minted, plus the label it will show back.
    expect(register).toHaveBeenCalledWith({
      endpoint: 'https://push.example.net/abc',
      p256dh: 'p256dh-value',
      auth: 'auth-value',
      deviceLabel: 'Chrome on Android',
    });
  });

  it('prompts for permission only when it has not been decided yet', async () => {
    const requestPermission = vi.fn(async () => 'granted' as NotificationPermission);

    await enablePush({
      vapidPublicKey: 'BExampleKey',
      register: async () => ({ id: 'one' }),
      browser: browser({ permission: () => 'default', requestPermission }),
    });

    await enablePush({
      vapidPublicKey: 'BExampleKey',
      register: async () => ({ id: 'two' }),
      browser: browser({ permission: () => 'granted', requestPermission }),
    });

    expect(requestPermission).toHaveBeenCalledTimes(1);
  });

  it('refuses before touching the browser when the server has no key', async () => {
    const subscribe = vi.fn();

    const outcome = await enablePush({
      vapidPublicKey: null,
      register: async () => ({ id: 'never' }),
      browser: browser({ subscribe }),
    });

    // A permission prompt for a channel that cannot deliver anything is the mistake this
    // order prevents: the browser would show a question whose answer changes nothing.
    expect(outcome).toEqual({ status: 'not-configured' });
    expect(subscribe).not.toHaveBeenCalled();
  });

  it('reports a browser that cannot push rather than failing at it', async () => {
    const outcome = await enablePush({
      vapidPublicKey: 'BExampleKey',
      register: async () => ({ id: 'never' }),
      browser: browser({ isSupported: () => false }),
    });

    // The Android WebView this app ships in is the case that matters here.
    expect(outcome).toEqual({ status: 'unsupported' });
  });

  it('reports a refusal as a refusal, without subscribing', async () => {
    const subscribe = vi.fn();
    const register = vi.fn();

    const outcome = await enablePush({
      vapidPublicKey: 'BExampleKey',
      register,
      browser: browser({ permission: () => 'default', requestPermission: async () => 'denied', subscribe }),
    });

    expect(outcome).toEqual({ status: 'denied' });
    expect(subscribe).not.toHaveBeenCalled();
    expect(register).not.toHaveBeenCalled();
  });

  it('does not claim success when the server end failed', async () => {
    const outcome = await enablePush({
      vapidPublicKey: 'BExampleKey',
      register: async () => {
        throw new Error('Request failed with status code 400');
      },
      browser: browser(),
    });

    // The browser is subscribed at this point, so the failure is real and worth saying: a card
    // reading "on" while the server holds nothing is the one lie this screen must not tell.
    expect(outcome.status).toBe('failed');
    expect(outcome).toMatchObject({ error: expect.stringContaining('400') });
  });
});

describe('disablePush', () => {
  it('clears the server rows and then drops the browser subscription', async () => {
    const order: string[] = [];
    const unregister = vi.fn(async (id: string) => {
      order.push(`server:${id}`);
    });

    const result = await disablePush({
      subscriptionIds: ['one', 'two'],
      unregister,
      browser: browser({
        unsubscribe: async () => {
          order.push('browser');
          return true;
        },
      }),
    });

    expect(result).toEqual({ removed: 2, stoppedLocally: true });

    // Server first, deliberately: the other order leaves rows the browser has discarded, and
    // every later dispatch spends a request on an endpoint that no longer exists.
    expect(order).toEqual(['server:one', 'server:two', 'browser']);
  });

  it('keeps going when one device cannot be removed', async () => {
    const result = await disablePush({
      subscriptionIds: ['one', 'two'],
      unregister: async (id) => {
        if (id === 'one') throw new Error('nope');
      },
      browser: browser(),
    });

    // One stale row must not leave the browser subscribed: the local unsubscribe still ran,
    // and the count is what reports the row that did not go.
    expect(result).toEqual({ removed: 1, stoppedLocally: true });
  });

  it('reports a browser that refuses to unsubscribe', async () => {
    const result = await disablePush({
      subscriptionIds: [],
      unregister: async () => undefined,
      browser: browser({ unsubscribe: async () => false }),
    });

    expect(result).toEqual({ removed: 0, stoppedLocally: false });
  });
});

describe('pushState', () => {
  it('names the browser before the deployment, and the deployment before the permission', () => {
    // A browser that cannot push at all: nothing else matters.
    expect(pushState({ vapidPublicKey: null, permission: 'unsupported', endpoint: null }))
      .toBe('unsupported');

    // Even when the permission was granted: this deployment could not deliver it.
    expect(pushState({ vapidPublicKey: null, permission: 'granted', endpoint: 'https://x/y' }))
      .toBe('server-not-configured');

    expect(pushState({ vapidPublicKey: 'BKey', permission: 'denied', endpoint: null })).toBe('denied');
    expect(pushState({ vapidPublicKey: 'BKey', permission: 'granted', endpoint: 'https://x/y' }))
      .toBe('subscribed');
    expect(pushState({ vapidPublicKey: 'BKey', permission: 'granted', endpoint: null })).toBe('available');
    expect(pushState({ vapidPublicKey: 'BKey', permission: 'default', endpoint: null })).toBe('available');
  });
});

describe('base64UrlToBytes', () => {
  it('decodes the alphabet a VAPID key arrives in', () => {
    // "hello" with the url-safe characters, padded and unpadded.
    const bytes = base64UrlToBytes('aGVsbG8');

    expect(Array.from(bytes)).toEqual([104, 101, 108, 108, 111]);
    expect(Array.from(base64UrlToBytes('aGVsbG8='))).toEqual([104, 101, 108, 108, 111]);
  });

  it('decodes the two characters base64url spells differently', () => {
    // 0xFB 0xFF encodes as "+/8=" in standard base64 and "-_8" here.
    expect(Array.from(base64UrlToBytes('-_8'))).toEqual([0xfb, 0xff]);
  });
});

describe('deviceLabelFrom', () => {
  it('names the browser and the platform the user would recognise', () => {
    expect(deviceLabelFrom(
      'Mozilla/5.0 (Linux; Android 14; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120 Mobile Safari/537.36',
    )).toBe('Chrome on Android');

    expect(deviceLabelFrom(
      'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1',
    )).toBe('Safari on iOS');

    expect(deviceLabelFrom(
      'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120 Safari/537.36 Edg/120',
    )).toBe('Edge on Windows');
  });

  it('says something rather than nothing for a user agent it does not know', () => {
    // A device list row with an empty label is indistinguishable from a broken one.
    expect(deviceLabelFrom('')).toBe('Browser');
    expect(deviceLabelFrom('SomeUnknownAgent/1.0')).toBe('Browser');
  });
});
