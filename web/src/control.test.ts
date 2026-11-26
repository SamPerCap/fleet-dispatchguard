import { describe, expect, it } from 'vitest';
import {
  activityText,
  alertTarget,
  clampStripHours,
  freshness,
  impactText,
  lanes,
  outcomeGroup,
  pct,
  relative,
  stripBlocks,
  stripRows,
  stripTicks,
  stripWindow,
  trend,
  incidentTier,
  incidentTypeText,
  operationStatus,
  merge,
  staleText,
  hourIndex,
} from './control';
import type { AlertType, Trip } from './types';

// Local times → API date (UTC without Z), so that the test does not depend on the zone of the machine.
const toApiDate = (date: Date) => date.toISOString().replace('Z', '');
const at = (hours: number, minutes = 0) => new Date(2026, 10, 18, hours, minutes);
const trip = (id: number, start: Date, end: Date, extra: Partial<Trip> = {}): Trip => ({
  Id: id,
  OriginLocationId: 1,
  DestinationLocationId: 2,
  Start: toApiDate(start),
  End: toApiDate(end),
  RequiredSeats: 10,
  ServiceType: 'Discretionary',
  Status: 'Assigned',
  Description: `S${id}`,
  AssignedDriverId: 1,
  AssignedVehicleId: 1,
  IsLocked: false,
  IsActive: true,
  BookingId: 1,
  ...extra,
});

describe('stripWindow', () => {
  it('11:40 → 11:00–15:00', () => {
    const { from, to } = stripWindow(new Date(2026, 10, 18, 11, 40, 25));
    expect([from.getHours(), from.getMinutes(), from.getSeconds()]).toEqual([11, 0, 0]);
    expect(to.getTime() - from.getTime()).toBe(4 * 3_600_000);
  });
  it('on the hour it stays at that hour', () => expect(stripWindow(at(11)).from.getHours()).toBe(11));
  it('the window lasts the requested hours', () => {
    const { from, to } = stripWindow(at(11, 40), 16);
    expect((to.getTime() - from.getTime()) / 3_600_000).toBe(16);
  });
});

describe('stripBlocks: status by schedule', () => {
  const from = at(10),
    to = at(14),
    now = at(11, 30);
  it('clips to the window and marks the clipped edge', () => {
    const [block] = stripBlocks([trip(1, at(13, 30), at(16))], from, to, now);
    expect(block.left).toBeCloseTo(87.5);
    expect(block.width).toBeCloseTo(12.5);
    expect([block.clipStart, block.clipEnd]).toEqual([false, true]);
    const [clipped] = stripBlocks([trip(2, at(9), at(10, 30))], from, to, now);
    expect([clipped.clipStart, clipped.clipEnd]).toEqual([true, false]);
  });
  it('what ends just as the window starts, or starts as it ends, does not appear', () => {
    expect(stripBlocks([trip(1, at(8), at(10)), trip(2, at(14), at(15))], from, to, now)).toHaveLength(0);
  });
  it('status: completed, in progress by schedule, uncovered, assigned; cancelled out; forced apart', () => {
    const blocks = stripBlocks(
      [
        trip(1, at(10), at(11)),
        trip(2, at(11), at(12)),
        trip(3, at(11, 45), at(12, 30), { AssignedDriverId: null }),
        trip(4, at(12), at(13), { ForcedDescription: 'Forzado por Ana: Sin alternativa' }),
        trip(5, at(12, 30), at(13), { Status: 'Cancelled' }),
      ],
      from,
      to,
      now,
    );
    expect(blocks.map(b => [b.trip.Id, b.status, b.forced])).toEqual([
      [1, 'completed', false],
      [2, 'running', false],
      [3, 'uncovered', false],
      [4, 'assigned', true],
    ]);
  });
  it('"in progress" comes from the schedule even if the stored status is Assigned', () => {
    const [block] = stripBlocks([trip(1, at(11), at(12), { Status: 'Assigned' })], from, to, now);
    expect(block.status).toBe('running');
  });
});

