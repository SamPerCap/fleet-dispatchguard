import { t } from './i18n/t';
import { AlertTriangle, CalendarClock, CalendarPlus, History, Info, Pencil, ShieldOff, Trash2 } from 'lucide-react';
import { useCallback, useEffect, useId, useRef, useState } from 'react';
import {
  AGENDA_TYPES,
  affectedText,
  caughtBy,

  draftKey,
  formOf,
  inputOf,
  MAX_NOTES,
  MAX_PROVIDER,
  newForm,
  periodOf,
  periodText,
  readDraft,
  spanOf,
  splitByFinished,
  typeHint,
  usableDraft,
  withStartDate,
  writeDraft,
  type AgendaDraft,
  type AgendaForm,
  type Period,
} from './agenda';
import { AffectedList } from './AffectedList';
import { agenda } from './api';
import type { MasterData } from './App';
import { parseApiDate } from './gantt';
import type { Loaded } from './home/data';
import { sortUnavailabilities, UNAVAILABILITY_LABEL } from './resources';
import type { ResourceKind } from './resourceUrl';
import { SaveError, useSave } from './save';
import type { AffectedTrip, Unavailability } from './types';
import { ErrorState, Section, SkeletonLines } from './ui';
import { useLoad } from './useLoad';
import { useFormVisit } from './visit';

const PREVIEW_DELAY_MS = 250;

export interface AgendaTabProps {
  kind: ResourceKind;
  id: number;
  items: Unavailability[];
  now: Date;
  version: number;
  master: MasterData;
  onOpenTrip: (bookingId: number, tripId: number) => void;
  onChanged: () => void;
}

const typeLabel = (type: string) => UNAVAILABILITY_LABEL[type] ?? type;

const conflictsText = (count: number) => t.agenda.conflictCount(count);

