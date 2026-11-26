import { describe, expect, it } from 'vitest';
import { avatarTone, initials } from './people';

describe('initials', () => {
  it('first letters of the first two words, skipping particles', () => {
    expect(initials('Ana Gil Pérez')).toBe('AG');
    expect(initials('María de la Fuente')).toBe('MF');
    expect(initials('  marta ')).toBe('M');
    expect(initials('Óscar Ñúñez')).toBe('ÓÑ');
  });
  it('empty or missing → "?"', () => {
    expect(initials('')).toBe('?');
    expect(initials(null)).toBe('?');
    expect(initials('de la')).toBe('?');
  });
  it('stable tone per key', () => {
    expect(avatarTone('Ana')).toBe(avatarTone('Ana'));
    expect(['blue', 'success', 'warning', 'navy']).toContain(avatarTone(42));
  });
});
