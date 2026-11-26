import { describe, expect, it } from 'vitest';
import { durationText, entryName, fieldChanges, groupByThread, kindText, resultText, toneOf } from './audit';
import type { TimelineEntry } from './types';

const entry = (over: Partial<TimelineEntry> = {}): TimelineEntry => ({
  At: '2026-11-25T10:00:00',
  Kind: 'Screen',
  Name: 'Reservas',
  TraceId: 'a',
  Status: null,
  DurationMs: null,
  RequestId: null,
  EntityId: null,
  Changes: null,
  DeclaredName: null,
  ...over,
});

describe('timeline', () => {
  it('names what changed', () => {
    expect(entryName(entry({ Kind: 'Changed', Name: 'Vehicle', EntityId: 12 }))).toBe('Vehículo #12');
    expect(entryName(entry({ Kind: 'Reset', Name: 'Demo' }))).toBe('Datos de la demo');
    expect(entryName(entry())).toBe('Reservas');
  });

  it('shows an unknown kind as it comes', () => expect(kindText('New')).toBe('New'));

  it.each([
    [entry({ Kind: 'Error' }), 'wrong'],
    [entry({ Kind: 'Request', Status: 422 }), 'wrong'],
    [entry({ Kind: 'RequestFailed', Status: 0 }), 'wrong'],
    [entry({ Kind: 'Request', Status: 200 }), 'plain'],
    [entry({ Kind: 'Created' }), 'change'],
  ])('tone %#', (shown, tone) => expect(toneOf(shown)).toBe(tone));

  it('says the result and how long it took', () => {
    expect(resultText(entry({ Status: 200, DurationMs: 35 }))).toBe('200 · 35 ms');
    expect(resultText(entry({ Status: 0 }))).toBe('sin respuesta');
    expect(resultText(entry())).toBe('');
  });

  it('reads the previous and the new value', () => {
    expect(fieldChanges('{"SeatingCapacity":{"Old":55,"New":38},"Notes":{"New":"•••"}}')).toEqual([
      { field: 'SeatingCapacity', from: '55', to: '38' },
      { field: 'Notes', from: '—', to: '•••' },
    ]);
    expect(fieldChanges(null)).toEqual([]);
    expect(fieldChanges('not json')).toEqual([]);
  });

  it('puts together an action and what it caused', () => {
    const groups = groupByThread([entry(), entry(), entry({ TraceId: 'b' }), entry({ TraceId: null }), entry({ TraceId: null })]);
    expect(groups.map(group => group.length)).toEqual([2, 1, 1, 1]);
  });

  it('measures the visit', () => {
    expect(durationText('2026-11-25T10:00:00', '2026-11-25T10:00:20')).toBe('menos de 1 min');
    expect(durationText('2026-11-25T10:00:00', '2026-11-25T10:12:00')).toBe('12 min');
  });
});
