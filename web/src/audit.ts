import { parseApiDate } from './gantt';
import type { TimelineEntry } from './types';

const KIND_TEXT: Record<string, string> = {
  Arrived: 'Llega con el enlace',
  Screen: 'Pantalla',
  Click: 'Botón',
  FormOpened: 'Abre el formulario',
  FormSent: 'Envía el formulario',
  FormAbandoned: 'Abandona el formulario',
  RequestFailed: 'Petición sin éxito',
  Error: 'Error de la web',
  Request: 'Petición',
  Created: 'Crea',
  Changed: 'Cambia',
  Deleted: 'Borra',
  Reset: 'Rehace la demo',
};

const ENTITY_TEXT: Record<string, string> = {
  AllocationAttempt: 'Intento de asignación',
  Booking: 'Reserva',
  Circuit: 'Circuito',
  Client: 'Cliente',
  Demo: 'Datos de la demo',
  Driver: 'Conductor',
  DriverUnavailability: 'Agenda de conductor',
  Location: 'Lugar',
  OperatingBase: 'Base',
  Trip: 'Servicio',
  TripStop: 'Parada',
  Vehicle: 'Vehículo',
  VehicleUnavailability: 'Agenda de vehículo',
};

const CHANGES = new Set(['Created', 'Changed', 'Deleted', 'Reset']);

export const kindText = (kind: string): string => KIND_TEXT[kind] ?? kind;

export const isChange = (entry: TimelineEntry): boolean => CHANGES.has(entry.Kind);

export function entryName(entry: TimelineEntry): string {
  if (!isChange(entry)) return entry.Name;
  const entity = ENTITY_TEXT[entry.Name] ?? entry.Name;
  return entry.EntityId === null ? entity : `${entity} #${entry.EntityId}`;
}

const isWrong = (entry: TimelineEntry) =>
  entry.Kind === 'Error' || entry.Kind === 'RequestFailed' || (entry.Status !== null && entry.Status >= 400);

export type EntryTone = 'wrong' | 'change' | 'plain';

export const toneOf = (entry: TimelineEntry): EntryTone => (isWrong(entry) ? 'wrong' : isChange(entry) ? 'change' : 'plain');

export function resultText(entry: TimelineEntry): string {
  const result = entry.Status === null ? null : entry.Status === 0 ? 'sin respuesta' : String(entry.Status);
  const duration = entry.DurationMs === null ? null : `${entry.DurationMs} ms`;
  return [result, duration].filter(Boolean).join(' · ');
}

export type FieldChange = { field: string; from: string; to: string };

const valueText = (value: unknown): string =>
  value === undefined || value === null ? '—' : typeof value === 'object' ? JSON.stringify(value) : String(value);

/** What is not understood is not shown, and the rest of the timeline is. */
export function fieldChanges(changes: string | null): FieldChange[] {
  if (!changes) return [];
  try {
    const fields = JSON.parse(changes) as Record<string, { Old?: unknown; New?: unknown }>;
    return Object.entries(fields).map(([field, change]) => ({ field, from: valueText(change.Old), to: valueText(change.New) }));
  } catch {
    return [];
  }
}

/** Entries in a row with the same thread are one action and what it caused. */
export function groupByThread(entries: TimelineEntry[]): TimelineEntry[][] {
  const groups: TimelineEntry[][] = [];
  for (const entry of entries) {
    const last = groups.at(-1);
    if (last && entry.TraceId !== null && last[0].TraceId === entry.TraceId) last.push(entry);
    else groups.push([entry]);
  }
  return groups;
}

export const shortSession = (sessionId: string): string => sessionId.slice(0, 8);

export const durationText = (from: string, to: string): string => {
  const minutes = Math.round((parseApiDate(to).getTime() - parseApiDate(from).getTime()) / 60_000);
  return minutes < 1 ? 'menos de 1 min' : `${minutes} min`;
};
