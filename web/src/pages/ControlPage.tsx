import { t } from '../i18n/t';
import { useCallback, useEffect, useRef, useState } from 'react';
import type { MasterData } from '../App';
import type { Page } from '../pageIds';
import { api, control, forgetReads, type Scope } from '../api';
import { clampStripHours, merge, type AlertTarget, type StripMode } from '../control';
import { ActivityModule } from '../home/Activity';
import { EfficiencyModule } from '../home/Efficiency';
import { EngineModule } from '../home/Engine';
import { IncidentsModule } from '../home/Incidents';
import { ResourcesModule } from '../home/Resources';
import { HomeHero } from '../home/Situation';
import { StripModule } from '../home/Strip';
import { settle, type HomeData, type Loaded } from '../home/data';
import { controlReads } from '../home/requests';
import { read, readScope, write, writeScope } from '../storage';
import type { BarExplanation } from '../Timeline';
import type { OperatingBase, RuleGroup, Trip } from '../types';
import { useRefresh } from '../useRefresh';

const HOURS_KEY = 'dispatchguard.stripHours';
const MODE_KEY = 'dispatchguard.stripMode';
const readMode = (): StripMode => {
  const stored = read(MODE_KEY);
  return stored === 'vehicle' || stored === 'service' ? stored : 'driver';
};