export function AgendaTab({ kind, id, items, now, version, master, onOpenTrip, onChanged }: AgendaTabProps) {
  const key = draftKey(kind, id);
  const [draft, setDraft] = useState<AgendaDraft | null>(() => usableDraft(readDraft(key), items, now));
  const [recovered, setRecovered] = useState(draft != null);
  const [saved, setSaved] = useState<string | null>(null);
  const addButton = useRef<HTMLButtonElement>(null);
  const { current, past } = sortUnavailabilities(items, now);

  const span = spanOf(current);
  const [affected, retryAffected] = useLoad(span ? () => agenda.affected(kind, id, span.start, span.end) : null, [
    kind,
    id,
    span?.start.getTime(),
    span?.end.getTime(),
    version,
  ]);

  const open = (next: AgendaDraft) => {
    setSaved(null);
    setRecovered(false);
    setDraft(next);
  };
  const close = useCallback(() => {
    writeDraft(key, null);
    setDraft(null);
    setRecovered(false);
    requestAnimationFrame(() => addButton.current?.focus());
  }, [key]);
  const change = (form: AgendaForm) => {
    if (!draft) return;
    const next = { ...draft, form };
    writeDraft(key, next);
    setDraft(next);
  };
  /** Out of the store while it is being saved: a card closed and reopened meanwhile must not offer to save it again. */
  const holdDraft = () => {
    writeDraft(key, null);
    return () => writeDraft(key, draft);
  };
  const done = (text: string) => {
    close();
    setSaved(text);
    onChanged();
  };

  const edited = draft?.entryId != null ? items.find(entry => entry.Id === draft.entryId) : undefined;
  const what = kind === 'driver' ? t.agenda.driverAbsences : t.agenda.vehicleStops;
  const empty = (text: string) => <p className="rounded-md bg-sunken px-3 py-3 text-meta text-ink-2">{text}</p>;
  const lists = (
    <div className="grid gap-6 xl:grid-cols-2">
      <Section title={t.agenda.current} icon={CalendarClock}>
        {affected && !affected.ok && (
          <ErrorState title={t.agenda.affectedFailed} onRetry={retryAffected}>
            {affected.error}
          </ErrorState>
        )}
        {current.length ? (
          <ul className="divide-y divide-line overflow-hidden rounded-md bg-sunken">
            {current.map(entry => (
              <OpenEntry
                key={entry.Id}
                kind={kind}
                id={id}
                entry={entry}
                now={now}
                affected={affected?.ok ? caughtBy(entry, affected.value, now).open : null}
                checking={affected == null}
                master={master}
                locked={draft != null}
                onEdit={() => open({ entryId: entry.Id, form: formOf(entry) })}
                onOpenTrip={onOpenTrip}
                onDeleted={() => done(t.agenda.deleted)}
                onChanged={onChanged}
              />
            ))}
          </ul>
        ) : (
          empty(t.agenda.noneAhead(what))
        )}
      </Section>
      <Section title={t.agenda.past} icon={History}>
        {past.length ? (
          <ul className="divide-y divide-line overflow-hidden rounded-md bg-sunken">
            {past.map(entry => (
              <li key={entry.Id} className="flex flex-wrap items-start justify-between gap-3 px-3 py-2.5 text-meta text-ink-2">
                <EntryText entry={entry} />
                <span className="shrink-0 text-xs">{periodText(entry)}</span>
              </li>
            ))}
          </ul>
        ) : (
          empty(t.agenda.nonePast(what))
        )}
      </Section>
    </div>
  );

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center gap-3">
        <p className="min-w-0 flex-1 text-meta text-ink-2">
          {t.agenda.explained}
        </p>
        {!draft && (
          <button
            ref={addButton}
            type="button"
            className="btn btn-primary"
            onClick={() => open({ entryId: null, form: newForm(kind, new Date()) })}
          >
            <CalendarPlus className="h-4 w-4" aria-hidden />
            {t.agenda.add}
          </button>
        )}
      </div>
      <p role="status" className={saved ? 'rounded-md bg-success-soft px-3 py-2 text-meta font-medium text-success-ink' : 'sr-only'}>
        {saved}
      </p>
      {draft && (
        <EntryForm
          key={draft.entryId ?? 'new'}
          kind={kind}
          id={id}
          entryId={draft.entryId}
          form={draft.form}
          original={edited}
          recovered={recovered}
          now={now}
          version={version}
          master={master}
          onForm={change}
          holdDraft={holdDraft}
          onCancel={close}
          onSaved={done}
          onChanged={onChanged}
          onOpenTrip={onOpenTrip}
        />
      )}
      {lists}
    </div>
  );
}

function EntryText({ entry, running }: { entry: Unavailability; running?: boolean }) {
  return (
    <span className="min-w-0 flex-1">
      <span className="font-medium">{typeLabel(entry.Type)}</span>
      {running && (
        <span className="ml-2 inline-flex h-5 items-center rounded-full bg-warning-soft px-2 text-xs font-medium text-warning-ink">{t.common.now}</span>
      )}
      {entry.ServiceProvider && <span className="block truncate text-xs text-ink-2">{t.agenda.workshop(entry.ServiceProvider)}</span>}
      {entry.Notes && <span className="block break-words text-xs text-ink-2">{entry.Notes}</span>}
    </span>
  );
}

