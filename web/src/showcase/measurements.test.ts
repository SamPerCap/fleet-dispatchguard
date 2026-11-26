/// <reference types="vite/client" />
import { afterEach, describe, expect, it } from 'vitest';
import { setLanguage } from '../language';
import { coldStartWait, EVIDENCE } from './measurements';
import { sourceUrl } from './repository';

/** The sources a figure points to, from the root of the repository. */
const repository: Record<string, string> = Object.fromEntries(
  Object.entries(
    import.meta.glob<string>(['../../../Tests/**/*.cs', '../../../tools/Benchmarks/*.cs'], { query: '?raw', import: 'default', eager: true }),
  ).map(([path, text]) => [path.replace(/^(\.\.\/){3}/, ''), text]),
);

describe('every figure of the cover points to what reproduces it (SPEC 0096 R2)', () => {
  it.each(EVIDENCE)('$id: $file has $member', ({ file, member }) => {
    expect(Object.keys(repository)).toContain(file);
    expect(repository[file]).toMatch(new RegExp(`\\b${member}\\(`));
  });

  it.each(EVIDENCE)('$id: the command reaches it', ({ command, file, member }) => {
    const filter = command.match(/~(\w+)/)?.[1];
    const project = command.match(/--project (\S+)/)?.[1];
    if (filter) expect(file.includes(filter) || member === filter).toBe(true);
    else expect(file.startsWith(`${project}/`)).toBe(true);
  });
});

describe('the code is linked only when there is a repository (SPEC 0096 R5)', () => {
  it('while there is none, there is no address at all', () => {
    expect(sourceUrl('Tests/a.cs', '')).toBeNull();
  });

  it('once there is, a file is read on the main branch', () => {
    expect(sourceUrl('Tests/a.cs', 'https://example.org/code/')).toBe('https://example.org/code/blob/master/Tests/a.cs');
  });
});

describe('the wait of a cold start says the measurement and its date (SPEC 0096 R8)', () => {
  afterEach(() => setLanguage('es'));

  it('in English', async () => {
    await setLanguage('en');
    expect(coldStartWait()).toEqual({ wait: '6.5–6.7 s', measuredOn: '25 Nov 2026' });
  });

  it('in Spanish', async () => {
    await setLanguage('es');
    expect(coldStartWait().wait).toBe('6,5–6,7 s');
    expect(coldStartWait().measuredOn).toMatch(/^25 nov\.? 2026$/);
  });
});
