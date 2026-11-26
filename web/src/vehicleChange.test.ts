import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { ProposalNote } from './AssignForm';
import type { BookingTrip, Explanation, Vehicle, VehicleCandidate } from './types';
import {
  busesOutByAgenda,
  changeOf,
  choiceText,
  clashText,
  doneText,
  forcedNotice,
  pickDriver,
  pickVehicle,
  proposedText,
  proposalState,
  returnNote,
  vehicleOptions,
  type BusProposal,
} from './vehicleChange';

const trip = {
  Id: 10,
  AssignedDriverId: 1,
  AssignedDriverName: 'Ana',
  AssignedVehicleId: 70,
  AssignedVehiclePlate: '7000AAA',
  ForcedDescription: null,
};
const bare = { ...trip, AssignedDriverId: null, AssignedDriverName: null, AssignedVehicleId: null, AssignedVehiclePlate: null };
const bus = (VehicleId: number, LicensePlate: string) =>
  ({ VehicleId, LicensePlate, SeatingCapacity: 50, Reason: 'base = salida' }) as VehicleCandidate;
const three = [bus(71, '7100BBB'), bus(72, '7200CCC'), bus(73, '7300DDD')];
const proposal = (vehicleId: number, plate: string): BusProposal => ({ vehicleId, plate, currentVehicleId: 70, currentPlate: '7000AAA' });

describe('driver chosen when reassigning', () => {
  const candidates = [{ DriverId: 2 }, { DriverId: 3 }];
  it('from the report the current one is kept; otherwise, the first one as before', () => {
    expect(pickDriver(null, trip, candidates, true)).toBe(1);
    expect(pickDriver(null, trip, candidates, false)).toBe(2);
  });
  it('what the user chose survives recalculating, also the current one, if it is still there', () => {
    expect(pickDriver(3, trip, candidates, true)).toBe(3);
    expect(pickDriver(1, trip, candidates, false)).toBe(1);
    expect(pickDriver(9, trip, candidates, true)).toBe(1);
    expect(pickDriver(9, trip, candidates, false)).toBe(2);
  });
  it('no candidates: the current one from the report; otherwise, nobody', () => {
    expect(pickDriver(null, trip, [], true)).toBe(1);
    expect(pickDriver(null, trip, [], false)).toBeNull();
  });
  it('a trip without driver has nobody to keep', () => {
    expect(pickDriver(null, bare, candidates, true)).toBe(2);
    expect(pickDriver(null, bare, [], true)).toBeNull();
  });
});

describe('buses offered', () => {
  const ids = (options: { id: number }[]) => options.map(x => x.id);
  it('without a report, as before: the current one and the candidates', () => {
    const options = vehicleOptions(trip, three, 72, null);
    expect(ids(options)).toEqual([70, 71, 72, 73]);
    expect(options[0]).toEqual({ id: 70, label: '7000AAA', tags: ['Actual'], detail: '' });
    expect(options[2]).toEqual({ id: 72, label: '7200CCC', tags: ['Recomendado'], seats: 50, detail: 'base = salida' });
    expect(options.some(x => x.tags.includes(proposedText()))).toBe(false);
  });
  it('a proposed bus not listed by the panel is added, marked and without claiming that it is valid', () => {
    const options = vehicleOptions(trip, three, null, proposal(99, '9900ZZZ'));
    expect(ids(options)).toEqual([70, 99, 71, 72, 73]);
    expect(options[1]).toEqual({ id: 99, label: '9900ZZZ', tags: [proposedText()], detail: 'Se comprueba al guardar' });
  });
  it('if it is already among the candidates it is marked, not repeated', () => {
    const options = vehicleOptions(trip, three, 72, proposal(72, '7200CCC'));
    expect(ids(options)).toEqual([70, 71, 72, 73]);
    expect(options[2].tags).toEqual(['Recomendado', proposedText()]);
  });
  it('if it is already the current one, the current one is marked', () => {
    const options = vehicleOptions(
      { ...trip, AssignedVehicleId: 99, AssignedVehiclePlate: '9900ZZZ' },
      three,
      null,
      proposal(99, '9900ZZZ'),
    );
    expect(ids(options)).toEqual([99, 71, 72, 73]);
    expect(options[0].tags).toEqual(['Actual', proposedText()]);
  });
  it('without candidates the proposed one can still be chosen', () => {
    expect(ids(vehicleOptions(trip, [], null, proposal(99, '9900ZZZ')))).toEqual([70, 99]);
  });
  it('the proposed one is never preselected', () => {
    const proposed = proposal(99, '9900ZZZ');
    expect(pickVehicle(null, trip, three, proposed)).toBe(70);
    expect(pickVehicle(null, bare, three, proposed)).toBe(71);
    expect(pickVehicle(null, bare, [], proposed)).toBeNull();
  });
  it('what the user chose is kept if it is still listed, also the proposed one', () => {
    expect(pickVehicle(99, trip, three, proposal(99, '9900ZZZ'))).toBe(99);
    expect(pickVehicle(99, trip, three, null)).toBe(70);
    expect(pickVehicle(73, trip, three, null)).toBe(73);
    expect(pickVehicle(73, trip, [bus(71, '7100BBB')], null)).toBe(70);
    expect(pickVehicle(70, trip, three, null)).toBe(70);
  });
});

