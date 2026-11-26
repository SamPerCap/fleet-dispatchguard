/// <reference types="vite/client" />
import { describe, expect, it } from 'vitest';
import index from '../../index.html?raw';

const published = import.meta.glob('../../public/*', { query: '?url', import: 'default' });
const metaOf = (name: string) => index.match(new RegExp(`(?:property|name)="${name}" content="([^"]+)"`))?.[1] ?? '';

describe('preview of the link (SPEC 0093 R7)', () => {
  it.each(['description', 'og:title', 'og:description', 'og:url', 'og:image', 'og:image:alt', 'twitter:card'])('has %s', name => {
    expect(metaOf(name)).not.toBe('');
  });

  it('the preview says the thesis of the cover (SPEC 0096 R9)', () => {
    expect(metaOf('og:title')).toContain('never saves an illegal assignment');
    expect(metaOf('og:image:alt')).toBe(metaOf('og:title'));
  });

  it('the image is a file of the web, with its whole address', () => {
    const { origin, pathname } = new URL(metaOf('og:image'));
    expect(origin).toBe(new URL(metaOf('og:url')).origin);
    expect(Object.keys(published)).toContain(`../../public${pathname}`);
  });
});
