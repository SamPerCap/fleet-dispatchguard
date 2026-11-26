import { t } from './i18n/t';
import { CircleCheck, IdCard, Info, Pencil, RefreshCw, UsersRound } from 'lucide-react';
import { useCallback, useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { draftKey } from './agenda';
import { AffectedList } from './AffectedList';
import { agenda, ApiError, resourceDetails } from './api';
import type { MasterData } from './App';
import type { Loaded } from './home/data';
import { LocationPicker } from './LocationPicker';
import {
  baseChangeText,
  capacityText,
  detailsDrafts,
  draftForm,
  editDraft,
  errorsOf,
  hasEdits,
  inputOf,
  isChangedBySomebodyElse,
  isOutdated,
  liveDraft,
  locationHint,
  lowersSeats,
  newDraft,
  onCurrentData,
  overCapacity,
  proposalHint,
  seatsOf,
  TEXT_FIELDS,
  typeText,
  type DetailsDraft,
  type DetailsEdits,
  type DetailsField,
} from './resourceDetails';
import type { ResourceKind } from './resourceUrl';
import { SaveError, useSave } from './save';
import type { AffectedTrip, Driver, OperatingBase, Vehicle } from './types';
import { ErrorState, Section, SkeletonLines } from './ui';
import { useLoad } from './useLoad';
import { useFormVisit } from './visit';

/** The query of affected services takes a period: this one never leaves any out. */
const FAR_AWAY = new Date('2100-01-01T00:00:00Z');

export interface DetailsSectionProps {
  kind: ResourceKind;
  resource: Driver | Vehicle;
  /** Shown as read, never edited here. */
  state: ReactNode;
  bases: Loaded<OperatingBase[]> | null;
  retryBases: () => void;
  baseName: (id?: number) => string;
  now: Date;
  version: number;
  master: MasterData;
  onOpenTrip: (bookingId: number, tripId: number) => void;
  onChanged: () => void;
}

export function DetailsSection(props: DetailsSectionProps) {
  const { kind, resource, onChanged } = props;
  const key = draftKey(kind, resource.Id);
  const [draft, setDraft] = useState<DetailsDraft | null>(() => detailsDrafts.read(key));
  const [recovered, setRecovered] = useState(draft != null);
  const [saved, setSaved] = useState(false);
  const editButton = useRef<HTMLButtonElement>(null);
  const editing = draft != null;
  const wasEditing = useRef(editing);
  useEffect(() => {
    if (wasEditing.current && !editing) editButton.current?.focus();
    wasEditing.current = editing;
  }, [editing]);

  const close = useCallback(() => {
    detailsDrafts.write(key, null);
    setDraft(null);
    setRecovered(false);
  }, [key]);
  const change = (next: DetailsDraft) => {
    detailsDrafts.write(key, hasEdits(next) ? next : null);
    setDraft(next);
  };
  /** Out of the store while it is being saved: a card closed and reopened meanwhile must not offer to save it again. */
  const holdDraft = () => {
    detailsDrafts.write(key, null);
    return () => detailsDrafts.write(key, draft);
  };

  return (
    <Section
      title={t.details.title}
      icon={IdCard}
      aside={
        !draft && (
          <button
            ref={editButton}
            type="button"
            className="btn btn-sm"
            onClick={() => {
              setSaved(false);
              setDraft(newDraft(resource));
            }}
          >
            <Pencil className="h-4 w-4" aria-hidden />
            {t.common.edit}
          </button>
        )
      }
    >
      <p role="status" className={saved ? 'rounded-md bg-success-soft px-3 py-2 text-meta font-medium text-success-ink' : 'sr-only'}>
        {saved && t.details.saved}
      </p>
      {draft ? (
        <DetailsForm
          {...props}
          draft={liveDraft(draft, resource)}
          recovered={recovered}
          onDraft={change}
          holdDraft={holdDraft}
          onCancel={close}
          onSaved={() => {
            close();
            setSaved(true);
            onChanged();
          }}
        />
      ) : (
        <DetailsList {...props} />
      )}
    </Section>
  );
}

const Field = ({ label, children }: { label: string; children: ReactNode }) => (
  <div className="min-w-0">
    <dt className="text-xs text-ink-3">{label}</dt>
    <dd className="mt-0.5 truncate font-medium text-ink">{children}</dd>
  </div>
);

function DetailsList({ kind, resource, state, baseName, master }: DetailsSectionProps) {
  return (
    <dl className="grid grid-cols-2 gap-4 rounded-md bg-sunken p-4 sm:grid-cols-3">
      {'LicensePlate' in resource ? (
        <>
          <Field label={t.common.state}>{state}</Field>
          <Field label={t.common.seats}>{resource.SeatingCapacity}</Field>
          <Field label={t.details.brandAndModel}>
            {resource.Brand} {resource.Model}
          </Field>
        </>
      ) : (
        <Field label={t.common.idNumber}>{resource.IdentificationNumber}</Field>
      )}
      <Field label={t.common.base}>{baseName(resource.BaseId)}</Field>
      <Field label={t.details.referencePlace}>{resource.HomeLocationId ? master.locName(resource.HomeLocationId) : '—'}</Field>
      <Field label={t.common.type}>{typeText(kind, resource.IsExternal)}</Field>
      <Field label={t.details.autoProposal}>{resource.AllowsAutoAssignment ? t.common.yes : t.common.no}</Field>
    </dl>
  );
}

/** A rejection about one field, while that field still has the value that was rejected. */
function useRefusal() {
  const [refusal, setRefusal] = useState<{ field: DetailsField; value: unknown; message: string } | null>(null);
  return {
    refused: refusal?.field,
    refuse: (field: DetailsField, value: unknown, message: string) => setRefusal({ field, value, message }),
    forget: () => setRefusal(null),
    refusalOf: (field: DetailsField, value: unknown) =>
      refusal?.field === field && refusal.value === value ? refusal.message : undefined,
  };
}

function DetailsForm({
  kind,
  resource,
  bases,
  retryBases,
  baseName,
  now,
  version,
  master,
  draft,
  recovered,
  onDraft,
  holdDraft,
  onCancel,
  onSaved,
  onChanged,
  onOpenTrip,
}: DetailsSectionProps & {
  draft: DetailsDraft;
  recovered: boolean;
  onDraft: (draft: DetailsDraft) => void;
  /** Returns how to put it back if the save fails. */
  holdDraft: () => () => void;
  onCancel: () => void;
  onSaved: () => void;
}) {
  useFormVisit(t.details.formName);
  const original = draft.seen.form;
  const form = draftForm(draft);
  const formElement = useRef<HTMLFormElement>(null);
  const focusFirstField = () => formElement.current?.querySelector('input')?.focus();
  useEffect(() => {
    // Not with a recovered draft: the card is just opening, and it has to remember where the focus was.
    if (!recovered) focusFirstField();
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  const vehicle = 'LicensePlate' in resource ? resource : null;
  const lowering = vehicle != null && lowersSeats(form, vehicle);
  const [assigned, retryAssigned] = useLoad(lowering ? () => agenda.affected(kind, resource.Id, now, FAR_AWAY) : null, [
    lowering,
    kind,
    resource.Id,
    version,
  ]);
  const checking = lowering && !assigned;

  const { busy, error, run, clearError } = useSave(onChanged);
  // Not while saving: what comes back changed may be what is being saved.
  const outdated = !busy && isOutdated(draft, resource);
  const refusedAsOld = error instanceof ApiError && isChangedBySomebodyElse(error);
  const loadCurrentData = () => {
    onDraft(onCurrentData(draft, resource));
    clearError();
    focusFirstField(); // The button that had the focus goes away.
  };
  const { refused, refuse, forget, refusalOf } = useRefusal();
  useEffect(() => {
    // Not while saving: the fields are disabled and cannot take the focus.
    if (refused && !busy) formElement.current?.querySelector<HTMLElement>(`[name="${refused}"]`)?.focus();
  }, [refused, busy]);
  const errors = errorsOf(kind, form);
  const errorOf = (name: DetailsField) => errors[name] ?? refusalOf(name, form[name]);
  const aboutAField = error instanceof ApiError && error.field != null && error.field in form;
  const invalid = Object.keys(errors).length > 0;
  const blocked = busy || invalid || checking || outdated || !hasEdits(draft);
  const edit = (patch: DetailsEdits) => onDraft(editDraft(draft, patch));

  const save = () => {
    if (blocked) return;
    forget();
    const restoreDraft = holdDraft();
    void run(
      () => resourceDetails.save(kind, resource.Id, inputOf(kind, form, draft.seen.version)),
      onSaved,
      cause => {
        restoreDraft();
        if (!(cause instanceof ApiError) || !cause.field || !(cause.field in form)) return;
        const field = cause.field as DetailsField;
        refuse(field, form[field], cause.detail ?? cause.message);
      },
    );
  };

  const movesTo = form.BaseId !== original.BaseId ? baseName(Number(form.BaseId)) : null;
  return (
    <form
      ref={formElement}
      aria-label={t.details.edit}
      className="space-y-4 rounded-md border border-line p-4"
      onSubmit={event => {
        event.preventDefault();
        save();
      }}
    >
      {recovered && (
        <p className="flex items-center gap-2 rounded-md bg-blue-soft px-3 py-2 text-meta text-ink">
          <Info className="h-4 w-4 shrink-0 text-blue-ink" aria-hidden />
          {t.common.halfDone}
        </p>
      )}
      <fieldset className="grid gap-3 sm:grid-cols-2" disabled={busy}>
        <legend className="sr-only">{t.details.title}</legend>
        {TEXT_FIELDS[kind].map(({ name, label, max }) => (
          <TextInput
            key={name}
            name={name}
            label={label}
            max={max}
            value={form[name]}
            error={errorOf(name)}
            onChange={text => edit({ [name]: text })}
          />
        ))}
        {vehicle && (
          <TextInput
            name="SeatingCapacity"
            label={t.common.seats}
            numeric
            max={10}
            value={form.SeatingCapacity}
            error={errorOf('SeatingCapacity')}
            onChange={text => edit({ SeatingCapacity: text })}
          />
        )}
      </fieldset>
      {lowering && (
        <div aria-live="polite">
          {!assigned ? (
            <SkeletonLines lines={1} label={t.agenda.checkingAssigned} />
          ) : !assigned.ok ? (
            <ErrorState title={t.details.checkFailed} onRetry={retryAssigned}>
              {t.common.saveAnyway(assigned.error)}
            </ErrorState>
          ) : (
            <CapacityPreview
              trips={overCapacity(assigned.value, seatsOf(form.SeatingCapacity)!, now)}
              seats={seatsOf(form.SeatingCapacity)!}
              master={master}
              onOpenTrip={onOpenTrip}
            />
          )}
        </div>
      )}
      <fieldset className="grid gap-3 sm:grid-cols-2" disabled={busy}>
        <legend className="sr-only">{t.details.baseAndPlace}</legend>
        <BaseSelect
          value={form.BaseId}
          stored={original.BaseId}
          bases={bases}
          retry={retryBases}
          baseName={baseName}
          onChange={BaseId => edit({ BaseId })}
        />
        <LocationPicker
          label={t.details.referencePlace}
          value={form.HomeLocationId}
          locations={master.locations}
          onChange={HomeLocationId => edit({ HomeLocationId })}
        />
      </fieldset>
      {movesTo && <Note live>{baseChangeText(kind, baseName(Number(original.BaseId)), movesTo)}</Note>}
      {form.HomeLocationId !== original.HomeLocationId && <Note live>{locationHint(kind)}</Note>}
      <fieldset className="flex flex-wrap items-end gap-x-6 gap-y-3" disabled={busy}>
        <legend className="sr-only">{t.details.proposals}</legend>
        <label className="block w-40">
          <span className="label">{t.common.type}</span>
          <select
            name="IsExternal"
            className="input mt-1 w-full"
            value={String(form.IsExternal)}
            onChange={event => edit({ IsExternal: event.target.value === 'true' })}
          >
            {[false, true].map(external => (
              <option key={String(external)} value={String(external)}>
                {typeText(kind, external)}
              </option>
            ))}
          </select>
        </label>
        <label className="flex h-9 items-center gap-2 text-meta font-medium text-ink">
          <input
            type="checkbox"
            name="AllowsAutoAssignment"
            className="h-4 w-4"
            checked={form.AllowsAutoAssignment}
            onChange={event => edit({ AllowsAutoAssignment: event.target.checked })}
          />
          {t.details.autoProposal}
        </label>
      </fieldset>
      <Note>{proposalHint(kind)}</Note>
      <p className="text-xs text-ink-3">{t.details.keepsAssigned}</p>
      {(outdated || (refusedAsOld && hasEdits(draft))) && <ChangedNotice onLoad={outdated ? loadCurrentData : undefined} />}
      {error && !aboutAField && !refusedAsOld && <SaveError error={error} busy={busy} onRetry={save} />}
      <div className="flex flex-wrap justify-end gap-2">
        <button type="button" className="btn" disabled={busy} onClick={onCancel}>
          {t.common.cancel}
        </button>
        <button type="submit" className="btn btn-primary" disabled={blocked}>
          {busy ? t.common.saving : t.common.save}
        </button>
      </div>
    </form>
  );
}

/** Without `onLoad` the current data have not arrived yet. */
const ChangedNotice = ({ onLoad }: { onLoad?: () => void }) => (
  <div role="alert" className="flex flex-wrap items-center gap-3 rounded-md bg-warning-soft px-3 py-2 text-meta text-warning-ink">
    <UsersRound className="h-4 w-4 shrink-0" aria-hidden />
    <p className="min-w-0 flex-1">
      <b className="font-semibold">{t.details.changedByOther}</b> {t.details.nothingSaved(onLoad ? t.details.onLoad : '')}
    </p>
    {onLoad && (
      <button type="button" className="btn btn-sm" onClick={onLoad}>
        <RefreshCw className="h-4 w-4" aria-hidden />
        {t.details.loadCurrent}
      </button>
    )}
  </div>
);

const Note = ({ children, live }: { children: ReactNode; live?: boolean }) => (
  <p className="flex items-start gap-2 rounded-md bg-sunken px-3 py-2 text-meta text-ink-2" role={live ? 'status' : undefined}>
    <Info className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
    <span>{children}</span>
  </p>
);

function TextInput({
  name,
  label,
  max,
  numeric,
  value,
  error,
  onChange,
}: {
  name: DetailsField;
  label: string;
  max: number;
  numeric?: boolean;
  value: string;
  error?: string;
  onChange: (text: string) => void;
}) {
  const id = useId();
  const errorId = `${id}-error`;
  return (
    <div className="min-w-0">
      <label className="label" htmlFor={id}>
        {label}
      </label>
      <input
        id={id}
        name={name}
        type="text"
        inputMode={numeric ? 'numeric' : undefined}
        autoComplete="off"
        className={`input mt-1 w-full ${error ? 'border-critical' : ''}`}
        maxLength={max}
        value={value}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? errorId : undefined}
        onChange={event => onChange(event.target.value)}
      />
      {error && (
        <p id={errorId} role="alert" className="mt-1 text-xs font-medium text-critical-ink">
          {error}
        </p>
      )}
    </div>
  );
}

function BaseSelect({
  value,
  stored,
  bases,
  retry,
  baseName,
  onChange,
}: {
  value: string;
  /** Offered even if it was deactivated, as the API lets it stay. */
  stored: string;
  bases: Loaded<OperatingBase[]> | null;
  retry: () => void;
  baseName: (id?: number) => string;
  onChange: (baseId: string) => void;
}) {
  const id = useId();
  const options = bases?.ok ? bases.value : [];
  const unlisted = [...new Set([stored, value])].filter(baseId => !options.some(option => String(option.Id) === baseId));
  return (
    <div className="min-w-0">
      <label className="label" htmlFor={id}>
        {t.common.base}
      </label>
      <select
        id={id}
        name="BaseId"
        className="input mt-1 w-full"
        value={value}
        disabled={!bases?.ok}
        onChange={event => onChange(event.target.value)}
      >
        {unlisted.map(baseId => (
          <option key={baseId} value={baseId}>
            {baseName(Number(baseId))}
          </option>
        ))}
        {options.map(option => (
          <option key={option.Id} value={option.Id}>
            {option.Name}
          </option>
        ))}
      </select>
      {bases && !bases.ok && (
        <p className="mt-1 text-xs text-critical-ink">
          {t.details.basesFailed(' ')}{' '}
          <button type="button" className="btn btn-link" onClick={retry}>
            {t.common.retry}
          </button>
        </p>
      )}
    </div>
  );
}

function CapacityPreview({
  trips,
  seats,
  master,
  onOpenTrip,
}: {
  trips: AffectedTrip[];
  seats: number;
  master: MasterData;
  onOpenTrip: (bookingId: number, tripId: number) => void;
}) {
  if (trips.length === 0)
    return (
      <p className="flex items-center gap-2 rounded-md bg-success-soft px-3 py-2 text-meta font-medium text-success-ink">
        <CircleCheck className="h-4 w-4 shrink-0" aria-hidden />
        {capacityText(0, seats)}
      </p>
    );
  return (
    <div className="space-y-1">
      <AffectedList
        title={capacityText(trips.length, seats)}
        trips={trips}
        master={master}
        detail={trip => `${trip.RequiredSeats} plazas`}
        onOpenTrip={onOpenTrip}
      />
      <p className="text-xs text-ink-2">
        {t.details.saveAnywayOpen(trips.length)}
      </p>
    </div>
  );
}
