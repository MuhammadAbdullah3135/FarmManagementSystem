import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach } from 'vitest';

afterEach(() => {
  cleanup();
});

// antd requires matchMedia; jsdom doesn't implement it.
if (!window.matchMedia) {
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
Element.prototype.scrollIntoView = Element.prototype.scrollIntoView ?? (() => {});

if (typeof window.HTMLElement.prototype.scrollTo !== 'function') {
  window.HTMLElement.prototype.scrollTo = () => {};
}
