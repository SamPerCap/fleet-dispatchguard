import { describe, expect, it } from 'vitest';
import {
  baseChangeText,
  capacityText,
  detailsDrafts,
  draftForm,
  editDraft,
  errorsOf,
  formOf,
  hasEdits,
  inputOf,
  isChangedBySomebodyElse,
  isOutdated,
  liveDraft,
  lowersSeats,
  newDraft,
  onCurrentData,
  overCapacity,
  seatsOf,
  shortOfSeatsText,
  TEXT_FIELDS,
  withEdit,
} from './resourceDetails';
import type { AffectedTrip, Driver, Vehicle } from './types';

const driver: Driver = {
  Id: 7,
  IdentificationNumber: '12345678A',
  FirstName: 'Ana',
  LastName: 'López',
  IsExternal: false,
  AllowsAutoAssignment: true,
  IsActive: true,
  HomeLocationId: 4,
  Unavailabilities: [],
  DetailsVersion: 'v1',
  BaseId: 1,
};
const vehicle: Vehicle = {
  Id: 9,
  LicensePlate: '1234ABC',
  Brand: 'Irizar',
  Model: 'i6',
  SeatingCapacity: 55,
  IsExternal: false,
  AllowsAutoAssignment: true,
  IsActive: true,
  HomeLocationId: null,
  Unavailabilities: [],
  DetailsVersion: 'v1',
  BaseId: 2,
};

describe('formOf and inputOf', () => {
  it('an untouched driver goes back as it came', () => {
    expect(inputOf('driver', formOf(driver), 'v1')).toEqual({
      IdentificationNumber: '12345678A',
      FirstName: 'Ana',
      LastName: 'López',
      IsExternal: false,
      AllowsAutoAssignment: true,
      HomeLocationId: 4,
      BaseId: 1,
      DetailsVersion: 'v1',
    });
  });
  it('an untouched vehicle goes back as it came, with the seats as a number and no location as null', () => {
    expect(inputOf('vehicle', formOf(vehicle), 'v1')).toEqual({
      LicensePlate: '1234ABC',
      Brand: 'Irizar',
      Model: 'i6',
      SeatingCapacity: 55,
      IsExternal: false,
      AllowsAutoAssignment: true,
      HomeLocationId: null,
      BaseId: 2,
      DetailsVersion: 'v1',
    });
  });
  it('texts are trimmed and emptying the location clears it', () => {
    const form = { ...formOf(driver), FirstName: '  Luisa ', HomeLocationId: '' };
    expect(inputOf('driver', form, 'v1')).toMatchObject({ FirstName: 'Luisa', HomeLocationId: null });
  });
});

describe('errorsOf', () => {
  it('nothing wrong', () => {
    expect(errorsOf('driver', formOf(driver))).toEqual({});
    expect(errorsOf('vehicle', formOf(vehicle))).toEqual({});
  });
  it('empty or only spaces', () => {
    const form = { ...formOf(driver), IdentificationNumber: '', FirstName: '   ', LastName: '' };
    expect(errorsOf('driver', form)).toEqual({
      IdentificationNumber: 'Escribe el DNI.',
      FirstName: 'Escribe el nombre.',
      LastName: 'Escribe los apellidos.',
    });
  });
  it.each(TEXT_FIELDS.driver.map(field => [field.name, field.max] as const))('driver %s: %d characters and no more', (name, max) => {
    expect(errorsOf('driver', { ...formOf(driver), [name]: ` ${'x'.repeat(max)} ` })).toEqual({});
    expect(errorsOf('driver', { ...formOf(driver), [name]: 'x'.repeat(max + 1) })).toEqual({ [name]: `Como mucho ${max} caracteres.` });
  });
  it.each(TEXT_FIELDS.vehicle.map(field => [field.name, field.max] as const))('vehicle %s: %d characters and no more', (name, max) => {
    expect(errorsOf('vehicle', { ...formOf(vehicle), [name]: 'x'.repeat(max) })).toEqual({});
    expect(errorsOf('vehicle', { ...formOf(vehicle), [name]: 'x'.repeat(max + 1) })).toEqual({ [name]: `Como mucho ${max} caracteres.` });
  });
  it('the limits are the ones of the columns', () => {
    expect([...TEXT_FIELDS.driver, ...TEXT_FIELDS.vehicle].map(field => [field.name, field.max])).toEqual([
      ['FirstName', 100],
      ['LastName', 100],
      ['IdentificationNumber', 50],
      ['LicensePlate', 20],
      ['Brand', 100],
      ['Model', 100],
    ]);
  });
  it('seats', () => {
    const noSeats = { ...formOf(vehicle), SeatingCapacity: '0' };
    expect(errorsOf('vehicle', noSeats)).toEqual({ SeatingCapacity: 'Escribe un número entero, 1 o más.' });
    expect(errorsOf('vehicle', { ...formOf(vehicle), SeatingCapacity: '1' })).toEqual({});
  });
  it('a driver has no seats or plate to check', () => {
    expect(errorsOf('driver', { ...formOf(driver), SeatingCapacity: '', LicensePlate: '' })).toEqual({});
  });
});