function OpenEntry({
  kind,
  id,
  entry,
  now,
  affected,
  checking,
  master,
  locked,
  onEdit,
  onOpenTrip,
  onDeleted,
  onChanged,
}: {
  kind: ResourceKind;
  id: number;
  entry: Unavailability;
  now: Date;
  /** Null: not known. */
  affected: AffectedTrip[] | null;
  checking: boolean;
  master: MasterData;
  /** A form is open: one thing at a time. */
  locked: boolean;
  onEdit: () => void;
  onOpenTrip: (bookingId: number, tripId: number) => void;
  onDeleted: () => void;
  onChanged: () => void;
}) {
  const [confirming, setConfirming] = useState(false);
  const { busy, error, run } = useSave(onChanged);
  const name = `${typeLabel(entry.Type)}, ${periodText(entry)}`;
  const remove = () => run(() => agenda.remove(kind, id, entry.Id), onDeleted);
  return (
    <li className="space-y-2 px-3 py-2.5 text-meta text-ink">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <EntryText entry={entry} running={parseApiDate(entry.Start) <= now} />
        <span className="shrink-0 pt-0.5 text-xs">{periodText(entry)}</span>
        <span className="-my-1 flex shrink-0 gap-1">
          <button
            type="button"
            className="btn btn-text btn-icon"
            aria-label={t.agenda.editName(name)}
            title={t.common.edit}
            disabled={locked}
            onClick={onEdit}
          >
            <Pencil className="h-4 w-4" aria-hidden />
          </button>
          <button
            type="button"
            className="btn btn-text btn-icon"
            aria-label={t.agenda.deleteName(name)}
            title={t.common.delete}
            disabled={locked || confirming}
            onClick={() => setConfirming(true)}
          >
            <Trash2 className="h-4 w-4" aria-hidden />
          </button>
        </span>
      </div>
      {checking && <p className="text-xs text-ink-3">{t.agenda.checking}</p>}
      {affected != null && affected.length > 0 && (
        <AffectedList title={affectedText(affected.length)} trips={affected} master={master} onOpenTrip={onOpenTrip} />
      )}
      {confirming && !locked && (
        <div role="group" aria-label={t.agenda.confirmDelete} className="flex flex-wrap items-center gap-2 rounded-md bg-surface p-2 shadow-1">
          <span className="min-w-0 flex-1">{t.agenda.deleteQuestion}</span>
          <button type="button" className="btn btn-sm" disabled={busy} onClick={() => setConfirming(false)}>
            {t.agenda.doNotDelete}
          </button>
          <button type="button" className="btn btn-sm btn-danger" disabled={busy} onClick={remove}>
            {busy ? t.common.deleting : t.common.delete}
          </button>
        </div>
      )}
      {error && <SaveError error={error} busy={busy} onRetry={remove} />}
    </li>
  );
}

/** Null while it is being asked, also between a change of the period and its answer. */
function useAffected(
  kind: ResourceKind,
  id: number,
  period: Period,
  version: number,
): [Loaded<AffectedTrip[]> | null, () => void] {
  const start = period.ok ? period.start.getTime() : null;
  const end = period.ok ? period.end.getTime() : null;
  const [attempt, setAttempt] = useState(0);
  const question = `${kind}-${id}-${start}-${end}-${version}-${attempt}`;
  const [answer, setAnswer] = useState<{ question: string; loaded: Loaded<AffectedTrip[]> } | null>(null);
  useEffect(() => {
    if (start == null || end == null) return;
    let live = true;
    const timer = setTimeout(() => {
      agenda.affected(kind, id, new Date(start), new Date(end)).then(
        value => live && setAnswer({ question, loaded: { ok: true, value } }),
        cause => live && setAnswer({ question, loaded: { ok: false, error: (cause as Error).message } }),
      );
    }, PREVIEW_DELAY_MS);
    return () => {
      live = false;
      clearTimeout(timer);
    };
  }, [question]); // eslint-disable-line react-hooks/exhaustive-deps
  return [answer?.question === question ? answer.loaded : null, useCallback(() => setAttempt(n => n + 1), [])];
}

