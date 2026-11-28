import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CLOSING_MS, createLoadingScene, MIN_RED_MS, type ClosingMark, type ScenePhase } from './loadingPhases';
import { en } from './text.en';
import { es } from './text.es';

const QUIET = 600;

function memoryMark(seen = false): ClosingMark & { seen: () => boolean } {
  let set = seen;
  return { isSet: () => set, set: () => (set = true), seen: () => set };
}

function start({ reducedMotion = false, mark = memoryMark() } = {}) {
  const phases: { phase: ScenePhase; at: number }[] = [];
  const scene = createLoadingScene({ onPhase: phase => phases.push({ phase, at: Date.now() }), closing: mark, reducedMotion, quietMs: QUIET });
  return { scene, phases, mark, names: () => phases.map(entry => entry.phase) };
}

describe('the loading scene (SPEC 0101)', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(0);
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  const wait = (ms: number) => vi.advanceTimersByTimeAsync(ms);

  describe('the first entry of a tab plays the whole scene', () => {
    it('R11: even with everything ready at once, the light stays red for the minimum, then green and in', async () => {
      const { scene, phases, mark } = start();
      await wait(200);
      scene.application();
      scene.database('ready');
      await wait(MIN_RED_MS + CLOSING_MS);
      expect(phases).toEqual([
        { phase: 'red', at: 0 },
        { phase: 'green', at: MIN_RED_MS },
        { phase: 'in', at: MIN_RED_MS + CLOSING_MS },
      ]);
      expect(mark.seen()).toBe(true);
    });

    it('R2–R4: a cold database longer than the minimum keeps the light red until it answers', async () => {
      const { scene, phases } = start();
      scene.application();
      scene.database('preparing');
      await wait(7_000);
      scene.database('ready');
      await wait(CLOSING_MS);
      expect(phases).toEqual([
        { phase: 'red', at: 0 },
        { phase: 'green', at: 7_000 },
        { phase: 'in', at: 7_000 + CLOSING_MS },
      ]);
    });

    it('R2: a slow download keeps the light red even with the database awake', async () => {
      const { scene, phases } = start();
      scene.database('ready');
      await wait(MIN_RED_MS + 1_000);
      expect(phases.map(entry => entry.phase)).toEqual(['red']);
      scene.application();
      expect(phases.at(-1)).toEqual({ phase: 'green', at: MIN_RED_MS + 1_000 });
    });

    it('R8: a download that fails never says everything loaded', async () => {
      const { scene, names } = start();
      await wait(1_000);
      scene.applicationFailed();
      scene.database('ready');
      await wait(MIN_RED_MS + CLOSING_MS);
      expect(names()).toEqual(['red', 'failed']);
    });

    it('R8: a database that never answers stays unavailable until retried', async () => {
      const { scene, names } = start();
      scene.application();
      await wait(MIN_RED_MS);
      scene.database('late');
      scene.database('preparing');
      scene.database('ready');
      await wait(CLOSING_MS);
      expect(names()).toEqual(['red', 'late', 'red', 'green', 'in']);
    });
  });

  describe('a later entry in the same tab waits only for the real work (R10)', () => {
    it('R5: everything ready before the quiet time goes straight in', async () => {
      const { scene, names } = start({ mark: memoryMark(true) });
      await wait(200);
      scene.application();
      scene.database('ready');
      await wait(5_000);
      expect(names()).toEqual(['in']);
    });

    it('a real wait shows the red light and goes in as soon as it ends', async () => {
      const { scene, phases } = start({ mark: memoryMark(true) });
      scene.application();
      await wait(3_000);
      scene.database('ready');
      expect(phases).toEqual([
        { phase: 'red', at: QUIET },
        { phase: 'in', at: 3_000 },
      ]);
    });
  });

  it('R6: with reduced motion nothing is added, not even on the first entry', async () => {
    const { scene, phases, mark } = start({ reducedMotion: true });
    scene.application();
    await wait(3_000);
    scene.database('ready');
    expect(phases).toEqual([
      { phase: 'red', at: QUIET },
      { phase: 'in', at: 3_000 },
    ]);
    expect(mark.seen()).toBe(false);
  });

  it('stopped, it says nothing more after the red light it started with', async () => {
    const { scene, names } = start();
    scene.stop();
    await wait(MIN_RED_MS);
    scene.application();
    scene.database('ready');
    expect(names()).toEqual(['red']);
  });
});

describe('the messages of the scene (SPEC 0101 R3)', () => {
  it.each([
    ['en', en],
    ['es', es],
  ] as const)('in %s they rotate and claim no progress', (_, { scene }) => {
    expect(scene.messages.length).toBeGreaterThanOrEqual(4);
    for (const message of scene.messages) expect(message).not.toMatch(/\d|%|✓|✔/);
    expect(scene.allLoaded).not.toBe('');
  });
});
