import type { ColdStartState } from '../coldStart';

/**
 * - `quiet`: nothing shown yet, so a warm start never flashes.
 * - `red`: real work is pending (the code, the session or the database).
 * - `green`: everything answered; the closing plays before going in.
 * - `in`: the application is shown.
 * - `failed`: the code could not be downloaded.
 * - `late`: the database did not answer in time.
 */
export type ScenePhase = 'quiet' | 'red' | 'green' | 'in' | 'failed' | 'late';

/** The only time added on purpose, and only once per tab (SPEC 0101 R4, R10). */
export const CLOSING_MS = 1_500;

export interface ClosingMark {
  isSet: () => boolean;
  set: () => void;
}

interface Options {
  onPhase: (phase: ScenePhase) => void;
  closing: ClosingMark;
  reducedMotion: boolean;
  quietMs: number;
}

export function createLoadingScene({ onPhase, closing, reducedMotion, quietMs }: Options) {
  let phase: ScenePhase = 'quiet';
  let applicationLoaded = false;
  let database: ColdStartState | null = null;
  let stopped = false;
  const timers: ReturnType<typeof setTimeout>[] = [];

  const go = (next: ScenePhase) => {
    if (stopped || phase === next) return;
    phase = next;
    onPhase(next);
  };

  const everythingAnswered = () => applicationLoaded && database === 'ready';

  const settle = () => {
    if (phase === 'failed' || phase === 'green' || phase === 'in') return;
    if (database === 'late') return go('late');
    if (!everythingAnswered()) {
      if (phase === 'late') go('red');
      return;
    }
    if (phase === 'quiet' || reducedMotion || closing.isSet()) return go('in');
    closing.set();
    go('green');
    timers.push(setTimeout(() => go('in'), CLOSING_MS));
  };

  timers.push(
    setTimeout(() => {
      if (phase === 'quiet') go('red');
    }, quietMs),
  );

  return {
    application: () => {
      applicationLoaded = true;
      settle();
    },
    applicationFailed: () => go('failed'),
    database: (state: ColdStartState) => {
      database = state;
      settle();
    },
    stop: () => {
      stopped = true;
      timers.forEach(clearTimeout);
    },
  };
}

const KEY = 'dispatchguard.loadingClosingSeen';

/** Without storage it counts as seen: better no closing than one on every entry. */
export const closingMarkOfTheTab: ClosingMark = {
  isSet: () => {
    try {
      return sessionStorage.getItem(KEY) !== null;
    } catch {
      return true;
    }
  },
  set: () => {
    try {
      sessionStorage.setItem(KEY, '1');
    } catch {
      /* Nothing can be kept: isSet already answers yes. */
    }
  },
};
