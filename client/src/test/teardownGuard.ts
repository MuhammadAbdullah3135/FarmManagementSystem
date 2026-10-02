/**
 * Drops work that a previous test file scheduled but that only runs after its environment is
 * gone.
 *
 * jsdom is torn down between files, but Node's timers, jsdom's animation frames and React's
 * scheduler all outlive it. A callback scheduled just before teardown — a stray animation
 * frame, a motion-end timer, a flush queued by an unmount — fires with no `window` in scope,
 * and the code in it throws:
 *
 *   ReferenceError: window is not defined
 *     at react-dom-client.development.js ❯ Immediate.performWorkUntilDeadline
 *
 * Vitest reports that as an unhandled error and fails the run even when every test passed; it
 * did so on three pushes, each time attributed to whichever file happened to be running when the
 * callback landed (AppLayout, MembersPage, swTemplate, AnimalImportPage) — i.e. never the file
 * that caused it. Because the deploy job waits on this suite, each of those runs deployed
 * nothing.
 *
 * **Three primitives, because one was not enough.** `setImmediate` alone was the first attempt
 * and it fixed the failures it was written for: React's scheduler binds `setImmediate` when the
 * global exists (scheduler.development.js checks it first, ahead of `MessageChannel` and
 * `setTimeout`), so React's own commits were covered. It does not cover the two other ways work
 * outlives a file, and both are load-bearing in this suite:
 *
 * - `requestAnimationFrame` — every antd overlay animates through rc-motion, which drives its
 *   frames with rAF. `setup.ts` destroys those overlays in `afterEach`, but destruction
 *   *starts* an exit animation; its frames and its motion-end timer are still pending when the
 *   `setTimeout(0)` that follows has already resolved.
 * - `setTimeout` — the same motion-end timers, and antd's own debounce and delay helpers.
 *
 * Both fire on a normal timeline measured in hundreds of milliseconds, which is why the failure
 * is intermittent and load-dependent rather than constant: it depends on whether the file
 * happened to finish before the last frame was delivered.
 *
 * The guard drops work whose environment has already been destroyed. That work cannot affect any
 * test — no test is running, and the tree it belongs to no longer exists — and running it against
 * the next file's environment would be worse. Work scheduled while the environment is alive is
 * untouched, so nothing inside a running test is affected: the wrappers are transparent until
 * the DOM is gone.
 *
 * Files that run without a DOM from the start (`@vitest-environment node`) are untouched: they
 * never had an environment to lose, and `setup.ts` makes the same check for the same reason.
 *
 * This has to be a separate setup file, listed before any React import: the scheduler binds
 * `setImmediate` once, at load.
 */
const hadDomAtSetup =
  typeof window !== 'undefined' && typeof document !== 'undefined';

/** True while this file's jsdom environment still exists. */
const environmentAlive = () =>
  typeof window !== 'undefined' && typeof document !== 'undefined';

if (hadDomAtSetup) {
  const realSetImmediate = globalThis.setImmediate?.bind(globalThis);
  const realSetTimeout = globalThis.setTimeout?.bind(globalThis);
  const realRequestAnimationFrame = globalThis.requestAnimationFrame?.bind(globalThis);

  if (typeof realSetImmediate === 'function') {
    globalThis.setImmediate = ((callback: (...args: unknown[]) => void, ...args: unknown[]) =>
      realSetImmediate(() => {
        // The jsdom environment has been torn down since this work was scheduled.
        if (!environmentAlive()) return;
        callback(...args);
      })) as typeof setImmediate;
  }

  if (typeof realSetTimeout === 'function') {
    // The real handle is returned unchanged, so `clearTimeout` still cancels it normally.
    globalThis.setTimeout = ((callback: (...args: unknown[]) => void, delay?: number, ...args: unknown[]) =>
      realSetTimeout(() => {
        if (!environmentAlive()) return;
        callback(...args);
      }, delay)) as typeof setTimeout;
  }

  if (typeof realRequestAnimationFrame === 'function') {
    // The timestamp is the one jsdom supplied rather than a fresh reading, because `performance`
    // is as gone as `window` by the time this could run.
    globalThis.requestAnimationFrame = ((callback: (timestamp: number) => void) =>
      realRequestAnimationFrame((timestamp) => {
        if (!environmentAlive()) return;
        callback(timestamp);
      })) as typeof requestAnimationFrame;
  }
}
