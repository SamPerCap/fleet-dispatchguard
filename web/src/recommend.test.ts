import { describe, expect, it } from 'vitest';
import {
  applyFailure,
  applyQueue,
  checkInput,
  isManual,
  isRecommendable,
  leftOutText,
  lineState,
  merge,
  optionText,
  originOf,
  reachText,
  stopsTheRest,
  summaryText,
  toLines,
  withPair,
  withResult,
  type ApplyResult,
} from './recommend';
import type { Recommendation, RecommendationLine, RecommendedResource } from './types';

const res = (Id: number, over: Partial<RecommendedResource> = {}): RecommendedResource => ({
  Id,
  Name: `R${Id}`,
  DistanceKm: 4.2,
  StepKm: 5,
  Where: 'desde Atocha',
  SameBooking: false,
  Late: null,
  ...over,
});
const line = (
  TripId: number,
  driver: number | null,
  vehicle: number | null,
  over: Partial<RecommendationLine> = {},
): RecommendationLine => ({
  TripId,
  BookingId: 1,
  CustomerName: 'C',
  BaseId: 1,
  Start: '2026-12-01T08:00:00Z',
  End: '2026-12-01T10:00:00Z',
  Origin: 'A',
  Destination: 'B',
  RequiredSeats: 20,
  Status: driver ? 'Proposed' : 'NoSolution',
  Driver: driver ? res(driver) : null,
  Vehicle: vehicle ? res(vehicle) : null,
  Reason: driver ? 'Propuesta' : 'Más lejos: 2',
  Warnings: [],
  Explanation: null,
  ...over,
});
const dto = (Lines: RecommendationLine[]): Recommendation => ({
  CalculatedAt: '2026-12-01T07:00:00Z',
  Lines,
  LeftOut: 0,
  MaxTrips: 100,
  MaxDays: 31,
  StepsKm: [5, 15, 30],
  Note: 'n',
  ArrivalNote: 'a',
});
const lines = () => toLines(dto([line(1, 10, 20), line(2, 11, 21), line(3, null, null)]));

describe('what is offered', () => {
  const now = new Date('2026-12-01T08:00:00Z');
  const trip = { AssignedDriverId: null, AssignedVehicleId: null, Status: 'Pending', IsLocked: false, Start: '2026-12-01T08:01:00Z' };
  it('only future trips without driver nor vehicle, unlocked and alive', () => {
    expect(isRecommendable(trip, now)).toBe(true);
    const not = [
      { Start: '2026-12-01T08:00:00Z' },
      { AssignedDriverId: 1 },
      { AssignedVehicleId: 1 },
      { IsLocked: true },
      { Status: 'Cancelled' },
    ];
    expect(not.map(x => isRecommendable({ ...trip, ...x }, now))).toEqual([false, false, false, false, false]);
  });
});

describe('origin', () => {
  it('is manual only while the pair is not the proposed one', () => {
    const changed = withPair(lines(), 1, { driverId: 99, vehicleId: 20 });
    expect(changed.map(originOf)).toEqual(['Manual', 'Recommended', 'Recommended']);
    const back = withPair(changed, 1, { driverId: 10, vehicleId: 20 });
    expect(back.map(isManual)).toEqual([false, false, false]);
    expect(originOf(withPair(lines(), 2, { driverId: 11, vehicleId: 99 })[1])).toBe('Manual');
    expect(originOf(withPair(lines(), 3, { driverId: 11, vehicleId: 99 })[2])).toBe('Manual'); // a line without solution, by hand
  });
});

describe('revalidation', () => {
  it('sends every line in order, except the applied ones, saying which are manual', () => {
    const current = withResult(withPair(lines(), 2, { driverId: 12, vehicleId: 21 }), 1, { state: 'applied', text: '' });
    expect(checkInput(current)).toEqual([
      { TripId: 2, DriverId: 12, VehicleId: 21, Manual: true },
      { TripId: 3, DriverId: null, VehicleId: null, Manual: false },
    ]);
  });
  it('takes the new verdicts, keeps why there was no solution and forgets an old rejection', () => {
    const current = withResult(lines(), 2, { state: 'rejected', text: 'x' });
    const out = merge(current, [
      line(1, 10, 20, { Status: 'Invalid', Reason: 'CE 561', Driver: null }),
      line(2, 11, 21),
      line(3, null, null, { Reason: 'Sin conductor y vehículo elegidos' }),
    ]);
    expect(out.map(x => [x.view.Status, x.view.Reason])).toEqual([
      ['Invalid', 'CE 561'],
      ['Proposed', 'Propuesta'],
      ['NoSolution', 'Más lejos: 2'],
    ]);
    expect(out[1].result).toBeUndefined();
    expect(out.map(x => x.chosen.driverId)).toEqual([10, 11, null]); // the pair on screen does not come from the answer
  });
  it('a line without solution recovers why after choosing by hand and going back', () => {
    const byHand = merge(withPair(lines(), 3, { driverId: 12, vehicleId: 21 }), [line(3, 12, 21)]);
    expect([byHand[2].view.Status, byHand[2].view.Reason]).toEqual(['Proposed', 'Propuesta']);
    const back = merge(withPair(byHand, 3, { driverId: null, vehicleId: null }), [
      line(3, null, null, { Reason: 'Sin conductor y vehículo elegidos' }),
    ]);
    expect([lineState(back[2]).text, back[2].view.Reason]).toEqual(['Sin solución', 'Más lejos: 2']);
  });
  it('an applied line, which is not sent, stays as it is', () => {
    const current = withResult(lines(), 1, { state: 'applied', text: 'ok' });
    expect(merge(current, [line(2, 11, 21)])[0]).toBe(current[0]);
  });
});

