import { locale } from './language';

/**
 * The X-ray of an answer (SPEC 0097): the API seals it into a header of the answer itself and only gives it back to this session, for
 * a few minutes. The token lives in this tab's memory, never in storage or in an address.
 */
export const XRAY_HEADER = 'X-DispatchGuard-XRay';

export type XRayKind = 'allocation' | 'recommendation';

export type XRayFigure = {
  Rule: string;
  Name: string | null;
  Article: string | null;
  Passed: boolean;
  Actual: number | null;
  Limit: number | null;
  Unit: string | null;
};

export type XRayCheck = { Step: string; Outcome: string; Rules: XRayFigure[] };
export type XRayRuleCount = { Rule: string; Passed: number; Failed: number };
export type XRayStage = { Stage: string; Times: number; Ms: number; Queries: number };
export type XRayLock = { Resource: string; Count: number };
export type XRayWrite = { Record: string; Count: number; Saved: boolean };

export type XRay = {
  Kind: XRayKind;
  Status: number;
  TotalMs: number;
  Queries: number;
  QueriesMs: number;
  Evaluations: number;
  Checks: XRayCheck[];
  Rules: XRayRuleCount[];
  Stages: XRayStage[];
  OtherMs: number;
  Locks: XRayLock[];
  Writes: XRayWrite[];
};

/**
 * Which answers can be looked inside, by their address; the API marks the same endpoints. Only what the panels next to the panel
 * ask for themselves: an answer asked in the background would take the place of the one on screen.
 */
const KINDS: [RegExp, XRayKind][] = [
  [/^\/api\/trips\/\d+\/allocate$/, 'allocation'],
  [/^\/api\/recommendations(\/check|\/options)?$/, 'recommendation'],
];

export function kindOf(url: string): XRayKind | null {
  const path = url.split('?')[0];
  return KINDS.find(([pattern]) => pattern.test(path))?.[1] ?? null;
}

export type Latest = { token: string; version: number };

/** The last answer of each kind; a new one replaces it, as the screen shows the new answer. */
export function createXRayStore() {
  const latest = new Map<XRayKind, Latest>();
  const listeners = new Set<() => void>();
  let version = 0;
  return {
    remember(url: string, token: string | null) {
      const kind = kindOf(url);
      if (!kind || !token) return;
      latest.set(kind, { token, version: ++version });
      listeners.forEach(listener => listener());
    },
    latest: (kind: XRayKind): Latest | null => latest.get(kind) ?? null,
    subscribe(listener: () => void) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
  };
}

export const xrays = createXRayStore();

export const xrayNumber = (value: number, decimals = 1): string =>
  value.toLocaleString(locale(), { maximumFractionDigits: decimals });

