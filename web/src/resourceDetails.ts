import { t } from './i18n/t';
import { splitByFinished } from './agenda';
import type { ApiError } from './api';
import { createDrafts } from './drafts';
import type { ResourceKind } from './resourceUrl';
import type { AffectedTrip, Driver, Vehicle } from './types';

/** As typed, with the names of the API: a rejection says which field it is about. A driver leaves the ones of a vehicle empty. */
export interface DetailsForm {
  IdentificationNumber: string;
  FirstName: string;
  LastName: string;
  LicensePlate: string;
  Brand: string;
  Model: string;
  SeatingCapacity: string;
  IsExternal: boolean;
  AllowsAutoAssignment: boolean;
  /** Id as text; '' = none. */
  HomeLocationId: string;
  BaseId: string;
}

export type DetailsField = keyof DetailsForm;

/** Only what differs from the data it was typed over. */
export type DetailsEdits = Partial<DetailsForm>;

/** What was typed, with the data it was typed over: a reload in the background must not hide that they changed (SPEC 0090 R4). */
export interface DetailsDraft {
  edits: DetailsEdits;
  seen: { version: string; form: DetailsForm };
}

interface TextField {
  name: 'IdentificationNumber' | 'FirstName' | 'LastName' | 'LicensePlate' | 'Brand' | 'Model';
  label: string;
  max: number;
  missing: string;
}

export const TEXT_FIELDS: Record<ResourceKind, readonly TextField[]> = {
  driver: [
    { name: 'FirstName', get label() { return t.common.name; }, max: 100, get missing() { return t.details.writeName; } },
    { name: 'LastName', get label() { return t.details.lastName; }, max: 100, get missing() { return t.details.writeLastName; } },
    { name: 'IdentificationNumber', get label() { return t.common.idNumber; }, max: 50, get missing() { return t.details.writeId; } },
  ],
  vehicle: [
    { name: 'LicensePlate', get label() { return t.common.plate; }, max: 20, get missing() { return t.details.writePlate; } },
    { name: 'Brand', get label() { return t.details.brand; }, max: 100, get missing() { return t.details.writeBrand; } },
    { name: 'Model', get label() { return t.common.model; }, max: 100, get missing() { return t.details.writeModel; } },
  ],
};

export function formOf(resource: Driver | Vehicle): DetailsForm {
  const texts =
    'LicensePlate' in resource
      ? {
          LicensePlate: resource.LicensePlate,
          Brand: resource.Brand,
          Model: resource.Model,
          SeatingCapacity: String(resource.SeatingCapacity),
          IdentificationNumber: '',
          FirstName: '',
          LastName: '',
        }
      : {
          IdentificationNumber: resource.IdentificationNumber,
          FirstName: resource.FirstName,
          LastName: resource.LastName,
          LicensePlate: '',
          Brand: '',
          Model: '',
          SeatingCapacity: '',
        };
  return {
    ...texts,
    IsExternal: resource.IsExternal,
    AllowsAutoAssignment: resource.AllowsAutoAssignment,
    HomeLocationId: resource.HomeLocationId == null ? '' : String(resource.HomeLocationId),
    BaseId: resource.BaseId == null ? '' : String(resource.BaseId),
  };
}

export function withEdit(edits: DetailsEdits, original: DetailsForm, patch: DetailsEdits): DetailsEdits {
  const next = Object.entries({ ...edits, ...patch }).filter(([name, value]) => value !== original[name as DetailsField]);
  return Object.fromEntries(next) as DetailsEdits;
}

export const newDraft = (resource: Driver | Vehicle): DetailsDraft => ({
  edits: {},
  seen: { version: resource.DetailsVersion, form: formOf(resource) },
});

export const hasEdits = (draft: DetailsDraft): boolean => Object.keys(draft.edits).length > 0;

/** Whoever typed nothing just sees the current data. */
export const liveDraft = (draft: DetailsDraft, resource: Driver | Vehicle): DetailsDraft => (hasEdits(draft) ? draft : newDraft(resource));

export const draftForm = (draft: DetailsDraft): DetailsForm => ({ ...draft.seen.form, ...draft.edits });

export const editDraft = (draft: DetailsDraft, patch: DetailsEdits): DetailsDraft => ({
  seen: draft.seen,
  edits: withEdit(draft.edits, draft.seen.form, patch),
});

export const isOutdated = (draft: DetailsDraft, resource: Driver | Vehicle): boolean => draft.seen.version !== resource.DetailsVersion;

