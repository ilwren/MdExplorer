// Test setup: jsdom polyfills + route relative API calls to the live dev server.
import '@testing-library/jest-dom/vitest';

(globalThis as unknown as { __API_BASE: string }).__API_BASE =
  process.env.EIDORIA_TEST_API || 'http://localhost:5173';

const base = (globalThis as unknown as { __API_BASE: string }).__API_BASE;

// jsdom has no matchMedia — the app uses it for system theme.
window.matchMedia = ((query: string) => ({
  matches: false,
  media: query,
  onchange: null,
  addListener: () => {},
  removeListener: () => {},
  addEventListener: () => {},
  removeEventListener: () => {},
  dispatchEvent: () => false,
})) as unknown as typeof window.matchMedia;

// Node's fetch requires absolute URLs — wrap it to hit the live server.
const realFetch = globalThis.fetch.bind(globalThis);
globalThis.fetch = ((input: RequestInfo | URL, init?: RequestInit) => {
  const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
  if (url.startsWith('/')) return realFetch(base + url, init);
  return realFetch(input as RequestInfo, init);
}) as typeof fetch;

// scroll APIs used by nothing critical, but jsdom lacks them
window.HTMLElement.prototype.scrollIntoView = () => {};