describe('strip lanes and rows', () => {
  const from = at(10),
    to = at(14),
    now = at(9);
  it('lanes: no overlaps, an adjacent block fits in the same lane', () => {
    const blocks = stripBlocks([trip(1, at(10), at(11)), trip(2, at(11), at(12)), trip(3, at(10, 30), at(11, 30))], from, to, now);
    expect(lanes(blocks).map(lane => lane.map(x => x.trip.Id))).toEqual([[1, 2], [3]]);
  });
  const names: Record<number, string> = { 1: 'Zoe Ruiz', 2: 'Ana Gil' };
  const name = (id: number) => names[id] ?? `#${id}`;
  const blocks = stripBlocks(
    [
      trip(1, at(10), at(11), { AssignedDriverId: 1 }),
      trip(2, at(11), at(12), { AssignedDriverId: 1 }),
      trip(3, at(10), at(12), { AssignedDriverId: 2 }),
      trip(4, at(10), at(11), { AssignedDriverId: null }),
      trip(5, at(10, 30), at(11, 30), { AssignedDriverId: null }),
    ],
    from,
    to,
    now,
  );
  it('by driver: "Sin conductor" on top (with lanes if they overlap), then by name', () => {
    const rows = stripRows(blocks, 'driver', name);
    expect(rows.map(row => [row.label, row.missing, row.blocks.map(b => b.trip.Id)])).toEqual([
      ['Sin conductor', true, [4]],
      ['', true, [5]],
      ['Ana Gil', false, [3]],
      ['Zoe Ruiz', false, [1, 2]],
    ]);
    expect(new Set(rows.map(r => r.key)).size).toBe(rows.length);
  });
  it('no row has overlapping blocks', () => {
    for (const mode of ['driver', 'vehicle', 'service'] as const)
      for (const row of stripRows(blocks, mode, name))
        row.blocks.forEach((block, index) => {
          if (index > 0) expect(row.blocks[index - 1].left + row.blocks[index - 1].width).toBeLessThanOrEqual(block.left + 1e-9);
        });
  });
  it('by vehicle groups by plate; by service, one row per service with its schedule', () => {
    expect(stripRows(blocks, 'vehicle', id => `V${id}`).map(r => r.label)).toEqual(['V1', '', '', '']);
    const service = stripRows(blocks, 'service', name);
    expect(service).toHaveLength(5);
    expect(service[0].label).toBe('S1');
    expect(service[0].sub).toMatch(/^\d\d:\d\d–\d\d:\d\d$/);
  });
});

describe('strip marks, without overlapping labels', () => {
  it('clamps to [1, 16] and rounds; anything not numeric goes back to 4 h', () => {
    expect([clampStripHours(0), clampStripHours(16), clampStripHours(20), clampStripHours('7'), clampStripHours(2.6)]).toEqual([
      1, 16, 16, 7, 3,
    ]);
    expect([clampStripHours(null), clampStripHours(''), clampStripHours('abc')]).toEqual([4, 4, 4]);
  });
  it('marks depend on the span and always include the end', () => {
    expect(stripTicks(1)).toEqual([0, 1]);
    expect(stripTicks(4)).toEqual([0, 1, 2, 3, 4]);
    expect(stripTicks(7)).toEqual([0, 2, 4, 6, 7]);
    expect(stripTicks(12)).toEqual([0, 2, 4, 6, 8, 10, 12]);
    expect(stripTicks(16)).toEqual([0, 4, 8, 12, 16]);
  });
  it('drops the last regular mark if it is less than half a step from the end', () => {
    expect(stripTicks(13)).toEqual([0, 4, 8, 13]);
    expect(stripTicks(11)).toEqual([0, 2, 4, 6, 8, 10, 11]); // exactly half a step away stays
  });
});

describe('incidents and activity', () => {
  it('each entity leads to its panel or card', () => {
    expect(alertTarget({ EntityType: 'Trip', EntityId: 7 })).toEqual({ kind: 'trip', tripId: 7 });
    expect(alertTarget({ EntityType: 'Booking', EntityId: 7 })).toEqual({ kind: 'booking', bookingId: 7 });
    expect(alertTarget({ EntityType: 'Driver', EntityId: 7 })).toEqual({ kind: 'driver', id: 7 });
    expect(alertTarget({ EntityType: 'Vehicle', EntityId: 7 })).toEqual({ kind: 'vehicle', id: 7 });
    expect(alertTarget({ EntityType: 'Otro', EntityId: 7 })).toBeNull();
  });
  it('impact in singular and plural', () => {
    expect(impactText({ AffectedTripCount: 3, AffectedSeats: 120 })).toBe('3 servicios · 120 plazas');
    expect(impactText({ AffectedTripCount: 1, AffectedSeats: 1 })).toBe('1 servicio · 1 plaza');
    expect(impactText({ AffectedTripCount: 0, AffectedSeats: 0 })).toBe('Sin servicios afectados hoy');
  });
  it('relative deadline', () => {
    const now = new Date('2026-11-18T10:40:00Z');
    expect(relative(new Date('2026-11-18T13:30:00Z'), now)).toBe('en 2 h 50');
    expect(relative(new Date('2026-11-18T11:40:00Z'), now)).toBe('en 1 h');
    expect(relative(new Date('2026-11-18T10:50:00Z'), now)).toBe('en 10 min');
    expect(relative(new Date('2026-11-18T10:40:20Z'), now)).toBe('ahora');
    expect(relative(new Date('2026-11-18T10:10:00Z'), now)).toBe('hace 30 min');
  });
  it('activity: resource and service by name; outcome group', () => {
    const item = {
      Id: 1,
      At: '',
      Who: 'Marta',
      TripId: 12,
      DriverId: 3,
      VehicleId: 4,
      Outcome: 'RejectedCompliance',
      Detail: null,
      TripLabel: 'Traslado T4',
    };
    const driverName = (id: number) => `C${id}`,
      plate = (id: number) => `V${id}`;
    expect(activityText(item, driverName, plate)).toBe('C3 y V4 · Traslado T4');
    expect(activityText({ ...item, TripLabel: null }, driverName, plate)).toBe('C3 y V4 · Servicio 12');
    expect(['Assigned', 'Forced', 'RejectedDriver', 'RejectedVehicle', 'RejectedSchedule'].map(outcomeGroup)).toEqual([
      'assigned',
      'forced',
      'blocked',
      'blocked',
      'blocked',
    ]);
  });
  it('pct without total = 0', () => {
    expect(pct(10, 12)).toBe(83);
    expect(pct(0, 0)).toBe(0);
  });
});

