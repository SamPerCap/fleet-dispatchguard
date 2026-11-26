// The database of the demo goes to sleep when nobody uses it and has a monthly allowance: a page left open must not keep it awake
// (SPEC 0091).

export const REFRESH_MS = 60_000;
export const IDLE_AFTER_MS = 10 * 60_000;

type Listeners = { onActivity: () => void; onVisibility: () => void };

type RefreshOptions = {
  load: () => void;
  /** Only when nobody is there: a hidden tab has nobody to tell. */
  onPaused: (paused: boolean) => void;
  isHidden: () => boolean;
  /** Returns how to stop listening. */
  listen: (listeners: Listeners) => () => void;
};

/** The first load is not its business: it counts as made when this starts. */
export function createRefresh({ load, onPaused, isHidden, listen }: RefreshOptions) {
  let loadedAt = Date.now();
  let activeAt = loadedAt;
  let paused = false;
  let timer: ReturnType<typeof setTimeout> | undefined;

  const setPaused = (value: boolean) => {
    if (paused === value) return;
    paused = value;
    onPaused(value);
  };

  const step = () => {
    clearTimeout(timer);
    if (isHidden()) return;
    const now = Date.now();
    if (now - activeAt >= IDLE_AFTER_MS) {
      setPaused(true);
      return;
    }
    if (now - loadedAt >= REFRESH_MS) {
      loadedAt = now;
      load();
    }
    timer = setTimeout(step, Math.min(loadedAt + REFRESH_MS, activeAt + IDLE_AFTER_MS) - now);
  };

  const stopListening = listen({
    onActivity: () => {
      activeAt = Date.now();
      if (!paused) return;
      setPaused(false);
      step();
    },
    onVisibility: () => {
      if (!isHidden()) {
        activeAt = Date.now();
        setPaused(false);
      }
      step();
    },
  });
  step();

  return {
    stop: () => {
      clearTimeout(timer);
      stopListening();
    },
  };
}
