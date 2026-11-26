import { describe, expect, it } from 'vitest';
import { affectedTrips, defaultLeaveWindow } from './incident';
import type { Trip } from './types';

const trip = (id: number, start: string, end: string, extra: Partial<Trip> = {}): Trip => ({
  Id: id,
  OriginLocationId: 1,
  DestinationLocationId: 2,
  Start: `2036-06-03T${start}:00`,
  End: `2036-06-03T${end}:00`,
  RequiredSeats: 10,
  ServiceType: 'Regular',
  Status: 'Assigned',
  Description: null,
  BookingId: 1,
  AssignedDriverId: 7,
  AssignedVehicleId: 3,
  IsLocked: false,
  IsActive: true,
  ...extra,
});
const leave = { start: new Date('2036-06-03T08:00:00Z'), end: new Date('2036-06-03T20:00:00Z') }; // API dates = UTC

describe('affectedTrips', () => {
  it('Overlap_IsAffected', () => {
    expect(affectedTrips([trip(1, '07:30', '08:30')], 7, leave).map(t => t.Id)).toEqual([1]);
  });
  it('EndsAtLeaveStart_NotAffected', () => {
    expect(affectedTrips([trip(1, '07:00', '08:00')], 7, leave)).toEqual([]);
  });
  it('StartsAtLeaveEnd_NotAffected', () => {
    expect(affectedTrips([trip(1, '20:00', '21:00')], 7, leave)).toEqual([]);
  });
  it('OtherDriver_NotAffected', () => {
    expect(affectedTrips([trip(1, '09:00', '10:00', { AssignedDriverId: 8 })], 7, leave)).toEqual([]);
  });
  it('CancelledOrCompleted_NotAffected', () => {
    const trips = [trip(1, '09:00', '10:00', { Status: 'Cancelled' }), trip(2, '09:00', '10:00', { Status: 'Completed' })];
    expect(affectedTrips(trips, 7, leave)).toEqual([]);
  });
  it('SortedByStart', () => {
    const trips = [trip(2, '12:00', '13:00'), trip(1, '09:00', '10:00')];
    expect(affectedTrips(trips, 7, leave).map(t => t.Id)).toEqual([1, 2]);
  });
});

describe('defaultLeaveWindow', () => {
  it('Today_StartsNow_EndsEndOfDay', () => {
    const now = new Date('2036-06-03T10:15:00');
    const leave = defaultLeaveWindow(new Date('2036-06-03T00:00:00'), now);
    expect(leave.start).toEqual(now);
    expect(leave.end).toEqual(new Date('2036-06-04T00:00:00'));
  });
  it('FutureDay_StartsAtDayStart', () => {
    const leave = defaultLeaveWindow(new Date('2036-06-05T00:00:00'), new Date('2036-06-03T10:15:00'));
    expect(leave.start).toEqual(new Date('2036-06-05T00:00:00'));
    expect(leave.end).toEqual(new Date('2036-06-06T00:00:00'));
  });
});
