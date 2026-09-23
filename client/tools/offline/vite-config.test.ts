import { readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

/**
 * A drift guard, in the same spirit as the backend's `TrackedConfigDriftTests`: the
 * offline shell depends on two lines of configuration that are trivially deletable
 * and whose deletion breaks nothing at build or test time — the app simply stops
 * opening offline, which no one notices until a device is in a field with no signal.
 *
 * These tests read the actual config files rather than importing them, so what is
 * asserted is what Vite will really run.
 */

const here = dirname(fileURLToPath(import.meta.url));
const clientRoot = resolve(here, '..', '..');

const viteConfig = readFileSync(join(clientRoot, 'vite.config.ts'), 'utf8');
const packageJson = JSON.parse(readFileSync(join(clientRoot, 'package.json'), 'utf8')) as {
  dependencies?: Record<string, string>;
  devDependencies?: Record<string, string>;
};

describe('vite.config.ts offline shell wiring', () => {
  it('still wires the offline-shell plugin into the build', () => {
    expect(viteConfig).toContain('offlineShell(');
    expect(viteConfig).toMatch(/from '\.\/tools\/offline\/vite-plugin-offline\.ts'/);
  });

  it('keeps the GitHub Pages base path the worker is scoped to', () => {
    // The service worker is registered at `${base}sw.js` with `scope: base`; a base
    // that drifts from the deployment path would 404 the registration silently and
    // leave the app online-only.
    expect(viteConfig).toMatch(/base:\s*'\/FarmManagementSystem\/'/);
  });
});

describe('package.json offline decisions', () => {
  const allDependencies = {
    ...packageJson.dependencies,
    ...packageJson.devDependencies,
  };

  it('gains no PWA plugin', () => {
    // The decision was a hand-written, auditable worker (~60 lines) instead of a
    // build-time dependency to keep working. If a PWA plugin is ever deliberately
    // adopted, this test is the moment to delete — along with the precache guards
    // that assume the worker's shape.
    const pwaPlugins = Object.keys(allDependencies).filter((name) =>
      /vite-plugin-pwa|workbox/i.test(name),
    );
    expect(pwaPlugins).toEqual([]);
  });

  it('adds no IndexedDB wrapper library — db.ts stays the only storage layer', () => {
    // A second IndexedDB API in the app would put writes outside the transactional
    // and scoping rules db.ts enforces (compound (accountId, farmId) keys, atomic
    // collection replacement). The dependency types (idb, dexie) are exactly what a
    // drive-by install produces.
    // (`fake-indexeddb` is the dev-only test double and deliberately allowed.)
    const idbWrappers = Object.keys(allDependencies).filter((name) => /^(idb|dexie)$/.test(name));
    expect(idbWrappers).toEqual([]);
  });
});
