import { beforeEach, describe, expect, it, vi } from 'vitest';

const fetched = vi.hoisted(() => vi.fn<(url: string, init?: RequestInit) => Promise<Response>>());
vi.mock('./session', () => ({ session: { fetch: fetched } }));

const { api, audit, forgetReads, recommendations } = await import('./api');

const answer = () => Promise.resolve(new Response('[]', { status: 200, headers: { 'Content-Type': 'application/json' } }));
const callsTo = (path: string) => fetched.mock.calls.filter(([url]) => url.startsWith(path)).length;

describe('reads through the API (SPEC 0102)', () => {
  beforeEach(() => {
    forgetReads();
    fetched.mockReset();
    fetched.mockImplementation(answer);
  });

  it('R1: reading the same list twice asks once', async () => {
    await api.drivers();
    await api.drivers();

    expect(callsTo('/api/drivers')).toBe(1);
  });

  it('R3: a write in between makes the next read ask again', async () => {
    await api.drivers();
    await recommendations.check([]);
    await api.drivers();

    expect(callsTo('/api/drivers')).toBe(2);
  });

  it('R4: a read with a key of its own is never kept', async () => {
    await audit.visits('key');
    await audit.visits('key');

    expect(callsTo('/api/audit/visits')).toBe(2);
  });

  it('R5: forgetting the reads asks again', async () => {
    await api.drivers();
    forgetReads();
    await api.drivers();

    expect(callsTo('/api/drivers')).toBe(2);
  });
});
