import { describe, expect, it } from 'vitest';
import { adrName, outcomeLabel } from './explanation';

describe('outcomeLabel', () => {
  it('labels every API outcome', () => {
    expect(outcomeLabel('Passed').text).toBe('cumple');
    expect(outcomeLabel('Failed').icon).toBe('✗');
    expect(outcomeLabel('NotEvaluated').text).toBe('no evaluado');
  });

  it('does not break with an unknown value', () => {
    expect(outcomeLabel('Nuevo')).toEqual({ icon: '?', text: 'Nuevo' });
  });
});

describe('adrName', () => {
  it('shortens the ADR path', () => {
    expect(adrName('docs/adr/0007-ranking-de-candidatos.md')).toBe('ADR 0007');
    expect(adrName('otra-cosa')).toBe('otra-cosa');
  });
});
