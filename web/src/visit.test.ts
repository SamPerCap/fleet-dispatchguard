import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createVisit, pathOf, takeReference, type VisitBatch } from './visit';
import { knownLabel, OTHER } from './visitLabels';

const ok = { method: 'GET', url: '/api/drivers', status: 200, durationMs: 12, trace: 'x' };

function newVisit(reference?: string, send?: (batch: VisitBatch) => Promise<unknown>) {
  const sent: VisitBatch[] = [];
  let count = 0;
  const visit = createVisit({
    send:
      send ??
      (async batch => {
        sent.push(batch);
      }),
    randomHex: length => (++count).toString(16).padStart(length, '0'),
    reference,
  });
  return { visit, sent };
}

const kinds = (batch: VisitBatch) => batch.Events.map(event => `${event.Kind} ${event.Name}`);

describe('timeline of the visit', () => {
  beforeEach(() => void vi.useFakeTimers());
  afterEach(() => void vi.useRealTimers());

  it('a page read before there was a visit keeps its own moment', () => {
    const { visit, sent } = newVisit();
    const read = Date.now();
    vi.advanceTimersByTime(90_000);
    visit.screen('Portada', read);
    visit.screen('Centro de control');
    visit.answered(ok);
    expect(sent[0].Events.map(event => [event.Name, event.AgoMs])).toEqual([
      ['Portada', 90_000],
      ['Centro de control', 0],
    ]);
  });

  it('leaves after the first answer, with the reference only once', () => {
    const { visit, sent } = newVisit('acme');
    visit.screen('Centro de control');
    expect(sent).toHaveLength(0);
    visit.answered(ok);
    expect(sent[0].Reference).toBe('acme');
    expect(kinds(sent[0])).toEqual(['Screen Centro de control']);
    vi.advanceTimersByTime(6_000);
    visit.screen('Reservas');
    visit.answered(ok);
    expect(sent[1].Reference).toBeUndefined();
  });

  it('never sends anything by itself', () => {
    const { visit, sent } = newVisit();
    visit.screen('Reservas');
    vi.advanceTimersByTime(60 * 60_000);
    expect(sent).toHaveLength(0);
    expect(vi.getTimerCount()).toBe(0);
  });

  it('has nothing to say: sends nothing', () => {
    const { visit, sent } = newVisit();
    visit.answered(ok);
    visit.hidden();
    expect(sent).toHaveLength(0);
  });

  it('waits between batches', () => {
    const { visit, sent } = newVisit();
    visit.action('Asignar');
    visit.answered(ok);
    visit.action('Cerrar');
    visit.answered(ok);
    expect(sent).toHaveLength(1);
    vi.advanceTimersByTime(5_000);
    visit.answered(ok);
    expect(kinds(sent[1])).toEqual(['Click Cerrar']);
    expect(sent[1].Events[0].AgoMs).toBe(5_000);
  });

  it('a hidden tab sends what is left while the database is awake', () => {
    const { visit, sent } = newVisit();
    visit.answered(ok);
    visit.screen('Reservas');
    visit.hidden();
    expect(sent).toHaveLength(1);
  });

  it('a hidden tab does not wake the database up', () => {
    const { visit, sent } = newVisit();
    visit.answered(ok);
    visit.screen('Reservas');
    vi.advanceTimersByTime(2 * 60_000);
    visit.hidden();
    expect(sent).toHaveLength(0);
  });

  it.each([0, 401, 429])('an answer that is not of the database (%i) sends nothing', status => {
    const { visit, sent } = newVisit();
    visit.answered({ ...ok, status });
    visit.hidden();
    expect(sent).toHaveLength(0);
  });

  it('a batch that is lost breaks nothing', async () => {
    const { visit } = newVisit(undefined, () => Promise.reject(new Error('down')));
    visit.screen('Reservas');
    expect(() => visit.answered(ok)).not.toThrow();
    await vi.advanceTimersByTimeAsync(1);
  });

  it('keeps no more than what fits in a batch', () => {
    const { visit, sent } = newVisit();
    for (let i = 0; i < 80; i++) visit.action('Asignar');
    visit.answered(ok);
    expect(sent[0].Events).toHaveLength(50);
  });

  it('a click and its requests share the thread; a refresh later has its own and says so', () => {
    const { visit, sent } = newVisit();
    visit.action('Asignar');
    const first = visit.thread();
    expect(first.header).toBe(`00-${first.id}-${first.header.split('-')[2]}-01`);
    expect(visit.thread().id).toBe(first.id);
    vi.advanceTimersByTime(6_000);
    const refresh = visit.thread();
    expect(refresh.id).not.toBe(first.id);
    expect(refresh.header.endsWith('-00')).toBe(true);
    visit.answered(ok);
    expect(sent[0].Events[0].TraceId).toBe(first.id);
  });

  it('a form that is closed without saving is abandoned', () => {
    const { visit, sent } = newVisit();
    const close = visit.openForm('Nueva reserva');
    visit.answered({ ...ok, method: 'POST', url: '/api/recommendations/check' });
    vi.advanceTimersByTime(6_000);
    close();
    visit.answered(ok);
    expect(sent.flatMap(kinds)).toEqual(['FormOpened Nueva reserva', 'FormAbandoned Nueva reserva']);
  });

  it('a form that saves is sent, once', () => {
    const { visit, sent } = newVisit();
    const close = visit.openForm('Nueva reserva');
    visit.answered({ ...ok, method: 'POST', url: '/api/bookings', status: 201 });
    visit.answered({ ...ok, method: 'POST', url: '/api/bookings', status: 201 });
    close();
    vi.advanceTimersByTime(6_000);
    visit.answered(ok);
    expect(sent.flatMap(kinds)).toEqual(['FormOpened Nueva reserva', 'FormSent Nueva reserva']);
  });

  it('a failed request is recorded without its query', () => {
    const { visit, sent } = newVisit();
    visit.answered({ method: 'POST', url: '/api/drivers?search=Ana', status: 422, durationMs: 30.4, trace: 'abc' });
    expect(sent[0].Events[0]).toEqual({ AgoMs: 0, Kind: 'RequestFailed', Name: 'POST /api/drivers', TraceId: 'abc', Status: 422, DurationMs: 30 });
  });

  it('an error leaves its type and not its message', () => {
    const { visit, sent } = newVisit();
    visit.error('TypeError');
    visit.error('Ana García no existe');
    visit.answered(ok);
    expect(kinds(sent[0])).toEqual(['Error TypeError', 'Error Error']);
  });
});

