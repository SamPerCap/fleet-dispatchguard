import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ASK_EVERY_MS, createColdStart, GIVE_UP_AFTER_MS, isReady, QUIET_MS, type ColdStartState } from './coldStart';

const SECOND = 1_000;

function start(answer: () => Promise<boolean>) {
  const states: ColdStartState[] = [];
  const ask = vi.fn(answer);
  const coldStart = createColdStart({ ask, onState: state => states.push(state) });
  return { ask, states, coldStart };
}

const after = (ms: number, value: boolean) => () => new Promise<boolean>(resolve => setTimeout(() => resolve(value), ms));

describe('cold start', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  const wait = (ms: number) => vi.advanceTimersByTimeAsync(ms);

  it('with the database up it goes in without showing anything', async () => {
    const { ask, states } = start(after(100, true));
    await wait(10 * SECOND);
    expect(states).toEqual(['ready']);
    expect(ask).toHaveBeenCalledTimes(1);
  });

  it('with the database asleep it shows the screen, asks and goes in by itself', async () => {
    let up = false;
    const { ask, states } = start(() => Promise.resolve(up));
    await wait(40 * SECOND);
    expect(states).toEqual(['preparing']);
    up = true;
    await wait(ASK_EVERY_MS);
    expect(states).toEqual(['preparing', 'ready']);
    const asked = ask.mock.calls.length;
    await wait(60 * SECOND);
    expect(ask).toHaveBeenCalledTimes(asked);
  });

  it('an answer that is slow in coming shows the screen meanwhile', async () => {
    const { states } = start(after(QUIET_MS + SECOND, true));
    await wait(QUIET_MS - 1);
    expect(states).toEqual([]);
    await wait(1);
    expect(states).toEqual(['preparing']);
    await wait(SECOND);
    expect(states).toEqual(['preparing', 'ready']);
  });

  it('does not ask again until the previous answer is here', async () => {
    const { ask } = start(after(5 * SECOND, false));
    await wait(5 * SECOND + ASK_EVERY_MS - 1);
    expect(ask).toHaveBeenCalledTimes(1);
    await wait(1);
    expect(ask).toHaveBeenCalledTimes(2);
  });

  it('a failure of the network is not an answer', async () => {
    let up = false;
    const { states } = start(() => (up ? Promise.resolve(true) : Promise.reject(new TypeError('Failed to fetch'))));
    await wait(10 * SECOND);
    up = true;
    await wait(ASK_EVERY_MS);
    expect(states).toEqual(['preparing', 'ready']);
  });

  it('after 2 min it says so and stops asking', async () => {
    const { ask, states } = start(() => Promise.resolve(false));
    await wait(GIVE_UP_AFTER_MS - 1);
    expect(states).toEqual(['preparing']);
    await wait(1);
    expect(states).toEqual(['preparing', 'late']);
    const asked = ask.mock.calls.length;
    await wait(10 * 60 * SECOND);
    expect(ask).toHaveBeenCalledTimes(asked);
    expect(GIVE_UP_AFTER_MS).toBe(120 * SECOND);
  });

  it('after 2 min it says so even with an answer still to come, which still lets in', async () => {
    const { states } = start(after(GIVE_UP_AFTER_MS + 5 * SECOND, true));
    await wait(GIVE_UP_AFTER_MS);
    expect(states).toEqual(['preparing', 'late']);
    await wait(5 * SECOND);
    expect(states).toEqual(['preparing', 'late', 'ready']);
  });

  it('asked to try again, it starts over', async () => {
    let up = false;
    const { coldStart, states } = start(() => Promise.resolve(up));
    await wait(GIVE_UP_AFTER_MS);
    up = true;
    coldStart.retry();
    await wait(0);
    expect(states).toEqual(['preparing', 'late', 'preparing', 'ready']);
  });

  it('stopped, it does not ask or say anything else', async () => {
    const { ask, states, coldStart } = start(after(SECOND, true));
    coldStart.stop();
    await wait(GIVE_UP_AFTER_MS * 2);
    expect(ask).toHaveBeenCalledTimes(1);
    expect(states).toEqual([]);
  });
});

describe('readiness', () => {
  it('only a 200 of the API is ready', async () => {
    const answer = (status: number) => isReady(() => Promise.resolve(new Response(null, { status })));
    expect(await answer(200)).toBe(true);
    expect(await answer(503)).toBe(false);
    expect(await answer(500)).toBe(false);
    expect(await answer(404)).toBe(false);
  });

  it('a page of the host in front is not the API', async () => {
    const page = new Response('<html>', { status: 200, headers: { 'Content-Type': 'text/html' } });
    expect(await isReady(() => Promise.resolve(page))).toBe(false);
  });

  it('asks the API and never a cache, with a limit of time', async () => {
    const fetchFrom = vi.fn<(url: string, init?: RequestInit) => Promise<Response>>(() => Promise.resolve(new Response(null, { status: 200 })));
    await isReady(fetchFrom);
    const [url, init] = fetchFrom.mock.calls[0];
    expect(url).toBe('/health/ready');
    expect(init?.cache).toBe('no-store');
    expect(init?.signal).toBeInstanceOf(AbortSignal);
  });
});
