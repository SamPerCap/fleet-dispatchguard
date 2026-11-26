/// <reference types="vite/client" />
import { createElement } from 'react';
import { renderToString } from 'react-dom/server';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AboutPage } from './AboutPage';
import { Cover } from './Cover';
import { EngineeringPage } from './EngineeringPage';
import { setLanguage } from '../language';

/** Every path starts with `../`, as in `../showcase/Cover.tsx`. */
const sources: Record<string, string> = Object.fromEntries(
  Object.entries(import.meta.glob<string>('../**/*.{ts,tsx}', { query: '?raw', import: 'default', eager: true })).map(([path, text]) => [
    path.replace(/^\.\//, '../showcase/'),
    text,
  ]),
);
const ASKS = /\bfetch\(|XMLHttpRequest|sendBeacon|EventSource|WebSocket/;
/** What is imported on demand, `import('…')`, is not loaded with the page. */
const IMPORT = /^(?:import|export)\s(?!type\s)(?:[^;]*?\sfrom\s)?['"](\.[^'"]+)['"]/gm;

function fileOf(importer: string, specifier: string): string | undefined {
  const inSources = importer.replace('../', '');
  const path = `..${new URL(specifier, `file:///${inSources}`).pathname}`;
  return [path, `${path}.ts`, `${path}.tsx`].find(candidate => candidate in sources);
}

function loadedWith(entry: string): string[] {
  const found = new Set<string>();
  const visit = (file: string) => {
    if (found.has(file)) return;
    found.add(file);
    for (const [, specifier] of sources[file].matchAll(IMPORT)) {
      const imported = fileOf(file, specifier);
      if (imported) visit(imported);
    }
  };
  visit(entry);
  return [...found];
}

/** In both languages. */
async function paint(): Promise<string[]> {
  const painted: string[] = [];
  for (const chosen of ['en', 'es'] as const) {
    await setLanguage(chosen);
    painted.push(renderToString(createElement(Cover)), renderToString(createElement(EngineeringPage)), renderToString(createElement(AboutPage)));
  }
  return painted;
}

describe('the cover and the engineering page ask nothing (SPEC 0093 CA1)', () => {
  afterEach(() => void vi.unstubAllGlobals());

  it('the page starts without the code that asks', () => {
    const loaded = loadedWith('../main.tsx');

    expect(loaded).toEqual(expect.arrayContaining(['../showcase/Cover.tsx', '../showcase/EngineeringPage.tsx', '../showcase/AboutPage.tsx']));
    const asking = ['../api.ts', '../session.ts', '../visit.ts', '../coldStart.ts', '../App.tsx'];
    expect(loaded.filter(file => asking.includes(file))).toEqual([]);
    expect(loaded.filter(file => ASKS.test(sources[file]))).toEqual([]);
  });

  it('the application does have it, so the check above sees what it looks for', () => {
    const loaded = loadedWith('../DispatcherApp.tsx');

    expect(loaded).toEqual(expect.arrayContaining(['../api.ts', '../session.ts', '../visit.ts']));
    expect(loaded.some(file => ASKS.test(sources[file]))).toBe(true);
  });

  it('painting them makes no request', async () => {
    const fetched = vi.fn();
    vi.stubGlobal('fetch', fetched);

    expect((await paint()).every(page => page.includes('<h1'))).toBe(true);
    expect(fetched).not.toHaveBeenCalled();
  });
});

describe('what would help an attacker, or is only for the author, is not in them (SPEC 0093 R6)', () => {
  it.each(['audit', 'ntfy', '?ref', '/api/', 'lof_app', 'neon.tech', 'cron_secret', 'x-vercel', 'sql server', 'localhost'])('«%s»', async secret => {
    expect((await paint()).join(' ').toLowerCase()).not.toContain(secret);
  });
});