/** No map and no "time saved" (SPEC 0049). No telemetry: "in progress" is by schedule. */
export function ControlPage({
  master,
  version,
  explained,
  onAction,
  onNavigate,
  onOptimize,
}: {
  master: MasterData;
  version: number;
  explained: Map<number, BarExplanation>;
  onAction: (target: AlertTarget, trips: Trip[]) => void;
  onNavigate: (page: Page) => void;
  onOptimize: () => void;
}) {
  const [bases, setBases] = useState<OperatingBase[]>([]);
  const [scope, setScope] = useState<Scope>(readScope);
  const [data, setData] = useState<HomeData | null>(null);
  const [rules, setRules] = useState<Loaded<RuleGroup[]> | null>(null);
  const [updatedAt, setUpdatedAt] = useState<Date | null>(null);
  const [loading, setLoading] = useState(false);
  const [clock, setClock] = useState(() => new Date());
  const [stripHours, setStripHours] = useState(() => clampStripHours(read(HOURS_KEY)));
  const [stripMode, setStripMode] = useState<StripMode>(readMode);
  const [basesError, setBasesError] = useState<string | null>(null);
  const seq = useRef(0); // Discards responses of a previous scope.

  useEffect(() => {
    control
      .bases()
      .then(list => {
        setBases(list);
        setBasesError(null);
        setScope(cur => (cur === 'all' || list.some(b => b.Id === cur) ? cur : 'all'));
      })
      .catch(e => setBasesError((e as Error).message));
    api.rules().then(
      r => setRules({ ok: true, value: r }),
      e => setRules({ ok: false, error: (e as Error).message }),
    );
  }, []);

  const chooseScope = (nextScope: Scope) => {
    setScope(nextScope);
    writeScope(nextScope);
  };
  const chooseHours = (requestedHours: number) => {
    const nextHours = clampStripHours(requestedHours);
    setStripHours(nextHours);
    write(HOURS_KEY, String(nextHours));
  };
  const chooseMode = (nextMode: StripMode) => {
    setStripMode(nextMode);
    write(MODE_KEY, nextMode);
  };

  const load = useCallback(async () => {
    const id = ++seq.current;
    setLoading(true);
    const at = new Date();
    const results = await controlReads(scope, at);
    if (id !== seq.current) return;
    // What fails keeps its last good value, marked as stale; nothing is emptied on refresh.
    setData(prev => ({
      summary: merge(prev?.summary, settle(results[0]), at),
      incidents: merge(prev?.incidents, settle(results[1]), at),
      resources: merge(prev?.resources, settle(results[2]), at),
      metrics: merge(prev?.metrics, settle(results[3]), at),
      metricsYesterday: merge(prev?.metricsYesterday, settle(results[4]), at),
      trips: merge(prev?.trips, settle(results[5]), at),
      trends: merge(prev?.trends, settle(results[6]), at),
      hourly: merge(prev?.hourly, settle(results[7]), at),
      efficiency: merge(prev?.efficiency, settle(results[8]), at),
    }));
    if (results.every(x => x.status === 'fulfilled')) setUpdatedAt(at);
    setClock(at);
    setLoading(false);
  }, [scope]);

  // Another scope is other data (skeletons). A data change (`version`) or the timer reload without emptying the page.
  useEffect(() => {
    setData(null);
    setUpdatedAt(null);
  }, [scope]);
  useEffect(() => {
    void load();
  }, [load, version]);
  const paused = useRefresh(() => void load());
  // The "now" line, the relative deadlines and the stale notes advance even without a reload.
  useEffect(() => {
    const timer = setInterval(() => setClock(new Date()), 30_000);
    return () => clearInterval(timer);
  }, []);

  const driverName = useCallback(
    (id: number) => {
      const driver = master.drivers.find(x => x.Id === id);
      return driver ? `${driver.FirstName} ${driver.LastName}` : t.common.driverOf(id);
    },
    [master.drivers],
  );
  const plate = useCallback((id: number) => master.vehicles.find(v => v.Id === id)?.LicensePlate ?? t.common.vehicleOf(id), [master.vehicles]);
  const trips = data?.trips;
  const act = useCallback((t: AlertTarget) => onAction(t, trips?.ok ? trips.value : []), [onAction, trips]);
  const scopeName = scope === 'all' ? t.common.allBases : (bases.find(b => b.Id === scope)?.Name ?? t.common.baseId(scope));
  const retry = () => {
    forgetReads();
    void load();
  };

  return (
    <div className="page space-y-6">
      <HomeHero
        data={data}
        now={clock}
        bases={bases}
        scope={scope}
        onScope={chooseScope}
        basesError={basesError}
        updatedAt={updatedAt}
        loading={loading}
        paused={paused}
        onRefresh={retry}
      />

      <div className="grid gap-6 xl:grid-cols-12">
        <IncidentsModule
          className="xl:col-span-8"
          incidents={data?.incidents}
          now={clock}
          scopeName={scopeName}
          onAction={act}
          onRetry={retry}
        />
        <EngineModule
          className="xl:col-span-4"
          metrics={data?.metrics}
          yesterday={data?.metricsYesterday}
          hourly={data?.hourly}
          rules={rules}
          now={clock}
          onOpen={() => onNavigate('engine')}
          onRetry={retry}
        />
      </div>

      <StripModule
        trips={data?.trips}
        now={clock}
        hours={stripHours}
        onHours={chooseHours}
        mode={stripMode}
        onMode={chooseMode}
        driverName={driverName}
        plate={plate}
        onOpen={t => act({ kind: 'trip', tripId: t.Id })}
        onRetry={retry}
      />

      {/* Three 4/5/3 columns only from 3xl, when each one is already ≥ 500 px. */}
      <div className="grid gap-6 lg:grid-cols-2 3xl:grid-cols-12">
        <ResourcesModule className="3xl:col-span-4" resources={data?.resources} scopeName={scopeName} now={clock} onRetry={retry} />
        <ActivityModule
          className="3xl:col-span-5"
          scope={scope}
          version={version}
          refreshedAt={updatedAt}
          driverName={driverName}
          plate={plate}
          explained={explained}
          onOpenTrip={id => act({ kind: 'trip', tripId: id })}
        />
        <EfficiencyModule
          className="lg:col-span-2 3xl:col-span-3"
          efficiency={data?.efficiency}
          now={clock}
          scopeName={scopeName}
          onOptimize={onOptimize}
          onRetry={retry}
        />
      </div>
    </div>
  );
}
