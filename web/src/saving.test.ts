import { describe, expect, it } from 'vitest';
import { createSaving } from './saving';

function manual<T>() {
  let ok!: (value: T) => void;
  const promise = new Promise<T>(resolve => {
    ok = resolve;
  });
  return { promise, ok };
}

describe('assignments in progress', () => {
  it('counts the one in progress and, when it ends, that one more has finished', async () => {
    const saving = createSaving();
    const first = manual<string>();
    const run = saving.track(() => first.promise);
    expect(saving.state()).toEqual({ pending: 1, settled: 0 });
    first.ok('hecho');
    expect(await run).toBe('hecho');
    expect(saving.state()).toEqual({ pending: 0, settled: 1 });
  });
  it('a failure counts too (it may have been stored without a response) and is rethrown as it is', async () => {
    const saving = createSaving();
    const error = new Error('sin red');
    await expect(saving.track(() => Promise.reject(error))).rejects.toBe(error);
    expect(saving.state()).toEqual({ pending: 0, settled: 1 });
  });
  it('with two at once it stays in progress until the last one ends', async () => {
    const saving = createSaving();
    const first = manual<number>(),
      second = manual<number>();
    const runs = [saving.track(() => first.promise), saving.track(() => second.promise)];
    second.ok(2);
    await runs[1];
    expect(saving.state()).toEqual({ pending: 1, settled: 1 });
    first.ok(1);
    await runs[0];
    expect(saving.state()).toEqual({ pending: 0, settled: 2 });
  });
  it('notifies each change to whoever listens, and stops notifying', async () => {
    const saving = createSaving();
    const seen: number[] = [];
    const off = saving.subscribe(() => seen.push(saving.state().pending));
    await saving.track(() => Promise.resolve());
    off();
    await saving.track(() => Promise.resolve());
    expect(seen).toEqual([1, 0]);
  });
  it('between changes it returns the same object (React requires it)', () => {
    const saving = createSaving();
    expect(saving.state()).toBe(saving.state());
  });
});
