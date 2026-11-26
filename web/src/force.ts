import { t } from './i18n/t';
import type { LaterService } from './types';

/** Same figures as `Core/ValueObjects/AllocationOverride.cs`: the server decides, this only saves a useless round trip. */
export const MIN_REASON = 10;
export const MAX_REASON = 500;

export interface ForceRequest {
  reason: string;
  by: string;
}

export function forceError(reason: string, by: string): string | null {
  const length = reason.trim().length;
  if (length < MIN_REASON) return t.force.reasonTooShort(MIN_REASON, length);
  if (length > MAX_REASON) return t.force.reasonTooLong(MAX_REASON, length);
  if (!by.trim()) return t.force.whoMissing;
  return null;
}

export function laterServicesText(tripId: number, later: LaterService[]): string | null {
  const trips = new Set(later.map(l => l.TripId)).size;
  if (trips === 0) return null;
  const mark = t.force.markOf(tripId);
  return trips === 1
    ? t.force.laterOne(mark)
    : t.force.laterMany(trips, mark);
}