function EntryForm({
  kind,
  id,
  entryId,
  form,
  original,
  recovered,
  now,
  version,
  master,
  onForm,
  holdDraft,
  onCancel,
  onSaved,
  onChanged,
  onOpenTrip,
}: {
  kind: ResourceKind;
  id: number;
  entryId: number | null;
  form: AgendaForm;
  original?: Unavailability;
  recovered: boolean;
  now: Date;
  version: number;
  master: MasterData;
  onForm: (form: AgendaForm) => void;
  /** Returns how to put it back if the save fails. */
  holdDraft: () => () => void;
  onCancel: () => void;
  onSaved: (text: string) => void;
  onChanged: () => void;
  onOpenTrip: (bookingId: number, tripId: number) => void;
}) {
  useFormVisit(t.agenda.formName);
  const title = entryId == null ? t.agenda.newEntry : t.agenda.editEntry;
  const titleId = useId();
  const errorId = useId();
  const firstField = useRef<HTMLSelectElement>(null);
  useEffect(() => firstField.current?.focus(), []);
  const period = periodOf(form, original);
  const [affected, retryAffected] = useAffected(kind, id, period, version);
  const { busy, error, run } = useSave(onChanged);
  const caught = affected?.ok ? splitByFinished(affected.value, now) : null;
  const hint = typeHint(form.type);

  const save = () => {
    if (!period.ok) return;
    const input = inputOf(kind, form, period);
    const restoreDraft = holdDraft();
    void run(
      () => (entryId == null ? agenda.add(kind, id, input) : agenda.change(kind, id, entryId, input)),
      () => onSaved(entryId == null ? t.agenda.added : t.common.changesSaved),
      restoreDraft,
    );
  };
  const saveLabel = caught?.open.length ? t.agenda.saveWith(conflictsText(caught.open.length)) : t.common.save;
  const field = (patch: Partial<AgendaForm>) => onForm({ ...form, ...patch });

  return (
    <form
      aria-labelledby={titleId}
      className="space-y-4 rounded-md border border-line p-4"
      onSubmit={event => {
        event.preventDefault();
        if (period.ok && affected && !busy) save();
      }}
    >
      <h3 id={titleId} className="text-meta font-semibold text-ink">
        {title}
      </h3>
      {recovered && (
        <p className="flex items-center gap-2 rounded-md bg-blue-soft px-3 py-2 text-meta text-ink">
          <Info className="h-4 w-4 shrink-0 text-blue-ink" aria-hidden />
          {t.common.halfDone}
        </p>
      )}
      <div className="grid gap-3 sm:grid-cols-2">
        <label className="block">
          <span className="label">{t.common.type}</span>
          <select
            ref={firstField}
            className="input mt-1 w-full"
            value={form.type}
            disabled={busy}
            onChange={event => field({ type: event.target.value })}
          >
            {AGENDA_TYPES[kind].map(type => (
              <option key={type} value={type}>
                {typeLabel(type)}
              </option>
            ))}
          </select>
        </label>
        {kind === 'vehicle' && (
          <label className="block">
            <span className="label">{t.agenda.workshopField}</span>
            <input
              className="input mt-1 w-full"
              value={form.provider}
              maxLength={MAX_PROVIDER}
              disabled={busy}
              onChange={event => field({ provider: event.target.value })}
            />
          </label>
        )}
      </div>
      {hint && (
        <p className="flex items-start gap-2 rounded-md bg-sunken px-3 py-2 text-meta text-ink-2">
          <Info className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
          {hint}
        </p>
      )}
      <label className="flex w-fit items-center gap-2 text-meta font-medium text-ink">
        <input
          type="checkbox"
          className="h-4 w-4"
          checked={form.wholeDays}
          disabled={busy}
          onChange={event => field({ wholeDays: event.target.checked })}
        />
        {t.agenda.wholeDays}
      </label>
      <fieldset className="grid gap-3 sm:grid-cols-2" disabled={busy} aria-describedby={period.ok ? undefined : errorId}>
        <legend className="sr-only">{t.common.period}</legend>
        <Moment
          label={form.wholeDays ? t.agenda.firstDay : t.agenda.starts}
          date={form.startDate}
          time={form.wholeDays ? null : form.startTime}
          invalid={!period.ok}
          onDate={date => onForm(withStartDate(form, date))}
          onTime={startTime => field({ startTime })}
        />
        <Moment
          label={form.wholeDays ? t.agenda.lastDay : t.agenda.ends}
          date={form.endDate}
          time={form.wholeDays ? null : form.endTime}
          invalid={!period.ok}
          onDate={endDate => field({ endDate })}
          onTime={endTime => field({ endTime })}
        />
      </fieldset>
      {!period.ok && (
        <p id={errorId} className="text-meta font-medium text-critical-ink">
          {period.error}
        </p>
      )}
      <label className="block">
        <span className="label flex justify-between">
          <span>{t.agenda.notesField}</span>
          <span aria-hidden>
            {form.notes.length}/{MAX_NOTES}
          </span>
        </span>
        <textarea
          className="input mt-1 h-auto w-full py-2"
          rows={2}
          value={form.notes}
          maxLength={MAX_NOTES}
          disabled={busy}
          onChange={event => field({ notes: event.target.value })}
        />
      </label>
      {period.ok && period.end <= now && (
        <p className="flex items-start gap-2 rounded-md bg-warning-soft px-3 py-2 text-meta font-medium text-warning-ink">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
          {t.agenda.willBeHistory}
        </p>
      )}
      {period.ok && (
        <div aria-live="polite">
          {!affected ? (
            <SkeletonLines lines={1} label={t.agenda.checkingAssigned} />
          ) : !affected.ok ? (
            <ErrorState title={t.agenda.checkFailed} onRetry={retryAffected}>
              {t.common.saveAnyway(affected.error)}
            </ErrorState>
          ) : (
            <Preview caught={caught!} master={master} onOpenTrip={onOpenTrip} />
          )}
        </div>
      )}
      {error && <SaveError error={error} busy={busy} onRetry={save} />}
      <div className="flex flex-wrap justify-end gap-2">
        <button type="button" className="btn" disabled={busy} onClick={onCancel}>
          {t.common.cancel}
        </button>
        <button type="submit" className="btn btn-primary" disabled={!period.ok || !affected || busy}>
          {busy ? t.common.saving : saveLabel}
        </button>
      </div>
    </form>
  );
}

