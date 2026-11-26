import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { api, recommendations } from './api';
import { setAssignedBy } from './operator';

/** SPEC 0089 R6: a name and a reason in a URL end up in logs and in the history of the browser. */
describe('the allocate call', () => {
  const calls: { url: string; body: unknown }[] = [];

  beforeEach(() => {
    calls.length = 0;
    const stored = new Map<string, string>();
    vi.stubGlobal('localStorage', { getItem: (key: string) => stored.get(key) ?? null, setItem: (key: string, value: string) => stored.set(key, value) });
    vi.stubGlobal('fetch', async (url: string, init?: RequestInit) => {
      if (init?.method === 'POST') calls.push({ url, body: JSON.parse(String(init.body)) });
      return new Response('{}', { status: 200 });
    });
    setAssignedBy('Ana');
  });

  afterEach(() => vi.unstubAllGlobals());

  it('by hand, carries everything in the body', async () => {
    await api.allocate(7, 1, 2);
    expect(calls).toEqual([{ url: '/api/trips/7/allocate', body: { DriverId: 1, VehicleId: 2, AssignedBy: 'Ana' } }]);
  });

  it('forced, carries the name and the reason of whoever decides it', async () => {
    await api.allocate(7, 1, 2, { by: ' Luis ', reason: ' Sin otro conductor ' });
    expect(calls).toEqual([
      { url: '/api/trips/7/allocate', body: { DriverId: 1, VehicleId: 2, AssignedBy: 'Luis', Force: true, Reason: 'Sin otro conductor' } },
    ]);
  });

  it('without a name, sends none', async () => {
    setAssignedBy('');
    await api.allocate(7, 1, 2);
    expect(calls[0].body).toEqual({ DriverId: 1, VehicleId: 2 });
  });

  it('from a recommendation, says so and never replaces an assignment', async () => {
    await recommendations.apply(7, 1, 2, 'Recommended');
    expect(calls).toEqual([
      {
        url: '/api/trips/7/allocate',
        body: { DriverId: 1, VehicleId: 2, AssignedBy: 'Ana', Origin: 'Recommended', OnlyIfUnassigned: true },
      },
    ]);
  });
});
