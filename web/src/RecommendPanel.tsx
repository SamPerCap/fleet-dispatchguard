import { t } from './i18n/t';
import { ArrowRight, BusFront, Check, RefreshCw, TriangleAlert, User, Wand2 } from 'lucide-react';
import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { recommendations } from './api';
import { createApplyRunner } from './applyRun';
import { InfoButton } from './ExplanationDialog';
import { formatDateTime, formatTime, parseApiDate } from './gantt';
import { createLatestGuard } from './latest';
import {
  applyQueue,
  checkInput,
  isApplied,
  isManual,
  leftOutText,
  lineState,
  merge,
  optionText,
  reachText,
  summaryText,
  toLines,
  withPair,
  withResult,
  type LineState,
  type Pair,
  type ProposalLine,
} from './recommend';
import type { BarExplanation } from './Timeline';
import type { Recommendation, RecommendationOptions, RecommendationRequest, RecommendedResource } from './types';
import { Badge, EmptyState, ErrorBox, SkeletonLines } from './ui';
import { XRayPanel } from './XRayPanel';

const TONE: Record<LineState['key'], 'ok' | 'warn' | 'danger' | 'muted'> = {
  proposed: 'ok',
  manual: 'ok',
  applied: 'ok',
  noSolution: 'warn',
  busy: 'warn',
  limited: 'warn',
  unknown: 'warn',
  removed: 'muted',
  invalid: 'danger',
  rejected: 'danger',
};

const problem = (error: unknown) => (error instanceof Error ? error.message : String(error));

/**
 * Nothing is stored before applying; each line is assigned on its own with the usual validation, so one that is no longer valid is
 * rejected with its reason and the rest goes on.
 */
