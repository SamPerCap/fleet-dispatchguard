import { t } from './i18n/t';
import { useEffect, useRef, useState } from 'react';
import { ApiError } from './api';
import { failureOf, type AttemptTarget, type Outcome } from './attempt';
import { InfoButton } from './ExplanationDialog';
import { forceError, laterServicesText, MAX_REASON, MIN_REASON, type ForceRequest } from './force';
import { assignedBy } from './operator';
import { failureTone } from './retry';
import type { LaterService } from './types';
import { XRayPanel } from './XRayPanel';

export function AssignResults({
  results,
  busy,
  onRetry,
  onForce,
  onDone,
}: {
  results: Outcome[];
  busy: boolean;
  onRetry: (result: Outcome) => void;
  onForce: (result: Outcome, request: ForceRequest) => Promise<void>;
  onDone: () => void;
}) {
  // They replace the button that was pressed: without this, the focus would go back to the top of the panel.
  const list = useRef<HTMLDivElement>(null);
  // The form they replace may be much taller and scrolled halfway: they could be left above what is seen.
  useEffect(() => {
    list.current?.scrollIntoView({ block: 'nearest' });
    list.current?.focus({ preventScroll: true });
  }, []);
  return (
    <div ref={list} tabIndex={-1} className="mt-3 space-y-2" role="status">
      {results.map(result => (
        <p
          key={result.target.tripId}
          className={`flex items-center gap-1 rounded-md px-3 py-2 text-meta font-medium ${result.ok ? 'bg-success-soft text-success-ink' : failureTone(result.retryable === true)}`}
        >
          {t.results.line(result.ok ? '✓' : result.retryable ? '↻' : '✗', result.target.tripId, result.text)}
          {result.retryable && (
            <button type="button" className="btn btn-sm ml-auto shrink-0" disabled={busy} onClick={() => onRetry(result)}>
              {busy ? t.common.retrying : t.common.retry}
            </button>
          )}
          <InfoButton
            explanation={result.explanation}
            label={
              result.ok ? t.results.explainedOk(result.target.tripId) : t.results.explainedRejected(result.target.tripId)
            }
          />
        </p>
      ))}
      {results
        .filter(r => r.forcible)
        .map(result => (
          <ForceForm
            key={`f${result.target.tripId}`}
            target={result.target}
            later={result.laterServices ?? []}
            onForce={request => onForce(result, request)}
          />
        ))}
      <XRayPanel kind="allocation" />
      <button type="button" className="btn btn-sm" onClick={onDone}>
        {t.common.done}
      </button>
    </div>
  );
}

/**
 * Offered only if the API marked the rejection as forcible; the server revalidates everything (unavailable driver, vehicle and
 * overlap are never forced, ADR 0015).
 */
function ForceForm({
  target,
  later,
  onForce,
}: {
  target: AttemptTarget;
  later: LaterService[];
  onForce: (request: ForceRequest) => Promise<void>;
}) {
  const { tripId } = target;
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState('');
  const [by, setBy] = useState(assignedBy());
  const [error, setError] = useState<{ text: string; retryable?: boolean } | null>(null);
  const [busy, setBusy] = useState(false);
  if (!open)
    return (
      <button type="button" className="btn btn-sm" onClick={() => setOpen(true)}>
        {t.results.forceOpen(tripId)}
      </button>
    );
  const submit = async () => {
    const invalid = forceError(reason, by);
    if (invalid) {
      setError({ text: invalid });
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await onForce({ reason, by });
    } catch (cause) {
      setError({ text: failureOf(target, cause), retryable: cause instanceof ApiError && cause.retryable });
      setBusy(false);
    }
  };
  return (
    <div className="space-y-3 rounded-lg bg-warning-soft p-4 text-meta">
      <p className="font-semibold text-warning-ink">{t.results.forceTitle(tripId)}</p>
      <p className="text-ink-2">{t.results.forceNote}</p>
      {later.length > 0 && (
        <div className="rounded-md bg-surface px-3 py-2.5">
          <p className="font-semibold text-warning-ink">{laterServicesText(tripId, later)}</p>
          <ul aria-label={t.results.laterAffected} className="mt-1.5 max-h-48 list-disc space-y-1 overflow-y-auto pl-5 text-ink">
            {later.map((service, index) => (
              <li key={index}>{service.Message}</li>
            ))}
          </ul>
        </div>
      )}
      <label className="block">
        <span className="label">
          {t.results.reasonField(MIN_REASON, MAX_REASON)}
        </span>
        <textarea
          className="input mt-1 w-full border-ink-3/75 hover:border-ink-3"
          rows={3}
          maxLength={MAX_REASON}
          value={reason}
          onChange={e => setReason(e.target.value)}
        />
      </label>
      <label className="block">
        <span className="label">{t.results.whoDecides}</span>
        <input className="input mt-1 w-full border-ink-3/75 hover:border-ink-3" value={by} onChange={e => setBy(e.target.value)} />
      </label>
      {error && (
        <p className={`rounded-md px-3 py-2 font-medium ${failureTone(error.retryable === true)}`} role="alert">
          {error.text}
        </p>
      )}
      <div className="flex gap-2">
        <button type="button" className="btn btn-sm" disabled={busy} onClick={submit}>
          {busy ? t.results.forcing : t.results.force}
        </button>
        <button type="button" className="btn btn-sm" disabled={busy} onClick={() => setOpen(false)}>
          {t.common.cancel}
        </button>
      </div>
    </div>
  );
}
