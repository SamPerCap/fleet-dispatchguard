import { createElement } from 'react';
import { renderToString } from 'react-dom/server';
import { afterEach, describe, expect, it } from 'vitest';
import { setLanguage } from '../language';
import { AboutPage } from './AboutPage';
import { AUTHOR } from './author';
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

describe('the About says how AI was used, without guides or commit counts (SPEC 0100 R1–R2)', () => {
  afterEach(() => setLanguage('es'));

  it.each(['en', 'es'] as const)('in %s: no hypothetical case, no guides, no co-authorship count', async chosen => {
    const page = await paintIn(chosen);

    expect(page).not.toMatch(/hypothetical|hipotético|NIST|OpenSSF|Co-Authored-By/);
    expect(page).toContain('Clean Architecture');
  });

  it.each(['en', 'es'] as const)('in %s every technology is a list item', async chosen => {
    const page = await paintIn(chosen);

    for (const group of showcaseText().about.architecture.technologies)
      for (const item of group.items) expect(page).toContain(`<li>${item.replace(/&/g, '&amp;')}</li>`);
  });
});

describe('the code once the public repository exists (SPEC 0099)', () => {
  it.each(['en', 'es'] as const)('in %s it links the repository and no longer announces it', async chosen => {
    const page = await paintIn(chosen);

    expect(REPOSITORY_URL).toMatch(/^https:\/\/github\.com\//);
    expect(linksOf(page)).toContain(REPOSITORY_URL);
    expect(page).not.toContain(showcaseText().about.code.pending);
  });
});

describe("the author's role and the AI's (SPEC 0104 R5)", () => {
  afterEach(() => setLanguage('es'));

  it.each(['en', 'es'] as const)('in %s: four decisions of the author, what the AI did and the limits', async chosen => {
    const page = await paintIn(chosen);
    const { role } = showcaseText().about;

    expect(role.decisions).toHaveLength(4);
    for (const decision of role.decisions) expect(page).toContain(decision.title);
    expect(page).toContain(role.assistant.title);
    expect(page).toContain(role.limits.title);
  });
});