describe('what changes and how it is said', () => {
  it('only the bus, something else or nothing', () => {
    expect(changeOf(trip, 1, 70)).toBe('none');
    expect(changeOf(trip, 1, 71)).toBe('vehicle');
    expect(changeOf(trip, 2, 70)).toBe('other');
    expect(changeOf(trip, 2, 71)).toBe('other');
    expect(changeOf(bare, 1, 70)).toBe('other');
  });
  it('a change of only the bus is not announced as assigning the driver', () => {
    expect(doneText(trip, 1, 71, '7100BBB')).toBe('Autobús cambiado a 7100BBB. Sigue Ana');
    expect(doneText({ ...trip, AssignedVehicleId: null }, 1, 71, '7100BBB')).toBe('Autobús asignado: 7100BBB. Sigue Ana');
    expect(doneText(trip, 2, 71, '7100BBB')).toBeUndefined();
    expect(doneText(trip, 1, 70, '7000AAA')).toBeUndefined();
  });
  it('without name or plate it uses the number, never "null"', () => {
    expect(doneText({ ...trip, AssignedDriverName: null }, 1, 71, '7100BBB')).toBe('Autobús cambiado a 7100BBB. Sigue el conductor #1');
    expect(vehicleOptions({ ...trip, AssignedVehiclePlate: null }, [], null, null)[0].label).toBe('#70');
  });
  it('the proposed one may be pending, applied, or start from another bus', () => {
    const proposed = proposal(99, '9900ZZZ');
    expect(proposalState(trip, proposed)).toBe('open');
    expect(proposalState({ ...trip, AssignedVehicleId: 99 }, proposed)).toBe('done');
    expect(proposalState({ ...trip, AssignedVehicleId: 71 }, proposed)).toBe('moved');
    expect(proposalState(bare, proposed)).toBe('moved');
  });
  it('a forced service warns only if its driver is kept', () => {
    const forced = { ...trip, ForcedDescription: 'Forzado por Jefa: motivo' };
    expect(forcedNotice(forced, 1)).toMatch(/volver a forzarlo/);
    expect(forcedNotice(forced, 2)).toBeNull();
    expect(forcedNotice(trip, 1)).toBeNull();
    expect(forcedNotice({ ...bare, ForcedDescription: 'x' }, null)).toBeNull();
  });
});

describe('reason of a bus overlap', () => {
  const DRIVER = 'Sin solape con otros servicios del conductor';
  const VEHICLE = 'Sin solape con otros servicios del vehículo';
  const item = (Title: string, Outcome: string, Detail: string) => ({ Code: Title === VEHICLE ? 'NoVehicleOverlap' : 'Other', Title, Reference: null, Outcome, Detail });
  const explanation = (...Business: unknown[]) => ({ Summary: '', Business, Flow: [], Patterns: [], Adrs: [] }) as unknown as Explanation;
  it('says which bus, with which service and what to do', () => {
    const text = clashText(
      explanation(item(DRIVER, 'Passed', 'Ninguno'), item(VEHICLE, 'Failed', 'Se solapa con el servicio #45.')),
      '9900ZZZ',
      true,
    );
    expect(text).toBe(
      'El autobús 9900ZZZ ya tiene otro servicio en ese horario. Se solapa con el servicio #45. Si el informe también propone otro autobús para él, cámbiaselo antes (si se intercambian los autobuses, pasa uno primero por otro que esté libre)',
    );
  });
  it('not coming from the report, it does not mention it', () => {
    expect(clashText(explanation(item(VEHICLE, 'Failed', 'Se solapa con el servicio #45, #46.')), '9900ZZZ', false)).toBe(
      'El autobús 9900ZZZ ya tiene otro servicio en ese horario. Se solapa con el servicio #45, #46.',
    );
  });
  it('a driver overlap, another rejection or no explanation keep the API reason', () => {
    expect(clashText(explanation(item(DRIVER, 'Failed', 'Se solapa con el servicio #45.')), '9900ZZZ', true)).toBeNull();
    expect(clashText(explanation(item(VEHICLE, 'NotEvaluated', '')), '9900ZZZ', true)).toBeNull();
    expect(clashText(undefined, '9900ZZZ', true)).toBeNull();
  });
});

