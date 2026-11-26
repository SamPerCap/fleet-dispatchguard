import { describe, expect, it } from 'vitest';
import { createLatestGuard } from './latest';

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

/** Same shape as PlanningPage.load: applies the result only if the load is still current. */
function loader(guard: ReturnType<typeof createLatestGuard>, state: { shown: string | null; error: string | null }) {
  return async (request: Promise<string>) => {
    const isCurrent = guard.begin();
    try {
      const value = await request;
      if (isCurrent()) state.shown = value;
    } catch (error) {
      if (isCurrent()) state.error = (error as Error).message;
    }
  };
}

describe('createLatestGuard', () => {
  it('discards the current week response if it arrives after the user has navigated', async () => {
    const state = { shown: null as string | null, error: null as string | null };
    const load = loader(createLatestGuard(), state);
    const currentWeek = deferred<string>();
    const tuesdayWeek = deferred<string>();

    const first = load(currentWeek.promise); // initial load: current week
    const second = load(tuesdayWeek.promise); // the user moves to Tuesday
    tuesdayWeek.resolve('semana del martes');
    await second;
    currentWeek.resolve('semana actual'); // the initial one arrives late
    await first;

    expect(state.shown).toBe('semana del martes');
  });

  it('also ignores the error of a stale request', async () => {
    const state = { shown: null as string | null, error: null as string | null };
    const load = loader(createLatestGuard(), state);
    const old = deferred<string>();
    const first = load(old.promise);
    await load(Promise.resolve('vigente'));
    old.reject(new Error('fallo viejo'));
    await first;
    expect(state).toEqual({ shown: 'vigente', error: null });
  });

  it('in normal order it applies each response', async () => {
    const state = { shown: null as string | null, error: null as string | null };
    const load = loader(createLatestGuard(), state);
    await load(Promise.resolve('a'));
    await load(Promise.resolve('b'));
    expect(state.shown).toBe('b');
  });
});
