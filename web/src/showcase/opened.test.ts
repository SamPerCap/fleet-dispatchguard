import { beforeEach, describe, expect, it, vi } from 'vitest';
import { createOpenedNotice } from './opened';

function memory() {
  const stored = new Map<string, string>();
  return { read: (key: string) => stored.get(key) ?? null, write: (key: string, value: string) => void stored.set(key, value) };
}

describe('telling the author that the site was opened (SPEC 0103)', () => {
  let storage: ReturnType<typeof memory>;
  let send: ReturnType<typeof vi.fn<(body: string) => void>>;

  beforeEach(() => {
    storage = memory();
    send = vi.fn<(body: string) => void>();
  });

  it.each([
    ['cover', 'Cover'],
    ['engineering', 'Engineering'],
    ['about', 'About'],
    ['application', 'Application'],
  ] as const)('R1: opening %s sends that page once', (place, page) => {
    const opened = createOpenedNotice({ send, storage });

    opened.announce(place);
    opened.announce('about');

    expect(send).toHaveBeenCalledTimes(1);
    expect(JSON.parse(send.mock.calls[0][0])).toEqual({ Page: page });
  });

  it("R5: the author's browser sends nothing", () => {
    const opened = createOpenedNotice({ send, storage });

    opened.markAuthor();
    opened.announce('cover');

    expect(send).not.toHaveBeenCalled();
  });

  it('a failure to send is never thrown at the page', () => {
    const opened = createOpenedNotice({
      send: () => {
        throw new Error('offline');
      },
      storage,
    });

    expect(() => opened.announce('cover')).not.toThrow();
  });
});
