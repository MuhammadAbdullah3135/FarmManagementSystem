import '@testing-library/jest-dom';

// Polyfill matchMedia for jsdom so Ant Design responsive hooks can mount.
if (typeof window !== 'undefined' && !window.matchMedia) {
  window.matchMedia = () => (
    {
      matches: false,
      media: '',
      onchange: null,
      addListener: () => {},
      removeListener: () => {},
      addEventListener: () => {},
      removeEventListener: () => {},
      dispatchEvent: () => false,
    }
  );
}
