import { useId, useState, useSyncExternalStore } from 'react';
import { ScanSearch } from 'lucide-react';
import { api, ApiError } from './api';
import { t } from './i18n/t';
import { xrayNumber, xrays, type XRay, type XRayCheck, type XRayFigure, type XRayKind } from './xray';

/**
 * «Look inside» the answer just shown (SPEC 0097): closed until asked, and then only what the API sealed for this session. Every
 * figure comes from the server; the panel only names the codes.
 */
export function XRayPanel({ kind }: { kind: XRayKind }) {
  const latest = useSyncExternalStore(xrays.subscribe, () => xrays.latest(kind), () => xrays.latest(kind));
  if (!latest) return null;
  return <XRayDisclosure key={latest.version} token={latest.token} />;
}

type Reading = { state: 'closed' } | { state: 'loading' } | { state: 'shown'; xray: XRay } | { state: 'failed'; expired: boolean };

function XRayDisclosure({ token }: { token: string }) {
  const [reading, setReading] = useState<Reading>({ state: 'closed' });
  const panelId = useId();
  const titleId = useId();
  const open = reading.state !== 'closed';

  const toggle = async () => {
    if (open) {
      setReading({ state: 'closed' });
      return;
    }
    setReading({ state: 'loading' });
    try {
      setReading({ state: 'shown', xray: await api.xray(token) });
    } catch (cause) {
      setReading({ state: 'failed', expired: cause instanceof ApiError && cause.status === 404 });
    }
  };

  return (
    <div className="rounded-md border border-line text-meta">
      <button
        type="button"
        className="btn btn-sm m-1"
        aria-expanded={open}
        aria-controls={panelId}
        onClick={() => void toggle()}
      >
        <ScanSearch className="h-3.5 w-3.5" aria-hidden />
        {open ? t.xray.close : t.xray.open}
      </button>
      {open && (
        <section id={panelId} aria-labelledby={titleId} className="space-y-3 border-t border-line px-3 py-3">
          <h3 id={titleId} className="font-semibold text-ink">
            {t.xray.title}
          </h3>
          <p className="text-ink-2">{t.xray.note}</p>
          {reading.state === 'loading' && <p role="status">{t.xray.loading}</p>}
          {reading.state === 'failed' && <p role="alert">{reading.expired ? t.xray.expired : t.xray.failed}</p>}
          {reading.state === 'shown' && <XRayBody xray={reading.xray} />}
        </section>
      )}
    </div>
  );
}

const named = (names: Record<string, string>, code: string) => names[code] ?? code;

