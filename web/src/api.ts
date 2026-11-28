import { t } from './i18n/t';
import type {
  ActivityItem,
  ActivityKind,
  ControlEfficiency,
  EngineAttemptDetail,
  EngineAuditPage,
  ControlIncident,
  ControlPage,
  ControlResources,
  ControlSummary,
  ControlTrend,
  EngineHourly,
  EngineMetrics,
  OperatingBase,
} from './types';
import type { AgendaInput } from './agenda';
import { assignedBy } from './operator';
import type { ForceRequest } from './force';
import type { DetailsInput } from './resourceDetails';
import type { ResourceKind } from './resourceUrl';
import { isRetryable } from './retry';
import { saving } from './saving';
import { session } from './session';
import type { TravelTime } from './travel';
import { visit, visitHeaders } from './visit';
import { XRAY_HEADER, xrays, type XRay } from './xray';
import type {
  AffectedTrip,
  AllocationResponse,
  AttemptOrigin,
  Booking,
  Recommendation,
  RecommendationLine,
  RecommendationLineInput,
  RecommendationOptions,
  RecommendationRequest,
  TripRecommendation,
  ResourceHistory,
  RuleGroup,
  Driver,
  DriverCandidate,
  DriverHours,
  DriverOptions,
  NewTrip,
  Explanation,
  LaterService,
  Location,
  Paged,
  RequestTraceDetail,
  Trip,
  Vehicle,
  VehicleCandidate,
  VehicleChaining,
  VisitSummary,
  VisitTimeline,
} from './types';
import { language } from './language';
import { createReadCache, readKeyOf } from './readCache';

/** Carries the backend ProblemDetails so that it is shown as it comes. */
export class ApiError extends Error {
  constructor(
    public status: number,
    public title: string,
    public detail?: string,
    public violations: string[] = [],
    public explanation?: Explanation,
    public forcible = false,
    public laterServices: LaterService[] = [],
    public retryable = false,
    /** The field the rejection is about, with its name in the API. */
    public field?: string,
  ) {
    super(detail ? `${title}: ${detail}` : title);
  }
}

/** What the backend may answer to a rejection; a success is read as its own type. */
type ProblemBody = {
  title?: string;
  detail?: string;
  violations?: unknown;
  explanation?: Explanation | null;
  forcible?: unknown;
  laterServices?: unknown;
  retryable?: unknown;
  field?: unknown;
};

function violationText(violation: unknown): string {
  if (typeof violation === 'string') return violation;
  const fields = violation as Record<string, unknown>;
  return String(fields.Message ?? fields.message ?? JSON.stringify(violation));
}

/** Its thread and its result are part of the timeline of the visit. */
async function send(url: string, init: RequestInit = {}): Promise<Response> {
  const method = init.method ?? 'GET';
  const thread = visit.thread();
  const startedAt = performance.now();
  const answered = (status: number) =>
    visit.answered({ method, url, status, durationMs: performance.now() - startedAt, trace: thread.id });
  try {
    const response = await session.fetch(url, {
      ...init,
      headers: { 'Content-Type': 'application/json', 'Accept-Language': language(), ...visitHeaders(method, thread.header), ...(init.headers as Record<string, string>) },
    });
    answered(response.status);
    xrays.remember(url, response.headers.get(XRAY_HEADER));
    return response;
  } catch (failure) {
    answered(0);
    throw failure;
  }
}

const refreshedListeners = new Set<() => void>();
let announcing: ReturnType<typeof setTimeout> | null = null;
/** Several reads refreshed behind at once make one reload of the views, not one each. */
const announceRefreshed = () => {
  if (announcing) return;
  announcing = setTimeout(() => {
    announcing = null;
    refreshedListeners.forEach(listener => listener());
  }, 300);
};
const reads = createReadCache(Date.now, announceRefreshed);

/** The app reloads its views when a value painted from memory has been asked again behind (SPEC 0102 R1). */
export function onReadsRefreshed(listener: () => void): () => void {
  refreshedListeners.add(listener);
  return () => {
    refreshedListeners.delete(listener);
  };
}

/** The Control Centre's refresh button asks again even within the time a read is kept (SPEC 0102 R5). */
export const forgetReads = () => reads.clear();

/** Never kept: what carries a key of its own, and what answers about the session or the server rather than about the data. */
const isKeptRead = (url: string, init?: RequestInit) =>
  (init?.method ?? 'GET') === 'GET' && !init?.headers && !url.startsWith('/api/session') && !url.startsWith('/health');

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  if (isKeptRead(url, init)) return reads.get(readKeyOf(url), () => answerOf<T>(url, init));
  if ((init?.method ?? 'GET') === 'GET') return answerOf<T>(url, init);
  // A write changes what the reads would say: forgotten before it leaves and again once it is answered, whatever the answer.
  reads.clear();
  try {
    return await answerOf<T>(url, init);
  } finally {
    reads.clear();
  }
}

