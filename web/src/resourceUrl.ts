// In the query string (not in the `#`, which is the view), so the card opens on top of any view and the link can be shared.

export type ResourceKind = 'driver' | 'vehicle';
export type ResourceTab = 'resumen' | 'historial' | 'auditoria' | 'agenda';
export const RESOURCE_TABS: readonly ResourceTab[] = ['resumen', 'historial', 'auditoria', 'agenda'];

export interface ResourceRef {
  kind: ResourceKind;
  id: number;
  tab: ResourceTab;
}

const PARAM: Record<ResourceKind, string> = { driver: 'conductor', vehicle: 'vehiculo' };
const TAB_PARAM = 'pestana';
/** Links shared before the tab was renamed (SPEC 0078). */
const FORMER_TABS = new Map<string, ResourceTab>([['indisponibilidades', 'agenda']]);

const positiveId = (value: string | null): number | null => {
  if (value == null || !/^\d+$/.test(value)) return null;
  const id = Number(value);
  return Number.isSafeInteger(id) && id > 0 ? id : null;
};

function readTab(value: string | null): ResourceTab {
  if ((RESOURCE_TABS as readonly string[]).includes(value ?? '')) return value as ResourceTab;
  return FORMER_TABS.get(value ?? '') ?? 'resumen';
}

/** With both parameters, the driver wins. */
export function readResource(search: string): ResourceRef | null {
  const query = new URLSearchParams(search);
  const tab = readTab(query.get(TAB_PARAM));
  for (const kind of ['driver', 'vehicle'] as const) {
    const id = positiveId(query.get(PARAM[kind]));
    if (id != null) return { kind, id, tab };
  }
  return null;
}

/** Keeps the other parameters. */
export function writeResource(search: string, ref: ResourceRef | null): string {
  const query = new URLSearchParams(search);
  query.delete(PARAM.driver);
  query.delete(PARAM.vehicle);
  query.delete(TAB_PARAM);
  if (ref) {
    query.set(PARAM[ref.kind], String(ref.id));
    if (ref.tab !== 'resumen') query.set(TAB_PARAM, ref.tab);
  }
  const queryString = query.toString();
  return queryString ? `?${queryString}` : '';
}

export const sameResource = (first: ResourceRef | null, second: ResourceRef | null) =>
  !!first && !!second && first.kind === second.kind && first.id === second.id;
