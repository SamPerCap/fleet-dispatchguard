import { test, type APIRequestContext } from '@playwright/test';

/**
 * The first entry of a tab plays the loading scene for at least 6 s (SPEC 0101 R11). Tests about something else start as a
 * later entry of the tab, so they wait only for the real work; the loader has its own test in `container.spec.ts`.
 */
export function skipLoaderClosingBeforeEach() {
  test.beforeEach(async ({ page }) => {
    await page.addInitScript(() => sessionStorage.setItem('dispatchguard.loadingClosingSeen', '1'));
  });
}

/** Nothing is read without a session either: for the tests that ask the API before opening the web. */
export function startSessionBeforeEach() {
  test.beforeEach(async ({ page }) => {
    await page.request.get('/api/session');
  });
}

/** Starts the session (the cookie stays in the context) and returns what a direct write must carry. */
export async function writer(request: APIRequestContext): Promise<Record<string, string>> {
  await request.get('/api/session');
  return { 'X-DispatchGuard-Request': '1' };
}
