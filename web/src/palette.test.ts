import { describe, expect, it } from 'vitest';
import { bookingWindow, kindsIn, normalize, searchPalette, type PaletteItem } from './palette';

const items: PaletteItem[] = [
  { kind: 'view', target: 'control', label: 'Centro de control', keywords: 'inicio portada' },
  { kind: 'view', target: 'planning', label: 'Planificación' },
  { kind: 'view', target: 'drivers', label: 'Conductores' },
  { kind: 'booking', target: 7, label: 'Colegio San José', detail: '#7 · Excursión a Ávila' },
  { kind: 'driver', target: 1, label: 'Ana García', detail: '12345678Z' },
  { kind: 'driver', target: 2, label: 'Juan Anaya', detail: '87654321X' },
  { kind: 'driver', target: 3, label: 'Mariana Ruiz', detail: '11111111H' },
  { kind: 'vehicle', target: 9, label: '1234-ABC', detail: 'Irizar i6 · 55 plazas' },
];
const labels = (query: string, per?: number) => searchPalette(items, query, per).map(i => i.label);

describe('normalize', () => {
  it('normalize_RemovesAccentsCaseAndSpaces', () => {
    expect(normalize('  Planificación   ÁVILA ')).toBe('planificacion avila');
  });
});

describe('searchPalette', () => {
  it('searchPalette_NoQuery_OnlyViews', () => {
    expect(labels('')).toEqual(['Centro de control', 'Planificación', 'Conductores']);
    expect(labels('   ')).toHaveLength(3);
  });

  it('searchPalette_IgnoresAccentsAndCase', () => {
    expect(labels('PLANIFICACION')).toEqual(['Planificación']);
    expect(labels('avila')).toEqual(['Colegio San José']);
  });

  it('searchPalette_EveryWordMustAppear', () => {
    expect(labels('ana gar')).toEqual(['Ana García']);
    expect(labels('ana zzz')).toEqual([]);
  });

  it('searchPalette_LabelPrefixBeforeContained', () => {
    // "ana": Ana García (starts with it) → Juan Anaya (a word starts with it) → Mariana Ruiz (only contains it)
    expect(labels('ana')).toEqual(['Ana García', 'Juan Anaya', 'Mariana Ruiz']);
  });

  it('searchPalette_SearchesDetailAndKeywords', () => {
    expect(labels('portada')).toEqual(['Centro de control']);
    expect(labels('irizar')).toEqual(['1234-ABC']);
    expect(labels('#7')).toEqual(['Colegio San José']);
  });

  it('searchPalette_EqualMatch_FixedKindOrder', () => {
    // "c": views and booking start with it (tie → views first); driver and vehicle only contain it (tie → driver first)
    expect(kindsIn(searchPalette(items, 'c'))).toEqual(['view', 'booking', 'driver', 'vehicle']);
  });

  it('searchPalette_GroupWithBestMatch_GoesFirst', () => {
    const extra: PaletteItem[] = [...items, { kind: 'booking', target: 8, label: 'Hotel Sol', detail: '#8 · Llegada mañana' }];
    // "ana": the driver that starts with "Ana" goes before the booking that only says "mañana" in its detail
    expect(kindsIn(searchPalette(extra, 'ana'))).toEqual(['driver', 'booking']);
  });

  it('searchPalette_CapPerKind', () => {
    expect(labels('a', 1).length).toBeLessThanOrEqual(4); // at most one of each of the 4 kinds
    expect(searchPalette(items, 'a', 1).filter(i => i.kind === 'driver')).toHaveLength(1);
  });
});

describe('bookingWindow', () => {
  it('bookingWindow_31DaysFromFifteenDaysAgo', () => {
    const { from, to } = bookingWindow(new Date(2026, 10, 22, 15, 30));
    expect(from).toEqual(new Date(2026, 10, 7));
    expect(to).toEqual(new Date(2026, 11, 8));
    expect(Math.round((to.getTime() - from.getTime()) / 86_400_000)).toBe(31);
  });
});