/** Keeps what was typed, except in the fields that changed too: nobody overwrites a value without having seen it. */
export function onCurrentData(draft: DetailsDraft, resource: Driver | Vehicle): DetailsDraft {
  const current = newDraft(resource);
  const untouched = Object.entries(draft.edits).filter(
    ([name]) => current.seen.form[name as DetailsField] === draft.seen.form[name as DetailsField],
  );
  return editDraft(current, Object.fromEntries(untouched) as DetailsEdits);
}

/** The other 409 of a save, a repeated DNI or plate, says which field it is about. */
export const isChangedBySomebodyElse = (error: Pick<ApiError, 'status' | 'field'>): boolean =>
  error.status === 409 && error.field == null;

/** The API reads it into 32 bits. */
const MAX_SEATS = 2_147_483_647;

export function seatsOf(text: string): number | null {
  const seats = /^\s*\d+\s*$/.test(text) ? Number(text) : 0;
  return seats >= 1 && seats <= MAX_SEATS ? seats : null;
}

export function errorsOf(kind: ResourceKind, form: DetailsForm): Partial<Record<DetailsField, string>> {
  const errors: Partial<Record<DetailsField, string>> = {};
  for (const { name, max, missing } of TEXT_FIELDS[kind]) {
    const text = form[name].trim();
    if (!text) errors[name] = missing;
    else if (text.length > max) errors[name] = t.details.atMost(max);
  }
  if (kind === 'vehicle' && seatsOf(form.SeatingCapacity) == null) errors.SeatingCapacity = t.details.wholeNumber;
  return errors;
}

export function inputOf(kind: ResourceKind, form: DetailsForm, version: string) {
  const texts = Object.fromEntries(TEXT_FIELDS[kind].map(({ name }) => [name, form[name].trim()]));
  return {
    ...texts,
    ...(kind === 'vehicle' ? { SeatingCapacity: seatsOf(form.SeatingCapacity) } : {}),
    IsExternal: form.IsExternal,
    AllowsAutoAssignment: form.AllowsAutoAssignment,
    HomeLocationId: form.HomeLocationId ? Number(form.HomeLocationId) : null,
    BaseId: Number(form.BaseId),
    DetailsVersion: version,
  };
}

export type DetailsInput = ReturnType<typeof inputOf>;

export function lowersSeats(form: Pick<DetailsForm, 'SeatingCapacity'>, vehicle: Pick<Vehicle, 'SeatingCapacity'>): boolean {
  const seats = seatsOf(form.SeatingCapacity);
  return seats != null && seats < vehicle.SeatingCapacity;
}

/** Same criterion as the engine: exactly the seats it needs is enough. */
const fits = (requiredSeats: number, seats: number) => seats >= requiredSeats;

export const overCapacity = (trips: AffectedTrip[], seats: number, now: Date): AffectedTrip[] =>
  splitByFinished(trips, now).open.filter(trip => !fits(trip.RequiredSeats, seats));

export function capacityText(count: number, seats: number): string {
  const limit = t.common.seatCount(seats);
  return count === 0
    ? t.details.noneNeedsMore(limit)
    : t.details.needMore(count, limit);
}

/** Null: it has enough. */
export const shortOfSeatsText = (requiredSeats: number, seats: number): string | null =>
  fits(requiredSeats, seats) ? null : t.details.needsSeats(t.common.seatCount(requiredSeats), seats);

export const typeText = (kind: ResourceKind, external: boolean): string =>
  external ? t.common.external : kind === 'driver' ? t.details.staff : t.details.own;

export function proposalHint(kind: ResourceKind): string {
  const who = kind === 'driver' ? t.details.proposedInDriver : t.details.proposedInVehicle;
  return (
    t.details.typeOnly(who) +
    t.details.sameRules
  );
}

export const locationHint = (kind: ResourceKind): string =>
  kind === 'driver'
    ? t.details.rankedByIt
    : t.details.positionWithout;

/**
 * The Control Center of one base only crosses resources and services of that base, so the alerts about services of the previous one
 * disappear from both (SPEC 0079 R7).
 */
export function baseChangeText(kind: ResourceKind, from: string, to: string): string {
  const who = kind === 'driver' ? t.card.theDriverLower : t.card.theVehicleLower;
  return (
    t.details.baseChange(who, from, to) +
    t.details.servicesStay(to) +
    t.details.candidatesSame
  );
}

export const detailsDrafts = createDrafts<DetailsDraft>();
