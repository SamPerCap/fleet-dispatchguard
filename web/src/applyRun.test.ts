import { describe, expect, it } from 'vitest';
import { ApiError } from './api';
import { createApplyRunner, type ApplyCall } from './applyRun';
import { applyQueue, toLines, withPair, withResult, type ApplyResult, type ProposalLine } from './recommend';
import type { AllocationResponse, Explanation, Recommendation, RecommendationLine } from './types';

const why = { Summary: 's', Business: [], Flow: [], Patterns: [], Adrs: [] } as Explanation;
const line = (TripId: number): RecommendationLine => ({
  TripId,
  BookingId: 1,
  CustomerName: 'C',
  BaseId: 1,
  Start: '2026-12-01T08:00:00Z',
  End: '2026-12-01T10:00:00Z',
  Origin: 'A',
  Destination: 'B',
  RequiredSeats: 20,
  Status: 'Proposed',
  Driver: { Id: TripId * 10, Name: `D${TripId}`, DistanceKm: 1, StepKm: 5, Where: '', SameBooking: false, Late: null },
  Vehicle: { Id: TripId * 100, Name: `V${TripId}`, DistanceKm: 1, StepKm: 5, Where: '', SameBooking: false, Late: null },
  Reason: 'r',
  Warnings: [],
  Explanation: null,
});
const lines = (count: number) => toLines({ Lines: Array.from({ length: count }, (_, index) => line(index + 1)) } as Recommendation);
const ok: AllocationResponse = { TripId: 1, DriverId: 1, VehicleId: 1, Status: 'Assigned', Explanation: why };
const busy = new ApiError(503, 'Otra operación en curso', 'espera', [], undefined, false, [], true);
const taken = new ApiError(409, 'No se puede asignar este servicio', 'El servicio #2 ya está asignado');
const illegal = new ApiError(422, 'No cumple', undefined, ['Jornada'], why);
const quiet = { onResult: () => undefined };

/** One error per trip, or the successful response; records the calls. */
function fake(byTrip: Record<number, Error> = {}) {
  const calls: Parameters<ApplyCall>[] = [];
  const apply: ApplyCall = async (...args) => {
    calls.push(args);
    await Promise.resolve();
    const failure = byTrip[args[0]];
    if (failure) throw failure;
    return ok;
  };
  return { apply, calls };
}
async function run(queue: ProposalLine[], byTrip: Record<number, Error> = {}) {
  const { apply, calls } = fake(byTrip);
  const results: [number, ApplyResult][] = [];
  const allocated: [number, string][] = [];
  const report = await createApplyRunner(apply).run(queue, {
    onResult: (line, result) => {
      results.push([line.view.TripId, result]);
    },
    onAllocated: (id, info) => {
      allocated.push([id, info.label]);
    },
  });
  return { report, results, allocated, calls };
}