describe('freshness and trend', () => {
  const start = new Date('2026-11-18T10:00:00Z');
  const after = (ms: number) => new Date(start.getTime() + ms);
  it('text depends on the age', () => {
    expect(freshness(start, after(2_000)).text).toBe('ahora mismo');
    expect(freshness(start, after(12_000)).text).toBe('hace 12 s');
    expect(freshness(start, after(185_000)).text).toBe('hace 3 min');
  });
  it('stale (amber) only above 2 refresh intervals (120 s)', () => {
    expect(freshness(start, after(120_000)).stale).toBe(false);
    expect(freshness(start, after(120_001)).stale).toBe(true);
  });
  it('a clock running ahead does not give a negative age', () =>
    expect(freshness(start, after(-5_000))).toEqual({ text: 'ahora mismo', stale: false }));
  it('trend against yesterday', () => {
    expect(trend(47, 44)).toEqual({ delta: 3, text: '+3 vs ayer' });
    expect(trend(40, 44)).toEqual({ delta: -4, text: '−4 vs ayer' });
    expect(trend(5, 5)).toEqual({ delta: 0, text: 'igual que ayer' });
    // Never an infinite percentage.
    expect(trend(3, 0)).toEqual({ delta: 3, text: 'nuevo hoy' });
    expect(trend(0, 0)).toEqual({ delta: 0, text: 'igual que ayer' });
  });
});

describe('tiers, operation status and stale data', () => {
  it('incidentTier: urgent is critical; the rest by type, as in the reference', () => {
    expect(incidentTier({ Severity: 'Urgent', Type: 'PartialBooking' })).toBe('critical');
    expect(incidentTier({ Severity: 'Warning', Type: 'VehicleUnavailable' })).toBe('high');
    expect(incidentTier({ Severity: 'Warning', Type: 'UnavailabilityGap' })).toBe('high');
    expect(incidentTier({ Severity: 'Warning', Type: 'UnassignedTrip' })).toBe('high');
    expect(incidentTier({ Severity: 'Warning', Type: 'InsufficientSeats' })).toBe('high');
    expect(incidentTier({ Severity: 'Urgent', Type: 'InsufficientSeats' })).toBe('critical');
    expect(incidentTier({ Severity: 'Warning', Type: 'DriverNearLimit' })).toBe('preventive');
    expect(incidentTier({ Severity: 'Warning', Type: 'ForcedAssignment' })).toBe('preventive');
    expect(incidentTier({ Severity: 'Warning', Type: 'PartialBooking' })).toBe('notice');
  });
  it('incidentTypeText: a name for each type, also for one that this version does not know', () => {
    expect(incidentTypeText('InsufficientSeats')).toBe('Plazas insuficientes');
    expect(incidentTypeText('Nuevo' as AlertType)).toBe('Otro aviso');
  });
  it('operationStatus follows the light', () => {
    expect(operationStatus('Green').text).toBe('Operación estable');
    expect(operationStatus('Amber').tone).toBe('warn');
    expect(operationStatus('Red')).toEqual({ text: 'Requiere acción urgente', tone: 'danger', light: 'rojo' });
    expect(operationStatus(undefined).text).toBe('Operación estable');
  });
  it('merge keeps the last good value as stale when a refresh fails', () => {
    const earlier = new Date('2036-01-01T10:00:00Z'),
      later = new Date('2036-01-01T10:01:00Z');
    const first = merge<number>(undefined, { ok: true, value: 5 }, earlier);
    expect(first).toEqual({ ok: true, value: 5, at: earlier });
    const failed = merge(first, { ok: false, error: 'caída' }, later);
    expect(failed).toEqual({ ok: true, value: 5, at: earlier, stale: { since: earlier, error: 'caída' } });
    expect(merge(failed, { ok: true, value: 6 }, later)).toEqual({ ok: true, value: 6, at: later });
    expect(merge<number>(undefined, { ok: false, error: 'x' }, later)).toEqual({ ok: false, error: 'x' });
  });
  it('staleText and hourIndex', () => {
    const since = new Date('2036-01-01T10:00:00Z');
    expect(staleText(since, new Date('2036-01-01T10:00:30Z'))).toBe('Sin actualizar desde hace menos de 1 min');
    expect(staleText(since, new Date('2036-01-01T10:03:10Z'))).toBe('Sin actualizar desde hace 3 min');
    expect(hourIndex(since, new Date('2036-01-01T12:59:00Z'))).toBe(2);
  });
});
