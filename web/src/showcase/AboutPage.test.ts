import { createElement } from 'react';
import { renderToString } from 'react-dom/server';
import { afterEach, describe, expect, it } from 'vitest';
import { setLanguage } from '../language';
import { AboutPage } from './AboutPage';
import { AUTHOR, GUIDES } from './author';
import { REPOSITORY_URL } from './repository';
import { showcaseText } from './showcaseText';

async function paintIn(chosen: 'en' | 'es'): Promise<string> {
  await setLanguage(chosen);
  return renderToString(createElement(AboutPage));
}

const linksOf = (html: string) => [...html.matchAll(/href="([^"]+)"/g)].map(match => match[1]);

describe('About: only what the author wrote or approved (SPEC 0096 R4)', () => {
  afterEach(() => setLanguage('es'));

  it.each(['en', 'es'] as const)('in %s: the name and the two profiles, and no e-mail', async chosen => {
    const page = await paintIn(chosen);

    expect(page).toContain(AUTHOR.name);
    expect(linksOf(page)).toEqual(expect.arrayContaining(AUTHOR.links.map(link => link.url)));
    expect(page).not.toMatch(/mailto:|[\w.]+@[\w-]+\.\w+/);
  });

  it('the headline is not invented while the author has not given it', () => {
    expect(AUTHOR.headline).toBeNull();
  });

  it.each(['en', 'es'] as const)('in %s nothing denies or hides where the idea comes from', async chosen => {
    expect((await paintIn(chosen)).toLowerCase()).not.toMatch(/from scratch|desde cero|independent creation|creación independiente/);
  });
});

describe('the guides are followed, never claimed (SPEC 0096 R6)', () => {
  afterEach(() => setLanguage('es'));

  it('each one links to its official source', () => {
    const official = ['csrc.nist.gov', 'www.nist.gov', 'best.openssf.org'];
    for (const guide of GUIDES) expect(official).toContain(new URL(guide.url).host);
  });

  it.each(['en', 'es'] as const)('in %s no compliance or certification is claimed', async chosen => {
    const page = (await paintIn(chosen)).toLowerCase();

    expect(page).not.toMatch(/compliant with|in compliance with|conforme a|de conformidad|certified by|certificado por/);
    for (const guide of GUIDES) expect(page).toContain(showcaseText().about.ai.follows[guide.id].toLowerCase().replace(/&/g, '&amp;'));
  });

  it('the co-authorship says how it was counted', async () => {
    const page = await paintIn('en');

    expect(page).toContain('Co-Authored-By');
    expect(page).toContain('git log');
  });
});

describe('the code before there is a public repository (SPEC 0096 R5)', () => {
  it.each(['en', 'es'] as const)('in %s it is announced, with no link', async chosen => {
    expect(REPOSITORY_URL).toBe('');
    const page = await paintIn(chosen);

    expect(page).toContain(showcaseText().about.code.pending);
    expect(linksOf(page).every(link => link !== '' && !link.includes('/blob/'))).toBe(true);
  });
});