async function answerOf<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await send(url, init);
  const text = await res.text();
  let body: ProblemBody | undefined;
  try {
    body = text ? JSON.parse(text) : undefined;
  } catch {
    // Non-JSON body (e.g. the Vite proxy with the API down): keeps the HTTP status.
    if (!res.ok) throw new ApiError(res.status, `HTTP ${res.status}`, text.slice(0, 200));
    throw new ApiError(res.status, t.api.badAnswer);
  }
  if (!res.ok) {
    const violations = Array.isArray(body?.violations) ? body.violations.map(violationText) : [];
    throw new ApiError(
      res.status,
      body?.title ?? `HTTP ${res.status}`,
      body?.detail,
      violations,
      body?.explanation ?? undefined,
      body?.forcible === true,
      Array.isArray(body?.laterServices) ? body.laterServices : [],
      isRetryable(res.status, body),
      typeof body?.field === 'string' ? body.field : undefined,
    );
  }
  return body as T;
}

const MORE_THAN_ANY_FLEET = 10_000;

const post = <T>(url: string, body: unknown) => request<T>(url, { method: 'POST', body: JSON.stringify(body) });

type AllocationRequest = {
  DriverId: number;
  VehicleId: number;
  AssignedBy?: string;
  Force?: true;
  Reason?: string;
  Origin?: AttemptOrigin;
  OnlyIfUnassigned?: true;
};

/** An undefined field is not sent: the API has its default. */
const allocate = (tripId: number, allocation: AllocationRequest) =>
  saving.track(() => post<AllocationResponse>(`/api/trips/${tripId}/allocate`, allocation));

/** Only `apply` stores anything. */
export const recommendations = {
  propose: (input: RecommendationRequest) => post<Recommendation>('/api/recommendations', input),
  check: (lines: RecommendationLineInput[]) => post<RecommendationLine[]>('/api/recommendations/check', { Lines: lines }),
  options: (tripId: number, lines: RecommendationLineInput[]) =>
    post<RecommendationOptions>('/api/recommendations/options', { TripId: tripId, Lines: lines }),
  forTrip: (tripId: number) => request<TripRecommendation>(`/api/trips/${tripId}/recommendation`),
  /** Never reassigns or forces: if someone else assigned it first, it is rejected. */
  apply: (tripId: number, driverId: number, vehicleId: number, origin: AttemptOrigin) =>
    allocate(tripId, {
      DriverId: driverId,
      VehicleId: vehicleId,
      AssignedBy: assignedBy() || undefined,
      Origin: origin,
      OnlyIfUnassigned: true,
    }),
};

export type Scope = number | 'all';
const ctl = (path: string, scope: Scope, params: Record<string, string | number> = {}) => {
  const query = new URLSearchParams(Object.entries(params).map(([key, value]) => [key, String(value)]));
  if (scope !== 'all') query.set('baseId', String(scope));
  const queryString = query.toString();
  return `/api/control/${path}${queryString ? `?${queryString}` : ''}`;
};

export const control = {
  bases: () => request<OperatingBase[]>('/api/control/bases'),
  summary: (scope: Scope) => request<ControlSummary>(ctl('summary', scope)),
  incidents: (scope: Scope) => request<ControlIncident[]>(ctl('incidents', scope)),
  resources: (scope: Scope) => request<ControlResources>(ctl('resources', scope)),
  engineMetrics: (scope: Scope, from: Date, to?: Date) =>
    request<EngineMetrics>(ctl('engine-metrics', scope, { from: from.toISOString(), ...(to ? { to: to.toISOString() } : {}) })),
  activity: (scope: Scope, since: Date, kind: ActivityKind = 'All', pageSize = 12) =>
    request<ControlPage<ActivityItem>>(ctl('activity', scope, { since: since.toISOString(), kind, pageSize })),
  trends: (scope: Scope, date: Date, days = 7) => request<ControlTrend>(ctl('trends', scope, { date: date.toISOString(), days })),
  engineHourly: (scope: Scope, date: Date) => request<EngineHourly>(ctl('engine-hourly', scope, { date: date.toISOString() })),
  efficiency: (scope: Scope, date: Date) => request<ControlEfficiency>(ctl('efficiency', scope, { date: date.toISOString() })),
};

export const engine = {
  attempts: (query: URLSearchParams) => request<EngineAuditPage>(`/api/engine/attempts?${query}`),
  attempt: (id: number) => request<EngineAttemptDetail>(`/api/engine/attempts/${id}`),
  hourly: (query: URLSearchParams) => request<EngineHourly>(`/api/engine/hourly?${query}`),
};

const resourceUrl = (kind: ResourceKind, id: number) => `/api/${kind === 'driver' ? 'drivers' : 'vehicles'}/${id}`;

