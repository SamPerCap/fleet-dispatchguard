// What is done in the web, for the timeline of the visit (SPEC 0085 R5). Never what is typed or shown: every name comes from the code.
import { useEffect } from 'react';
import { assignedBy } from './operator';
import { session } from './session';
import { knownLabel } from './visitLabels';

export type VisitEventKind = 'Screen' | 'Click' | 'FormOpened' | 'FormSent' | 'FormAbandoned' | 'RequestFailed' | 'Error';

export type VisitBatch = {
  Reference?: string;
  Events: { AgoMs: number; Kind: VisitEventKind; Name: string; TraceId: string; Status?: number; DurationMs?: number }[];
};

type Recorded = { at: number; kind: VisitEventKind; name: string; trace: string; status?: number; durationMs?: number };

type VisitOptions = {
  /** A failure is lost: the visit goes on. */
  send: (batch: VisitBatch) => Promise<unknown>;
  randomHex: (length: number) => string;
  reference?: string;
};

/** Of the API. */
const BATCH_LIMIT = 50;
const QUIET_BETWEEN_BATCHES_MS = 5_000;
/** The database stays awake for 5 minutes after a request: within this time, a batch does not wake it up. */
const AWAKE_FOR_MS = 2 * 60_000;
/** What the web asks for later is a refresh that nobody asked for. */
const FOLLOWS_FOR_MS = 5_000;

/** They are sent as a form, but nothing is saved. */
const CALCULATIONS = ['/api/recommendations', '/api/optimization', '/api/visits'];

/** Without the query, where there may be something typed. */
export function pathOf(url: string): string {
  const segments = url.split('?')[0].split('/');
  return segments.map(segment => (/^([a-z-]*|\d+)$/.test(segment) ? segment : ':id')).join('/');
}

/** Nothing here starts by itself: it leaves after a request of the web, or when the tab is hidden soon after one. */
export function createVisit({ send, randomHex, reference }: VisitOptions) {
  let pending: Recorded[] = [];
  let pendingReference = reference;
  let forms: { name: string; sent: boolean }[] = [];
  let trace = randomHex(32);
  let actionAt = Date.now();
  let answeredAt: number | null = null;
  let sentAt: number | null = null;

  const record = (
    kind: VisitEventKind,
    name: string,
    extra: { at?: number; status?: number; durationMs?: number; trace?: string } = {},
  ) => {
    if (pending.length < BATCH_LIMIT) pending.push({ at: Date.now(), kind, name, trace, ...extra });
  };

  const flush = () => {
    if (pending.length === 0 && !pendingReference) return;
    const now = Date.now();
    const batch: VisitBatch = {
      ...(pendingReference ? { Reference: pendingReference } : {}),
      Events: pending.map(event => ({
        AgoMs: now - event.at,
        Kind: event.kind,
        Name: event.name,
        TraceId: event.trace,
        ...(event.status === undefined ? {} : { Status: event.status }),
        ...(event.durationMs === undefined ? {} : { DurationMs: event.durationMs }),
      })),
    };
    pending = [];
    pendingReference = undefined;
    sentAt = now;
    send(batch).catch(() => {});
  };

  return {
    screen: (name: string, at = Date.now()) => record('Screen', name, { at }),

    /** Everything that follows shares its thread. */
    action: (label: string | null) => {
      trace = randomHex(32);
      actionAt = Date.now();
      if (label) record('Click', label);
    },

    error: (name: string) => record('Error', /^[A-Za-z]{1,40}$/.test(name) ? name : 'Error'),

    /** Returns what to call when it closes. */
    openForm: (name: string) => {
      const form = { name, sent: false };
      forms.push(form);
      record('FormOpened', name);
      return () => {
        forms = forms.filter(open => open !== form);
        if (!form.sent) record('FormAbandoned', name);
      };
    },

    /** The thread of a request, for its header and for its result. */
    thread: () => {
      const follows = Date.now() - actionAt < FOLLOWS_FOR_MS;
      const id = follows ? trace : randomHex(32);
      return { id, header: `00-${id}-${randomHex(16)}-${follows ? '01' : '00'}` };
    },

    /** `status` is 0 without an answer. */
    answered: (request: { method: string; url: string; status: number; durationMs: number; trace: string }) => {
      const { method, url, status } = request;
      if (status === 0 || status >= 400) {
        record('RequestFailed', `${method} ${pathOf(url)}`, { status, durationMs: Math.round(request.durationMs), trace: request.trace });
      } else if (method !== 'GET' && !CALCULATIONS.some(calculation => url.startsWith(calculation))) {
        const form = forms.at(-1);
        if (form && !form.sent) {
          form.sent = true;
          record('FormSent', form.name, { trace: request.trace });
        }
      }
      // Only an answer of the database says that it is awake; the limits and the session do not ask it.
      if (status === 0 || status === 401 || status === 429) return;
      answeredAt = Date.now();
      if (sentAt === null || answeredAt - sentAt >= QUIET_BETWEEN_BATCHES_MS) flush();
    },

    hidden: () => {
      if (answeredAt !== null && Date.now() - answeredAt < AWAKE_FOR_MS) flush();
    },
  };
}

