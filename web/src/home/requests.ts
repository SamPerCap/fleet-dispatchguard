import { api, control, type Scope } from '../api';
import { STRIP_MAX_HOURS, stripWindow } from '../control';
import { addDays, startOfDay } from '../gantt';

/**
 * Every read of the Control Centre, in one place: the page uses it, and the loading scene starts it early so that the page finds
 * its answers kept (SPEC 0102 R6).
 */
export function controlReads(scope: Scope, at: Date) {
  const today = startOfDay(at);
  const yesterday = addDays(today, -1);
  const { from, to } = stripWindow(at, STRIP_MAX_HOURS); // Loads the widest strip: changing the hours does not reload.
  return Promise.allSettled([
    control.summary(scope),
    control.incidents(scope),
    control.resources(scope),
    control.engineMetrics(scope, today),
    control.engineMetrics(scope, yesterday, today),
    api.trips(from, to).then(loaded => loaded.filter(trip => scope === 'all' || trip.BaseId == null || trip.BaseId === scope)),
    control.trends(scope, at, 7),
    control.engineHourly(scope, at),
    control.efficiency(scope, at),
  ] as const);
}
