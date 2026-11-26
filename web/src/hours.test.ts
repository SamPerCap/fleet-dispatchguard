import { describe, expect, it } from 'vitest';
import { hourLimits } from './hours';

describe('hourLimits', () => {
  it('day, week and two weeks, each with its API limit', () =>
    expect(hourLimits({ Today: 1, ThisWeek: 2, LastTwoWeeks: 3, MaxDaily: 9, MaxWeekly: 56, MaxBiWeekly: 90 })).toEqual([
      { label: 'Jornada', value: 1, max: 9 },
      { label: 'Semana', value: 2, max: 56 },
      { label: 'Dos semanas', value: 3, max: 90 },
    ]));
});