describe('apply loop shared by "Recomendar" and "Asignar semana"', () => {
  it('applies in order, with the pair on screen and its origin', async () => {
    const outcome = await run(withPair(lines(2), 2, { driverId: 99, vehicleId: 200 }));
    expect(outcome.calls).toEqual([
      [1, 10, 100, 'Recommended'],
      [2, 99, 200, 'Manual'],
    ]);
    expect(outcome.results.map(([id, result]) => [id, result.state])).toEqual([
      [1, 'applied'],
      [2, 'applied'],
    ]);
    expect(outcome.results[0][1]).toEqual({ state: 'applied', text: 'Asignado a D1', explanation: why });
    expect(outcome.allocated).toEqual([
      [1, 'Asignación de D1 al servicio #1'],
      [2, 'Asignación de D2 al servicio #2'],
    ]);
    expect(outcome.report).toEqual({ end: 'done', saved: true });
  });

  it('does not send a line until the previous one has answered', async () => {
    let inFlight = 0;
    let most = 0;
    const apply: ApplyCall = async () => {
      most = Math.max(most, ++inFlight);
      await new Promise(f => setTimeout(f, 1));
      inFlight--;
      return ok;
    };
    await createApplyRunner(apply).run(lines(4), quiet);
    expect(most).toBe(1);
  });

  it('nothing in the queue: no call, nothing saved', async () => {
    const outcome = await run([]);
    expect([outcome.calls.length, outcome.report]).toEqual([0, { end: 'done', saved: false }]);
  });

  it('a rejection or a busy resource in the middle does not stop the rest', async () => {
    const outcome = await run(lines(5), { 2: taken, 3: busy, 4: illegal });
    expect(outcome.results.map(([, result]) => result.state)).toEqual(['applied', 'rejected', 'busy', 'rejected', 'applied']);
    expect(outcome.results[1][1].text).toMatch(/ya está asignado/);
    expect(outcome.results[3][1].explanation).toBe(why);
    expect(outcome.report).toEqual({ end: 'done', saved: true });
    expect(outcome.allocated.map(([id]) => id)).toEqual([1, 5]);
  });

  it('all rejected: nothing was saved', async () => {
    expect((await run(lines(2), { 1: taken, 2: illegal })).report).toEqual({ end: 'done', saved: false });
  });

  it.each([
    ['no answer', new TypeError('Failed to fetch'), 'unknown', true],
    ['a 500', new ApiError(500, 'Error'), 'unknown', true],
    ['a 503 that is not retryable', new ApiError(503, 'HTTP 503'), 'unknown', true],
    ['a rate limit', new ApiError(429, 'Too many requests'), 'limited', false],
  ])('%s stops the rest, which are not tried', async (_, error, state, maybeSaved) => {
    const outcome = await run(lines(4), { 2: error });
    expect(outcome.results.map(([, result]) => result.state)).toEqual(['applied', state]);
    expect(outcome.calls).toHaveLength(2);
    expect(outcome.report).toEqual({ end: 'api', saved: true });
    expect((await run(lines(2), { 1: error })).report).toEqual({ end: 'api', saved: maybeSaved });
  });

  it('a 503 is retried as it is; what is applied or rejected is not', async () => {
    let current = lines(3);
    const first = await run(applyQueue(current), { 2: busy, 3: taken });
    for (const [id, result] of first.results) current = withResult(current, id, result);
    expect((await run(applyQueue(current))).calls).toEqual([[2, 20, 200, 'Recommended']]);
  });
});

describe('stop and double click', () => {
  it('stopping with the third in flight: it keeps its result and none starts after it', async () => {
    const sent: number[] = [];
    const runner = createApplyRunner(async id => {
      sent.push(id);
      if (id === 3) runner.stop();
      return ok;
    });
    const done: number[] = [];
    expect(
      await runner.run(lines(10), {
        onResult: line => {
          done.push(line.view.TripId);
        },
      }),
    ).toEqual({ end: 'stopped', saved: true });
    expect([sent, done]).toEqual([
      [1, 2, 3],
      [1, 2, 3],
    ]);
  });

  it('stopping during the last one leaves nothing undone', async () => {
    const runner = createApplyRunner(async id => {
      if (id === 3) runner.stop();
      return ok;
    });
    expect((await runner.run(lines(3), quiet))!.end).toBe('done');
  });

  it('a second run at the same time does nothing: no line is sent twice', async () => {
    const { apply, calls } = fake();
    const runner = createApplyRunner(apply);
    const [first, second] = await Promise.all([runner.run(lines(3), quiet), runner.run(lines(3), quiet)]);
    expect([first!.end, second]).toEqual(['done', null]);
    expect(runner.running).toBe(false);
    expect(calls.map(c => c[0])).toEqual([1, 2, 3]);
  });

  it('it can run again after ending, after a stop and after a listener that fails', async () => {
    const { apply, calls } = fake();
    const runner = createApplyRunner(apply);
    runner.stop(); // with nothing running it does not count
    expect(
      (await runner.run(lines(2), {
        onResult: line => {
          if (line.view.TripId === 1) runner.stop();
        },
      }))!.end,
    ).toBe('stopped');
    expect((await runner.run(lines(2), quiet))!.end).toBe('done');
    await expect(
      runner.run(lines(1), {
        onResult: () => {
          throw new Error('x');
        },
      }),
    ).rejects.toThrow('x');
    expect((await runner.run(lines(1), quiet))!.end).toBe('done');
    expect(calls).toHaveLength(5);
  });
});
