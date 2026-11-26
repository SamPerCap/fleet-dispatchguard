import { expect, test, type Page } from '@playwright/test';
import { startSessionBeforeEach, writer } from './session';
import { iso, nextMonday } from './week';

startSessionBeforeEach();

const tuesday = () => {
  const date = nextMonday();
  date.setDate(date.getDate() + 1);
  return date;
};

/** Day view on the seeded Tuesday. */
async function planningDay(page: Page) {
  await page.goto('/#planning');
  await page.getByLabel('Ir a la fecha').fill(iso(tuesday()));
  await page.getByRole('radio', { name: 'Día' }).click();
  await expect(page.getByRole('table', { name: 'Línea de tiempo' })).toBeVisible();
}

/** An active driver with a service on the seeded Tuesday that others could legally do. */
async function driverWithServices(page: Page): Promise<{ driverId: number; name: string }> {
  for (const trip of await tuesdayTrips(page)) {
    if (trip.Status !== 'Assigned' || trip.AssignedDriverId == null) continue;
    const candidates = await (await page.request.get(`/api/trips/${trip.Id}/driver-candidates`)).json();
    const driver = await (await page.request.get(`/api/drivers/${trip.AssignedDriverId}`)).json();
    if (candidates.length > 0 && driver.IsActive) return { driverId: driver.Id, name: `${driver.FirstName} ${driver.LastName}` };
  }
  throw new Error('No replaceable driver on Tuesday (was the week seeded?)');
}

/** Over whatever it has now. */
async function putVehicle(page: Page, id: number, vehicle: Record<string, unknown>) {
  const url = `/api/vehicles/${id}`;
  const { DetailsVersion } = await (await page.request.get(url)).json();
  const { LicensePlate, Brand, Model, SeatingCapacity, IsExternal, AllowsAutoAssignment, HomeLocationId, BaseId } = vehicle;
  const saved = await page.request.put(url, {
    headers: await writer(page.request),
    data: { LicensePlate, Brand, Model, SeatingCapacity, IsExternal, AllowsAutoAssignment, HomeLocationId, BaseId, DetailsVersion },
  });
  expect(saved.ok()).toBe(true);
}

