import { QUIET_MS } from './quietTime';

// The database of the demo sleeps when nobody uses it, and takes a while to wake up (SPEC 0091).

type Fetch = (url: string, init?: RequestInit) => Promise<Response>;

export type ColdStartState = 'preparing' | 'late' | 'ready';

export const ASK_EVERY_MS = 3_000;
export const GIVE_UP_AFTER_MS = 120_000;
export { QUIET_MS };
/** The API gives up before: this is for a connection that never answers. */
const ANSWER_WITHIN_MS = 10_000;

/** A page is what a host in front answers in place of the API. */
export async function isReady(fetchFrom: Fetch): Promise<boolean> {
  const response = await fetchFrom('/health/ready', { cache: 'no-store', signal: AbortSignal.timeout(ANSWER_WITHIN_MS) });
  return response.ok && !response.headers.get('Content-Type')?.startsWith('text/html');
}

/** Says nothing until there is something to say: with the database up, the first news is `ready`. */
export function createColdStart({ ask, onState }: { ask: () => Promise<boolean>; onState: (state: ColdStartState) => void }) {
  let state: ColdStartState | null = null;
  let run = 0;
  let timers: ReturnType<typeof setTimeout>[] = [];

  const go = (next: ColdStartState) => {
    if (state === next) return;
    state = next;
    onState(next);
  };

  const stop = () => {
    run++;
    timers.forEach(clearTimeout);
    timers = [];
  };

  const start = () => {
    stop();
    const current = run;
    const askAgain = async () => {
      const ready = await ask().catch(() => false);
      if (current !== run) return;
      if (ready) {
        stop();
        go('ready');
      } else if (state !== 'late') {
        go('preparing');
        timers.push(setTimeout(() => void askAgain(), ASK_EVERY_MS));
      }
    };
    timers.push(
      setTimeout(() => go('preparing'), QUIET_MS),
      setTimeout(() => {
        timers.forEach(clearTimeout);
        go('late');
      }, GIVE_UP_AFTER_MS),
    );
    void askAgain();
  };

  start();
  return {
    stop,
    retry: () => {
      go('preparing');
      start();
    },
  };
}
