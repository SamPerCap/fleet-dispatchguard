// Kept apart so that the modules do not import the page.
import { staleText, type Fresh } from '../control';
import type {
  ControlEfficiency,
  ControlIncident,
  ControlResources,
  ControlSummary,
  ControlTrend,
  EngineHourly,
  EngineMetrics,
  Trip,
} from '../types';

/** Each module shows its own error without taking the page down. */
export type Loaded<T> = { ok: true; value: T } | { ok: false; error: string };

export const settle = <T>(result: PromiseSettledResult<T>): Loaded<T> =>
  result.status === 'fulfilled'
    ? { ok: true, value: result.value }
    : { ok: false, error: (result.reason as Error)?.message ?? String(result.reason) };

export interface HomeData {
  summary: Fresh<ControlSummary>;
  incidents: Fresh<ControlIncident[]>;
  resources: Fresh<ControlResources>;
  metrics: Fresh<EngineMetrics>;
  metricsYesterday: Fresh<EngineMetrics>;
  trips: Fresh<Trip[]>;
  trends: Fresh<ControlTrend>;
  hourly: Fresh<EngineHourly>;
  efficiency: Fresh<ControlEfficiency>;
}

export const staleOf = (fresh: Fresh<unknown> | undefined, now: Date): string | null =>
  fresh?.ok && fresh.stale ? staleText(fresh.stale.since, now) : null;

export const valueOf = <T>(fresh: Fresh<T> | undefined): T | null => (fresh?.ok ? fresh.value : null);
