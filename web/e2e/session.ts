import { test, type APIRequestContext } from '@playwright/test';

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
