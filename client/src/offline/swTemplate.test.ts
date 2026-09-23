import { describe, expect, it } from 'vitest';
import {
  buildPrecacheManifest,
  buildServiceWorker,
  hashManifest,
  normalizeBase,
  selectServiceWorkerVersion,
  SHELL_FILE_NAME,
} from '../../tools/offline/sw-template';

const base = '/FarmManagementSystem/';

const builtAssets = (extra: string[] = []) => [
  'index.html',
  'favicon.svg',
  'assets/index-a1b2c3.js',
  'assets/index-a1b2c3.css',
  ...extra,
];

describe('offline shell build inputs', () => {
  it('precaches the app shell and every hashed asset, under the app base path', () => {
    const manifest = buildPrecacheManifest({ assets: builtAssets(), base });

    expect(manifest).toContain('/FarmManagementSystem/index.html');
    expect(manifest).toContain('/FarmManagementSystem/assets/index-a1b2c3.js');
    expect(manifest).toContain('/FarmManagementSystem/favicon.svg');
  });

  /// A worker that cannot serve the shell installs "successfully" and is then useless
  /// offline — the worst possible outcome, because nothing looks broken until a user is
  /// in a field with no signal. Failing the build is the only honest option.
  it('refuses to build when the app shell is missing', () => {
    expect(() => buildPrecacheManifest({ assets: ['assets/index-a1b2c3.js'], base })).toThrow(
      /index\.html/,
    );
  });

  it('never precaches the worker itself or source maps', () => {
    const manifest = buildPrecacheManifest({
      assets: [...builtAssets(), 'sw.js', 'assets/index-a1b2c3.js.map'],
      base,
    });

    expect(manifest.some((url) => url.endsWith('sw.js'))).toBe(false);
    expect(manifest.some((url) => url.endsWith('.map'))).toBe(false);
  });

  it('lists each file once', () => {
    const manifest = buildPrecacheManifest({
      assets: [...builtAssets(), 'index.html', 'assets/index-a1b2c3.js'],
      base,
    });

    expect(manifest).toHaveLength(new Set(manifest).size);
  });

  it('handles a root base and a bare base alike', () => {
    expect(normalizeBase('/')).toBe('/');
    expect(normalizeBase('')).toBe('/');
    expect(normalizeBase('/FarmManagementSystem')).toBe('/FarmManagementSystem/');
    expect(buildPrecacheManifest({ assets: ['index.html'], base: '/' })).toEqual(['/index.html']);
  });
});

describe('offline shell versioning', () => {
  it('uses the deployment sha when there is one', () => {
    const manifest = buildPrecacheManifest({ assets: builtAssets(), base });

    expect(selectServiceWorkerVersion({ buildSha: 'abc1234', manifest })).toBe('abc1234');
  });

  it('falls back to a manifest hash, so the same build keeps the same cache', () => {
    const manifest = buildPrecacheManifest({ assets: builtAssets(), base });

    const first = selectServiceWorkerVersion({ buildSha: 'dev', manifest });
    const second = selectServiceWorkerVersion({ buildSha: undefined, manifest });

    expect(first).toBe(second);
    expect(first).toMatch(/^manifest-[0-9a-f]{8}$/);
  });

  it('changes when the build changes', () => {
    const before = buildPrecacheManifest({ assets: builtAssets(), base });
    const after = buildPrecacheManifest({
      assets: builtAssets(['assets/chunk-d4e5f6.js']),
      base,
    });

    expect(hashManifest(before)).not.toBe(hashManifest(after));
  });
});

describe('generated worker', () => {
  const source = buildServiceWorker({
    version: 'test-version',
    precache: buildPrecacheManifest({ assets: builtAssets(), base }),
    shellUrl: `${base}${SHELL_FILE_NAME}`,
    assetPrefix: `${base}assets/`,
  });

  it('carries the version, the shell and the precache list', () => {
    expect(source).toContain('"test-version"');
    expect(source).toContain('"/FarmManagementSystem/index.html"');
    expect(source).toContain('/FarmManagementSystem/assets/index-a1b2c3.js');
  });

  /// The API is on another origin (Heroku) and its responses are per-user. A worker that
  /// cached them would hand one session's data to the next, so the guard is asserted
  /// rather than left to code review.
  it('never handles cross-origin requests', () => {
    expect(source).toContain('url.origin !== self.location.origin');
    expect(source).toContain("if (request.method !== 'GET') return;");
  });

  /// Same-origin is not a licence: only the precache manifest and the hashed asset
  /// prefix are ever served or stored. A catch-all runtime cache would have stored a
  /// same-origin API's authenticated 200 (found by the browser smoke test in 5.1),
  /// so same-origin handling is an allowlist and the fallback is "no worker at all".
  it('serves and stores same-origin requests only from the precache/asset allowlist', () => {
    expect(source).toContain('var ASSET_PREFIX');
    expect(source).toContain('PRECACHE.indexOf(url.pathname) !== -1 || url.pathname.indexOf(ASSET_PREFIX) === 0');
    // The unconditional catch-all is gone: a request outside the allowlist must reach
    // the network untouched, never `respondWith`.
    expect(source).not.toContain('event.respondWith(cacheFirstAsset(request));\n});');
  });

  it('serves navigations network-first and assets cache-first', () => {
    // Network-first for the shell preserves the existing decision to revalidate the
    // bundle after a deploy (see the WebView handler's cache-mode comment).
    expect(source).toContain("request.mode === 'navigate'");
    expect(source).toContain('networkFirstShell');
    expect(source).toContain('cacheFirstAsset');
  });

  it('clears its own previous caches on activation', () => {
    expect(source).toContain('name.indexOf(CACHE_PREFIX) === 0 && name !== CACHE_NAME');
    expect(source).toContain('skipWaiting');
    expect(source).toContain('clients.claim');
  });

  it('refuses an empty precache list', () => {
    expect(() => buildServiceWorker({ version: 'v', precache: [], shellUrl: '/index.html', assetPrefix: '/assets/' }))
      .toThrow(/empty/i);
  });
});