describe('going back to the report', () => {
  it('without recalculating it says nothing; if recalculated, whether the service is still listed', () => {
    expect(returnNote(10, false, true)).toBeNull();
    expect(returnNote(10, true, true)).toMatch(/recalculado.*#10 sigue/);
    expect(returnNote(10, true, false)).toMatch(/recalculado.*#10 ya no está/);
  });
});

describe('buses out by their agenda', () => {
  const service = { Start: '2026-11-25T08:00:00Z', End: '2026-11-25T10:00:00Z', RequiredSeats: 50, AssignedVehicleId: 70 };
  const itv = { Id: 5, Start: '2026-11-25T09:00:00Z', End: '2026-11-25T11:00:00Z', Type: 'TechnicalInspection', Notes: null };
  const vehicle = (Id: number, extra: Partial<Vehicle> = {}): Vehicle => ({
    Id,
    LicensePlate: `${Id}00AAA`,
    Brand: '',
    Model: '',
    SeatingCapacity: 50,
    IsExternal: false,
    AllowsAutoAssignment: true,
    IsActive: true,
    HomeLocationId: null,
    Unavailabilities: [itv],
    DetailsVersion: 'v1',
    ...extra,
  });
  const out = (vehicles: Vehicle[]) => busesOutByAgenda(service, vehicles, [bus(71, '7100BBB')]).map(b => b.resource.Id);

  it('the one that could go if it were not for its agenda, with the entry', () => {
    expect(busesOutByAgenda(service, [vehicle(80)], [])).toMatchObject([{ resource: { Id: 80 }, entries: [{ Id: 5 }] }]);
  });
  it('not the free ones, nor those that start when it ends', () => {
    expect(out([vehicle(80, { Unavailabilities: [] }), vehicle(81, { Unavailabilities: [{ ...itv, Start: service.End }] })])).toEqual([]);
  });
  it('not those that would not be offered anyway', () => {
    expect(
      out([
        vehicle(80, { SeatingCapacity: 49 }),
        vehicle(81, { IsActive: false }),
        vehicle(82, { IsExternal: true }),
        vehicle(83, { AllowsAutoAssignment: false }),
        vehicle(84),
      ]),
    ).toEqual([84]);
  });
  it('not the current one (its trip already says so) nor one that the engine does offer', () => {
    expect(out([vehicle(70), vehicle(71)])).toEqual([]);
  });
});

describe('notice of the proposed bus', () => {
  const html = (patch: Partial<BookingTrip>, chosen = false) =>
    renderToStaticMarkup(
      createElement(ProposalNote, {
        trip: { ...trip, ...patch } as unknown as BookingTrip,
        proposal: proposal(99, '9900ZZZ'),
        chosen,
        onChoose: () => undefined,
      }),
    );
  it('names it, says that it has not been checked and offers to choose it', () => {
    const markup = html({});
    expect(markup).toContain(proposedText());
    expect(markup).toContain('en lugar de 7000AAA');
    expect(markup).toContain('se comprueba al guardar');
    expect(markup).toMatch(/aria-pressed="false"[^>]*>Elegir 9900ZZZ</);
    expect(html({}, true)).toMatch(/aria-pressed="true"[^>]*>Elegido 9900ZZZ</);
  });
  it('if it already has it there is nothing to choose; if it started from another bus, it warns', () => {
    expect(html({ AssignedVehicleId: 99, AssignedVehiclePlate: '9900ZZZ' })).not.toContain('<button');
    expect(html({ AssignedVehicleId: 71, AssignedVehiclePlate: '7100BBB' })).toContain('partía de 7000AAA');
    expect(html({})).not.toContain('partía de');
  });
});

describe('choiceText', () => {
  it.each([
    ['Ana', '7000AAA', 1, 'Ana · 7000AAA'],
    ['Ana', '7000AAA', 3, 'Ana en 3 viajes · 7000AAA en este'],
    [null, '7000AAA', 1, 'Falta elegir conductor'],
    ['Ana', null, 3, 'Falta elegir vehículo'],
    [null, null, 1, 'Falta elegir conductor y vehículo'],
  ])('driver %s, bus %s, %i trips → %s', (driver, plate, tripCount, text) => {
    expect(choiceText(driver, plate, tripCount)).toBe(text);
  });
});