describe('seatsOf', () => {
  it.each([
    ['55', 55],
    [' 55 ', 55],
    ['1', 1],
    ['2147483647', 2147483647],
    ['2147483648', null], // more than the API can read
    ['0', null],
    ['-5', null],
    ['5.5', null],
    ['5e1', null],
    ['', null],
    ['cincuenta', null],
  ])('%s → %s', (text, seats) => expect(seatsOf(text)).toBe(seats));
});

describe('withEdit', () => {
  const original = formOf(driver);
  it('keeps only what differs from the stored data', () => {
    const edits = withEdit({}, original, { FirstName: 'Luisa' });
    expect(edits).toEqual({ FirstName: 'Luisa' });
    expect(withEdit(edits, original, { IsExternal: true })).toEqual({ FirstName: 'Luisa', IsExternal: true });
    expect(withEdit(edits, original, { FirstName: 'Ana' })).toEqual({});
  });
});

describe('two people editing the same card', () => {
  const typed = editDraft(newDraft(driver), { FirstName: 'Luisa' });
  const changedByOther = { ...driver, LastName: 'Gómez', BaseId: 3, DetailsVersion: 'v2' };

  it('the version that is sent is the one of the data it was typed over, also after a reload', () => {
    const draft = editDraft(liveDraft(typed, changedByOther), { IsExternal: true });
    expect(draft.seen.version).toBe('v1');
    expect(isOutdated(draft, changedByOther)).toBe(true);
    expect(inputOf('driver', draftForm(draft), draft.seen.version)).toMatchObject({ LastName: 'López', DetailsVersion: 'v1' });
  });
  it('what was typed stays and the rest does not change under the eyes of who is typing', () => {
    expect(draftForm(liveDraft(typed, changedByOther))).toEqual({ ...formOf(driver), FirstName: 'Luisa' });
  });
  it('whoever typed nothing, or took it back, just sees the new data', () => {
    for (const untouched of [newDraft(driver), editDraft(typed, { FirstName: 'Ana' })]) {
      const draft = liveDraft(untouched, changedByOther);
      expect(isOutdated(draft, changedByOther)).toBe(false);
      expect(draftForm(draft)).toEqual(formOf(changedByOther));
      expect(editDraft(draft, { FirstName: 'Luisa' }).seen.version).toBe('v2');
    }
  });
  it('a change of the agenda is not a change of the data', () => {
    const onHoliday = { ...driver, Unavailabilities: [{ Id: 1, Start: '2026-11-25T08:00:00', End: '2026-11-26T08:00:00', Type: 'Vacation', Notes: null }] };
    expect(isOutdated(liveDraft(typed, onHoliday), onHoliday)).toBe(false);
  });
  it('changed and put back by the other person: nothing to load', () => {
    expect(isOutdated(typed, { ...driver })).toBe(false);
  });
  it('on the current data: what the other person did not touch keeps what was typed', () => {
    const draft = onCurrentData(typed, changedByOther);
    expect(draftForm(draft)).toEqual({ ...formOf(changedByOther), FirstName: 'Luisa' });
    expect(draft.seen.version).toBe('v2');
    expect(isOutdated(draft, changedByOther)).toBe(false);
  });
  it('on the current data: the fields that both changed show the current value', () => {
    const both = editDraft(typed, { LastName: 'Ruiz', HomeLocationId: '' });
    const draft = onCurrentData(both, { ...changedByOther, HomeLocationId: 9 });
    expect(draft.edits).toEqual({ FirstName: 'Luisa' });
    expect(draftForm(draft)).toMatchObject({ LastName: 'Gómez', HomeLocationId: '9' });
  });
  it('on the current data: both wrote the same, so nothing is left to save', () => {
    const draft = onCurrentData(typed, { ...driver, FirstName: 'Luisa', DetailsVersion: 'v2' });
    expect(hasEdits(draft)).toBe(false);
  });
  it('a vehicle: the seats typed over a number that changed are not kept, the brand is', () => {
    const draft = editDraft(newDraft(vehicle), { SeatingCapacity: '49', Brand: 'Setra' });
    const loaded = onCurrentData(draft, { ...vehicle, SeatingCapacity: 50, DetailsVersion: 'v2' });
    expect(draftForm(loaded)).toMatchObject({ SeatingCapacity: '50', Brand: 'Setra' });
  });
  it('is told from a repeated DNI or plate, which says its field', () => {
    expect(isChangedBySomebodyElse({ status: 409 })).toBe(true);
    expect(isChangedBySomebodyElse({ status: 409, field: 'LicensePlate' })).toBe(false);
    for (const status of [400, 404, 503, 500]) expect(isChangedBySomebodyElse({ status })).toBe(false);
  });
});

