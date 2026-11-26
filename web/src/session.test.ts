import { describe, expect, it } from 'vitest';
import { createSession } from './session';

const json = (status: number, body: unknown = {}) => new Response(JSON.stringify(body), { status });
const noSession = () => json(401, { sessionMissing: true });

/** Answers in order to each url; the last answer is repeated. */
function server(answers: Record<string, (() => Response | Promise<Response>)[]>) {
  const calls: { url: string; method: string; headers: Record<string, string> }[] = [];
  return {
    calls,
    of: (url: string) => calls.filter(call => call.url === url),
    fetch: async (url: string, init: RequestInit = {}) => {
      const turn = calls.filter(call => call.url === url).length;
      calls.push({ url, method: init.method ?? 'GET', headers: (init.headers ?? {}) as Record<string, string> });
      const list = answers[url];
      return list[Math.min(turn, list.length - 1)]();
    },
  };
}

describe('anonymous session', () => {
  it('a read waits for the session too, and carries no header', async () => {
    const api = server({ '/api/session': [() => json(200)], '/api/trips': [() => json(200)] });
    await createSession(api.fetch).fetch('/api/trips');
    expect(api.calls.map(call => call.url)).toEqual(['/api/session', '/api/trips']);
    expect(api.calls[1].headers).toEqual({});
  });

  it('an expired session is renewed and the read is sent again', async () => {
    const api = server({ '/api/session': [() => json(200)], '/api/trips': [noSession, () => json(200)] });
    expect((await createSession(api.fetch).fetch('/api/trips')).status).toBe(200);
    expect(api.of('/api/session')).toHaveLength(2);
  });

  it('several reads refused at once renew the session once', async () => {
    const api = server({ '/api/session': [() => json(200)], '/a': [noSession, () => json(200)], '/b': [noSession, () => json(200)] });
    const session = createSession(api.fetch);
    const answers = await Promise.all([session.fetch('/a'), session.fetch('/b')]);
    expect(answers.map(answer => answer.status)).toEqual([200, 200]);
    expect(api.of('/api/session')).toHaveLength(2);
  });

  it('a write waits for the session and keeps its own headers', async () => {
    const api = server({ '/api/session': [() => json(200)], '/api/bookings': [() => json(201)] });
    await createSession(api.fetch).fetch('/api/bookings', { method: 'POST', headers: { 'Content-Type': 'application/json' } });
    expect(api.calls.map(call => call.url)).toEqual(['/api/session', '/api/bookings']);
    expect(api.calls[1].headers).toEqual({ 'Content-Type': 'application/json', 'X-DispatchGuard-Request': '1' });
  });

  it('several writes at once ask for one session only', async () => {
    const api = server({ '/api/session': [() => json(200)], '/api/bookings': [() => json(201)] });
    const session = createSession(api.fetch);
    await Promise.all([1, 2, 3].map(() => session.fetch('/api/bookings', { method: 'POST' })));
    expect(api.of('/api/session')).toHaveLength(1);
    expect(api.of('/api/bookings')).toHaveLength(3);
  });

  it('an expired session is renewed and the write is sent again', async () => {
    const api = server({ '/api/session': [() => json(200)], '/api/bookings': [noSession, () => json(201)] });
    const response = await createSession(api.fetch).fetch('/api/bookings', { method: 'POST', body: '{"a":1}' });
    expect(response.status).toBe(201);
    expect(api.calls.map(call => call.url)).toEqual(['/api/session', '/api/bookings', '/api/session', '/api/bookings']);
  });

  it('it is sent again once, not forever', async () => {
    const api = server({ '/api/session': [() => json(200)], '/api/bookings': [noSession] });
    const response = await createSession(api.fetch).fetch('/api/bookings', { method: 'POST' });
    expect(response.status).toBe(401);
    expect(api.of('/api/bookings')).toHaveLength(2);
    expect(await response.json()).toEqual({ sessionMissing: true });
  });

  it('another 401 is not a lack of session: a wrong demo key is not sent twice', async () => {
    const api = server({ '/api/session': [() => json(200)], '/api/admin/demo/reset': [() => json(401, { title: 'Clave' })] });
    const response = await createSession(api.fetch).fetch('/api/admin/demo/reset', { method: 'POST' });
    expect(response.status).toBe(401);
    expect(api.of('/api/admin/demo/reset')).toHaveLength(1);
    expect(await response.json()).toEqual({ title: 'Clave' });
  });

  it('a 401 without JSON does not break', async () => {
    const api = server({ '/api/session': [() => json(200)], '/api/bookings': [() => new Response('no', { status: 401 })] });
    expect((await createSession(api.fetch).fetch('/api/bookings', { method: 'DELETE' })).status).toBe(401);
  });

  it('if the API was down when starting, the next request asks again', async () => {
    const down = () => Promise.reject(new TypeError('Failed to fetch'));
    const api = server({ '/api/session': [down, () => json(503), () => json(200)], '/api/bookings': [() => json(201)] });
    const session = createSession(api.fetch);
    await session.fetch('/api/bookings', { method: 'POST' });
    await session.fetch('/api/bookings', { method: 'POST' });
    await session.fetch('/api/bookings', { method: 'POST' });
    expect(api.of('/api/session')).toHaveLength(3);
  });
});
