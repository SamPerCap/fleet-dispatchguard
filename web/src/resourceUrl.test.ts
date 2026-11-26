import { describe, expect, it } from 'vitest';
import { readResource, sameResource, writeResource } from './resourceUrl';

describe('readResource', () => {
  it('reads driver or vehicle with its tab', () => {
    expect(readResource('?conductor=12')).toEqual({ kind: 'driver', id: 12, tab: 'resumen' });
    expect(readResource('?vehiculo=7&pestana=historial')).toEqual({ kind: 'vehicle', id: 7, tab: 'historial' });
  });
  it('no card, invalid id or zero → none', () => {
    for (const search of [
      '',
      '?',
      '?conductor=',
      '?conductor=abc',
      '?conductor=0',
      '?conductor=-3',
      '?conductor=1.5',
      '?vehiculo=1e3',
      '?otra=1',
    ])
      expect(readResource(search)).toBeNull();
  });
  it('unknown tab → resumen', () => {
    expect(readResource('?conductor=3&pestana=mapa')?.tab).toBe('resumen');
  });
  it('links to the former name of the agenda still open it', () => {
    expect(readResource('?vehiculo=7&pestana=indisponibilidades')).toEqual({ kind: 'vehicle', id: 7, tab: 'agenda' });
  });
  it('with both, the driver wins', () => {
    expect(readResource('?vehiculo=7&conductor=12')?.kind).toBe('driver');
  });
});

describe('writeResource', () => {
  it('writes the card and leaves out the resumen tab', () => {
    expect(writeResource('', { kind: 'driver', id: 12, tab: 'resumen' })).toBe('?conductor=12');
    expect(writeResource('', { kind: 'vehicle', id: 7, tab: 'auditoria' })).toBe('?vehiculo=7&pestana=auditoria');
  });
  it('keeps the other parameters and replaces the previous card', () => {
    expect(writeResource('?debug=1&conductor=3&pestana=historial', { kind: 'vehicle', id: 9, tab: 'resumen' })).toBe('?debug=1&vehiculo=9');
  });
  it('removing it leaves a clean query string', () => {
    expect(writeResource('?conductor=3&pestana=historial', null)).toBe('');
    expect(writeResource('?debug=1&vehiculo=2', null)).toBe('?debug=1');
  });
  it('round trip', () => {
    const ref = { kind: 'vehicle' as const, id: 41, tab: 'agenda' as const };
    expect(readResource(writeResource('', ref))).toEqual(ref);
  });
});

describe('sameResource', () => {
  it('compares kind and id, not the tab', () => {
    expect(sameResource({ kind: 'driver', id: 1, tab: 'resumen' }, { kind: 'driver', id: 1, tab: 'historial' })).toBe(true);
    expect(sameResource({ kind: 'driver', id: 1, tab: 'resumen' }, { kind: 'vehicle', id: 1, tab: 'resumen' })).toBe(false);
    expect(sameResource(null, { kind: 'driver', id: 1, tab: 'resumen' })).toBe(false);
  });
});