export function RecommendPanel({
  request,
  onApplied,
  onAllocated,
  onClose,
}: {
  request: RecommendationRequest;
  /** Something was stored: refresh what is shown behind. */
  onApplied: () => void;
  onAllocated: (tripId: number, info: BarExplanation) => void;
  onClose: () => void;
}) {
  const [proposal, setProposal] = useState<Recommendation | null>(null);
  const [lines, setLines] = useState<ProposalLine[]>([]);
  const [editing, setEditing] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<'propose' | 'check' | 'apply' | null>('propose');
  const [guard] = useState(createLatestGuard);
  // Read again on recalculating; not requested again if the parent creates another equal object.
  const asked = useRef(request);
  asked.current = request;
  const [runner] = useState(() => createApplyRunner(recommendations.apply));
  useEffect(() => () => runner.stop(), [runner]); // Closing the panel leaves the rest unapplied.

  const propose = useCallback(async () => {
    const current = guard.begin();
    setBusy('propose');
    setError(null);
    setEditing(null);
    try {
      const recommendation = await recommendations.propose(asked.current);
      if (!current()) return;
      setProposal(recommendation);
      setLines(toLines(recommendation));
    } catch (cause) {
      if (current()) setError(t.recommend.failed(problem(cause)));
    }
    if (current()) setBusy(null);
  }, [guard]);
  useEffect(() => {
    void propose();
  }, [propose]);

  /** The changed line and all the others go through the engine again. */
  const change = async (tripId: number, pair: Pair) => {
    const next = withPair(lines, tripId, pair);
    const current = guard.begin();
    setBusy('check');
    setError(null);
    try {
      const checked = await recommendations.check(checkInput(next));
      if (!current()) return;
      setLines(merge(next, checked));
      setEditing(null);
    } catch (cause) {
      if (current()) setError(t.recommend.checkFailed(problem(cause)));
    }
    if (current()) setBusy(null);
  };

  const applyLines = async (queue: ProposalLine[]) => {
    setBusy('apply');
    setError(null);
    setEditing(null);
    const report = await runner.run(queue, {
      onResult: (line, result) => setLines(prev => withResult(prev, line.view.TripId, result)),
      onAllocated,
    });
    if (!report) return; // Another run was already in progress.
    setBusy(null);
    if (report.saved) onApplied();
  };

  const queue = applyQueue(lines);
  const done = lines.filter(isApplied).length;
  const pendingWork = lines.some(l => isManual(l) && !isApplied(l));

  if (!proposal) {
    return error ? (
      <div className="space-y-3">
        <ErrorBox>{error}</ErrorBox>
        <button type="button" className="btn btn-sm" onClick={() => void propose()}>
          {t.common.retry}
        </button>
      </div>
    ) : (
      <SkeletonLines lines={6} label={t.recommend.calculating} />
    );
  }

  const leftOut = leftOutText(proposal);
  return (
    <div className="space-y-4">
      <div className="space-y-1.5 rounded-lg bg-blue-soft/60 p-4 text-meta ring-1 ring-blue/25">
        <div className="flex flex-wrap items-center gap-2">
          <Badge tone="muted">{proposal.Note}</Badge>
          <span className="text-ink-2">{t.recommend.calculatedAt(formatTime(parseApiDate(proposal.CalculatedAt)))}</span>
          <button
            type="button"
            className="btn btn-sm ml-auto"
            disabled={busy != null}
            onClick={() => void propose()}
            title={pendingWork ? t.recommend.losesChanges : undefined}
          >
            <RefreshCw className="h-4 w-4" aria-hidden />
            {busy === 'propose' ? t.common.calculating : t.common.recalculate}
          </button>
        </div>
        <p className="font-semibold text-ink" role="status">
          {summaryText(lines)}
        </p>
        {leftOut && <p className="font-medium text-warning-ink">{leftOut}</p>}
        <p className="text-xs text-ink-2">
          {t.recommend.nothingSavedYet(proposal.ArrivalNote)}
        </p>
      </div>

      <XRayPanel kind="recommendation" />
      {error && <ErrorBox>{error}</ErrorBox>}
      {lines.length === 0 && (
        <EmptyState icon={Check} title={t.recommend.nothing}>
          {t.recommend.which}
        </EmptyState>
      )}

      <ul className="space-y-3">
        {lines.map(line => (
          <Line
            key={line.view.TripId}
            line={line}
            steps={proposal.StepsKm}
            busy={busy != null}
            editing={editing === line.view.TripId}
            lines={lines}
            onEdit={open => setEditing(open ? line.view.TripId : null)}
            onChange={pair => change(line.view.TripId, pair)}
            onRetry={() => void applyLines([line])}
          />
        ))}
      </ul>

      {lines.length > 0 && (
        <div className="sticky bottom-0 -mx-1 flex flex-wrap items-center gap-2 border-t border-line bg-surface px-1 py-3">
          <button
            type="button"
            className="btn btn-primary"
            disabled={busy != null || queue.length === 0}
            onClick={() => void applyLines(queue)}
          >
            <Check className="h-4 w-4" aria-hidden />
            {busy === 'apply' ? t.recommend.applying : t.recommend.apply(queue.length)}
          </button>
          <button type="button" className="btn" onClick={onClose}>
            {done > 0 && queue.length === 0 ? t.common.done : t.common.close}
          </button>
          {busy === 'check' && (
            <span className="text-meta text-ink-2" role="status">
              {t.recommend.checkingAll}
            </span>
          )}
          {busy === 'apply' && (
            <span className="text-meta text-ink-2" role="status">
              {t.recommend.appliedOf(done, lines.length)}
            </span>
          )}
        </div>
      )}
    </div>
  );
}

