import { expect, test } from '@playwright/test';

// What only the image has: the web served by the API with its CSP, and the start of a database that sleeps (SPEC 0091).
test.skip(!process.env.E2E_BASE_URL, 'Vite serves the web in development');

test('The CSP blocks nothing of the web', async ({ page }) => {
  const violations: string[] = [];
  page.on('console', message => {
    if (message.text().startsWith('CSP violation')) violations.push(message.text());
  });
  await page.addInitScript(() =>
    addEventListener('securitypolicyviolation', event =>
      console.error(`CSP violation: ${event.effectiveDirective} ${event.blockedURI} ${event.sample}`),
    ),
  );

  await page.goto('/#control');
  for (const view of ['Planificación', 'Reservas', 'Conductores', 'Vehículos', 'Motor normativo', 'Centro de control']) {
    await page.getByRole('navigation', { name: 'Principal' }).getByRole('button', { name: view }).click();
    await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible();
  }
  // The dialog library inserts its style when it opens.
  await page.keyboard.press('Control+k');
  await expect(page.getByRole('dialog')).toBeVisible();

  expect(violations).toEqual([]);
});

test('A script from inside the page does not run', async ({ page }) => {
  await page.goto('/#control');
  await page.evaluate(() => {
    const script = document.createElement('script');
    script.textContent = 'window.injected = true';
    document.body.append(script);
  });
  expect(await page.evaluate(() => 'injected' in window)).toBe(false);
});

test('With the database asleep it waits and goes in by itself', async ({ page }) => {
  let asleep = true;
  await page.route('**/health/ready', route => (asleep ? route.fulfill({ status: 503 }) : route.continue()));
  await page.goto('/#control');
  await expect(page.getByRole('heading', { name: 'Preparando la demo…' })).toBeVisible();
  await expect(page.locator('.traffic-vehicle')).toHaveCount(5);
  await expect(page.getByRole('navigation', { name: 'Principal' })).toHaveCount(0);

  asleep = false;

  await expect(page.getByRole('navigation', { name: 'Principal' })).toBeVisible({ timeout: 15_000 });
  await expect(page.getByRole('heading', { name: 'Preparando la demo…' })).toHaveCount(0);
});
