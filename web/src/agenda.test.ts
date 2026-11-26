import { describe, expect, it } from 'vitest';
import {
  AGENDA_TYPES,
  affectedText,
  caughtBy,
  draftKey,
  formOf,
  inputOf,
  newForm,
  periodOf,
  periodText,
  readDraft,
  spanOf,
  typeHint,
  usableDraft,
  withStartDate,
  writeDraft,
  type AgendaForm,
} from './agenda';
import { UNAVAILABILITY_LABEL } from './resources';
import type { Unavailability } from './types';

const now = new Date('2026-11-24T10:00:00Z');
const entry = (Start: string, End: string, extra: Partial<Unavailability> = {}): Unavailability => ({
  Id: 1,
  Start,
  End,
  Type: 'TechnicalInspection',
  Notes: null,
  ServiceProvider: null,
  ...extra,
});
const form = (extra: Partial<AgendaForm> = {}): AgendaForm => ({ ...newForm('vehicle', now), ...extra });
const days = (startDate: string, endDate: string) => form({ wholeDays: true, startDate, endDate });
const iso = (value: ReturnType<typeof periodOf>) => (value.ok ? [value.start.toISOString(), value.end.toISOString()] : value.error);

describe('types', () => {
  it('every offered type has a Spanish name', () => {
    for (const type of [...AGENDA_TYPES.driver, ...AGENDA_TYPES.vehicle]) expect(UNAVAILABILITY_LABEL[type]).toBeTruthy();
  });
  it('only sick leave and weekly rest carry a warning', () => {
    expect(typeHint('SickLeave')).toContain('médicos');
    expect(typeHint('WeeklyRest')).toContain('No declara el descanso semanal');
    expect(typeHint('Vacation')).toBeNull();
  });
});

describe('newForm', () => {
  it('a driver starts with whole days and a vehicle with hours, today in Madrid', () => {
    const lateEvening = new Date('2026-11-24T23:30:00Z'); // 00:30 of the 25th in Madrid
    expect(newForm('driver', lateEvening)).toMatchObject({ type: 'Vacation', wholeDays: true, startDate: '2026-11-25', endDate: '2026-11-25' });
    expect(newForm('vehicle', now)).toMatchObject({ type: 'TechnicalInspection', wholeDays: false, startDate: '2026-11-24' });
  });
});

describe('periodOf', () => {
  it('with hours', () => {
    expect(iso(periodOf(form({ startDate: '2026-11-25', endDate: '2026-11-25' })))).toEqual([
      '2026-11-25T07:00:00.000Z',
      '2026-11-25T09:00:00.000Z',
    ]);
  });
  it.each([
    ['2026-11-24', '2026-11-24', '2026-11-23T23:00:00.000Z', '2026-11-24T23:00:00.000Z'],
    ['2026-10-25', '2026-10-25', '2026-10-24T22:00:00.000Z', '2026-10-25T23:00:00.000Z'], // 25 h
    ['2026-03-29', '2026-03-29', '2026-03-28T23:00:00.000Z', '2026-03-29T22:00:00.000Z'], // 23 h
    ['2026-08-01', '2026-08-31', '2026-07-31T22:00:00.000Z', '2026-08-31T22:00:00.000Z'],
  ])('whole days %s – %s', (first, last, start, end) => expect(iso(periodOf(days(first, last)))).toEqual([start, end]));

  it.each<[string, AgendaForm, string]>([
    ['no date', form({ startDate: '' }), 'Elige la fecha de inicio y la de fin.'],
    ['half-typed year', form({ endDate: '0202-11-24' }), 'La fecha no es válida.'],
    ['five-digit year', days('2026-11-24', '20266-11-24'), 'La fecha no es válida.'],
    ['no time', form({ endTime: '' }), 'Elige la hora de inicio y la de fin.'],
    ['empty', form({ startTime: '10:00', endTime: '10:00' }), 'El fin debe ser posterior al inicio.'],
    ['inverted', form({ startTime: '10:01', endTime: '10:00' }), 'El fin debe ser posterior al inicio.'],
    ['inverted days', days('2026-11-25', '2026-11-24'), 'El último día no puede ser anterior al primero.'],
    [
      'skipped hour',
      form({ startDate: '2026-03-29', startTime: '02:30', endDate: '2026-03-29', endTime: '05:00' }),
      'La hora de inicio no existe ese día por el cambio de hora.',
    ],
  ])('rejects: %s', (_, invalid, error) => expect(iso(periodOf(invalid))).toBe(error));

  it('what was not touched keeps its instant, even inside the repeated hour or with seconds', () => {
    const original = entry('2026-10-25T01:30:15Z', '2026-10-25T09:00:00Z'); // the second 02:30
    expect(iso(periodOf(formOf(original), original))).toEqual(['2026-10-25T01:30:15.000Z', '2026-10-25T09:00:00.000Z']);
    expect(iso(periodOf({ ...formOf(original), endTime: '11:00' }, original))).toEqual([
      '2026-10-25T01:30:15.000Z',
      '2026-10-25T10:00:00.000Z',
    ]);
    expect(iso(periodOf({ ...formOf(original), startTime: '02:45' }, original))[0]).toBe('2026-10-25T00:45:00.000Z');
  });
  it('switching to whole days takes the days typed', () => {
    const original = entry('2026-11-25T07:00:00Z', '2026-11-25T09:00:00Z');
    expect(iso(periodOf({ ...formOf(original), wholeDays: true }, original))).toEqual([
      '2026-11-24T23:00:00.000Z',
      '2026-11-25T23:00:00.000Z',
    ]);
  });
});

