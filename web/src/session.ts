type Fetch = (url: string, init?: RequestInit) => Promise<Response>;

/** Another site can make the browser send a form, but not this header: the API rejects any write without it. */
const GUARD_HEADER = { 'X-DispatchGuard-Request': '1' };

const isWrite = (method = 'GET') => !['GET', 'HEAD', 'OPTIONS'].includes(method.toUpperCase());

/** Not any 401: a wrong demo key is one too. */
async function lacksSession(response: Response): Promise<boolean> {
  if (response.status !== 401) return false;
  const body: { sessionMissing?: unknown } | null = await response
    .clone()
    .json()
    .catch(() => null);
  return body?.sessionMissing === true;
}

/** The anonymous session lives in a cookie that this code cannot read; here it is only asked for and waited for (SPEC 0084). */
export function createSession(fetchFrom: Fetch) {
  let started: Promise<void> | null = null;

  const start = (): Promise<void> => {
    started ??= fetchFrom('/api/session').then(
      response => {
        // It may be back for the next request.
        if (!response.ok) started = null;
      },
      () => {
        started = null;
      },
    );
    return started;
  };

  return {
    /** Nothing is answered without a session (SPEC 0086): it waits for it, and is sent again once if it had expired. */
    async fetch(url: string, init: RequestInit = {}): Promise<Response> {
      const request = isWrite(init.method) ? { ...init, headers: { ...(init.headers as Record<string, string>), ...GUARD_HEADER } } : init;
      const used = start();
      await used;
      const response = await fetchFrom(url, request);
      if (!(await lacksSession(response))) return response;
      // Of several requests refused at once, only the first one asks for another session.
      if (started === used) started = null;
      await start();
      return fetchFrom(url, request);
    },
  };
}

export const session = createSession((url, init) => fetch(url, init));
