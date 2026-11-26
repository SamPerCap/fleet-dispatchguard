import { t } from '../i18n/t';
import { ArrowRight, ChevronRight, CircleCheck, History, ShieldAlert, ShieldX, type LucideIcon } from 'lucide-react';
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { control, engine, type Scope } from '../api';
import { activityText, outcomeGroup, outcomeText, relative, staleText, type OutcomeGroup } from '../control';
import { originText } from '../engine';
import { InfoButton } from '../ExplanationDialog';
import { formatDayTime, formatTime, parseApiDate } from '../gantt';
import type { BarExplanation } from '../Timeline';
import type { ActivityItem, ActivityKind, AttemptOrigin, Explanation } from '../types';
import { Badge, EmptyState, ErrorState, Module, Sheet, SkeletonLines, StatusBadge } from '../ui';
import { whoText } from '../people';

const KINDS: { id: ActivityKind; label: string }[] = [
  { id: 'All', get label() { return t.activity.everything; } },
  { id: 'Assigned', get label() { return t.activity.assignments; } },
  { id: 'Blocked', get label() { return t.activity.blocks; } },
  { id: 'Forced', get label() { return t.activity.forced; } },
];
const EMPTY: Record<ActivityKind, string> = {
  get All() { return t.activity.noneYet; },
  get Assigned() { return t.activity.noneAccepted; },
  get Blocked() { return t.activity.noneBlocked; },
  get Forced() { return t.activity.noneForced; },
};
const OUTCOME_ICON: Record<OutcomeGroup, { icon: LucideIcon; cls: string }> = {
  assigned: { icon: CircleCheck, cls: 'bg-success-soft text-success-ink' },
  blocked: { icon: ShieldX, cls: 'bg-critical-soft text-critical-ink' },
  forced: { icon: ShieldAlert, cls: 'bg-warning-soft text-warning-ink' },
};

type StoredExplanation = { id: number; explanation: Explanation | null } | 'loading' | 'error' | null;
type State = { items: ActivityItem[]; total: number; at: Date; staleSince?: Date } | { error: string };

export function ActivityModule({
  scope,
  version,
  refreshedAt,
  driverName,
  plate,
  explained,
  onOpenTrip,
  className,
}: {
  scope: Scope;
  version: number;
  refreshedAt: Date | null;
  driverName: (id: number) => string;
  plate: (id: number) => string;
  explained: Map<number, BarExplanation>;
  onOpenTrip: (tripId: number) => void;
  className?: string;
}) {
  const [kind, setKind] = useState<ActivityKind>('All');
  const [state, setState] = useState<State | null>(null);
  const [open, setOpen] = useState<ActivityItem | null>(null);
  const [now, setNow] = useState(() => new Date());
  const seq = useRef(0);
  const refreshKey = refreshedAt?.getTime() ?? 0;

  useEffect(() => {
    const id = ++seq.current;
    control
      .activity(scope, new Date(Date.now() - 24 * 3_600_000), kind, 15)
      .then(loaded => {
        if (id === seq.current) setState({ items: loaded.Items, total: loaded.TotalCount, at: new Date() });
      })
      // If a reload fails, the previous list is kept and marked as stale.
      .catch(cause => {
        if (id === seq.current)
          setState(prev =>
            prev && 'items' in prev ? { ...prev, staleSince: prev.staleSince ?? prev.at } : { error: (cause as Error).message },
          );
      });
    setNow(new Date());
  }, [scope, kind, version, refreshKey]);
  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), 30_000);
    return () => clearInterval(timer);
  }, []);
  useEffect(() => {
    setState(null);
  }, [scope]);

  const choose = (nextKind: ActivityKind) => {
    if (nextKind !== kind) {
      setState(null);
      setKind(nextKind);
    }
  };
  const items = state && 'items' in state ? state.items : null;

  return (
    <Module
      title={t.activity.title}
      icon={History}
      className={className}
      stale={state && 'items' in state && state.staleSince ? staleText(state.staleSince, now) : null}
      sub={
        <span className="text-meta text-ink-2">
          {t.activity.last24(state && 'total' in state && state.total > state.items.length ? t.activity.shownOf(state.items.length, state.total) : '')}
        </span>
      }
    >
      <div className="seg mb-3" role="group" aria-label={t.activity.filter}>
        {KINDS.map(option => (
          <button key={option.id} type="button" aria-pressed={kind === option.id} onClick={() => choose(option.id)}>
            {option.label}
          </button>
        ))}
      </div>
      {!state && <SkeletonLines lines={5} label={t.activity.loading} />}
      {state && 'error' in state && <ErrorState title={t.activity.failed}>{state.error}</ErrorState>}
      {items && items.length === 0 && (
        <EmptyState icon={History} title={t.activity.none}>
          {EMPTY[kind]}
        </EmptyState>
      )}
      {items && items.length > 0 && (
        <ol className="-mx-2 max-h-[400px] overflow-y-auto">
          {items.map(item => {
            const group = outcomeGroup(item.Outcome);
            const { icon: Icon, cls } = OUTCOME_ICON[group];
            const at = parseApiDate(item.At);
            return (
              <li key={item.Id}>
                <button
                  type="button"
                  onClick={() => setOpen(item)}
                  aria-haspopup="dialog"
                  aria-label={t.activity.seeDetail(outcomeText(item.Outcome), activityText(item, driverName, plate), formatTime(at))}
                  className="group flex w-full items-center gap-3 rounded-md px-2 py-2.5 text-left hover:bg-sunken"
                >
                  <span aria-hidden className={`grid h-8 w-8 shrink-0 place-items-center rounded-full ${cls}`}>
                    <Icon className="h-4 w-4" />
                  </span>
                  <span className="min-w-0 flex-1">
                    <span className="block truncate text-meta font-medium text-ink">{activityText(item, driverName, plate)}</span>
                    <span className="block truncate text-xs text-ink-2">
                      {outcomeText(item.Outcome)} · {t.activity.byWho(whoText(item.Who))}
                    </span>
                  </span>
                  <time className="shrink-0 text-xs text-ink-3" dateTime={at.toISOString()} title={formatDayTime(at)}>
                    {relative(at, now)}
                  </time>
                  <ChevronRight className="h-4 w-4 shrink-0 text-ink-3 group-hover:text-ink" aria-hidden />
                </button>
              </li>
            );
          })}
        </ol>
      )}
      <ActivityDetail
        item={open}
        onClose={() => setOpen(null)}
        driverName={driverName}
        plate={plate}
        explained={explained}
        onOpenTrip={id => {
          setOpen(null);
          onOpenTrip(id);
        }}
      />
    </Module>
  );
}

