import { expect, test } from '@playwright/test';
import { skipLoaderClosingBeforeEach, writer } from './session';

skipLoaderClosingBeforeEach();

test('Control centre: traffic light, panels, base filter and an alert that opens its booking', async ({ page, request }) => {
  // An uncovered service leaving in 10 min at base 1 → urgent alert "Asignar conductor y vehículo".
  const headers = await writer(request);
  const [loc] = await (await request.get('/api/locations')).json();
  const start = new Date(Date.now() + 10 * 60_000),
    end = new Date(start.getTime() + 30 * 60_000);
  const name = `E2E control ${Date.now()}`;
  const created = await request.post('/api/bookings', {
    headers,
    data: {
      CustomerName: name,
      Description: name,
      Notes: null,
      Trips: [
        {
          OriginLocationId: loc.Id,
          DestinationLocationId: loc.Id,
          Start: start.toISOString(),
          End: end.toISOString(),
          RequiredSeats: 10,
          ServiceType: 'Discretionary',
          Description: name,
        },
      ],
    },
  });
  expect(created.ok()).toBeTruthy();

  await page.goto('/#control');
  await expect(page.getByRole('heading', { name: 'Centro de control' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Centro de control' })).toHaveAttribute('aria-current', 'page');
  // There are several bases, sorted by name; the service above belongs to base 1.
  await page.getByLabel('Base', { exact: true }).selectOption('1');

  const status = page.getByRole('region', { name: 'Situación' });
  await expect(status.getByRole('img', { name: /^Semáforo rojo$/ })).toBeVisible(); // leaves in 10 min → urgent
  for (const region of ['Prioridades', 'Motor normativo', 'Próximas horas', 'Recursos ahora', 'Actividad reciente'])
    await expect(page.getByRole('region', { name: region })).toBeVisible();

  const engine = page.getByRole('region', { name: 'Motor normativo' });
  await engine.getByRole('button', { name: 'Qué es: Bloqueadas' }).click();
  // The ⓘ is painted in a portal (its module does not clip it), so it is looked up in the page.
  await expect(page.getByRole('tooltip')).toContainText('No cuenta los candidatos');

  const options = page.getByLabel('Base', { exact: true }).locator('option');
  await expect(options.first()).toHaveText(/^Todas las bases/);
  expect(await options.count()).toBeGreaterThan(1);

  // Neither map nor time saved (SPEC 0049).
  await expect(page.getByText(/tiempo ahorrado|mapa/i)).toHaveCount(0);

  // The trip and its single-trip booking are ONE incident.
  const incidents = page.getByRole('region', { name: 'Prioridades' });
  await expect(incidents.getByRole('button', { name: `Asignar conductor y vehículo: ${name}` })).toHaveCount(1);
  await expect(incidents.getByRole('button', { name: `Completar la reserva: ${name}` })).toHaveCount(0);

  await page
    .getByRole('region', { name: 'Prioridades' })
    .getByRole('button', { name: `Asignar conductor y vehículo: ${name}` })
    .click();
  const block = page.getByRole('dialog', { name: /^Reserva/ }).locator('article[aria-current="true"]');
  await expect(block.getByText('Sin conductor', { exact: true })).toBeVisible();
  await expect(block.getByRole('region', { name: 'Asignar viaje 1' })).toBeVisible();
});