function randomHex(length: number): string {
  const bytes = crypto.getRandomValues(new Uint8Array(length / 2));
  return Array.from(bytes, byte => byte.toString(16).padStart(2, '0')).join('');
}

/** Taken off the address: whoever copies it does not pass on somebody else's link. */
export function takeReference(href: string): { reference?: string; clean: string } {
  const url = new URL(href);
  const reference = url.searchParams.get('ref') ?? undefined;
  url.searchParams.delete('ref');
  return { reference: reference && /^[\w-]{1,32}$/.test(reference) ? reference : undefined, clean: url.toString() };
}

function referenceOfThisVisit(): string | undefined {
  if (typeof location === 'undefined') return undefined;
  const { reference, clean } = takeReference(location.href);
  if (clean !== location.href) history.replaceState(history.state, '', clean);
  return reference;
}

/** It survives the closing of the tab. */
const sendBatch = (batch: VisitBatch) =>
  session.fetch('/api/visits', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(batch),
    keepalive: true,
  });

export const visit = createVisit({ send: sendBatch, randomHex, reference: referenceOfThisVisit() });

/** The declared name, for what changes; a header only admits ASCII. */
export function visitHeaders(method: string, threadHeader: string): Record<string, string> {
  const name = method === 'GET' ? '' : assignedBy();
  return { traceparent: threadHeader, ...(name ? { 'X-DispatchGuard-Name': encodeURIComponent(name) } : {}) };
}

const CONTROLS = 'button, a[href], [role="button"], [role="tab"], [role="option"]';

export function listenToVisit(): () => void {
  const onClick = (event: MouseEvent) => {
    const control = event.target instanceof Element ? event.target.closest(CONTROLS) : null;
    if (control) visit.action(knownLabel(control.getAttribute('aria-label') ?? control.textContent ?? ''));
  };
  const onError = (event: ErrorEvent) => visit.error(event.error instanceof Error ? event.error.name : 'Error');
  const onRejection = (event: PromiseRejectionEvent) => visit.error(event.reason instanceof Error ? event.reason.name : 'Error');
  const onVisibility = () => {
    if (document.hidden) visit.hidden();
  };
  document.addEventListener('click', onClick, true);
  addEventListener('error', onError);
  addEventListener('unhandledrejection', onRejection);
  document.addEventListener('visibilitychange', onVisibility);
  return () => {
    document.removeEventListener('click', onClick, true);
    removeEventListener('error', onError);
    removeEventListener('unhandledrejection', onRejection);
    document.removeEventListener('visibilitychange', onVisibility);
  };
}

export function useFormVisit(name: string): void {
  useEffect(() => visit.openForm(name), [name]);
}
