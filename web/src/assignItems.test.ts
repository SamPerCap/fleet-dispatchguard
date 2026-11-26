import { describe, expect, it } from 'vitest';
import { busItems, DRIVER_CARDS, driverCards, driverItems, leaveItems, replacementItems, whyBusNotOffered } from './assignItems';
import { MAX_OPTIONS, searchItems } from './pickerSearch';
import type { Driver, DriverCandidate, DriverOptions, Vehicle, VehicleCandidate } from './types';
import { proposedText } from './vehicleChange';

const trip = {
  AssignedDriverId: 1,
  AssignedDriverName: 'Ana Ruiz',
  AssignedVehicleId: 70,
  AssignedVehiclePlate: '7000-AAA',
  Start: '2026-11-25T08:00:00Z',
  End: '2026-11-25T10:00:00Z',
  RequiredSeats: 50,
};
const unassigned = { ...trip, AssignedDriverId: null, AssignedDriverName: null, AssignedVehicleId: null, AssignedVehiclePlate: null };

const driver = (DriverId: number, FullName: string, rest: Partial<DriverCandidate> = {}) =>
  ({ Rank: DriverId, DriverId, FullName, DistanceKm: 5, Reason: 'base a 5 km', Warnings: [], ...rest }) as DriverCandidate;
const options = (Candidates: DriverCandidate[], Discarded: DriverOptions['Discarded'] = []): DriverOptions => ({ Candidates, Discarded });

const bus = (VehicleId: number, LicensePlate: string) =>
  ({ VehicleId, LicensePlate, SeatingCapacity: 55, Reason: 'base = salida' }) as VehicleCandidate;
const vehicle = (Id: number, LicensePlate: string, rest: Partial<Vehicle> = {}): Vehicle => ({
  Id,
  LicensePlate,
  Brand: 'Irizar',
  Model: 'i6',
  SeatingCapacity: 55,
  IsExternal: false,
  AllowsAutoAssignment: true,
  IsActive: true,
  HomeLocationId: null,
  Unavailabilities: [],
  DetailsVersion: 'v1',
  ...rest,
});
const agenda = [{ Id: 1, Start: '2026-11-25T09:00:00Z', End: '2026-11-25T12:00:00Z', Type: 'Repair', Notes: null }];

describe('driverItems', () => {
  it('driverItems_CurrentFirst_ThenInTheOrderOfTheEngine', () => {
    const items = driverItems(trip, options([driver(9, 'Zoe Gil'), driver(4, 'Blas Mora')]));
    expect(items.map(i => i.label)).toEqual(['Ana Ruiz', 'Zoe Gil', 'Blas Mora']);
    expect(items[0].tags).toEqual(['Actual']);
    expect(searchItems(items, '').matches.map(i => i.id)).toEqual([1, 9, 4]);
  });

  it('driverItems_StaleListWithTheCurrentOne_IsNotRepeated', () => {
    expect(driverItems(trip, options([driver(1, 'Ana Ruiz'), driver(2, 'Luis Gil')])).map(i => i.id)).toEqual([1, 2]);
  });

  it('driverItems_NoDriverYet_OnlyCandidates', () => {
    expect(driverItems(unassigned, options([driver(2, 'Luis Gil')])).map(i => i.id)).toEqual([2]);
  });

  it('driverItems_CurrentWithoutName_ShowsTheNumber', () => {
    expect(driverItems({ ...trip, AssignedDriverName: null }, options([]))[0].label).toBe('#1');
  });

  it('driverItems_DetailAndTags_SayWhatTheCardSays', () => {
    const candidate = driver(2, 'Luis Gil', { CoversSameBooking: true, WonTieBreak: true, Warnings: ['Cerca del límite'] });
    const [, item] = driverItems(trip, options([candidate]), 2);
    expect(item.tags).toEqual(['Recomendado', 'Misma reserva', 'Desempate Art. 8.6']);
    expect(item.detail).toMatch(/^5(,0)? km · base a 5 km · ⚠ Cerca del límite$/);
    expect(driverItems(trip, options([driver(3, 'Eva', { DistanceKm: null })]))[1].detail).toMatch(/^sin base · /);
  });

  it('driverItems_Discarded_FoundWithItsReason_ButBlocked', () => {
    const items = driverItems(trip, options([driver(2, 'Luis Gil')], [{ DriverId: 5, FullName: 'Óscar Gil', Reason: 'De vacaciones' }]));
    expect(searchItems(items, '').matches.map(i => i.id)).toEqual([1, 2]);
    const found = searchItems(items, 'gil');
    expect(found.matches.map(i => i.id)).toEqual([2]);
    expect(found.blocked).toEqual([{ id: 5, label: 'Óscar Gil', detail: 'De vacaciones', blocked: true }]);
  });

  it('driverItems_LargeCompany_EveryLegalDriverIsReachableAndTheListIsCapped', () => {
    const items = driverItems(trip, options(Array.from({ length: 300 }, (_, index) => driver(index + 10, `Conductor ${index}`))));
    expect(searchItems(items, '')).toMatchObject({ total: 301 });
    expect(searchItems(items, '').matches).toHaveLength(MAX_OPTIONS);
    expect(searchItems(items, 'conductor 299').matches.map(i => i.label)).toEqual(['Conductor 299']);
  });
});

