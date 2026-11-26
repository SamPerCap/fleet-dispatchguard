import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createRefresh, IDLE_AFTER_MS, REFRESH_MS } from './refresh';

const SECOND = 1_000;
const MINUTE = 60 * SECOND;
const HOUR = 60 * MINUTE;

function screen(hidden = false) {
  let isHidden = hidden;
  let notify: { onActivity: () => void; onVisibility: () => void } | null = null;
  const load = vi.fn();
  const paused: boolean[] = [];
  const refresh = createRefresh({
    load,
    onPaused: value => paused.push(value),
    isHidden: () => isHidden,
    listen: listeners => {
      notify = listeners;
      return () => {
        notify = null;
      };
    },
  });
  return {
    load,
    paused,
    stop: refresh.stop,
    isListening: () => notify !== null,
    act: () => notify!.onActivity(),
    show: (visible: boolean) => {
      isHidden = !visible;
      notify!.onVisibility();
    },
  };
}

describe('periodic refresh', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  const wait = (ms: number) => vi.advanceTimersByTime(ms);

  it('loads every minute while somebody is there', () => {
    const view = screen();
    for (let minute = 0; minute < 30; minute++) {
      view.act();
      wait(MINUTE);
    }
    expect(view.load).toHaveBeenCalledTimes(30);
    expect(view.paused).toEqual([]);
  });

  it('hidden for 3 h asks for nothing, and loads once when it is back', () => {
    const view = screen();
    view.show(false);
    wait(3 * HOUR);
    expect(view.load).not.toHaveBeenCalled();
    view.show(true);
    expect(view.load).toHaveBeenCalledTimes(1);
    wait(MINUTE);
    expect(view.load).toHaveBeenCalledTimes(2);
  });

  it('a look at another tab does not ask again for data of a moment ago', () => {
    const view = screen();
    wait(20 * SECOND);
    view.show(false);
    wait(10 * SECOND);
    view.show(true);
    expect(view.load).not.toHaveBeenCalled();
    wait(29 * SECOND);
    expect(view.load).not.toHaveBeenCalled();
    wait(SECOND);
    expect(view.load).toHaveBeenCalledTimes(1);
  });

  it('10 min without activity pause it, not a second before', () => {
    const view = screen();
    wait(IDLE_AFTER_MS - SECOND);
    expect(view.load).toHaveBeenCalledTimes(9);
    expect(view.paused).toEqual([]);
    wait(SECOND);
    expect(view.paused).toEqual([true]);
    wait(3 * HOUR);
    expect(view.load).toHaveBeenCalledTimes(9);
  });

  it('the 10 min count from the last activity', () => {
    const view = screen();
    wait(9 * MINUTE + 30 * SECOND);
    view.act();
    wait(IDLE_AFTER_MS - SECOND);
    expect(view.paused).toEqual([]);
    expect(view.load).toHaveBeenCalledTimes(19);
    wait(SECOND);
    expect(view.paused).toEqual([true]);
    expect(view.load).toHaveBeenCalledTimes(19);
  });

  it('activity resumes it with one load at once', () => {
    const view = screen();
    wait(HOUR);
    view.load.mockClear();
    view.act();
    view.act();
    expect(view.load).toHaveBeenCalledTimes(1);
    expect(view.paused).toEqual([true, false]);
    wait(MINUTE);
    expect(view.load).toHaveBeenCalledTimes(2);
  });

  it('coming back to the tab is activity', () => {
    const view = screen();
    wait(9 * MINUTE);
    view.show(false);
    wait(5 * MINUTE);
    view.load.mockClear();
    view.show(true);
    wait(IDLE_AFTER_MS - SECOND);
    expect(view.paused).toEqual([]);
    expect(view.load).toHaveBeenCalledTimes(10);
  });

  it('paused and hidden, it is back to normal in one load', () => {
    const view = screen();
    wait(HOUR);
    view.show(false);
    wait(HOUR);
    view.load.mockClear();
    view.show(true);
    expect(view.load).toHaveBeenCalledTimes(1);
    expect(view.paused).toEqual([true, false]);
  });

  it('activity in a hidden tab asks for nothing', () => {
    const view = screen();
    view.show(false);
    wait(HOUR);
    view.act();
    wait(HOUR);
    expect(view.load).not.toHaveBeenCalled();
  });

  it('opened in a hidden tab, it waits to be seen', () => {
    const view = screen(true);
    wait(HOUR);
    expect(view.load).not.toHaveBeenCalled();
    view.show(true);
    expect(view.load).toHaveBeenCalledTimes(1);
  });

  it('a computer that slept does not load on waking up: nobody has touched it', () => {
    const view = screen();
    wait(30 * SECOND);
    vi.setSystemTime(Date.now() + 8 * HOUR);
    wait(MINUTE);
    expect(view.load).not.toHaveBeenCalled();
    expect(view.paused).toEqual([true]);
  });

  it('stopped, it neither asks nor listens', () => {
    const view = screen();
    view.stop();
    wait(HOUR);
    expect(view.load).not.toHaveBeenCalled();
    expect(view.paused).toEqual([]);
    expect(view.isListening()).toBe(false);
  });

  it('asks every minute', () => {
    expect(REFRESH_MS).toBe(MINUTE);
    expect(IDLE_AFTER_MS).toBe(10 * MINUTE);
  });
});
