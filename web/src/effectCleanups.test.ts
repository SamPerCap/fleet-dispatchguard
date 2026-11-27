/// <reference types="vite/client" />
import { describe, expect, it } from 'vitest';

const sources = import.meta.glob<string>(['./**/*.{ts,tsx}', '!./**/*.test.ts'], { query: '?raw', import: 'default', eager: true });
const EXPRESSION_EFFECT = /use(Layout)?Effect\(\(\) => (?!\{|\(\) =>)/;

describe('effects return nothing but a cleanup (SPEC 0100)', () => {
  // `useEffect(() => scrollTo(...))` returned the Promise of scrollTo, and React called it on unmount: blank page on Back.
  it('no effect has an expression body other than a cleanup arrow', () => {
    const offenders = Object.entries(sources).flatMap(([file, text]) =>
      text
        .split('\n')
        .flatMap((line, index) => (EXPRESSION_EFFECT.test(line) ? [`${file}:${index + 1}`] : [])),
    );

    expect(offenders).toEqual([]);
  });
});