function Line({
  line,
  lines,
  steps,
  busy,
  editing,
  onEdit,
  onChange,
  onRetry,
}: {
  line: ProposalLine;
  lines: ProposalLine[];
  steps: number[];
  busy: boolean;
  editing: boolean;
  onEdit: (open: boolean) => void;
  onChange: (pair: Pair) => Promise<void>;
  onRetry: () => void;
}) {
  const view = line.view;
  const state = lineState(line);
  const start = parseApiDate(view.Start);
  const retry = line.result?.state === 'busy' || line.result?.state === 'limited' || line.result?.state === 'unknown';
  const why =
    line.result && line.result.state !== 'applied'
      ? line.result.text
      : state.key === 'noSolution' || state.key === 'invalid'
        ? view.Reason
        : null;
  const explanation = line.result?.state === 'applied' || line.result?.state === 'rejected' ? line.result.explanation : view.Explanation;
  return (
    <li className="rounded-lg bg-surface p-4 shadow-1">
      <div className="flex flex-wrap items-center gap-2 text-xs text-ink-2">
        <Badge tone={TONE[state.key]}>{state.text}</Badge>
        <span className="font-semibold text-ink">{view.CustomerName}</span>
        <span>
          {t.recommend.lineInfo(formatDateTime(start), formatTime(parseApiDate(view.End)), view.RequiredSeats, view.TripId)}
        </span>
        <span className="ml-auto">
          <InfoButton
            explanation={explanation}
            label={t.recommend.explained(isApplied(line) ? t.recommend.allocationOf : line.result?.state === 'rejected' ? t.recommend.whyRejected : t.recommend.proposalFor, view.TripId)}
          />
        </span>
      </div>
      <p className="mt-1.5 flex flex-wrap items-center gap-1.5 text-data font-semibold text-ink">
        {view.Origin}
        <ArrowRight className="h-4 w-4 text-ink-3" aria-label={t.common.to} />
        {view.Destination}
      </p>
      {(view.Driver || view.Vehicle) && state.key !== 'removed' && (
        <dl className="mt-2 grid gap-2 border-t border-line pt-2 text-meta sm:grid-cols-2">
          <Resource icon={<User className="h-4 w-4" aria-hidden />} label={t.common.driver} value={view.Driver} steps={steps} />
          <Resource icon={<BusFront className="h-4 w-4" aria-hidden />} label={t.common.vehicle} value={view.Vehicle} steps={steps} />
        </dl>
      )}
      {why && (
        <p
          className={`mt-2 rounded-md px-3 py-2 text-meta font-medium ${TONE[state.key] === 'danger' ? 'bg-critical-soft text-critical-ink' : 'bg-warning-soft text-warning-ink'}`}
        >
          {why}
        </p>
      )}
      {view.Warnings.map(warning => (
        <p key={warning} className="mt-1 text-xs text-warning-ink">
          ⚠ {warning}
        </p>
      ))}
      {!isApplied(line) && !editing && (
        <div className="mt-3 flex flex-wrap gap-2">
          <button
            type="button"
            className="btn btn-sm"
            disabled={busy}
            onClick={() => onEdit(true)}
            aria-label={t.recommend.changeService(view.TripId)}
          >
            <Wand2 className="h-4 w-4" aria-hidden />
            {line.chosen.driverId == null ? t.recommend.chooseByHand : t.common.change}
          </button>
          {line.chosen.driverId != null && line.chosen.vehicleId != null && (
            <button
              type="button"
              className="btn btn-sm btn-text"
              disabled={busy}
              onClick={() => void onChange({ driverId: null, vehicleId: null })}
              aria-label={t.recommend.removeService(view.TripId)}
            >
              {t.common.remove}
            </button>
          )}
          {isManual(line) && (
            <button
              type="button"
              className="btn btn-sm btn-text"
              disabled={busy}
              onClick={() => void onChange(line.proposed)}
              aria-label={t.recommend.backToProposed(view.TripId)}
            >
              {t.recommend.back}
            </button>
          )}
          {retry && (
            <button type="button" className="btn btn-sm" disabled={busy} onClick={onRetry}>
              {t.common.retry}
            </button>
          )}
        </div>
      )}
      {editing && <LineEditor line={line} lines={lines} steps={steps} busy={busy} onCancel={() => onEdit(false)} onChange={onChange} />}
    </li>
  );
}

const Resource = ({
  icon,
  label,
  value,
  steps,
}: {
  icon: ReactNode;
  label: string;
  value: RecommendedResource | null;
  steps: number[];
}) => (
  <div className="flex items-start gap-2">
    <span className="mt-0.5 grid h-7 w-7 shrink-0 place-items-center rounded-full bg-muted text-ink-3">{icon}</span>
    <dt className="sr-only">{label}</dt>
    <dd className="min-w-0">
      <span className="font-medium text-ink">{value?.Name ?? t.recommend.without(label.toLowerCase())}</span>
      {value && <span className="block text-xs text-ink-2">{reachText(value, steps)}</span>}
      {value?.Late && (
        <span className="flex items-start gap-1 text-xs text-warning-ink">
          <TriangleAlert className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden />
          {value.Late}
        </span>
      )}
    </dd>
  </div>
);

