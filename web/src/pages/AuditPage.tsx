import { ArrowLeft, Fingerprint, KeyRound, ListChecks, RefreshCw } from 'lucide-react';
import { useEffect, useState } from 'react';
import { audit } from '../api';
import { durationText, entryName, fieldChanges, groupByThread, kindText, resultText, shortSession, toneOf, type EntryTone } from '../audit';
import { formatDateTime, parseApiDate, TIME_ZONE } from '../gantt';
import type { RequestTraceDetail, TimelineEntry, VisitSummary, VisitTimeline } from '../types';
import { EmptyState, ErrorState, PageHeader, SkeletonLines } from '../ui';
import { useLoad } from '../useLoad';
import { openedNotice } from '../showcase/opened';

const TONE_CLASS: Record<EntryTone, string> = {
  wrong: 'bg-critical-soft text-critical-ink',
  change: 'bg-warning-soft text-warning-ink',
  plain: 'bg-muted text-ink-2',
};

const exactTime = (at: string) =>
  parseApiDate(at).toLocaleTimeString('es-ES', { hour: '2-digit', minute: '2-digit', second: '2-digit', timeZone: TIME_ZONE });

/**
 * Only for the author: no menu leads here, and the API answers 404 to whoever does not have the key, which lives only in this page
 * while it is open (SPEC 0085 R7).
 */
export function AuditPage() {
  const [key, setKey] = useState<string | null>(null);
  return <div className="page">{key === null ? <KeyForm onKey={setKey} /> : <Visits auditKey={key} onLeave={() => setKey(null)} />}</div>;
}

function Visits({ auditKey, onLeave }: { auditKey: string; onLeave: () => void }) {
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [list, retryList] = useLoad(() => audit.visits(auditKey), [auditKey]);
  const [timeline, retryTimeline] = useLoad(sessionId === null ? null : () => audit.visit(sessionId, auditKey), [sessionId, auditKey]);
  const showsList = sessionId === null;

  // The key was accepted: from now on this browser is the author's, and opening the site is not a notice (SPEC 0103 R5).
  useEffect(() => {
    if (list?.ok) openedNotice.markAuthor();
  }, [list]);

  return (
    <>
      <PageHeader title="Auditoría" icon={Fingerprint} subtitle="Todas las visitas de los últimos 30 días">
        <button type="button" className="btn" onClick={showsList ? onLeave : () => setSessionId(null)}>
          <ArrowLeft className="h-4 w-4" aria-hidden />
          {showsList ? 'Salir' : 'Volver a la lista'}
        </button>
        <button type="button" className="btn" onClick={showsList ? retryList : retryTimeline}>
          <RefreshCw className="h-4 w-4" aria-hidden />
          Actualizar
        </button>
      </PageHeader>
      {showsList ? (
        <>
          {!list && <SkeletonLines lines={6} label="Cargando las visitas" />}
          {list && !list.ok && (
            <ErrorState title="No se pudo abrir: revisa la clave" onRetry={onLeave}>
              {list.error}
            </ErrorState>
          )}
          {list?.ok && <VisitList visits={list.value} onOpen={setSessionId} />}
        </>
      ) : (
        <>
          {!timeline && <SkeletonLines lines={8} label="Cargando la visita" />}
          {timeline && !timeline.ok && <ErrorState onRetry={retryTimeline}>{timeline.error}</ErrorState>}
          {timeline?.ok && <Timeline timeline={timeline.value} auditKey={auditKey} />}
        </>
      )}
    </>
  );
}