export const agenda = {
  add: (kind: ResourceKind, id: number, entry: AgendaInput) =>
    saving.track(() => post<unknown>(`${resourceUrl(kind, id)}/unavailabilities`, entry)),
  change: (kind: ResourceKind, id: number, entryId: number, entry: AgendaInput) =>
    saving.track(() =>
      request<unknown>(`${resourceUrl(kind, id)}/unavailabilities/${entryId}`, { method: 'PUT', body: JSON.stringify(entry) }),
    ),
  remove: (kind: ResourceKind, id: number, entryId: number) =>
    saving.track(() => request<unknown>(`${resourceUrl(kind, id)}/unavailabilities/${entryId}`, { method: 'DELETE' })),
  /** Assigned services inside [from, to), however long it is. */
  affected: (kind: ResourceKind, id: number, from: Date, to: Date) =>
    request<AffectedTrip[]>(
      `${resourceUrl(kind, id)}/affected-trips?${new URLSearchParams({ from: from.toISOString(), to: to.toISOString() })}`,
    ),
};

export const resourceDetails = {
  save: (kind: ResourceKind, id: number, details: DetailsInput) =>
    saving.track(() => request<unknown>(resourceUrl(kind, id), { method: 'PUT', body: JSON.stringify(details) })),
};

export const api = {
  /** 404 once it is no longer this session's or after a few minutes. */
  xray: (token: string) => request<XRay>('/api/xray', { headers: { [XRAY_HEADER]: token } }),
  drivers: () => request<Driver[]>('/api/drivers'),
  vehicles: () => request<Vehicle[]>('/api/vehicles'),
  locations: () => request<Location[]>('/api/locations'),
  travelTime: (originId: number, destinationId: number) =>
    request<TravelTime>(`/api/locations/travel-time?originId=${originId}&destinationId=${destinationId}`),
  trip: (id: number) => request<Trip>(`/api/trips/${id}`),
  async trips(from: Date, to: Date): Promise<Trip[]> {
    const all: Trip[] = [];
    for (let page = 1; ; page++) {
      const query = new URLSearchParams({ from: from.toISOString(), to: to.toISOString(), page: String(page), pageSize: '200' });
      const response = await request<Paged<Trip>>(`/api/trips?${query}`);
      all.push(...response.Items);
      if (all.length >= response.TotalCount || response.Items.length === 0) return all;
    }
  },
  sickLeave: (driverId: number, start: Date, end: Date, notes: string) =>
    request<Driver>(`/api/drivers/${driverId}/unavailabilities`, {
      method: 'POST',
      body: JSON.stringify({ Start: start.toISOString(), End: end.toISOString(), Type: 'SickLeave', Notes: notes || null }),
    }),
  driverCandidates: (tripId: number) => request<DriverCandidate[]>(`/api/trips/${tripId}/driver-candidates`),
  /** By default the API returns only three, but it evaluates the whole fleet either way. */
  everyVehicleCandidate: (tripId: number) =>
    request<VehicleCandidate[]>(`/api/trips/${tripId}/vehicle-candidates?maxResults=${MORE_THAN_ANY_FLEET}`),
  allocate: (tripId: number, driverId: number, vehicleId: number, force?: ForceRequest) =>
    allocate(tripId, {
      DriverId: driverId,
      VehicleId: vehicleId,
      AssignedBy: (force ? force.by.trim() : assignedBy()) || undefined,
      ...(force && { Force: true, Reason: force.reason.trim() }),
    }),
  bookings: (from: Date, to: Date) =>
    request<Booking[]>(`/api/bookings?${new URLSearchParams({ from: from.toISOString(), to: to.toISOString() })}`),
  booking: (id: number) => request<Booking>(`/api/bookings/${id}`),
  rules: () => request<RuleGroup[]>('/api/rules'),
  createBooking: (CustomerName: string, Description: string, Trips: NewTrip[], Notes?: string) =>
    request<Booking>('/api/bookings', { method: 'POST', body: JSON.stringify({ CustomerName, Description, Trips, Notes: Notes || null }) }),
  driverOptions: (tripId: number) => request<DriverOptions>(`/api/trips/${tripId}/driver-options`),
  driverHours: (driverId: number, at?: Date) =>
    request<DriverHours>(`/api/drivers/${driverId}/hours${at ? `?at=${encodeURIComponent(at.toISOString())}` : ''}`),
  history: (kind: ResourceKind, id: number, page: number, pageSize: number) =>
    request<ResourceHistory>(`${resourceUrl(kind, id)}/history?page=${page}&pageSize=${pageSize}`),
  /** The key is typed by whoever resets; 404 means that the environment does not allow it. */
  resetDemo: (key: string) =>
    request<unknown>('/api/admin/demo/reset', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Demo-Reset-Key': key } }),
  vehicleChaining: (from: string, to: string, returnToBase: boolean) =>
    request<VehicleChaining>(`/api/optimization/vehicle-chaining?from=${from}&to=${to}&returnToBase=${returnToBase}`),
};

const asAuthor = (key: string): RequestInit => ({ headers: { 'X-Audit-Key': key } });

/** Only for the author. */
export const audit = {
  request: (id: number, key: string) => request<RequestTraceDetail>(`/api/audit/requests/${id}`, asAuthor(key)),
  visits: (key: string) => request<VisitSummary[]>('/api/audit/visits', asAuthor(key)),
  visit: (sessionId: string, key: string) => request<VisitTimeline>(`/api/audit/visits/${sessionId}`, asAuthor(key)),
};