export function XRayBody({ xray }: { xray: XRay }) {
  return (
    <div className="space-y-3 break-words">
      <p className="font-medium text-ink">
        {t.xray.summary(xray.Status, xrayNumber(xray.TotalMs), xray.Queries, xrayNumber(xray.QueriesMs))}
      </p>
      {xray.Evaluations > 1 && <p>{t.xray.evaluations(xray.Evaluations)}</p>}
      {xray.Checks.length > 0 && <Checks checks={xray.Checks} />}
      {xray.Checks.length === 0 && xray.Rules.length > 0 && <RuleCounts xray={xray} />}
      <Stages xray={xray} />
      <div>
        <h4 className="font-semibold text-ink">{t.xray.locks}</h4>
        {xray.Locks.length === 0 ? (
          <p>{t.xray.noLocks}</p>
        ) : (
          <ul className="list-disc pl-5">
            {xray.Locks.map(lock => (
              <li key={lock.Resource}>{t.xray.lock(named(t.xrayResource, lock.Resource), lock.Count)}</li>
            ))}
          </ul>
        )}
      </div>
      <div>
        <h4 className="font-semibold text-ink">{t.xray.writes}</h4>
        {xray.Writes.length === 0 ? (
          <p>{t.xray.noWrites}</p>
        ) : (
          <ul className="list-disc pl-5">
            {xray.Writes.map(write => (
              <li key={`${write.Record}-${write.Saved}`}>
                {t.xray.write(named(t.xrayRecord, write.Record), write.Count, write.Saved ? t.xray.saved : t.xray.discarded)}
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}

function Checks({ checks }: { checks: XRayCheck[] }) {
  return (
    <div>
      <h4 className="font-semibold text-ink">{t.xray.checks}</h4>
      <ol className="list-decimal space-y-1 pl-5">
        {checks.map(check => (
          <li key={check.Step}>
            <span className="font-medium text-ink">{named(t.xrayStep, check.Step)}</span>
            {': '}
            <span data-outcome={check.Outcome}>{named(t.xrayOutcome, check.Outcome)}</span>
            {check.Rules.length > 0 && <Figures figures={check.Rules} />}
          </li>
        ))}
      </ol>
    </div>
  );
}

function Figures({ figures }: { figures: XRayFigure[] }) {
  const measured = figures.some(figure => figure.Limit !== null);
  return (
    <>
      <ul className="mt-1 space-y-0.5 pl-1 text-ink-2">
        {figures.map((figure, index) => (
          <li key={`${figure.Rule}-${index}`}>
            <span className={figure.Passed ? undefined : 'font-semibold text-critical-ink'}>
              {[figure.Article, figure.Name ?? figure.Rule].filter(Boolean).join(' · ')}
            </span>
            {figure.Actual !== null && figure.Limit !== null && (
              <>
                {': '}
                {t.xray.figure(xrayNumber(figure.Actual, 2), xrayNumber(figure.Limit, 2), named(t.xrayUnit, figure.Unit ?? ''))}
              </>
            )}
            {' — '}
            {figure.Passed ? t.xrayOutcome.passed : t.xrayOutcome.rejected}
          </li>
        ))}
      </ul>
      {measured && <p className="mt-0.5 text-ink-3">{t.xray.limitSource}</p>}
    </>
  );
}

function RuleCounts({ xray }: { xray: XRay }) {
  return (
    <div>
      <h4 className="font-semibold text-ink">{t.xray.rules}</h4>
      <ul className="space-y-0.5">
        {xray.Rules.map(rule => (
          <li key={rule.Rule}>
            <span className="font-medium text-ink">{rule.Rule}</span>
            {': '}
            {t.xray.ruleCount(rule.Passed, rule.Failed)}
          </li>
        ))}
      </ul>
    </div>
  );
}

function Stages({ xray }: { xray: XRay }) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[18rem] text-left">
        <caption className="text-left font-semibold text-ink">{t.xray.stages}</caption>
        <thead className="text-ink-3">
          <tr>
            <th scope="col" className="py-1 pr-2 font-medium">{t.xray.stage}</th>
            <th scope="col" className="py-1 pr-2 text-right font-medium">{t.xray.times}</th>
            <th scope="col" className="py-1 pr-2 text-right font-medium">{t.xray.milliseconds}</th>
            <th scope="col" className="py-1 text-right font-medium">{t.xray.queries}</th>
          </tr>
        </thead>
        <tbody>
          {xray.Stages.map(stage => (
            <tr key={stage.Stage} className="border-t border-line">
              <th scope="row" className="py-1 pr-2 font-normal">{named(t.xrayStep, stage.Stage)}</th>
              <td className="py-1 pr-2 text-right tabular-nums">{stage.Times}</td>
              <td className="py-1 pr-2 text-right tabular-nums">{xrayNumber(stage.Ms)}</td>
              <td className="py-1 text-right tabular-nums">{stage.Queries}</td>
            </tr>
          ))}
          <tr className="border-t border-line text-ink-2">
            <th scope="row" className="py-1 pr-2 font-normal">{t.xray.other}</th>
            <td className="py-1 pr-2" />
            <td className="py-1 pr-2 text-right tabular-nums">{xrayNumber(xray.OtherMs)}</td>
            <td className="py-1" />
          </tr>
        </tbody>
      </table>
    </div>
  );
}
