import { describe, expect, it } from 'vitest';
import { ApiError } from './api';
import { attempt, failureOf, forced, repeat, type Allocate, type AttemptTarget } from './attempt';
import type { AllocationResponse } from './types';

const ana = { driverId: 1, driverName: 'Ana' };
const blas = { driverId: 2, driverName: 'Blas' };
const ok = { Forced: false, Explanation: null } as unknown as AllocationResponse;
const busy = new ApiError(503, 'Otra operación en curso', 'No se ha guardado nada', [], undefined, false, [], true);
const illegal = new ApiError(422, 'No cumple', undefined, ['Jornada'], undefined, true, [{ TripId: 9, Message: 'x' } as never]);

/** Answers in order and records what it was called with. */
function fakeApi(...replies: (AllocationResponse | Error)[]) {
  const calls: Parameters<Allocate>[] = [];
  const allocate: Allocate = async (...args) => {
    calls.push(args);
    const reply = replies.shift()!;
    if (reply instanceof Error) throw reply;
    return reply;
  };
  return { allocate, calls };
}

/** As in the panel: the choice on screen changes when the booking refreshes; the attempts were made before. */
async function wholeBooking(...replies: (AllocationResponse | Error)[]) {
  const { allocate, calls } = fakeApi(...replies);
  let selected = ana;
  const target = (tripId: number, vehicleId: number): AttemptTarget => ({ tripId, vehicleId, ...selected });
  const out = [await attempt(target(10, 70), allocate), await attempt(target(11, 71), allocate)];
  selected = blas;
  return { out, calls, allocate, selected };
}

describe('repeating an attempt uses the driver it was made with', () => {
  it('retrying after a 503 with another driver already chosen on screen', async () => {
    const { out, calls, allocate, selected } = await wholeBooking(ok, busy, ok);
    expect(out.map(o => [o.ok, o.retryable === true])).toEqual([
      [true, false],
      [false, true],
    ]);
    const again = await repeat(out[1], allocate);
    expect(calls[2]).toEqual([11, ana.driverId, 71, undefined]);
    expect(calls[2][1]).not.toBe(selected.driverId);
    expect(again).toMatchObject({ ok: true, text: 'Asignado a Ana' });
  });

  it('a retry that gets another 503, or a forcible 422, keeps the same driver', async () => {
    const { out, calls, allocate } = await wholeBooking(ok, busy, busy, illegal, ok);
    const third = await repeat(await repeat(out[1], allocate), allocate);
    expect(third).toMatchObject({ ok: false, forcible: true, target: { tripId: 11, vehicleId: 71, ...ana } });
    await forced(third, { reason: 'Motivo suficiente', by: 'Jefa' }, allocate);
    expect(calls.slice(1).map(c => c.slice(0, 3))).toEqual(Array(4).fill([11, ana.driverId, 71]));
  });

  it('forcing after a 422 forces the driver of the attempt, with the reason and the name', async () => {
    const { out, calls, allocate } = await wholeBooking(ok, illegal, { ...ok, Forced: true });
    expect(out[1]).toMatchObject({ ok: false, forcible: true, text: 'No cumple · Jornada' });
    expect(out[1].laterServices).toHaveLength(1);
    const force = { reason: ' Motivo suficiente ', by: 'Jefa' };
    const done = await forced(out[1], force, allocate);
    expect(calls[2]).toEqual([11, ana.driverId, 71, force]);
    expect(done).toMatchObject({ ok: true, text: 'Forzado: Motivo suficiente', target: { tripId: 11, vehicleId: 71, ...ana } });
    expect(done.forcible).toBeUndefined();
  });

  it('forcing something already legal says so; a failure on forcing reaches the form as it is', async () => {
    const { out, allocate } = await wholeBooking(ok, illegal, ok, busy);
    expect((await forced(out[1], { reason: 'Motivo suficiente', by: 'Jefa' }, allocate)).text).toBe('Asignado (era legal)');
    await expect(forced(out[1], { reason: 'Motivo suficiente', by: 'Jefa' }, allocate)).rejects.toBe(busy);
  });

  it('an error that is not from the API offers neither retrying nor forcing', async () => {
    const { allocate } = fakeApi(new Error('Sin red'));
    const outcome = await attempt({ tripId: 10, vehicleId: 70, ...ana }, allocate);
    expect([outcome.ok, outcome.text, outcome.retryable, outcome.forcible]).toEqual([false, 'Sin red', undefined, undefined]);
  });

  it('a change of only the bus is announced as such, also when retrying it', async () => {
    const { allocate } = fakeApi(busy, ok);
    const first = await attempt({ tripId: 10, vehicleId: 99, ...ana, done: 'Autobús cambiado a 9900ZZZ. Sigue Ana' }, allocate);
    expect(first).toMatchObject({ ok: false, retryable: true });
    expect((await repeat(first, allocate)).text).toBe('Autobús cambiado a 9900ZZZ. Sigue Ana');
  });

  it('a vehicle overlap names the bus and the service; with another reason, the API one', async () => {
    const clash = (Title: string) =>
      new ApiError(409, 'Solape de horario', 'Se solapa con: #45', [], {
        Summary: '',
        Business: [{ Code: Title.includes('vehículo') ? 'NoVehicleOverlap' : 'NoDriverOverlap', Title, Reference: null, Outcome: 'Failed', Detail: 'Se solapa con el servicio #45.' }],
        Flow: [],
        Patterns: [],
        Adrs: [],
      });
    const VEHICLE = 'Sin solape con otros servicios del vehículo';
    const { allocate } = fakeApi(clash(VEHICLE), clash('Sin solape con otros servicios del conductor'), clash(VEHICLE));
    const target = { tripId: 10, vehicleId: 99, ...ana, plate: '9900ZZZ', fromReport: true };
    expect((await attempt(target, allocate)).text).toMatch(
      /^El autobús 9900ZZZ ya tiene otro servicio en ese horario\. Se solapa con el servicio #45\. Si el informe/,
    );
    expect((await attempt(target, allocate)).text).toBe('Solape de horario: Se solapa con: #45');
    expect((await attempt({ tripId: 10, vehicleId: 99, ...ana }, allocate)).text).toBe('Solape de horario: Se solapa con: #45');
    // The force form uses the same reason.
    expect(failureOf(target, clash(VEHICLE))).toMatch(/^El autobús 9900ZZZ/);
    expect(failureOf(target, illegal)).toBe('No cumple · Jornada');
    expect(failureOf(target, new Error('Sin red'))).toBe('Sin red');
  });
});
