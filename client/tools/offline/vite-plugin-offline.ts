import { readdirSync, statSync, writeFileSync } from 'node:fs';
import { join, relative, resolve, sep } from 'node:path';
import type { Plugin } from 'vite';
import {
  buildPrecacheManifest,
  buildServiceWorker,
  normalizeBase,
  selectServiceWorkerVersion,
  SERVICE_WORKER_FILE_NAME,
  SHELL_FILE_NAME,
} from './sw-template.ts';

/** Every file under `dir`, as output-relative POSIX paths (e.g. `assets/index-a1b2.js`). */
function listOutputFiles(dir: string): string[] {
  const found: string[] = [];

  const walk = (current: string) => {
    for (const entry of readdirSync(current)) {
      const full = join(current, entry);
      if (statSync(full).isDirectory()) {
        walk(full);
        continue;
      }
      found.push(relative(dir, full).split(sep).join('/'));
    }
  };

  walk(dir);
  return found;
}

/**
 * Writes `sw.js` into the build output, precaching exactly what that build produced.
 *
 * The manifest is read from the **written output directory** rather than from the
 * in-memory bundle, for two reasons: the app shell (`index.html`) is emitted by Vite's
 * own HTML plugin and is not reliably present in the bundle at `generateBundle` time
 * (it was not, in the Vite version this was built against — the build failed loudly,
 * which is how that was discovered), and reading what is actually on disk is the only
 * way to guarantee the worker never asks for a file that a later step removed.
 *
 * A missing shell or an empty manifest fails the build: a worker that installs
 * "successfully" and then cannot serve the app offline is the worst possible outcome,
 * because nothing looks wrong until a user has no signal.
 *
 * Applied to builds only — in dev there is no hashed bundle to precache and a worker
 * would fight HMR.
 */
export function offlineShell(options: { buildSha?: string } = {}): Plugin {
  let base = '/';
  let outDir = 'dist';

  return {
    name: 'fms-offline-shell',
    apply: 'build',

    configResolved(config) {
      base = config.base;
      outDir = resolve(config.root, config.build.outDir);
    },

    writeBundle() {
      const assets = listOutputFiles(outDir);

      let precache: string[];
      try {
        precache = buildPrecacheManifest({ assets, base });
      } catch (error) {
        this.error(error instanceof Error ? error.message : String(error));
        return;
      }

      if (!assets.includes(SHELL_FILE_NAME)) {
        this.error(`Offline shell build failed: '${SHELL_FILE_NAME}' was not emitted.`);
        return;
      }

      const version = selectServiceWorkerVersion({ buildSha: options.buildSha, manifest: precache });
      const source = buildServiceWorker({
        version,
        precache,
        shellUrl: `${normalizeBase(base)}${SHELL_FILE_NAME}`,
      });

      writeFileSync(join(outDir, SERVICE_WORKER_FILE_NAME), source, 'utf8');

      this.info?.(`offline shell: ${precache.length} files precached, cache "${version}"`);
    },
  };
}