describe('seats and assigned services', () => {
  const now = new Date('2026-11-24T10:00:00Z');
  const trip = (TripId: number, RequiredSeats: number, extra: Partial<AffectedTrip> = {}) =>
    ({ TripId, RequiredSeats, Start: '2026-11-25T08:00:00', End: '2026-11-25T10:00:00', Status: 'Assigned', ...extra }) as AffectedTrip;

  it.each([
    ['54', true],
    ['55', false],
    ['56', false],
    ['', false],
  ])('from 55 to %s: lowered = %s', (seats, lowered) => {
    expect(lowersSeats({ ...formOf(vehicle), SeatingCapacity: seats }, vehicle)).toBe(lowered);
  });
  it('a service of 50 fits in 50 and not in 49', () => {
    expect(overCapacity([trip(1, 50)], 50, now)).toEqual([]);
    expect(overCapacity([trip(1, 50)], 49, now).map(t => t.TripId)).toEqual([1]);
  });
  it('finished and cancelled services do not count; a running one does', () => {
    const trips = [
      trip(1, 50, { End: '2026-11-24T10:00:00' }),
      trip(2, 50, { Status: 'Cancelled' }),
      trip(3, 50, { Status: 'Completed' }),
      trip(4, 50, { Start: '2026-11-24T09:00:00', End: '2026-11-24T10:01:00' }),
    ];
    expect(overCapacity(trips, 30, now).map(t => t.TripId)).toEqual([4]);
  });
  it('texts', () => {
    expect(capacityText(0, 49)).toBe('Ningún servicio asignado pendiente necesita más de 49 plazas.');
    expect(capacityText(1, 49)).toBe('1 servicio asignado necesita más de 49 plazas');
    expect(capacityText(3, 1)).toBe('3 servicios asignados necesitan más de 1 plaza');
    expect(shortOfSeatsText(50, 49)).toBe('Necesita 50 plazas y el vehículo tiene 49');
    expect(shortOfSeatsText(50, 50)).toBeNull();
  });
});

describe('baseChangeText', () => {
  it('names both bases and says what does not change', () => {
    const text = baseChangeText('driver', 'Madrid', 'València');
    expect(text).toContain('deja de contar en Madrid y pasa a contar en València');
    expect(text).toContain('todas las bases');
    expect(text).toContain('siguen asignados');
  });
});

describe('drafts', () => {
  it('are kept per driver or vehicle until they are cleared', () => {
    const draft = editDraft(newDraft(driver), { FirstName: 'Luisa' });
    detailsDrafts.write('driver-7', draft);
    expect(detailsDrafts.read('driver-7')).toEqual(draft);
    expect(detailsDrafts.read('vehicle-7')).toBeNull();
    detailsDrafts.write('driver-7', null);
    expect(detailsDrafts.read('driver-7')).toBeNull();
  });
});
