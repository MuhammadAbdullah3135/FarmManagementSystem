/**
 * Drops work that a previous test file scheduled but that only runs after its environment is
 * gone.
 *
 * React's scheduler flushes through Node's `setImmediate`, captured once when
 * `scheduler` is first loaded (see `localSetImmediate` in scheduler.development.js). jsdom is torn
 * down between files, so a callback scheduled just before that — a stray animation frame, a
 * timer, a flush queued by an unmount — fires with no `window` in scope, and react-dom's commit
 * phase throws:
 *
 *   ReferenceError: window is not defined
 *     at react-dom-client.development.js ❯ Immediate.performWorkUntilDeadline
 *
 * Vitest reports that as an unhandled error and fails the run even though every test passed; it
 * did so on three pushes, each time attributed to whichever file happened to be running when the
 * callback landed (AppLayout, MembersPage, swTemplate, AnimalImportPage) — i.e. never the file
 * that caused it. Because the deploy job waits on this suite, each of those runs deployed nothing.
 *
 * The guard only drops work whose environment has already been destroyed. That work cannot affect
 * any test — no test is running, and the tree it belongs to no longer exists — and running it
 * against the next file's environment would be worse. Files that run without a DOM from the start
 * (`@vitest-environment node`) are untouched: they never had an environment to lose.
 *
 * This has to be a separate setup file, listed before any React import: the scheduler binds
 * `setImmediate` once, at load.
 */
const hadDomAtSetup =
  typeof window !== 'undefined' && typeof document !== 'undefined';

const realSetImmediate = globalThis.setImmediate?.bind(globalThis);

if (hadDomAtSetup && typeof realSetImmediate === 'function') {
  globalThis.setImmediate = ((callback: (...args: unknown[]) => void, ...args: unknown[]) =>
    realSetImmediate(() => {
      // The jsdom environment has been torn down since this work was scheduled.
      if (typeof window === 'undefined' || typeof document === 'undefined') return;
      callback(...args);
    })) as typeof setImmediate;
}
