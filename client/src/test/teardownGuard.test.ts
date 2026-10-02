import { afterEach, test, expect } from 'vitest';
import { setTimeout as sleep } from 'node:timers/promises';

/**
 * The guard installed by `teardownGuard.ts` (a setup file, so it is already in place here) decides
 * whether a scheduled callback may run by asking whether a DOM environment still exists. jsdom
 * globals are `configurable`, so both branches of that decision can be exercised in-process rather
 * than by waiting for a real teardown — which is untestable by construction, because the failure
 * only happens once the test that caused it has finished.
 *
 * Each primitive is covered, because covering only `setImmediate` left the other two to fail in
 * production: React's scheduler binds `setImmediate`, but antd's exit animations run on
 * `requestAnimationFrame` and `setTimeout`, and those were the ones still leaking.
 *
 * **These tests wait on Node's own timer, not the global one.** That is not a detail. Once the
 * globals are deleted, the guarded `setTimeout` drops its own callbacks — which is the entire
 * point of the guard — so a test that awaited through it would wait forever. The first draft of
 * this file did exactly that: the await never resolved, the test timed out, its `finally` never
 * ran, and the DOM globals stayed deleted for every test after it. Using `node:timers` keeps the
 * harness independent of the thing under test, and the `afterEach` below means even a genuine
 * failure cannot leak that state into the next test.
 */
const realWindow = globalThis.window;
const realDocument = globalThis.document;

function deleteDomGlobals(): void {
  delete (globalThis as Record<string, unknown>).window;
  delete (globalThis as Record<string, unknown>).document;
}

/**
 * Puts the DOM globals back.
 *
 * `defineProperty` rather than assignment, because on the jsdom window `document` is a
 * getter-only accessor: assigning to it throws "Cannot set property document of [object Window]
 * which has only a getter". Plain assignment only ever worked here because a `delete` had
 * already removed the accessor and left a writable data property behind — so restoring from a
 * test that never deleted anything was impossible, which is the case this function exists for.
 */
function restoreDomGlobals(): void {
  for (const [name, value] of [['window', realWindow], ['document', realDocument]] as const) {
    if ((globalThis as Record<string, unknown>)[name] === value) continue;
    Object.defineProperty(globalThis, name, { value, configurable: true, writable: true });
  }
}

/** Long enough for a 0ms timer and an animation frame, short enough to keep the suite quick. */
const settle = () => sleep(40);

afterEach(() => {
  // Unconditional: a test that threw mid-way must not leave the environment without a DOM for
  // the next one, which is how one hung test became six failures.
  restoreDomGlobals();
});

test('runs callbacks while the environment is alive', async () => {
  const immediate = { ran: false };
  const timer = { ran: false };
  const frame = { ran: false };

  setImmediate(() => {
    immediate.ran = true;
  });
  setTimeout(() => {
    timer.ran = true;
  }, 0);
  requestAnimationFrame(() => {
    frame.ran = true;
  });
  await settle();

  // The wrappers are transparent while a test is running. If any of these were dropped here the
  // suite would break in a far more obvious way, but the assertion is what makes that a failure
  // rather than a surprise.
  expect(immediate.ran).toBe(true);
  expect(timer.ran).toBe(true);
  expect(frame.ran).toBe(true);
});

test('drops setImmediate callbacks scheduled by a file whose environment is gone', async () => {
  let ran = false;

  deleteDomGlobals();
  // A React component's pending commit, an unmount effect, a stray flush.
  setImmediate(() => {
    ran = true;
    // The real react-dom commit phase touches globals like this one.
    document.body.setAttribute('data-stray', '1');
  });

  await settle();

  expect(ran).toBe(false);
});

test('drops an animation frame scheduled by a file whose environment is gone', async () => {
  // This is the one the guard did not cover. antd destroys an overlay's exit animation through
  // rc-motion, which drives it with requestAnimationFrame; the frames were delivered after teardown,
  // and every one of them read a `window` that no longer existed.
  //
  // Scheduled *before* the teardown, which is the order these really arrive in: the guard
  // decides at delivery time, not at scheduling time, so work queued by a live test and
  // released into a dead environment is exactly what it has to catch.
  let ran = false;

  requestAnimationFrame(() => {
    ran = true;
    document.body.setAttribute('data-stray', '1');
  });
  deleteDomGlobals();

  await settle();

  expect(ran).toBe(false);
});

test('drops a motion-end timer scheduled by a file whose environment is gone', async () => {
  // And this one. rc-motion finishes on a timeout measured in hundreds of milliseconds, so it is
  // still pending when a file ends; whether it lands before or after teardown is exactly what
  // made the original failure intermittent.
  let ran = false;

  setTimeout(() => {
    ran = true;
    document.body.setAttribute('data-stray', '1');
  }, 0);
  deleteDomGlobals();

  await settle();

  expect(ran).toBe(false);
});

test('drops callbacks even when only the document is gone', async () => {
  const immediate = { ran: false };
  const frame = { ran: false };

  delete (globalThis as Record<string, unknown>).document;
  setImmediate(() => {
    immediate.ran = true;
  });
  requestAnimationFrame(() => {
    frame.ran = true;
  });
  await settle();

  expect(immediate.ran).toBe(false);
  expect(frame.ran).toBe(false);
});

test('clearTimeout still cancels through the wrapper', async () => {
  // The wrapper returns the real handle, so cancellation has to keep working — a guard that
  // broke it would silently turn every cancelled timer in the app into a stray one.
  let ran = false;
  const handle = setTimeout(() => {
    ran = true;
  }, 0);
  clearTimeout(handle);

  await settle();

  expect(ran).toBe(false);
});

test('a timer keeps its delay and its arguments', async () => {
  // Transparency while the environment is alive, including the parts the wrapper could plausibly
  // have got wrong: the delay, the extra arguments, and the return value's usability as a handle.
  const seen: unknown[] = [];
  const handle = setTimeout((first: string, second: number) => {
    seen.push(first, second);
  }, 5, 'value', 42);

  expect(typeof handle === 'object' || typeof handle === 'number').toBe(true);
  await sleep(60);

  expect(seen).toEqual(['value', 42]);
});
