import { describe, expect, it } from 'vitest';
import { BACKDROP, navPlacement } from './menu';

const classes = (value: string) => value.split(/\s+/);

describe('the side menu on a narrow screen (SPEC 0096 R7)', () => {
  it('closed, it takes no room below lg', () => {
    expect(classes(navPlacement(false))).toContain('hidden');
    expect(classes(navPlacement(false))).not.toContain('fixed');
  });

  it('open, it floats over the page instead of pushing it', () => {
    expect(classes(navPlacement(true))).toEqual(expect.arrayContaining(['fixed', 'flex', 'z-40']));
    expect(classes(navPlacement(true))).not.toContain('hidden');
  });

  it.each([true, false])('from lg it is the fixed column whatever the state (open: %s)', open => {
    expect(classes(navPlacement(open))).toEqual(expect.arrayContaining(['lg:sticky', 'lg:flex', 'lg:top-0']));
  });

  it('the shade behind it is under it and never there from lg', () => {
    expect(classes(BACKDROP)).toEqual(expect.arrayContaining(['z-30', 'lg:hidden']));
  });
});