function ActivityDetail({
  item,
  onClose,
  driverName,
  plate,
  explained,
  onOpenTrip,
}: {
  item: ActivityItem | null;
  onClose: () => void;
  driverName: (id: number) => string;
  plate: (id: number) => string;
  explained: Map<number, BarExplanation>;
  onOpenTrip: (tripId: number) => void;
}) {
  const group = item ? outcomeGroup(item.Outcome) : null;
  const info = item ? explained.get(item.TripId) : undefined;
  // The audit log stores the explanation of each attempt; those of this session are the fallback.
  const [stored, setStored] = useState<StoredExplanation>(null);
  useEffect(() => {
    if (!item) {
      setStored(null);
      return;
    }
    let live = true;
    setStored('loading');
    engine.attempt(item.Id).then(
      d => live && setStored({ id: item.Id, explanation: d.Explanation }),
      () => live && setStored('error'),
    );
    return () => {
      live = false;
    };
  }, [item]);
  const saved = stored && typeof stored === 'object' && stored.id === item?.Id ? stored.explanation : null;
  return (
    <Sheet open={item != null} onClose={onClose} title={t.common.attemptDetail}>
      {item && group && (
        <div className="space-y-5">
          <div className="flex flex-wrap items-center gap-2">
            <OutcomeBadge outcome={item.Outcome} />
            <span className="text-meta text-ink-2">{formatDayTime(parseApiDate(item.At))}</span>
          </div>
          <dl className="grid grid-cols-[120px_minmax(0,1fr)] gap-x-4 gap-y-3 text-data">
            <Row label={t.common.service}>{item.TripLabel?.trim() || t.common.serviceOf(item.TripId)}</Row>
            <Row label={t.common.driver}>{driverName(item.DriverId)}</Row>
            <Row label={t.common.vehicle}>{plate(item.VehicleId)}</Row>
            <Row label={t.common.result}>{outcomeText(item.Outcome)}</Row>
            <Row label={t.common.who}>
              {whoText(item.Who)} <span className="text-ink-3">{t.common.originOf(originText(item.Origin).toLowerCase())}</span>
            </Row>
            <Row label={group === 'forced' ? t.common.reason : t.activity.ruleOrReason}>
              {item.Detail ?? (
                <span className="text-ink-2">
                  {group === 'assigned'
                    ? t.activity.metEverything
                    : t.activity.noReason}
                </span>
              )}
            </Row>
          </dl>
          <div className="well space-y-2 px-4 py-3 text-meta">
            <p className="font-semibold text-ink">{t.common.engineExplanation}</p>
            <EngineExplanation saved={saved} session={info} status={stored} />
          </div>
          <button type="button" className="btn" onClick={() => onOpenTrip(item.TripId)}>
            {t.common.openServiceBooking}
            <ArrowRight className="h-4 w-4" aria-hidden />
          </button>
        </div>
      )}
    </Sheet>
  );
}

function EngineExplanation({
  saved,
  session,
  status,
}: {
  saved: Explanation | null | undefined;
  session: BarExplanation | null | undefined;
  status: StoredExplanation;
}) {
  if (saved)
    return (
      <p className="flex items-center gap-2 text-ink-2">
        {saved.Summary}
        <InfoButton explanation={saved} label={t.activity.attemptExplanation} />
      </p>
    );
  if (session)
    return (
      <p className="flex items-center gap-2 text-ink-2">
        {session.label}
        <InfoButton explanation={session.explanation} label={session.label} />
      </p>
    );
  if (status === 'loading') return <SkeletonLines lines={2} label={t.common.loadingExplanation} />;
  return (
    <p className="text-ink-2">
      {status === 'error'
        ? t.activity.explanationFailed
        : t.activity.olderAttempt}
    </p>
  );
}

export function OutcomeBadge({ outcome }: { outcome: string }) {
  const group = outcomeGroup(outcome);
  if (group === 'assigned') return <StatusBadge status="assigned" label={t.common.assigned} title={t.activity.validated} />;
  if (group === 'forced') return <StatusBadge status="forced" label={t.control.forcedWithReason} />;
  return (
    <span className="inline-flex h-6 shrink-0 items-center gap-1 whitespace-nowrap rounded-full bg-critical-soft pl-1.5 pr-2 text-xs font-semibold text-critical-ink">
      <ShieldX className="h-3.5 w-3.5" aria-hidden />
      {outcomeText(outcome)}
    </span>
  );
}

export const OriginBadge = ({ origin }: { origin?: AttemptOrigin }) =>
  origin === 'Recommended' ? <Badge tone="ok">{originText(origin)}</Badge> : null;

export const Row = ({ label, children }: { label: string; children: ReactNode }) => (
  <>
    <dt className="text-meta text-ink-3">{label}</dt>
    <dd className="min-w-0 break-words text-ink">{children}</dd>
  </>
);
