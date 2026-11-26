import type { Page } from './pageIds';

/**
 * The only texts of a control that are recorded. Any other may have data inside, a name that somebody typed, and stays as `OTHER`.
 * A label also stands for the texts that go on after it: «Asignar 12 servicios» is «Asignar». Only the author reads them, so they are
 * recorded in Spanish whatever the language of the visitor (SPEC 0095 R7).
 */
const LABELS: [recorded: string, english: string][] = [
  ['Abrir la reserva', 'Open the booking'],
  ['Actualizar ahora', 'Update now'],
  ['Actualizar', 'Update'],
  ['Ahora no', 'Not now'],
  ['Ahorro de km', 'Km saving'],
  ['Anterior', 'Previous'],
  ['Aplicar', 'Apply'],
  ['Asignar el siguiente', 'Allocate the next'],
  ['Asignar semana', 'Allocate week'],
  ['Asignar', 'Allocate'],
  ['Auditoría', 'Audit'],
  ['Añadir entrada', 'Add entry'],
  ['Añadir viaje', 'Add trip'],
  ['Borrar', 'Delete'],
  ['Buscar otro conductor', 'Look for another driver'],
  ['Cancelar', 'Cancel'],
  ['Cargar los datos actuales', 'Load the current data'],
  ['Catálogo', 'Catalogue'],
  ['Centro de control', 'Control Centre'],
  ['Cerrar', 'Close'],
  ['Comprobar otra vez', 'Check again'],
  ['Cómo está hecho', "How it's built"],
  ['Conductores', 'Drivers'],
  ['Confirmar borrado', 'Confirm deletion'],
  ['Día', 'Day'],
  ['Editar', 'Edit'],
  ['Empezar', 'Start'],
  ['Forzar', 'Force'],
  ['Guardar', 'Save'],
  ['Hecho', 'Done'],
  ['Hoy', 'Today'],
  ['Ir a', 'Go to'],
  ['Mes', 'Month'],
  ['Motor normativo', 'Compliance engine'],
  ['No borrar', 'Keep it'],
  ['Nueva reserva', 'New booking'],
  ['Planificación', 'Planning'],
  ['Quitar', 'Remove'],
  ['Recomendar', 'Recommend'],
  ['Recorrido', 'Tour'],
  ['Reintentar', 'Retry'],
  ['Reservas', 'Bookings'],
  ['Restablecer', 'Reset'],
  ['Resumen', 'Summary'],
  ['Agenda', 'Agenda'],
  ['Historial', 'History'],
  ['Semana', 'Week'],
  ['Siguiente', 'Next'],
  ['Vehículos', 'Vehicles'],
  ['Ver en el catálogo', 'See in the catalogue'],
  ['Ver informe', 'See report'],
  ['Ver motor normativo', 'See the compliance engine'],
  ['Ver todos', 'See all'],
  ['Volver', 'Back'],
];

export const OTHER = 'Otro control';

/** For the test that checks them against the catalogue. */
export const LABELS_IN_ENGLISH = LABELS.map(([, english]) => english);

const LONGEST_FIRST = LABELS.flatMap(([recorded, english]) => [
  [recorded, recorded],
  [english, recorded],
]).sort(([a], [b]) => b.length - a.length);

/** Always one of the constants above. */
export function knownLabel(text: string): string {
  const clean = text.replace(/\s+/g, ' ').trim();
  const found = LONGEST_FIRST.find(([label]) => clean === label || (clean.startsWith(label) && !/[\p{L}\p{N}]/u.test(clean[label.length])));
  return found ? found[1] : OTHER;
}

/** The name of each view in the timeline of the visit, as it was before the catalogue. */
const SCREENS: Record<Page, string> = {
  control: 'Centro de control',
  planning: 'Planificación',
  bookings: 'Reservas',
  drivers: 'Conductores',
  vehicles: 'Vehículos',
  engine: 'Motor normativo',
  rules: 'Catálogo de reglas',
  audit: 'Acceso',
};

export const screenName = (page: Page): string => SCREENS[page];
