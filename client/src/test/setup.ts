import '@testing-library/jest-dom/vitest';
// Initialises i18next for every test, exactly as main.tsx does for the app. Without it
// `useTranslation` would return raw keys and every text assertion would fail; with it,
// the suite asserts the very English copy the app ships.
import '../i18n';
import { cleanup, configure } from '@testing-library/react';
import { afterEach } from 'vitest';
import { Modal, message, notification } from 'antd';

// This file is shared by every test file, including any that declare
// `@vitest-environment node`. It used to dereference `window` unconditionally, which failed such
// a file's whole environment with "window is not defined" before a single test ran.
const hasDom = typeof window !== 'undefined' && typeof document !== 'undefined';

// How long `findBy*`/`waitFor` may wait before failing, per query. The library default is
// one second, which is enough on a laptop but not on a CI runner executing 79 test files in
// parallel: AttendancePage's boot (IndexedDB open + employee fetch) took longer than that
// there, and the suite failed on a query whose page was about to render. Five seconds is
// the budget the antd-heavy suites already give their whole tests; giving it to the queries
// removes the load-dependent coin toss without hiding a genuinely missing element — a
// passing query still resolves the moment it matches.
if (hasDom) {
  configure({ asyncUtilTimeout: 5_000 });
}

afterEach(async () => {
  if (!hasDom) return;

  // antd's static `message` / `notification` / `Modal.confirm` helpers render into React roots of
  // their own, and testing-library's cleanup unmounts only the containers it created. Left
  // mounted, those trees keep scheduling React work — motion, focus, the closing animation — and
  // some of it lands after vitest has torn the jsdom environment down, where react-dom's commit
  // phase reads a global `window` that no longer exists:
  //
  //   ReferenceError: window is not defined
  //     at react-dom-client.development.js ❯ Immediate.performWorkUntilDeadline
  //
  // Vitest reports that as an *unhandled* error and fails the run while every test passed. It did
  // exactly that on two pushes, attributed to the file whose last test opened a confirm dialog,
  // and the deploy job that waits on this suite skipped both times. Closing the overlays inside
  // the test's lifetime stops that work being scheduled at all.
  Modal.destroyAll();
  message.destroy();
  notification.destroy();

  cleanup();

  // Then let the scheduler flush what is already queued: React batches renders into a macrotask,
  // and teardown has deleted the globals before the next one runs.
  await new Promise((resolve) => setTimeout(resolve, 0));
});

// antd requires matchMedia; jsdom doesn't implement it.
if (hasDom && !window.matchMedia) {
  window.matchMedia = ((query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: () => undefined,
    removeListener: () => undefined,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    dispatchEvent: () => false,
  })) as unknown as typeof window.matchMedia;
}

// recharts/antd need ResizeObserver; jsdom doesn't implement it.
if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver;
}

// antd message/notification render outside React tree — jsdom lacks scrollIntoView.
if (hasDom) {
  Element.prototype.scrollIntoView = Element.prototype.scrollIntoView ?? (() => {});

  if (typeof window.HTMLElement.prototype.scrollTo !== 'function') {
    window.HTMLElement.prototype.scrollTo = () => {};
  }
}
