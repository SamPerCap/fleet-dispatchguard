import { RefreshCw } from 'lucide-react';
import { useEffect, useState } from 'react';
import { BRAND } from '../brand';
import { BrandMark } from '../brand/BrandArt';
import { language } from '../language';
import { TrafficRoad } from '../Traffic';
import type { ScenePhase } from './loadingPhases';
import { coldStartWait } from './measurements';
import { showcaseText } from './showcaseText';

/** Flavour, not progress: it changes on a clock, never on a step (SPEC 0101 R3). */
export const MESSAGE_EVERY_MS = 1_500;

function useRotatingMessage(messages: string[], rotating: boolean): string {
  const [index, setIndex] = useState(0);
  useEffect(() => {
    if (!rotating) return;
    const timer = setInterval(() => setIndex(current => (current + 1) % messages.length), MESSAGE_EVERY_MS);
    return () => clearInterval(timer);
  }, [messages.length, rotating]);
  return messages[index % messages.length];
}

/** Shown from `red` to `in`; `quiet` paints nothing so that a warm start never flashes (SPEC 0101 R5). */
export function LoadingScene({ phase, databaseWaking, onRetry }: { phase: ScenePhase; databaseWaking: boolean; onRetry: () => void }) {
  const { tagline, scene, tryAgain } = showcaseText();
  const message = useRotatingMessage(scene.messages, phase === 'red');
  if (phase === 'quiet' || phase === 'in' || phase === 'failed') return null;
  const { wait, measuredOn } = coldStartWait();
  const green = phase === 'green';
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-8 p-6" lang={language()}>
      <TrafficRoad green={green} />
      <div role="status" className="text-center">
        {phase === 'late' ? (
          <h1 className="text-page font-bold text-ink">{scene.unavailable}</h1>
        ) : (
          <h1 className="text-page font-bold text-ink">{green ? scene.allLoaded : message}</h1>
        )}
      </div>
      <div className="w-full max-w-md rounded-lg bg-surface p-8 text-center shadow-2">
        <div className="flex justify-center">
          <BrandMark size={48} />
        </div>
        <p className="mt-3 text-meta text-ink-2">
          {BRAND.name} · {tagline}
        </p>
        {phase === 'late' ? (
          <p className="mt-4 text-data text-ink-2">{scene.timedOut}</p>
        ) : (
          // Only when it is the database that keeps the light red: a slow download is not a cold start.
          databaseWaking && <p className="mt-4 text-data text-ink-2">{scene.waking(wait, measuredOn)}</p>
        )}
        {phase === 'late' && (
          <button type="button" className="btn btn-primary mt-6" onClick={onRetry}>
            <RefreshCw className="h-4 w-4" aria-hidden />
            {tryAgain}
          </button>
        )}
      </div>
    </main>
  );
}
