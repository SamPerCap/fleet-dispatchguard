import { expect, test, type Page } from '@playwright/test';

// SPEC 0093 CA3.

function requestsOf(page: Page): string[] {
  const asked: string[] = [];
  page.on('request', request => {
    const { pathname } = new URL(request.url());
    if (/^\/(api|health)/.test(pathname)) asked.push(pathname);
  });
  return asked;
}

test('The cover asks nothing until «Probar», and the back button returns to it', async ({ page }) => {
  const asked = requestsOf(page);
  await page.goto('/');
  await expect(page.getByRole('heading', { level: 1 })).toContainText('Nunca guarda una asignación ilegal');
  await page.getByRole('button', { name: 'English' }).click();
  await expect(page.getByRole('heading', { level: 1 })).toContainText('never saves an illegal assignment');
  await page.getByRole('button', { name: 'Español' }).click();
  await page.getByRole('link', { name: 'Cómo está hecho' }).click();
  await expect(page.getByRole('heading', { name: 'Cómo está hecho', level: 1 })).toBeVisible();
  expect(asked).toEqual([]);

  await page.getByRole('link', { name: 'Probar' }).click();
  await expect(page.getByRole('navigation', { name: 'Principal' })).toBeVisible();
  expect(asked[0]).toBe('/api/session');

  await page.goBack();
  await expect(page.getByRole('heading', { name: 'Cómo está hecho', level: 1 })).toBeVisible();
});

test('About asks nothing either, and on a phone the menu of the application waits behind a button (SPEC 0096)', async ({ page }) => {
  const asked = requestsOf(page);
  await page.goto('/');
  await page.getByRole('link', { name: 'Sobre mí' }).click();
  await expect(page.getByRole('heading', { name: 'Sobre mí', level: 1 })).toBeVisible();
  await expect(page.getByRole('link', { name: 'GitHub' })).toBeVisible();
  expect(asked).toEqual([]);

  await page.setViewportSize({ width: 360, height: 740 });
  await page.getByRole('link', { name: 'Probar' }).click();
  const menu = page.getByRole('button', { name: 'Abrir el menú' });
  await expect(menu).toBeVisible({ timeout: 15_000 });
  await expect(page.getByRole('navigation', { name: 'Principal' })).toBeHidden();
  await menu.click();
  await page.getByRole('navigation', { name: 'Principal' }).getByRole('button', { name: 'Planificación' }).click();
  await expect(page.getByRole('navigation', { name: 'Principal' })).toBeHidden();
});

test('A direct link skips the cover and offers no tour', async ({ page }) => {
  await page.goto('/#planning');
  await expect(page.getByRole('navigation', { name: 'Principal' })).toBeVisible();
  await expect(page.getByRole('complementary', { name: 'Recorrido' })).toHaveCount(0);
});

test('The tour writes nothing, and can be left', async ({ page }) => {
  const written: string[] = [];
  page.on('request', request => {
    const { pathname } = new URL(request.url());
    if (request.method() !== 'GET' && pathname !== '/api/visits') written.push(`${request.method()} ${pathname}`);
  });
  await page.goto('/');
  await page.getByRole('link', { name: 'Probar' }).click();
  const tour = page.getByRole('complementary', { name: 'Recorrido' });
  await tour.getByRole('button', { name: 'Empezar' }).click();

  // Another test may have left the day without a case: then it says so.
  await expect(tour.getByRole('heading', { name: /El servicio|Ahora mismo no hay ningún caso/ })).toBeVisible({ timeout: 30_000 });
  if (await tour.getByText('Paso 1 de 4').isVisible()) {
    await tour.getByRole('button', { name: 'Siguiente' }).click();
    await tour.getByRole('button', { name: 'Siguiente' }).click();
    await expect(tour.getByText('No cumple CE 561/2006.')).toBeVisible();
  }
  await tour.getByRole('button', { name: 'Cerrar' }).click();

  await expect(tour).toHaveCount(0);
  expect(written).toEqual([]);
});

test.describe('in English, for whoever has chosen nothing (SPEC 0095)', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('the cover, and the application, are in English; the switch keeps the screen and what is typed', async ({ page }) => {
    await page.goto('/');
    await expect(page.locator('html')).toHaveAttribute('lang', 'en');
    await expect(page.getByRole('heading', { level: 1 })).toContainText('never saves an illegal assignment');

    await page.getByRole('link', { name: 'Try it' }).click();
    const nav = page.getByRole('navigation', { name: 'Main' });
    await nav.getByRole('button', { name: 'Planning' }).click();
    await page.getByLabel('Allocating as').fill('Ana');

    await page.getByRole('button', { name: 'Español' }).click();
    await expect(page.locator('html')).toHaveAttribute('lang', 'es');
    await expect(page.getByRole('navigation', { name: 'Principal' }).getByRole('button', { name: 'Planificación' })).toHaveAttribute(
      'aria-current',
      'page',
    );
    await expect(page.getByLabel('Asignando como')).toHaveValue('Ana');

    await page.reload();
    await expect(page.getByRole('navigation', { name: 'Principal' })).toBeVisible();
  });
});