describe('apply', () => {
  it('only what the engine accepts, in order, not what is done or rejected', () => {
    let current = toLines(
      dto([
        line(5, 1, 1),
        line(3, 2, 2),
        line(9, 3, 3),
        line(1, 4, 4, { Status: 'Invalid' }),
        line(2, null, null),
        line(7, 5, 5),
        line(8, 6, 6),
      ]),
    );
    current = withResult(current, 3, { state: 'applied', text: '' });
    current = withResult(current, 9, { state: 'rejected', text: '' });
    current = withResult(current, 7, { state: 'busy', text: '' });
    expect(applyQueue(current).map(x => x.view.TripId)).toEqual([5, 7, 8]);
  });
  it('a busy resource or a rate limit is not a rejection; without an answer nobody knows', () => {
    expect(applyFailure({ status: 503, title: 'Otra operación en curso', message: 'm', retryable: true }).state).toBe('busy');
    expect(applyFailure({ status: 503, message: 'm' }).state).toBe('unknown');
    expect(applyFailure({ status: 429, message: 'm' }).state).toBe('limited');
    expect(applyFailure({ message: 'Failed to fetch' }).state).toBe('unknown');
    expect(applyFailure({ status: 500, message: 'm' }).state).toBe('unknown');
    const taken = 'No se puede asignar este servicio: El servicio #1 ya está asignado';
    expect(applyFailure({ status: 409, title: 'No se puede asignar este servicio', message: taken })).toEqual({
      state: 'rejected',
      text: taken,
      explanation: undefined,
    });
    expect(applyFailure({ status: 422, title: 'CE 561', message: 'largo', violations: ['a', 'b'] }).text).toBe('CE 561 · a · b');
    const states: ApplyResult['state'][] = ['applied', 'rejected', 'busy', 'limited', 'unknown'];
    expect(states.filter(state => stopsTheRest({ state, text: '' }))).toEqual(['limited', 'unknown']);
  });
});

describe('texts', () => {
  it('state and summary of each line', () => {
    let current = toLines(dto([line(1, 1, 1), line(2, 2, 2), line(3, null, null), line(4, 4, 4, { Status: 'Invalid' }), line(5, 5, 5)]));
    current = withPair(current, 2, { driverId: 9, vehicleId: 2 });
    current = merge(withPair(current, 5, { driverId: null, vehicleId: null }), [line(5, null, null)]);
    expect(current.map(x => lineState(x).text)).toEqual(['Propuesto', 'Cambiado a mano', 'Sin solución', 'No se aplicaría', 'Quitado']);
    expect(summaryText(current)).toBe('5 viajes: 1 propuesto, 1 cambiado a mano, 1 sin solución, 1 que no se aplicaría y 1 quitado.');
    expect(summaryText(withResult([current[0]], 1, { state: 'applied', text: '' }))).toBe('1 viaje: 1 aplicado.');
    expect(lineState(withResult(current, 1, { state: 'busy', text: '' })[0])).toEqual({ key: 'busy', text: 'Ocupado' });
  });
  it('says how many are left out of the limits', () => {
    expect(leftOutText({ LeftOut: 0, MaxTrips: 100, MaxDays: 31 })).toBeNull();
    expect(leftOutText({ LeftOut: 1, MaxTrips: 100, MaxDays: 31 })).toBe(
      '1 viaje pendiente queda fuera: como mucho 100 viajes y 31 días desde la primera salida.',
    );
    expect(leftOutText({ LeftOut: 30, MaxTrips: 100, MaxDays: 31 })).toMatch(/^30 viajes pendientes quedan fuera/);
  });
  it('distances with a decimal comma, where from and why', () => {
    const steps = [5, 15, 30];
    expect(reachText(res(1), steps)).toBe('a 4,2 km en línea recta desde Atocha · radio 5 km');
    expect(reachText(res(1, { DistanceKm: 70, StepKm: null, SameBooking: true }), steps)).toBe(
      'a 70,0 km en línea recta desde Atocha · se mantiene de la misma reserva',
    );
    expect(reachText(res(1, { DistanceKm: 42, StepKm: null }), steps)).toBe('a 42,0 km en línea recta desde Atocha · más de 30 km');
    expect(reachText(res(1, { DistanceKm: null, StepKm: null }), steps)).toBe('posición desconocida');
    expect(optionText(res(1, { DistanceKm: 42, StepKm: null, Late: 'no llega (estimación)' }), steps)).toBe(
      'R1 · 42,0 km · más de 30 km · no llega (estimación)',
    );
    expect(optionText(res(1, { SameBooking: true }), steps)).toBe('R1 · 4,2 km · misma reserva');
  });
});
