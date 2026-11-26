import { t } from './i18n/t';
import { ApiError } from './api';
import { applyFailure, originOf, stopsTheRest, type ApplyResult, type ProposalLine } from './recommend';
import type { BarExplanation } from './Timeline';
import type { AllocationResponse, AttemptOrigin } from './types';

export type ApplyCall = (tripId: number, driverId: number, vehicleId: number, origin: AttemptOrigin) => Promise<AllocationResponse>;

export interface ApplyHooks {
  onResult: (line: ProposalLine, result: ApplyResult) => void;
  onAllocated?: (tripId: number, info: BarExplanation) => void;
}

/** `stopped`: asked to stop; `api`: the API did not answer or asked to wait. `saved` is also true when it is not known. */
export interface ApplyReport {
  end: 'done' | 'stopped' | 'api';
  saved: boolean;
}

const driverName = (line: ProposalLine) => line.view.Driver?.Name ?? `#${line.chosen.driverId}`;

async function applyOne(line: ProposalLine, apply: ApplyCall): Promise<ApplyResult> {
  try {
    const response = await apply(line.view.TripId, line.chosen.driverId!, line.chosen.vehicleId!, originOf(line));
    return { state: 'applied', text: t.results.allocatedTo(driverName(line)), explanation: response.Explanation };
  } catch (error) {
    return applyFailure(error instanceof ApiError ? error : { message: error instanceof Error ? error.message : String(error) });
  }
}

/**
 * The only loop that applies a proposal, shared by "Recomendar" and "Asignar semana". A rejection does not stop the rest. One run at
 * a time: a second one returns null and does nothing.
 */
export function createApplyRunner(apply: ApplyCall) {
  let running = false;
  let stopAsked = false;
  return {
    get running(): boolean {
      return running;
    },
    /** Finishes the current line and starts no other. */
    stop(): void {
      stopAsked = true;
    },
    async run(queue: ProposalLine[], hooks: ApplyHooks): Promise<ApplyReport | null> {
      if (running) return null;
      running = true;
      stopAsked = false;
      let saved = false;
      try {
        for (const line of queue) {
          if (stopAsked) return { end: 'stopped', saved };
          const result = await applyOne(line, apply);
          saved ||= result.state === 'applied' || result.state === 'unknown';
          hooks.onResult(line, result);
          if (result.state === 'applied' && result.explanation) {
            hooks.onAllocated?.(line.view.TripId, {
              label: t.results.explainedTo(driverName(line), line.view.TripId),
              explanation: result.explanation,
            });
          }
          if (stopsTheRest(result)) return { end: 'api', saved };
        }
        return { end: 'done', saved };
      } finally {
        running = false;
      }
    },
  };
}
