import { describe, expect, it, vi } from 'vitest';
import { createAppLoader, type ReloadMark } from './appLoader';

function markInMemory(): ReloadMark {
  let marked = false;
  return { isSet: () => marked, set: () => void (marked = true), clear: () => void (marked = false) };
}

const gone = () => Promise.reject(new TypeError('Failed to fetch dynamically imported module'));

describe('loading the application', () => {
  it('gives what it loads, without reloading', async () => {
    const reload = vi.fn();
    expect(await createAppLoader(async () => 'application', reload, markInMemory())()).toBe('application');
    expect(reload).not.toHaveBeenCalled();
  });

  it('reloads once when the file is gone, and then says that it fails', async () => {
    const reload = vi.fn();
    const mark = markInMemory();
    void createAppLoader(gone, reload, mark)();
    await vi.waitFor(() => expect(reload).toHaveBeenCalledTimes(1));

    await expect(createAppLoader(gone, reload, mark)()).rejects.toThrow('Failed to fetch');
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('can reload again after a load that went well', async () => {
    const mark = markInMemory();
    mark.set();
    await createAppLoader(async () => 'application', vi.fn(), mark)();
    expect(mark.isSet()).toBe(false);
  });
});