test('last-minute leave → candidates → assign → ⓘ (panel and timeline bar)', async ({ page }) => {
  await page.goto('/#drivers');
  await page.getByLabel('Día', { exact: true }).fill(iso(tuesday()));

  const incident = page.getByRole('region', { name: 'Baja de última hora' });
  const person = incident.getByRole('combobox', { name: 'Conductor', exact: true });
  const { driverId, name } = await driverWithServices(page);
  const register = incident.getByRole('button', { name: 'Dar de baja' });
  // Typing a name puts nobody on leave: they have to be chosen.
  await person.fill(name);
  await expect(register).toBeDisabled();
  await page.keyboard.press('Enter');
  await expect(person).toHaveValue(name);
  await expect(incident.getByText(/^[1-9]\d* servicio/)).toBeVisible();
  await register.click();

  await expect(incident.getByText(/^Baja registrada para/)).toBeVisible();
  await expect(incident.getByText('Consultando…')).toHaveCount(0); // until then, which service is the first one can change
  // Choosing the same driver again drops nothing.
  await person.fill(name);
  await page.keyboard.press('Enter');
  await expect(incident.getByText(/^Baja registrada para/)).toBeVisible();

  const service = incident.getByRole('article').filter({ has: page.getByRole('button', { name: 'Asignar' }) }).first();
  await expect(service.getByRole('button', { name: /^Explicación: Candidato #1/ })).toBeVisible();
  const loaded = page.waitForResponse(response => response.url().includes('/driver-options'));
  await service.getByRole('button', { name: 'Buscar otro conductor' }).click();
  const { Candidates } = (await (await loaded).json()) as { Candidates: { DriverId: number; FullName: string }[] };
  const other = service.getByRole('combobox', { name: 'Otro conductor' });
  await expect(other).toBeFocused();

  // Whoever is on leave is found, but cannot be chosen.
  await other.fill(name);
  await expect(page.getByRole('listbox').getByRole('option').filter({ hasText: 'De baja' })).toHaveCount(1);
  await page.keyboard.press('Escape');
  expect(Candidates.map(candidate => candidate.DriverId)).not.toContain(driverId);

  // Choosing only adds the card; the assignment is its button.
  const substitute = Candidates[Candidates.length - 1].FullName;
  await other.fill(substitute);
  await page.keyboard.press('Enter');
  await expect(other).toHaveValue(substitute);
  await service.getByRole('listitem').filter({ hasText: substitute }).last().getByRole('button', { name: 'Asignar' }).click();
  await expect(incident.getByText(`✓ Reasignado a ${substitute}`)).toBeVisible();

  await incident
    .getByRole('button', { name: /^Explicación: Asignación de/ })
    .first()
    .click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await dialog.getByRole('tab', { name: 'Para programadores' }).click();
  await dialog.getByRole('button', { name: 'Cerrar' }).click();
  await expect(dialog).toBeHidden();

  await page.getByRole('button', { name: 'Planificación' }).click();
  await page.getByLabel('Ir a la fecha').fill(iso(tuesday()));
  const barInfo = page.getByRole('table', { name: 'Línea de tiempo' }).getByRole('button', { name: /^Explicación: Asignación de/ });
  await expect(barInfo).toHaveCount(1);
  await barInfo.click();
  await expect(page.getByRole('dialog')).toBeVisible();
});

test('booking panel: reassigning shows only legal drivers, real hours, and assigns with ⓘ', async ({ page }) => {
  await planningDay(page);
  await page
    .getByRole('table', { name: 'Línea de tiempo' })
    .getByRole('button', { name: /Abrir reserva de/ })
    .first()
    .click();
  // The bar already opens the form of its trip; this one is of another trip, so everything is looked up inside it.
  const panel = page.getByRole('dialog');
  const reassign = panel.getByRole('button', { name: 'Reasignar' }).first();
  const name = (await reassign.getAttribute('aria-label')) ?? '';
  await reassign.click();
  const form = panel.getByRole('region', { name, exact: true });
  await expect(form.getByText(/^Conductores legales \(\d+\)/)).toBeVisible();
  await expect(form.getByRole('region', { name: 'Horas de conducción' })).toContainText(/\/ 9 h/);
  // The buttons stay at the bottom of the panel, with nothing to scroll, however long the list is.
  await expect(form.getByRole('button', { name: 'Confirmar asignación' })).toBeInViewport({ ratio: 1 });
  await expect(form.getByRole('button', { name: 'Cancelar' })).toBeInViewport({ ratio: 1 });
  await form.getByRole('button', { name: 'Confirmar asignación' }).click();
  await expect(form.getByText(/✓ Servicio #\d+: Asignado a/).first()).toBeVisible();
  // Inside this answer, as the server sealed it for this browser (SPEC 0097): checks in order, stages and what was saved.
  const inside = form.getByRole('button', { name: 'Ver por dentro' });
  await expect(inside).toHaveAttribute('aria-expanded', 'false');
  await inside.focus();
  await page.keyboard.press('Enter');
  const xray = form.getByRole('region', { name: 'Por dentro de esta respuesta' });
  await expect(xray.getByRole('listitem').filter({ hasText: 'CE 561/2006' }).first()).toContainText('cumple');
  await expect(xray.getByRole('table', { name: 'Tiempo por etapa' })).toBeVisible();
  await expect(xray).toContainText('Servicio × 1: guardado');
  await form
    .getByRole('button', { name: /^Explicación: Asignación del servicio/ })
    .first()
    .click();
  await expect(page.getByRole('dialog', { name: /Asignación del servicio/ })).toBeVisible();
});

interface SeededTrip {
  Id: number;
  BookingId: number;
  Start: string;
  Status: string;
  AssignedDriverId: number | null;
  AssignedVehicleId: number | null;
}

async function tuesdayTrips(page: Page): Promise<SeededTrip[]> {
  const day = tuesday();
  const range = new URLSearchParams({
    from: day.toISOString(),
    to: new Date(day.getTime() + 86_400_000).toISOString(),
    pageSize: '200',
  });
  const { Items } = (await (await page.request.get(`/api/trips?${range}`)).json()) as { Items: SeededTrip[] };
  return Items;
}

/** An assigned service of the seeded Tuesday, with some other legal driver and free bus, and in a booking with more trips. */
async function reassignableTrip(page: Page): Promise<SeededTrip> {
  for (const trip of (await tuesdayTrips(page)).filter(t => t.Status === 'Assigned' && t.AssignedDriverId != null && t.AssignedVehicleId != null)) {
    const booking = await (await page.request.get(`/api/bookings/${trip.BookingId}`)).json();
    if (booking.Trips.length < 2) continue;
    const options = await (await page.request.get(`/api/trips/${trip.Id}/driver-options`)).json();
    const buses = await (await page.request.get(`/api/trips/${trip.Id}/vehicle-candidates`)).json();
    if (options.Candidates.length > 0 && buses.length > 0) return trip;
  }
  throw new Error('No service with another legal driver and bus on Tuesday (was the week seeded?)');
}

/** Its form opens by itself. */
async function openFromTimeline(page: Page, trip: SeededTrip) {
  const { LicensePlate } = await (await page.request.get(`/api/vehicles/${trip.AssignedVehicleId}`)).json();
  const departure = new Date(trip.Start.endsWith('Z') ? trip.Start : `${trip.Start}Z`).toLocaleTimeString('es-ES', {
    hour: '2-digit',
    minute: '2-digit',
    timeZone: 'Europe/Madrid',
  });
  await planningDay(page);
  await page.getByRole('radio', { name: 'Vehículos' }).click();
  await page
    .getByRole('table', { name: 'Línea de tiempo' })
    .getByRole('row')
    .filter({ hasText: LicensePlate })
    .getByRole('button', { name: new RegExp(`· ${departure}–.*Abrir reserva de`) })
    .click();
}

test('booking panel: a trip opened from the timeline is marked and reassigned inside its own block', async ({ page }) => {
  const trip = await reassignableTrip(page);
  await openFromTimeline(page, trip);

  const panel = page.getByRole('dialog', { name: /^Reserva/ });
  const block = panel.locator('article[aria-current="true"]');
  await expect(block).toHaveCount(1);
  await expect(block.getByRole('heading', { name: /^Viaje \d+ de \d+/ })).toBeVisible();
  await expect(block.getByText('Seleccionado', { exact: true })).toBeVisible();
  await expect(block.getByText(`#${trip.Id}`, { exact: true })).toBeVisible();
  for (const label of ['Salida', 'Llegada', 'Duración', 'Conductor', 'Vehículo', 'Plazas', 'Tipo', 'Servicio'])
    await expect(block.locator('dt').filter({ hasText: new RegExp(`^${label}$`) })).toBeVisible();

  const form = block.getByRole('region', { name: /^Reasignar viaje \d+$/ });
  await expect(form.getByText(/^Reasignar viaje \d+$/)).toBeFocused();
  const chosen = form.getByRole('radio').last(); // the first one is the current driver
  await chosen.check();

  // Opening another trip discards nothing, and cancelling it gives the focus back to its button.
  const other = panel.getByRole('button', { name: /^(Asignar|Reasignar) viaje \d+$/ }).first();
  const otherName = (await other.getAttribute('aria-label')) ?? '';
  await other.click();
  await panel.getByRole('region', { name: otherName, exact: true }).getByRole('button', { name: 'Cancelar' }).click();
  await expect(panel.getByRole('button', { name: otherName, exact: true })).toBeFocused();
  await expect(chosen).toBeChecked();

  await form.getByRole('button', { name: 'Confirmar asignación' }).click();
  await expect(form.getByText(new RegExp(`✓ Servicio #${trip.Id}: Asignado a`))).toBeVisible();
  await form.getByRole('button', { name: 'Hecho' }).click();
  await expect(block.getByRole('button', { name: /^Reasignar viaje \d+$/ })).toBeFocused();
});

test('Km saving (Planning report) shows metrics and its ⓘ', async ({ page }) => {
  await planningDay(page);
  await page.getByRole('button', { name: 'Ahorro de km', exact: true }).click();
  const panel = page.getByRole('region', { name: 'Ahorro de km' });
  await panel.getByRole('button', { name: 'Calcular informe' }).click();
  await expect(panel.getByText('Km en vacío ahora', { exact: true })).toBeVisible();
  await panel.getByRole('button', { name: 'Explicación: Resumen del informe' }).click();
  await expect(page.getByRole('dialog', { name: 'Resumen del informe' })).toBeVisible();
  await page.keyboard.press('Escape');
  // Changing the checkbox recalculates on its own.
  const recalculated = page.waitForResponse(
    response => response.url().includes('/vehicle-chaining') && response.url().includes('returnToBase=false'),
  );
  await panel.getByLabel('Los autobuses vuelven a su base cada día').uncheck();
  expect((await recalculated).ok()).toBe(true);
  await expect(panel.getByText('Km en vacío ahora', { exact: true })).toBeVisible();
});

test('Km saving: open a service with the keyboard, change only its bus and go back to the report', async ({ page }) => {
  await planningDay(page);
  await page.getByRole('button', { name: 'Ahorro de km', exact: true }).click();
  const report = page.getByRole('region', { name: 'Ahorro de km' });
  await report.getByRole('button', { name: 'Calcular informe' }).click();
  const numbers = report.getByRole('button', { name: /^Abrir el servicio #\d+/ });
  await expect(numbers.first()).toBeVisible();
  const service = numbers.nth((await numbers.count()) - 1); // the last one, so that there is scroll position to restore
  const id = (await service.innerText()).slice(1);
  await service.focus();
  await page.keyboard.press('Enter');

  const panel = page.getByRole('dialog', { name: /^Reserva/ });
  await expect(panel.getByText('Propuesto en Ahorro de km', { exact: true })).toBeVisible();
  await expect(report).toBeHidden();
  await expect(panel.getByRole('radio', { name: /Actual/ })).toBeChecked();
  await expect(panel.getByRole('button', { name: 'Confirmar asignación' })).toBeDisabled();

  await page.keyboard.press('Escape');
  await expect(report.getByRole('button', { name: new RegExp(`^Abrir el servicio #${id} `) })).toBeFocused();
  await expect(report.getByText(/^Informe recalculado/)).toHaveCount(0);

  // With the proposed bus it is stored or rejected with its reason; either way the report recalculates.
  await page.keyboard.press('Enter');
  await panel.getByRole('button', { name: /^Elegir / }).click();
  await panel.getByRole('button', { name: 'Confirmar asignación' }).click();
  await expect(panel.getByText(`Servicio #${id}: `).first()).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(report.getByText(`Informe recalculado con lo guardado ahora. El servicio #${id} `)).toBeVisible();
});

test('Bookings: search and filter', async ({ page }) => {
  await page.goto('/#bookings');
  const table = page.getByRole('table', { name: 'Reservas' });
  await expect(table).toBeVisible();
  await page.getByLabel('Buscar reserva').fill('zzz-ninguna');
  await expect(table.getByText('Ninguna reserva coincide.')).toBeVisible();
});

test('new booking: origin and destination are searched, and only a chosen site counts', async ({ page }) => {
  await page.goto('/#bookings');
  await page.getByRole('button', { name: 'Nueva reserva' }).click();
  const panel = page.getByRole('dialog', { name: 'Nueva reserva' });
  await panel.getByLabel('Cliente').fill('Cliente de la búsqueda de sitios (E2E)');
  await panel.getByLabel('Descripción').fill('Origen y destino buscados');
  // Far from the seeded week, so that the other tests do not see this trip.
  const day = nextMonday();
  day.setDate(day.getDate() + 60);
  await panel.getByLabel('Fecha').fill(iso(day));
  const origin = panel.getByRole('combobox', { name: 'Origen' });
  const destination = panel.getByRole('combobox', { name: 'Destino' });
  const submit = panel.getByRole('button', { name: 'Crear reserva' });
  // Scoped to the list: the native selects of the form have options too.
  const options = page.getByRole('listbox').getByRole('option');

  // Accents do not matter and the words go in any order.
  await origin.fill('pio principe');
  await expect(options).toHaveCount(1);
  await page.keyboard.press('Enter');
  await expect(origin).toHaveValue('Intercambiador Príncipe Pío');
  await expect(panel).toBeVisible();

  // Typed but not chosen: neither Enter nor leaving the field turns it into a location, and Escape closes only the list.
  await destination.fill('zzz-ningún sitio');
  await expect(page.getByText('Nada coincide con «zzz-ningún sitio».').last()).toBeVisible();
  await page.keyboard.press('Enter');
  await page.keyboard.press('Escape');
  await expect(page.getByRole('listbox')).toHaveCount(0);
  await expect(panel).toBeVisible();
  await expect(destination).toHaveValue('');
  await destination.fill('moncloa');
  await page.keyboard.press('Tab');
  await expect(destination).toHaveValue('');
  await expect(submit).toBeDisabled();

  // With the mouse, and never more options than the cap.
  await destination.click();
  await expect(page.getByRole('group', { name: 'Ya en esta reserva' }).getByRole('option')).toHaveCount(1);
  expect(await options.count()).toBeLessThanOrEqual(20);
  await destination.pressSequentially('fuenlabrada');
  await options.filter({ hasText: 'Fuenlabrada - Estación Central' }).click();
  await expect(destination).toHaveValue('Fuenlabrada - Estación Central');

  // Emptying the field clears the choice; Escape gives it back while it is still being edited.
  await destination.fill('');
  await page.keyboard.press('Escape');
  await expect(destination).toHaveValue('Fuenlabrada - Estación Central');
  await panel.getByRole('button', { name: 'Quitar destino' }).click();
  await expect(destination).toHaveValue('');
  await expect(submit).toBeDisabled();
  await destination.fill('central fuenl');
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  await expect(destination).toHaveValue('Fuenlabrada - Estación Central');
  await expect(panel.getByLabel('Llegada')).not.toHaveValue('10:00'); // autofilled with the travel time

  // The form is complete, but in these fields Enter only opens the list and chooses.
  await page.keyboard.press('Enter');
  await expect(page.getByRole('listbox')).toBeVisible();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('listbox')).toHaveCount(0);
  await expect(submit).toBeEnabled();

  const creation = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/api/bookings'));
  await submit.click();
  const response = await creation;
  expect(response.ok()).toBe(true);
  const sites = (await (await page.request.get('/api/locations')).json()) as { Id: number; Name: string }[];
  const nameOf = (id: number) => sites.find(site => site.Id === id)?.Name;
  const [trip] = (await response.json()).Trips;
  expect(nameOf(trip.OriginLocationId)).toBe('Intercambiador Príncipe Pío');
  expect(nameOf(trip.DestinationLocationId)).toBe('Fuenlabrada - Estación Central');
  await expect(panel).toBeHidden();
});

test('times are shown in Europe/Madrid', async ({ page }) => {
  await planningDay(page);
  const bar = page.getByRole('table', { name: 'Línea de tiempo' }).locator('.bar').first();
  // The demo plan starts at 07:00 Madrid or later (in UTC the first one would be 05:00/06:00).
  await expect(bar).toHaveAttribute('title', /· (0[7-9]|1\d|2[0-3]):\d\d–/);
});

test('resource card: driver with hours, history and agenda; back closes it; vehicle with location; an upcoming service opens the booking', async ({
  page,
}) => {
  await page.goto('/#drivers');
  const drivers = page.getByRole('table', { name: 'Conductores' });
  await drivers.getByRole('button').first().click();
  let card = page.getByRole('dialog');
  await expect(page).toHaveURL(/[?&]conductor=\d+/);
  await expect(
    card
      .getByRole('region', { name: 'Horas de conducción' })
      .getByText(/\/ \d+ h/)
      .first(),
  ).toBeVisible();
  await card.getByRole('tab', { name: /^Historial/ }).click();
  await expect(page).toHaveURL(/pestana=historial/);
  await expect(card.getByRole('img', { name: /^Últimos 28 días/ })).toBeVisible();
  await card.getByRole('tab', { name: 'Agenda' }).click();
  await expect(page).toHaveURL(/pestana=agenda/);
  await expect(card.getByRole('region', { name: 'Pasadas' })).toBeVisible();
  // Switching tab adds no history entries: a single "back" closes the card and leaves the view.
  await page.goBack();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page).not.toHaveURL(/conductor=/);
  await expect(drivers).toBeVisible();

  await page.getByLabel('Buscar conductor').fill('zzzz-nadie');
  await expect(drivers.getByText('Ningún conductor coincide con la búsqueda.')).toBeVisible();

  await page.getByRole('button', { name: 'Vehículos' }).click();
  const plates = page.getByRole('table', { name: 'Vehículos' }).getByRole('button');
  const count = await plates.count();
  for (let index = 0; index < count; index++) {
    await plates.nth(index).click();
    card = page.getByRole('dialog');
    await expect(card.getByRole('region', { name: 'Ubicación actual' })).toBeVisible();
    const upcoming = card.getByRole('region', { name: 'Próximos servicios' }).getByRole('button');
    await expect(upcoming.first().or(card.getByText('Sin servicios en los próximos 14 días.'))).toBeVisible(); // services loaded
    if (await upcoming.count()) {
      await upcoming.first().click();
      // No stacked modals: the card closes and the booking panel opens on that trip.
      await expect(page.getByRole('dialog')).toHaveCount(1);
      await expect(page.getByRole('dialog', { name: /^Reserva/ })).toBeVisible();
      await expect(page).not.toHaveURL(/vehiculo=/);
      return;
    }
    await card.getByRole('button', { name: 'Cerrar ficha' }).click();
    await expect(page.getByRole('dialog')).toHaveCount(0);
  }
  throw new Error('No vehicle with upcoming services (was the week seeded?)');
});

const AGENDA_NOTES = 'E2E: agenda del vehículo';

/** Whatever happens in the test, no other one finds the bus off the road. */
async function clearAgenda(page: Page, vehicleId: number) {
  const vehicle = await (await page.request.get(`/api/vehicles/${vehicleId}`)).json();
  for (const entry of vehicle.Unavailabilities)
    if (entry.Notes === AGENDA_NOTES)
      await page.request.delete(`/api/vehicles/${vehicleId}/unavailabilities/${entry.Id}`, { headers: await writer(page.request) });
}

test('vehicle agenda: an ITV over an assigned service warns, stays visible in the card and in Planning, and is deleted', async ({ page }) => {
  // The demo covers the next seven days, whatever the day of the week.
  const tomorrow = new Date(Date.now() + 86_400_000);
  const range = new URLSearchParams({
    from: tomorrow.toISOString(),
    to: new Date(tomorrow.getTime() + 3 * 86_400_000).toISOString(),
    pageSize: '200',
  });
  const { Items } = await (await page.request.get(`/api/trips?${range}`)).json();
  const trip = Items.find(
    (candidate: { AssignedVehicleId: number | null; Status: string }) => candidate.AssignedVehicleId != null && candidate.Status === 'Assigned',
  );
  expect(trip, 'an assigned service in the next days (was the week seeded?)').toBeTruthy();
  const day = new Date(trip.Start.endsWith('Z') ? trip.Start : `${trip.Start}Z`).toLocaleDateString('sv-SE', { timeZone: 'Europe/Madrid' });
  const vehicleId: number = trip.AssignedVehicleId;
  const { LicensePlate } = await (await page.request.get(`/api/vehicles/${vehicleId}`)).json();
  try {
    // The former link to the tab still opens it.
    await page.goto(`/?vehiculo=${vehicleId}&pestana=indisponibilidades#vehicles`);
    const card = page.getByRole('dialog');
    await expect(card.getByRole('tab', { name: 'Agenda' })).toHaveAttribute('aria-selected', 'true');
    await card.getByRole('button', { name: 'Añadir entrada' }).click();
    const form = card.getByRole('form', { name: 'Nueva entrada' });
    await form.getByLabel('Tipo').selectOption({ label: 'ITV' });
    await form.getByLabel('Días completos').check();
    await form.getByLabel('Primer día').fill(day);
    await form.getByLabel('Último día (incluido)').fill(day);
    await form.getByLabel(/^Notas/).fill(AGENDA_NOTES);
    await expect(form.getByText(/^Pilla \d+ servicios? ya asignados?$/)).toBeVisible();
    await form.getByRole('button', { name: /^Guardar con \d+ conflictos?$/ }).click();
    await expect(card.getByText('Entrada añadida.')).toBeVisible();

    const entry = card.getByRole('region', { name: 'Vigentes y próximas' }).getByRole('listitem').filter({ hasText: AGENDA_NOTES });
    await expect(entry.getByText(/^\d+ servicios? afectados?$/)).toBeVisible();
    await entry
      .getByRole('list', { name: /afectados?$/ })
      .getByRole('button')
      .first()
      .click();
    // No stacked modals: the card closes and the booking opens.
    await expect(page.getByRole('dialog')).toHaveCount(1);
    const conflict = page.getByRole('dialog', { name: /^Reserva/ }).locator('article[aria-current="true"]');
    await expect(conflict.getByText('En conflicto con la agenda', { exact: true })).toBeVisible();
    await expect(conflict.getByText(new RegExp(`^${LicensePlate}\\. Indisponible: ITV`))).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(page.getByRole('dialog')).toHaveCount(0);

    await page.goto('/#planning');
    await page.getByLabel('Ir a la fecha').fill(day);
    await page.getByRole('radio', { name: 'Día' }).click();
    await page.getByRole('radio', { name: 'Vehículos' }).click();
    const row = page.getByRole('table', { name: 'Línea de tiempo' }).getByRole('row').filter({ hasText: LicensePlate });
    await expect(row.getByRole('img', { name: /^Indisponible: ITV/ })).toBeVisible();
    await expect(row.getByText(/^\d+ en conflicto con la agenda$/)).toBeVisible();
    await expect(
      row.getByRole('button', { name: new RegExp(`En conflicto con la agenda de ${LicensePlate}.*Abrir reserva de`) }).first(),
    ).toBeVisible();

    await page.goto(`/?vehiculo=${vehicleId}&pestana=agenda#vehicles`);
    const saved = page.getByRole('region', { name: 'Vigentes y próximas' }).getByRole('listitem').filter({ hasText: AGENDA_NOTES });
    await saved.getByRole('button', { name: /^Borrar ITV/ }).click();
    await saved.getByRole('group', { name: 'Confirmar borrado' }).getByRole('button', { name: 'Borrar', exact: true }).click();
    await expect(page.getByText('Entrada borrada.')).toBeVisible();
    await expect(saved).toHaveCount(0);
  } finally {
    await clearAgenda(page, vehicleId);
  }
});

test('vehicle data: an invalid plate is explained, fewer seats warn about the assigned services, and the change is seen everywhere', async ({
  page,
}) => {
  const tomorrow = new Date(Date.now() + 86_400_000);
  const range = new URLSearchParams({
    from: tomorrow.toISOString(),
    to: new Date(tomorrow.getTime() + 3 * 86_400_000).toISOString(),
    pageSize: '200',
  });
  const { Items } = await (await page.request.get(`/api/trips?${range}`)).json();
  const trip = Items.find(
    (candidate: { AssignedVehicleId: number | null; Status: string; RequiredSeats: number }) =>
      candidate.AssignedVehicleId != null && candidate.Status === 'Assigned' && candidate.RequiredSeats > 1,
  );
  expect(trip, 'an assigned service in the next days (was the week seeded?)').toBeTruthy();
  const url = `/api/vehicles/${trip.AssignedVehicleId}`;
  const before = await (await page.request.get(url)).json();
  const other = (await (await page.request.get('/api/vehicles')).json()).find((candidate: { Id: number }) => candidate.Id !== before.Id);
  const seats: number = trip.RequiredSeats - 1;
  const open = () => page.goto(`/?vehiculo=${before.Id}&pestana=resumen#vehicles`);
  try {
    await open();
    const data = page.getByRole('dialog').getByRole('region', { name: 'Datos' });
    await data.getByRole('button', { name: 'Editar' }).click();
    const form = data.getByRole('form', { name: 'Editar datos' });
    const plate = form.getByLabel('Matrícula');
    const save = form.getByRole('button', { name: 'Guardar' });
    await expect(plate).toBeFocused();
    await expect(save).toBeDisabled(); // nothing changed yet

    await plate.fill('');
    await expect(form.getByText('Escribe la matrícula.')).toBeVisible();
    await expect(save).toBeDisabled();
    await plate.fill(other.LicensePlate);
    await save.click();
    await expect(form.getByText(/ya es de otro vehículo/)).toBeVisible();
    await expect(plate).toBeFocused();
    await plate.fill(before.LicensePlate);
    await expect(form.getByText(/ya es de otro vehículo/)).toHaveCount(0);

    await form.getByLabel('Marca').fill('Marca E2E');
    await form.getByLabel('Plazas').fill(String(seats));
    const affected = form.getByRole('list', { name: new RegExp(`asignados? necesitan? más de ${seats} plazas?$`) });
    // No stacked modals: a service closes the card, and what was typed is there on coming back.
    await affected.getByRole('button').first().click();
    await expect(page.getByRole('dialog', { name: /^Reserva/ })).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await page
      .getByRole('table', { name: 'Vehículos' })
      .getByRole('button', { name: before.LicensePlate, exact: true })
      .click();
    await expect(form.getByText(/^Lo habías dejado a medias/)).toBeVisible();
    await expect(form.getByLabel('Marca')).toHaveValue('Marca E2E');
    await expect(affected).toBeVisible();
    await save.click();

    await expect(data.getByText('Datos guardados.')).toBeVisible();
    await expect(data.getByRole('button', { name: 'Editar' })).toBeFocused();
    await expect(data.getByText(`Marca E2E ${before.Model}`)).toBeVisible();
    await page.keyboard.press('Escape');
    const row = page.getByRole('table', { name: 'Vehículos' }).getByRole('row').filter({ hasText: before.LicensePlate });
    await expect(row.getByText(`Marca E2E ${before.Model}`)).toBeVisible();
    await expect(row.getByRole('cell', { name: String(seats), exact: true })).toBeVisible();
    // Nothing assigned was taken away.
    const kept = await (await page.request.get(`/api/trips/${trip.Id}`)).json();
    expect(kept.AssignedVehicleId).toBe(before.Id);
  } finally {
    await putVehicle(page, before.Id, before);
  }
});

test('vehicle data: somebody else saves first; what was typed stays, and the current data are loaded before saving', async ({ page }) => {
  const [before] = await (await page.request.get('/api/vehicles')).json();
  const url = `/api/vehicles/${before.Id}`;
  try {
    await page.goto(`/?vehiculo=${before.Id}&pestana=resumen#vehicles`);
    const data = page.getByRole('dialog').getByRole('region', { name: 'Datos' });
    await data.getByRole('button', { name: 'Editar' }).click();
    const form = data.getByRole('form', { name: 'Editar datos' });
    const save = form.getByRole('button', { name: 'Guardar' });
    await form.getByLabel('Marca').fill('Marca mía');
    await form.getByLabel('Modelo').fill('Modelo mío');
    await putVehicle(page, before.Id, { ...before, Model: 'Modelo ajeno' });

    await save.click();
    const notice = form.getByRole('alert').filter({ hasText: 'Otra persona ha cambiado estos datos' });
    await expect(notice).toBeVisible();
    await expect(form.getByLabel('Marca')).toHaveValue('Marca mía');
    await expect(form.getByLabel('Modelo')).toHaveValue('Modelo mío');
    expect((await (await page.request.get(url)).json()).Brand).toBe(before.Brand);

    await notice.getByRole('button', { name: 'Cargar los datos actuales' }).click();
    await expect(notice).toHaveCount(0);
    await expect(form.getByLabel('Matrícula')).toBeFocused();
    await expect(form.getByLabel('Marca')).toHaveValue('Marca mía');
    await expect(form.getByLabel('Modelo')).toHaveValue('Modelo ajeno');
    await save.click();
    await expect(data.getByText('Datos guardados.')).toBeVisible();
    await expect(data.getByText('Marca mía Modelo ajeno')).toBeVisible();
  } finally {
    await putVehicle(page, before.Id, before);
  }
});

test('booking panel: the driver and the bus are searched, and who cannot be chosen says why', async ({ page }) => {
  const trip = await reassignableTrip(page);
  const options = await (await page.request.get(`/api/trips/${trip.Id}/driver-options`)).json();
  const buses = await (await page.request.get(`/api/trips/${trip.Id}/vehicle-candidates?maxResults=1000`)).json();
  const driver = options.Candidates.at(-1) as { DriverId: number; FullName: string };
  const bus = buses.at(-1) as { VehicleId: number; LicensePlate: string };
  await openFromTimeline(page, trip);

  const panel = page.getByRole('dialog', { name: /^Reserva/ });
  const form = panel.getByRole('region', { name: /^Reasignar viaje \d+$/ });
  const driverField = form.getByRole('combobox', { name: 'Conductor' });
  const busField = form.getByRole('combobox', { name: 'Vehículo' });
  const list = page.getByRole('listbox');
  await expect(driverField).toBeVisible();

  // A discarded driver is found with the reason, but neither Enter nor leaving the field chooses them.
  if (options.Discarded.length > 0) {
    const before = await driverField.inputValue();
    await driverField.fill(options.Discarded[0].FullName);
    const blocked = list.getByRole('group', { name: 'No se pueden elegir' }).getByRole('option');
    await expect(blocked.first()).toContainText(options.Discarded[0].Reason);
    await page.keyboard.press('End');
    await page.keyboard.press('Enter');
    await expect(list).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(list).toHaveCount(0);
    await expect(panel).toBeVisible();
    await expect(driverField).toHaveValue(before);
  }

  await driverField.fill(driver.FullName.toUpperCase());
  await page.keyboard.press('Enter');
  await expect(driverField).toHaveValue(driver.FullName);
  // Its card is there with its explanation, however low it ranks.
  await expect(form.getByRole('radio', { name: new RegExp(driver.FullName) })).toBeChecked();

  await busField.fill(bus.LicensePlate.replace(/\W/g, '').toLowerCase());
  await list.getByRole('option').filter({ hasText: bus.LicensePlate }).first().click();
  await expect(busField).toHaveValue(bus.LicensePlate);
  // A choice is always needed: emptying the field gives it back.
  await busField.fill('');
  await page.keyboard.press('Tab');
  await expect(busField).toHaveValue(bus.LicensePlate);

  const allocation = page.waitForResponse(r => r.request().method() === 'POST' && r.url().includes(`/api/trips/${trip.Id}/allocate`));
  await form.getByRole('button', { name: 'Confirmar asignación' }).click();
  const sent: unknown = (await allocation).request().postDataJSON();
  expect(sent).toMatchObject({ DriverId: driver.DriverId, VehicleId: bus.VehicleId });
  await expect(form.getByText(new RegExp(`✓ Servicio #${trip.Id}: Asignado a ${driver.FullName}`))).toBeVisible();
});