describe('leaveItems', () => {
  const person = (Id: number, FirstName: string, rest: Partial<Driver> = {}) =>
    ({ Id, FirstName, LastName: 'Gil', IdentificationNumber: `1234567${Id}Z`, IsActive: true, IsExternal: false, ...rest }) as Driver;

  it('leaveItems_AlphabeticalInSpanish', () => {
    const items = leaveItems([person(1, 'Zoe'), person(2, 'Óscar'), person(3, 'Ana')]);
    expect(items.map(i => i.label)).toEqual(['Ana Gil', 'Óscar Gil', 'Zoe Gil']);
  });

  it('leaveItems_FoundByNameOrIdNumber', () => {
    const items = leaveItems([person(1, 'Zoe'), person(2, 'Óscar')]);
    expect(searchItems(items, 'oscar').matches.map(i => i.id)).toEqual([2]);
    expect(searchItems(items, '12345671z').matches.map(i => i.id)).toEqual([1]);
  });

  it('leaveItems_Inactive_OnlyWhenSearchedAndBlocked', () => {
    const items = leaveItems([person(1, 'Zoe'), person(2, 'Óscar', { IsActive: false })]);
    expect(searchItems(items, '').matches.map(i => i.id)).toEqual([1]);
    expect(searchItems(items, 'oscar')).toMatchObject({ matches: [], blocked: [{ id: 2, detail: '12345672Z · inactivo' }] });
  });

  it('leaveItems_External_CanBeOnLeave', () => {
    const [item] = leaveItems([person(1, 'Zoe', { IsExternal: true })]);
    expect(item.detail).toBe('12345671Z · externo');
    expect(item.blocked).toBeUndefined();
  });
});

describe('replacementItems', () => {
  const onLeave = { id: 1, name: 'Ana Ruiz' };

  it('replacementItems_DriverOnLeave_IsNeverOffered', () => {
    const discarded = [{ DriverId: 1, FullName: 'Ana Ruiz', Reason: 'Baja' }];
    for (const items of [
      replacementItems(options([driver(2, 'Luis Gil')]), onLeave),
      replacementItems(options([driver(2, 'Luis Gil')], discarded), onLeave),
      replacementItems(options([driver(1, 'Ana Ruiz'), driver(2, 'Luis Gil')]), onLeave),
    ]) {
      expect(items.filter(i => !i.blocked).map(i => i.id)).toEqual([2]);
      expect(searchItems(items, 'ana').blocked).toEqual([
        { id: 1, label: 'Ana Ruiz', detail: 'De baja: es a quien se sustituye', blocked: true },
      ]);
    }
  });

  it('replacementItems_DiscardedKeepTheReasonOfTheEngine', () => {
    const items = replacementItems(options([], [{ DriverId: 5, FullName: 'Óscar Gil', Reason: 'De vacaciones' }]), onLeave);
    expect(searchItems(items, 'gil').blocked).toMatchObject([{ id: 5, detail: 'De vacaciones' }]);
  });
});

