import { t } from './i18n/t';
import { ApiError } from './api';
import type { ForceRequest } from './force';
import { failureText } from './retry';
import type { AllocationResponse, Explanation, LaterService } from './types';
import { clashText } from './vehicleChange';

/** Travels inside the result: the choice on screen may change when the booking refreshes. */
export interface AttemptTarget {
  tripId: number;
  driverId: number;
  driverName: string;
  vehicleId: number;
  /** `plate` explains an overlap; `done` announces a change of only the bus (SPEC 0072). */
  plate?: string;
  fromReport?: boolean;
  done?: string;
}

export interface Outcome {
  target: AttemptTarget;
  ok: boolean;
  text: string;
  explanation?: Explanation | null;
  forcible?: boolean;
  laterServices?: LaterService[];
  retryable?: boolean;
}

export type Allocate = (tripId: number, driverId: number, vehicleId: number, force?: ForceRequest) => Promise<AllocationResponse>;

const send = ({ tripId, driverId, vehicleId }: AttemptTarget, allocate: Allocate, force?: ForceRequest) =>
  allocate(tripId, driverId, vehicleId, force);

/** A bus overlap is explained with its plate and the other service; anything else, as the API says it. */
export function failureOf(target: AttemptTarget, error: unknown): string {
  const apiError = error instanceof ApiError ? error : null;
  const clash = target.plate ? clashText(apiError?.explanation, target.plate, target.fromReport === true) : null;
  return clash ?? (apiError ? failureText(apiError) : (error as Error).message);
}

/** A failure is a result, not an exception. */
export async function attempt(target: AttemptTarget, allocate: Allocate): Promise<Outcome> {
  try {
    const response = await send(target, allocate);
    return { target, ok: true, text: target.done ?? t.results.allocatedTo(target.driverName), explanation: response.Explanation };
  } catch (error) {
    const apiError = error instanceof ApiError ? error : null;
    return {
      target,
      ok: false,
      text: failureOf(target, error),
      explanation: apiError?.explanation,
      forcible: apiError?.forcible,
      laterServices: apiError?.laterServices,
      retryable: apiError?.retryable,
    };
  }
}

/** Another operation held the resources: the same assignment of the attempt is repeated (ADR 0018). */
export const repeat = (outcome: Outcome, allocate: Allocate): Promise<Outcome> => attempt(outcome.target, allocate);

/** Throws on failure: the form shows the error and stays open. */
export async function forced(outcome: Outcome, force: ForceRequest, allocate: Allocate): Promise<Outcome> {
  const response = await send(outcome.target, allocate, force);
  return {
    target: outcome.target,
    ok: true,
    text: response.Forced ? t.results.forcedWith(force.reason.trim()) : t.results.wasLegal,
    explanation: response.Explanation,
  };
}
