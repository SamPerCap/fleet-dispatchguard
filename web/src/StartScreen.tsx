import { t } from './i18n/t';
import { RefreshCw } from 'lucide-react';
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { BRAND } from './brand';
import { BrandMark } from './brand/BrandArt';
import { createColdStart, isReady, type ColdStartState } from './coldStart';
import { session } from './session';
import { coldStartWait } from './showcase/measurements';
import { Traffic } from './Traffic';

function waking(): string {
  const { wait, measuredOn } = coldStartWait();
  return t.start.waking(wait, measuredOn);
}

/** Nothing of the web is mounted until the database answers: every view would fail. */
export function StartScreen({ children }: { children: ReactNode }) {
  const [state, setState] = useState<ColdStartState | null>(null);
  const coldStart = useRef<ReturnType<typeof createColdStart> | null>(null);

  useEffect(() => {
    const started = createColdStart({
      ask: () => isReady(session.fetch),
      onState: setState,
    });
    coldStart.current = started;
    return started.stop;
  }, []);

  if (state === 'ready') return children;
  if (state === null) return null;
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-6 p-6">
      {state === 'preparing' && <Traffic />}
      <div className="w-full max-w-md rounded-lg bg-surface p-8 text-center shadow-2">
        <div className="flex justify-center">
          <BrandMark size={48} />
        </div>
        <p className="mt-3 text-meta text-ink-2">
          {BRAND.name} · {t.common.tagline}
        </p>
        <div role="status">
          {state === 'preparing' ? (
            <>
              <h1 className="mt-4 text-page font-bold text-ink">{t.start.preparing}</h1>
              <p className="mt-2 text-data text-ink-2">
                {waking()}
              </p>
            </>
          ) : (
            <>
              <h1 className="mt-4 text-page font-bold text-ink">{t.start.unavailable}</h1>
              <p className="mt-2 text-data text-ink-2">
                {t.start.timedOut}
              </p>
            </>
          )}
        </div>
        {state === 'late' && (
          <button type="button" className="btn btn-primary mt-6" onClick={() => coldStart.current?.retry()}>
            <RefreshCw className="h-4 w-4" aria-hidden />
            {t.start.tryAgain}
          </button>
        )}
      </div>
    </main>
  );
}