/** Everything legal given the other lines, including what is far or would not arrive: it is said, and a person decides. */
function LineEditor({
  line,
  lines,
  steps,
  busy,
  onCancel,
  onChange,
}: {
  line: ProposalLine;
  lines: ProposalLine[];
  steps: number[];
  busy: boolean;
  onCancel: () => void;
  onChange: (pair: Pair) => Promise<void>;
}) {
  const [options, setOptions] = useState<RecommendationOptions | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [driverId, setDriverId] = useState<number | null>(line.chosen.driverId);
  const [vehicleId, setVehicleId] = useState<number | null>(line.chosen.vehicleId);
  const tripId = line.view.TripId;
  useEffect(() => {
    let current = true;
    recommendations
      .options(tripId, checkInput(lines))
      .then(loaded => {
        if (!current) return;
        setOptions(loaded);
        setDriverId(id => id ?? loaded.Drivers[0]?.Id ?? null);
        setVehicleId(id => id ?? loaded.Vehicles[0]?.Id ?? null);
      })
      .catch(e => current && setError(problem(e)));
    return () => {
      current = false;
    };
  }, []); // eslint-disable-line react-hooks/exhaustive-deps -- the lines do not change while the editor is open

  if (error)
    return (
      <div className="mt-3 space-y-2">
        <ErrorBox>{t.recommend.optionsFailed(error)}</ErrorBox>
        <button type="button" className="btn btn-sm" onClick={onCancel}>
          {t.common.cancel}
        </button>
      </div>
    );
  if (!options)
    return (
      <div className="mt-3">
        <SkeletonLines lines={2} label={t.recommend.searching} />
      </div>
    );
  return (
    <div className="mt-3 space-y-3 border-t border-line pt-3">
      <Choice
        label={t.common.driver}
        none={t.recommend.noLegalDriver}
        value={driverId}
        onChange={setDriverId}
        options={options.Drivers}
        current={line.view.Driver}
        steps={steps}
      />
      <Choice
        label={t.common.vehicle}
        none={t.recommend.noFitVehicle}
        value={vehicleId}
        onChange={setVehicleId}
        options={options.Vehicles}
        current={line.view.Vehicle}
        steps={steps}
      />
      <p className="text-xs text-ink-2">{t.recommend.allCheckedAgain}</p>
      <div className="flex gap-2">
        <button
          type="button"
          className="btn btn-sm btn-primary"
          disabled={busy || driverId == null || vehicleId == null}
          onClick={() => void onChange({ driverId, vehicleId })}
        >
          {busy ? t.recommend.checking : t.recommend.usePair}
        </button>
        <button type="button" className="btn btn-sm" disabled={busy} onClick={onCancel}>
          {t.common.cancel}
        </button>
      </div>
    </div>
  );
}

function Choice({
  label,
  none,
  value,
  onChange,
  options,
  current,
  steps,
}: {
  label: string;
  none: string;
  value: number | null;
  onChange: (id: number | null) => void;
  options: RecommendedResource[];
  current: RecommendedResource | null;
  steps: number[];
}) {
  const missing = current && !options.some(o => o.Id === current.Id) ? current : null;
  if (options.length === 0 && !missing)
    return <p className="rounded-md bg-warning-soft px-3 py-2 text-meta font-medium text-warning-ink">{none}</p>;
  return (
    <label className="block">
      <span className="label">{label}</span>
      <select className="input mt-1 w-full" value={value ?? ''} onChange={e => onChange(Number(e.target.value) || null)}>
        {value == null && <option value="">{t.recommend.choose(label.toLowerCase())}</option>}
        {missing && <option value={missing.Id}>{t.recommend.noLonger(missing.Name)}</option>}
        {options.map(choice => (
          <option key={choice.Id} value={choice.Id}>
            {optionText(choice, steps)}
          </option>
        ))}
      </select>
    </label>
  );
}
