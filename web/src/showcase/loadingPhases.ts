import type { ColdStartState } from '../coldStart';

/**
 * - `quiet`: nothing shown yet, so a warm start on a later entry never flashes.
 * - `red`: real work is pending (the code, the session or the database).
 * - `green`: everything answered; the closing plays before going in.
 * - `in`: the application is shown.
 * - `failed`: the code could not be downloaded.
 * - `late`: the database did not answer in time.
 */
export type ScenePhase = 'quiet' | 'red' | 'green' | 'in' | 'failed' | 'late';

/** The closing after the green light: only on the first entry of a tab (SPEC 0101 R4, R10). */
export const CLOSING_MS = 1_500;
/**
 * On the first entry of a tab the red light lasts at least this, so that a warm start still shows a few messages and the first
 * vehicles stopping (SPEC 0101 R11, user 2026-11-28). Later entries add nothing.
 */
export const MIN_RED_MS = 4_500;

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
  let minimumPassed = false;
  const timers: ReturnType<typeof setTimeout>[] = [];
  const wholeScene = !reducedMotion && !closing.isSet();

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
    if (!wholeScene) return go('in');
    if (!minimumPassed) return;
    closing.set();
    go('green');
    timers.push(setTimeout(() => go('in'), CLOSING_MS));
  };

  if (wholeScene) {
    go('red');
    timers.push(
      setTimeout(() => {
        minimumPassed = true;
        settle();
      }, MIN_RED_MS),
    );
  } else {
    timers.push(
      setTimeout(() => {
        if (phase === 'quiet') go('red');
      }, quietMs),
    );
  }

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