function Moment({
  label,
  date,
  time,
  invalid,
  onDate,
  onTime,
}: {
  label: string;
  date: string;
  /** Null: only the day counts. */
  time: string | null;
  invalid: boolean;
  onDate: (date: string) => void;
  onTime: (time: string) => void;
}) {
  return (
    <div>
      <span className="label">{label}</span>
      <div className="mt-1 flex gap-2">
        <input
          type="date"
          className="input min-w-0 flex-1"
          aria-label={time == null ? label : t.agenda.dateOf(label)}
          aria-invalid={invalid || undefined}
          value={date}
          onChange={event => onDate(event.target.value)}
        />
        {time != null && (
          <input
            type="time"
            className="input w-28"
            aria-label={t.agenda.timeOf(label)}
            aria-invalid={invalid || undefined}
            value={time}
            onChange={event => onTime(event.target.value)}
          />
        )}
      </div>
    </div>
  );
}

function Preview({
  caught,
  master,
  onOpenTrip,
}: {
  caught: ReturnType<typeof splitByFinished<AffectedTrip>>;
  master: MasterData;
  onOpenTrip: (bookingId: number, tripId: number) => void;
}) {
  const { open, finished } = caught;
  const already = finished.length > 0 && (
    <p className="text-xs text-ink-2">
      {finished.length === 1 ? t.agenda.doneOne : t.agenda.doneMany(finished.length)}
    </p>
  );
  if (open.length === 0)
    return (
      <div className="space-y-1">
        <p className="flex items-center gap-2 rounded-md bg-success-soft px-3 py-2 text-meta font-medium text-success-ink">
          <ShieldOff className="h-4 w-4 shrink-0" aria-hidden />
          {t.agenda.noneHit}
        </p>
        {already}
      </div>
    );
  return (
    <div className="space-y-1">
      <AffectedList
        title={t.agenda.hits(open.length)}
        trips={open}
        master={master}
        onOpenTrip={onOpenTrip}
      />
      <p className="text-xs text-ink-2">
        {t.agenda.saveAnywayOpen(open.length)}
      </p>
      {already}
    </div>
  );
}