describe('formOf', () => {
  it('whole days come back as first and last day', () => {
    expect(formOf(entry('2026-10-24T22:00:00', '2026-10-25T23:00:00'))).toMatchObject({
      wholeDays: true,
      startDate: '2026-10-25',
      endDate: '2026-10-25',
    });
  });
  it('hours come back in Madrid, with its notes and workshop', () => {
    expect(formOf(entry('2026-11-25T07:00:00', '2026-11-26T09:30:00', { Notes: 'Cita', ServiceProvider: 'ITV Getafe' }))).toMatchObject({
      wholeDays: false,
      startDate: '2026-11-25',
      startTime: '08:00',
      endDate: '2026-11-26',
      endTime: '10:30',
      notes: 'Cita',
      provider: 'ITV Getafe',
    });
  });
});

it('withStartDate drags the end only when it is left behind', () => {
  expect(withStartDate(days('2026-11-24', '2026-11-25'), '2026-11-30').endDate).toBe('2026-11-30');
  expect(withStartDate(days('2026-11-24', '2026-11-28'), '2026-11-25').endDate).toBe('2026-11-28');
  expect(withStartDate(days('2026-11-24', '2026-11-28'), '').endDate).toBe('2026-11-28');
});

it('inputOf: the workshop only for vehicles and blank texts as none', () => {
  const period = { start: new Date('2026-11-25T07:00:00Z'), end: new Date('2026-11-25T09:00:00Z') };
  expect(inputOf('vehicle', form({ notes: '  ', provider: ' ITV Getafe ' }), period)).toEqual({
    Start: '2026-11-25T07:00:00.000Z',
    End: '2026-11-25T09:00:00.000Z',
    Type: 'TechnicalInspection',
    Notes: null,
    ServiceProvider: 'ITV Getafe',
  });
  expect(inputOf('driver', form({ type: 'Training' }), period)).not.toHaveProperty('ServiceProvider');
});

it.each([
  ['2026-11-25T07:00:00', '2026-11-25T09:00:00', '25/11/2026, 08:00 – 10:00'],
  ['2026-11-25T07:00:00', '2026-11-26T09:00:00', '25/11/2026, 08:00 – 26/11/2026, 10:00'],
  ['2026-11-24T23:00:00', '2026-11-25T23:00:00', '25/11/2026 · todo el día'],
  ['2026-12-30T23:00:00', '2027-01-02T23:00:00', '31/12/2026 – 02/01/2027 · días completos'],
])('periodText %s – %s', (start, end, text) => expect(periodText(entry(start, end))).toBe(text));

describe('caughtBy', () => {
  const trip = (id: number, Start: string, End: string, Status = 'Assigned') => ({
    Id: id,
    Start,
    End,
    Status,
    AssignedDriverId: 1,
    AssignedVehicleId: 1,
  });
  const itv = entry('2026-11-24T08:00:00', '2026-11-24T14:00:00');
  it('[start, end), without cancelled ones, and what already finished apart', () => {
    const caught = caughtBy(
      itv,
      [
        trip(1, '2026-11-24T06:00:00', '2026-11-24T08:00:00'), // ends when it starts
        trip(2, '2026-11-24T08:00:00', '2026-11-24T10:00:00'), // ends exactly now
        trip(3, '2026-11-24T09:00:00', '2026-11-24T10:01:00'), // running
        trip(4, '2026-11-24T13:59:00', '2026-11-24T16:00:00'),
        trip(5, '2026-11-24T14:00:00', '2026-11-24T16:00:00'), // leaves when it ends
        trip(6, '2026-11-24T12:00:00', '2026-11-24T13:00:00', 'Cancelled'),
        trip(7, '2026-11-24T12:00:00', '2026-11-24T13:00:00', 'Completed'),
      ],
      now,
    );
    expect(caught.open.map(t => t.Id)).toEqual([3, 4]);
    expect(caught.finished.map(t => t.Id)).toEqual([2, 7]);
  });
  it('singular and plural', () => {
    expect(affectedText(1)).toBe('1 servicio afectado');
    expect(affectedText(3)).toBe('3 servicios afectados');
  });
});

it('spanOf covers every entry', () => {
  expect(spanOf([])).toBeNull();
  const span = spanOf([entry('2026-11-26T08:00:00', '2026-12-26T08:00:00'), entry('2026-11-25T08:00:00', '2026-11-25T09:00:00')]);
  expect([span!.start.toISOString(), span!.end.toISOString()]).toEqual(['2026-11-25T08:00:00.000Z', '2026-12-26T08:00:00.000Z']);
});

describe('drafts', () => {
  it('one per resource, until it is cleared', () => {
    const key = draftKey('driver', 7);
    writeDraft(key, { entryId: null, form: form({ notes: 'a medias' }) });
    expect(readDraft(key)?.form.notes).toBe('a medias');
    expect(readDraft(draftKey('vehicle', 7))).toBeNull();
    writeDraft(key, null);
    expect(readDraft(key)).toBeNull();
  });
  it('the draft of an entry that disappeared or ended is dropped', () => {
    const draft = { entryId: 1, form: form() };
    expect(usableDraft(draft, [entry('2026-11-24T08:00:00', '2026-11-24T10:01:00')], now)).toBe(draft);
    expect(usableDraft(draft, [entry('2026-11-24T08:00:00', '2026-11-24T10:00:00')], now)).toBeNull();
    expect(usableDraft(draft, [], now)).toBeNull();
    expect(usableDraft({ ...draft, entryId: null }, [], now)).not.toBeNull();
  });
});
