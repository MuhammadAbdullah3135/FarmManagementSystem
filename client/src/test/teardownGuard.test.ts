import { test, expect } from 'vitest';

/**
 * The guard installed by `teardownGuard.ts` (a setup file, so it is already in place here) decides
 * whether a scheduler callback may run by asking whether a DOM environment still exists. jsdom
 * globals are `configurable`, so both branches of that decision can be exercised in-process rather
 * than by waiting for a real teardown — which is untestable by construction, because the failure
 * only happens once the test that caused it has finished.
 */
function deleteDomGlobals(): void {
  delete (globalThis as Record<string, unknown>).window;
  delete (globalThis as Record<string, unknown>).document;
}

function restoreDomGlobals(windowValue: Window, documentValue: Document): void {
  (globalThis as Record<string, unknown>).window = windowValue;
  (globalThis as Record<string, unknown>).document = documentValue;
}

test('runs callbacks while the environment is alive', async () => {
  let ran = false;
  setImmediate(() => {
    ran = true;
  });
  await new Promise((resolve) => setTimeout(resolve, 20));

  expect(ran).toBe(true);
});

test('drops callbacks scheduled by a file whose environment is gone', async () => {
  const windowValue = window;
  const documentValue = document;
  let ran = false;

  try {
    deleteDomGlobals();
    // A React component's pending commit, an unmount effect, a stray timer: all of it arrives
    // through this same primitive after teardown.
    setImmediate(() => {
      ran = true;
      // The real react-dom commit phase touches globals like this one.
      document.body.setAttribute('data-stray', '1');
    });

    await new Promise((resolve) => setTimeout(resolve, 20));
  } finally {
    restoreDomGlobals(windowValue, documentValue);
  }

  expect(ran).toBe(false);
});

test('drops callbacks even when only the document is gone', async () => {
  const documentValue = document;
  let ran = false;

  try {
    delete (globalThis as Record<string, unknown>).document;
    setImmediate(() => {
      ran = true;
    });
    await new Promise((resolve) => setTimeout(resolve, 20));
  } finally {
    (globalThis as Record<string, unknown>).document = documentValue;
  }

  expect(ran).toBe(false);
});