describe('driverCards', () => {
  const ids = (count: number, chosen: number | null) =>
    driverCards(Array.from({ length: count }, (_, index) => ({ DriverId: index + 1 })), chosen).map(c => c.DriverId);

  it('driverCards_UpToTheLimit_All', () => {
    expect(ids(DRIVER_CARDS, null)).toEqual([1, 2, 3, 4, 5]);
    expect(ids(0, 3)).toEqual([]);
  });

  it('driverCards_More_TheBestOnesAndTheChosenOne', () => {
    expect(ids(6, null)).toEqual([1, 2, 3, 4, 5]);
    expect(ids(40, 2)).toEqual([1, 2, 3, 4, 5]);
    expect(ids(40, 37)).toEqual([1, 2, 3, 4, 5, 37]);
    expect(ids(40, 999)).toEqual([1, 2, 3, 4, 5]);
  });
});

describe('busItems', () => {
  const fleet = [vehicle(70, '7000-AAA'), vehicle(71, '1234-ABC'), vehicle(72, '5678-XYZ', { Brand: 'Mercedes', Model: 'Tourismo' })];
  const items = (proposal = null as Parameters<typeof busItems>[3]) =>
    busItems(trip, [bus(72, '5678-XYZ'), bus(71, '1234-ABC')], 71, proposal, fleet);
  const found = (query: string) => searchItems(items(), query).matches.map(i => i.id);

  it('busItems_CurrentFirst_ThenInTheOrderOfTheEngine', () => {
    expect(items().map(i => i.id)).toEqual([70, 72, 71]);
    expect(items()[0]).toMatchObject({ label: '7000-AAA', tags: ['Actual'], detail: 'Irizar i6 · 55 plazas' });
    expect(items()[2]).toMatchObject({ tags: ['Recomendado'], detail: 'Irizar i6 · 55 plazas · base = salida' });
  });

  it('busItems_PlateWithOrWithoutSeparators', () => {
    for (const query of ['1234-ABC', '1234abc', '1234 abc', '34ab', 'abc 1234']) expect(found(query)).toEqual([71]);
  });

  it('busItems_ByModelAndSeats', () => {
    expect(found('tourismo')).toEqual([72]);
    expect(found('irizar')).toEqual([70, 71]);
    expect(found('55 plazas')).toEqual([70, 72, 71]);
  });

  it('busItems_ProposedOutsideTheList_CanBeFoundAndIsMarked', () => {
    const proposal = { vehicleId: 99, plate: '9900-ZZZ', currentVehicleId: 70, currentPlate: '7000-AAA' };
    const item = items(proposal).find(i => i.id === 99)!;
    expect(item).toMatchObject({ tags: [proposedText()], detail: 'Se comprueba al guardar' });
    expect(item.blocked).toBeUndefined();
  });

  it('busItems_FleetNotOffered_OnlyWhenSearched_WithTheReason', () => {
    const all = busItems(trip, [bus(71, '1234-ABC')], null, null, [...fleet, vehicle(80, '8000-BBB', { SeatingCapacity: 38 })]);
    expect(searchItems(all, '').matches.map(i => i.id)).toEqual([70, 71]);
    expect(searchItems(all, '8000bbb').blocked).toMatchObject([{ id: 80, detail: 'Tiene 38 plazas; el servicio necesita 50' }]);
  });
});

describe('whyBusNotOffered', () => {
  const why = (rest: Partial<Vehicle>) => whyBusNotOffered(trip, vehicle(1, 'X', rest));

  it('whyBusNotOffered_FirstReasonThatApplies', () => {
    expect(why({ IsActive: false, SeatingCapacity: 10 })).toBe('Vehículo no activo');
    expect(why({ IsExternal: true })).toBe('Vehículo externo');
    expect(why({ AllowsAutoAssignment: false })).toBe('No es auto-asignable');
    expect(why({ SeatingCapacity: 49 })).toBe('Tiene 49 plazas; el servicio necesita 50');
    expect(why({ Unavailabilities: agenda })).toMatch(/^Indisponible: /);
  });

  it('whyBusNotOffered_NothingKnown_DoesNotClaimAnOverlap', () => {
    expect(why({ SeatingCapacity: 50 })).toBe('Ocupado con otro servicio o no apto al calcular la lista');
    expect(why({ Unavailabilities: [{ ...agenda[0], Start: trip.End }] })).toBe('Ocupado con otro servicio o no apto al calcular la lista');
  });
});