describe('what is recorded has no data inside', () => {
  it.each([
    ['Asignar 12 servicios', 'Asignar'],
    ['  Nueva\n reserva ', 'Nueva reserva'],
    ['Ver informe', 'Ver informe'],
    ['Ana García', OTHER],
    ['Asignarme', OTHER],
    ['Asignarían', OTHER],
    ['Asignar a Ana García', 'Asignar'],
    ['', OTHER],
  ])('label %j → %j', (text, label) => expect(knownLabel(text)).toBe(label));

  it.each([
    ['/api/trips/12/allocate', '/api/trips/12/allocate'],
    ['/api/drivers?search=Ana', '/api/drivers'],
    ['/api/audit/visits/0af7651916cd43dd', '/api/audit/visits/:id'],
  ])('path %s → %s', (url, path) => expect(pathOf(url)).toBe(path));
});

describe('link of an application', () => {
  it.each([
    ['https://dispatchguard.test/?ref=acme', 'acme', 'https://dispatchguard.test/'],
    ['https://dispatchguard.test/?ref=acme&driver=3#engine', 'acme', 'https://dispatchguard.test/?driver=3#engine'],
    ['https://dispatchguard.test/?ref=%3Cb%3E', undefined, 'https://dispatchguard.test/'],
    ['https://dispatchguard.test/#engine', undefined, 'https://dispatchguard.test/#engine'],
  ])('%s', (href, reference, clean) => expect(takeReference(href)).toEqual({ reference, clean }));
});
