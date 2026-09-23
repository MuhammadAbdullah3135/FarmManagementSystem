import { readdirSync, readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

/**
 * The Phase 5 constraints that are properties of the *code* rather than of one call path.
 *
 * <para>
 * The behavioural suites prove what the offline layer does; these pin the four decisions a
 * later increment could undo without failing anything else, which is exactly how a constraint
 * like "the queue never polls" dies — quietly, in a commit about something else. They read the
 * sources rather than importing them, in the same spirit as `vite-config.test.ts` and the
 * backend's config-drift guards: what is asserted is what will really ship.
 * </para>
 */

const here = dirname(fileURLToPath(import.meta.url));
const clientRoot = resolve(here, '..', '..');

const read = (relative: string) => readFileSync(join(clientRoot, relative), 'utf8');

/** Every offline source, excluding its own tests. */
const offlineSources = readdirSync(here)
  .filter((name) => /\.tsx?$/.test(name) && !/\.test\.tsx?$/.test(name))
  .map((name) => ({ name, source: readFileSync(join(here, name), 'utf8') }));

const engine = read('src/offline/syncEngine.ts');
const axiosSource = read('src/api/axios.ts');
const syncApi = read('src/api/sync.ts');
const db = read('src/offline/db.ts');

describe('the queue never polls', () => {
  it('has no interval anywhere in the offline layer', () => {
    // Every flush is an event: connectivity, focus, visibility, a successful request, a token
    // refresh, or an explicit "Sync now". The only timer the engine may arm is its own single
    // backoff retry — a `setTimeout`, cleared by the next clean pass.
    const offenders = offlineSources
      .filter(({ source }) => source.includes('setInterval('))
      .map(({ name }) => name);

    expect(offenders).toEqual([]);
  });

  it('arms exactly one retry timer and clears it', () => {
    expect(engine).toContain('setTimeout(');
    expect(engine).toContain('clearTimeout(');
    // The retry is scheduled by the failure paths, not by a loop: one timer, and cancelling it
    // is what a clean pass does.
    expect(engine).toMatch(/retryTimer = setTimeout\(/);
  });
});

describe('queue traffic cannot re-trigger the flush', () => {
  it('marks the sync request, so its own success is not a trigger', () => {
    // Without this a successful flush would ask for another flush, forever.
    expect(syncApi).toMatch(/syncRequest:\s*true/);
  });

  it('skips the success trigger for marked requests', () => {
    expect(axiosSource).toMatch(/if \(!response\.config\?\.syncRequest\)/);
  });

  it('still resets the offline write window from a pass that reached the server', () => {
    // 5.6's window and the flush have to agree about what "we reached the server" means. The
    // semantic half is asserted in `syncEngine.test.ts`; this is the tripwire for the wiring
    // being deleted, which would silently re-open the gap the reset closes.
    expect(engine).toContain('noteServerContact(');
    expect(engine).toContain('reachedTheServer(');
    // A 401 must not count: the session cannot deliver, which is the state the window flags.
    expect(engine).toMatch(/case 'auth':\n\s*case 'skipped':\n\s*return false;/);
  });
});

describe('the store is scoped by construction', () => {
  it('keys cached rows, collection metadata and queued items by account and farm', () => {
    // (accountId, farmId) in the key path is what makes a cross-farm read impossible rather
    // than merely unlikely: the rows are not in the result set to be filtered out.
    expect(db).toContain("keyPath: ['accountId', 'farmId', 'collection', 'id']");
    expect(db).toContain("keyPath: ['accountId', 'farmId', 'collection']");
    expect(db).toContain("keyPath: ['accountId', 'farmId', 'mutationId']");
  });

  it('reads a collection through the scoped index, not a scan', () => {
    expect(db).toMatch(/index\('byScope'\)\.getAll\(\[/);
  });

  it('keeps one session marker per account, outside the read cache', () => {
    // Outside the cache on purpose: clearing cached data must not reset the offline-write
    // window, or clearing it would be a way to keep writing past the cutoff.
    expect(db).toContain("export const SESSION_STORE = 'session'");
    expect(db).toMatch(/createObjectStore\(SESSION_STORE, \{ keyPath: \['accountId'\] \}\)/);
  });
});
