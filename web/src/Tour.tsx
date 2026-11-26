import { t } from './i18n/t';
import { ShieldX, X } from 'lucide-react';
import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { api, engine } from './api';
import { formatDateTime, parseApiDate } from './gantt';
import { findCase, type TourCase, type TourSource } from './tourCase';

const REJECTED_OF_THE_WEEK = new URLSearchParams({ kind: 'Blocked', days: '7', pageSize: '100' });

const SOURCE: TourSource = {
  trips: api.trips,
  rejectedAttempts: async () => (await engine.attempts(REJECTED_OF_THE_WEEK)).Items,
  driverOptions: api.driverOptions,
};

type State =
  | { at: 'offer' }
  | { at: 'searching' }
  | { at: 'step'; step: number; found: TourCase }
  | { at: 'none' }
  | { at: 'failed'; error: string };

interface Props {
  /** Offered, or already asked for. */
  startAt: 'offer' | 'searching';
  locName: (id: number) => string;
  onTryIt: (trip: TourCase['trip']) => void;
  onEngine: () => void;
  onClose: () => void;
}

/** Not modal: the visitor can leave it or ignore it at any time (SPEC 0093 R2). */
export function Tour({ startAt, locName, onTryIt, onEngine, onClose }: Props) {
  const [state, setState] = useState<State>({ at: startAt });
  const search = useCallback(() => setState({ at: 'searching' }), []);

  const isSearching = state.at === 'searching';
  useEffect(() => {
    if (!isSearching) return;
    let live = true;
    findCase(SOURCE, new Date()).then(
      found => live && setState(found ? { at: 'step', step: 0, found } : { at: 'none' }),
      failure => live && setState({ at: 'failed', error: (failure as Error).message }),
    );
    return () => {
      live = false;
    };
  }, [isSearching]);

  // Only what the visitor asked for takes the focus.
  const heading = useRef<HTMLHeadingElement>(null);
  const step = state.at === 'step' ? state.step : null;
  useEffect(() => {
    if (step !== null) heading.current?.focus();
  }, [step]);

  return (
    <aside
      aria-label={t.app.tour}
      className="fade-in fixed bottom-6 right-6 z-40 w-[min(400px,calc(100vw-48px))] rounded-lg bg-surface p-5 shadow-pop"
    >
      <button type="button" aria-label={t.common.close} className="btn btn-ghost btn-icon absolute right-3 top-3" onClick={onClose}>
        <X className="h-4 w-4" aria-hidden />
      </button>
      {state.at === 'offer' && (
        <Card
          title={t.tour.title}
          actions={
            <>
              <button type="button" className="btn btn-primary" onClick={search}>
                {t.tour.begin}
              </button>
              <button type="button" className="btn btn-text" onClick={onClose}>
                {t.tour.notNow}
              </button>
            </>
          }
        >
          {t.tour.intro}
        </Card>
      )}
      {state.at === 'searching' && (
        <p role="status" className="pr-8 text-data text-ink-2">
          {t.tour.searching}
        </p>
      )}
      {state.at === 'none' && (
        <Card
          title={t.tour.noCase}
          actions={
            <button type="button" className="btn" onClick={onEngine}>
              {t.tour.seeEngine}
            </button>
          }
        >
          {t.tour.noCaseDetail}
        </Card>
      )}
      {state.at === 'failed' && (
        <Card
          title={t.tour.failed}
          actions={
            <button type="button" className="btn" onClick={search}>
              {t.common.retry}
            </button>
          }
        >
          {state.error}
        </Card>
      )}
      {state.at === 'step' && (
        <Step
          heading={heading}
          step={state.step}
          found={state.found}
          locName={locName}
          onNext={() => setState({ ...state, step: state.step + 1 })}
          onBack={() => setState({ ...state, step: state.step - 1 })}
          onTryIt={() => onTryIt(state.found.trip)}
        />
      )}
    </aside>
  );
}

const stepTitles = () => [t.tour.stepService, t.tour.stepDriver, t.tour.stepRejection, t.tour.stepTry];

interface StepProps {
  heading: React.Ref<HTMLHeadingElement>;
  step: number;
  found: TourCase;
  locName: (id: number) => string;
  onNext: () => void;
  onBack: () => void;
  onTryIt: () => void;
}

function Step({ heading, step, found, locName, onNext, onBack, onTryIt }: StepProps) {
  const { trip, driverName, reason } = found;
  const titles = stepTitles();
  const isLast = step === titles.length - 1;
  return (
    <Card
      heading={heading}
      eyebrow={t.tour.step(step + 1, titles.length)}
      title={titles[step]}
      actions={
        <>
          <button type="button" className="btn btn-primary" onClick={isLast ? onTryIt : onNext}>
            {isLast ? t.tour.openBooking : t.common.next}
          </button>
          {step > 0 && (
            <button type="button" className="btn btn-text" onClick={onBack}>
              {t.common.previous}
            </button>
          )}
        </>
      }
    >
      {step === 0 && (
        <>
          {t.tour.serviceOf(trip.Id)} <strong>{locName(trip.OriginLocationId)}</strong> {t.tour.to}{' '}
          <strong>{locName(trip.DestinationLocationId)}</strong>
          {t.tour.leavesAt(formatDateTime(parseApiDate(trip.Start)), formatDateTime(parseApiDate(trip.End)))}
        </>
      )}
      {step === 1 && (
        <>
          <strong>{driverName}</strong> {t.tour.looksFine}
        </>
      )}
      {step === 2 && (
        <>
          <span className="flex items-start gap-2 rounded-md bg-critical-soft p-3 text-critical-ink">
            <ShieldX className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
            <span>
              <strong>{t.tour.notCompliant}</strong> {reason}
            </span>
          </span>
          <span className="mt-3 block">
            {t.tour.justCalculated}
          </span>
        </>
      )}
      {step === 3 && (
        <>
          {t.tour.tryIt(driverName)}
        </>
      )}
    </Card>
  );
}

function Card({
  eyebrow,
  title,
  heading,
  children,
  actions,
}: {
  eyebrow?: string;
  title: string;
  heading?: React.Ref<HTMLHeadingElement>;
  children: ReactNode;
  actions: ReactNode;
}) {
  return (
    <>
      {eyebrow && <p className="eyebrow">{eyebrow}</p>}
      <h2 ref={heading} tabIndex={-1} className="module-title pr-8">
        {title}
      </h2>
      <div className="mt-2 text-data text-ink-2">{children}</div>
      <div className="mt-4 flex flex-wrap gap-2">{actions}</div>
    </>
  );
}