function VisitList({ visits, onOpen }: { visits: VisitSummary[]; onOpen: (sessionId: string) => void }) {
  if (visits.length === 0) return <EmptyState icon={ListChecks} title="Todavía no ha entrado nadie" />;
  return (
    <div className="card overflow-x-auto p-4">
      <table className="w-full min-w-[640px] text-left text-data">
        <caption className="sr-only">Visitas, de la más reciente a la más antigua. Cada una abre su línea de tiempo.</caption>
        <thead>
          <tr className="text-xs text-ink-3">
            {['Entrada', 'Candidatura', 'Sesión', 'Duración', 'Pasos', 'Cambios'].map(title => (
              <th key={title} scope="col" className="border-b border-line px-2 pb-2 font-medium">
                {title}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {visits.map(visit => (
            <tr key={visit.SessionId}>
              <td className="px-2 py-2">
                <button type="button" className="btn btn-link" onClick={() => onOpen(visit.SessionId)}>
                  {formatDateTime(parseApiDate(visit.StartedAt))}
                </button>
              </td>
              <td className="px-2 py-2">{visit.Reference ?? '—'}</td>
              <td className="px-2 py-2 font-mono text-xs">{shortSession(visit.SessionId)}</td>
              <td className="px-2 py-2">{durationText(visit.StartedAt, visit.LastAt)}</td>
              <td className="px-2 py-2">{visit.Events}</td>
              <td className="px-2 py-2">{visit.Changes}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function Timeline({ timeline, auditKey }: { timeline: VisitTimeline; auditKey: string }) {
  if (timeline.Entries.length === 0) {
    return <EmptyState icon={ListChecks} title="No hay nada registrado de esta visita" />;
  }

  return (
    <section aria-label="Línea de tiempo de la visita" className="card p-4">
      <p className="mb-3 text-meta text-ink-2">
        Sesión <span className="font-mono">{shortSession(timeline.SessionId)}</span>
        {timeline.StartedAt && ` · desde ${formatDateTime(parseApiDate(timeline.StartedAt))}`}
        {timeline.Reference && ` · candidatura ${timeline.Reference}`}
      </p>
      <ol className="space-y-2">
        {groupByThread(timeline.Entries).map((group, index) => (
          <li key={index} className="rounded-md border border-line px-3 py-2">
            <ol className="space-y-1">
              {group.map((entry, position) => (
                <Entry key={position} entry={entry} auditKey={auditKey} />
              ))}
            </ol>
          </li>
        ))}
      </ol>
    </section>
  );
}

function Entry({ entry, auditKey }: { entry: TimelineEntry; auditKey: string }) {
  const [open, setOpen] = useState(false);
  const changes = fieldChanges(entry.Changes);
  const { RequestId: requestId } = entry;
  const hasDetail = requestId !== null || changes.length > 0;
  const result = resultText(entry);
  return (
    <li className="text-data">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
        <time className="w-20 shrink-0 font-mono text-xs text-ink-3" dateTime={entry.At}>
          {exactTime(entry.At)}
        </time>
        <span className={`rounded-full px-2 py-0.5 text-xs font-semibold ${TONE_CLASS[toneOf(entry)]}`}>{kindText(entry.Kind)}</span>
        <span className="min-w-0 break-words text-ink">{entryName(entry)}</span>
        {result && <span className="text-meta text-ink-2">{result}</span>}
        {entry.DeclaredName && <span className="text-meta text-ink-2">nombre declarado: {entry.DeclaredName}</span>}
        {hasDetail && (
          <button type="button" className="btn btn-link btn-sm" aria-expanded={open} onClick={() => setOpen(shown => !shown)}>
            {open ? 'Ocultar' : 'Detalle'}
          </button>
        )}
      </div>
      {open && changes.length > 0 && (
        <ul className="mt-1 space-y-0.5 pl-[92px] text-meta text-ink-2">
          {changes.map(change => (
            <li key={change.field} className="break-words">
              <span className="font-mono text-xs">{change.field}</span>: {change.from} → {change.to}
            </li>
          ))}
        </ul>
      )}
      {open && requestId !== null && <RequestDetail id={requestId} auditKey={auditKey} />}
    </li>
  );
}

function RequestDetail({ id, auditKey }: { id: number; auditKey: string }) {
  const [loaded, retry] = useLoad(() => audit.request(id, auditKey), [id, auditKey]);
  if (!loaded) return <SkeletonLines lines={2} label="Cargando la petición" />;
  if (!loaded.ok) return <ErrorState onRetry={retry}>{loaded.error}</ErrorState>;
  return <RequestFigures request={loaded.value} />;
}

function RequestFigures({ request }: { request: RequestTraceDetail }) {
  return (
    <div className="mt-1 space-y-2 pl-[92px] text-meta text-ink-2">
      <p>
        {request.DurationMs} ms en total · {request.Queries} {request.Queries === 1 ? 'consulta' : 'consultas'} en {request.QueriesMs} ms
      </p>
      {request.Detail.Rules.length > 0 && (
        <ul aria-label="Reglas evaluadas" className="flex flex-wrap gap-1.5">
          {request.Detail.Rules.map(rule => (
            <li
              key={rule.Id}
              className={`rounded-full px-2 py-0.5 text-xs ${rule.Failed > 0 ? 'bg-critical-soft text-critical-ink' : 'bg-success-soft text-success-ink'}`}
            >
              {rule.Id}: {rule.Passed} sí · {rule.Failed} no
            </li>
          ))}
        </ul>
      )}
      <ol aria-label="Consultas a la base de datos, sin sus valores" className="space-y-1">
        {request.Detail.Queries.map((query, index) => (
          <li key={index} className="break-words rounded bg-sunken px-2 py-1 font-mono text-xs">
            {query.Times > 1 && `${query.Times} × `}
            {query.Milliseconds} ms · {query.Sql}
          </li>
        ))}
      </ol>
    </div>
  );
}

function KeyForm({ onKey }: { onKey: (key: string) => void }) {
  const [text, setText] = useState('');
  return (
    <form
      className="flex flex-wrap items-end gap-2"
      onSubmit={event => {
        event.preventDefault();
        if (text.trim()) onKey(text.trim());
      }}
    >
      <label className="text-meta text-ink-2">
        <span className="mb-1 block">Clave</span>
        <input className="input w-72" type="password" autoComplete="off" value={text} onChange={event => setText(event.target.value)} />
      </label>
      <button type="submit" className="btn" disabled={!text.trim()}>
        <KeyRound className="h-4 w-4" aria-hidden />
        Entrar
      </button>
    </form>
  );
}
