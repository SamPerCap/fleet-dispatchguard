/// <reference types="vite/client" />
import { createElement } from 'react';
import { renderToString } from 'react-dom/server';
import { afterEach, describe, expect, it } from 'vitest';
import { setLanguage } from '../language';
import { AllocationFlow, FLOW_STEPS } from './AllocationFlow';
import { showcaseText } from './showcaseText';

const evaluator = Object.values(
  import.meta.glob<string>('../../../src/Application/Services/AllocationEvaluator.cs', { query: '?raw', import: 'default', eager: true }),
)[0];

/** Where each step of the drawing is decided in the engine. */
const ENGINE_CALL = {
  driver: 'DriverEligibilityCheck.For(',
  compliance: '_legal.ValidateDriverForTripDetailed(',
  vehicle: '_vehicles.CheckEligibility(',
  overlap: '_schedule.CheckDriverConflict(',
};

describe('the drawing of the flow is the order of the engine (SPEC 0096 R3)', () => {
  it('the steps come in the order AllocationEvaluator decides them', () => {
    const decide = evaluator.slice(evaluator.indexOf('DecideAsync('));
    const positions = FLOW_STEPS.map(step => decide.indexOf(ENGINE_CALL[step.id]));

    expect(positions.every(position => position > 0)).toBe(true);
    expect([...positions].sort((a, b) => a - b)).toEqual(positions);
    expect(decide.indexOf('_schedule.CheckVehicleConflict(')).toBeGreaterThan(positions[3]);
  });

  it('only the step of EC 561/2006 can be forced, as in the engine', () => {
    expect(FLOW_STEPS.filter(step => step.forcible).map(step => step.id)).toEqual(['compliance']);
    expect(evaluator.match(/pastCompliance\)/g)).toHaveLength(1);
    expect(evaluator).toContain('!compliance.ToSnapshot().IsCompliant && !pastCompliance');
  });
});

describe('the drawing can be read without seeing it', () => {
  afterEach(() => setLanguage('es'));

  it.each(['en', 'es'] as const)('in %s it is one image with its name, its description and every step', async chosen => {
    await setLanguage(chosen);
    const text = showcaseText().flow;
    const painted = renderToString(createElement(AllocationFlow));

    expect(painted).toContain('role="img"');
    expect(painted).toMatch(/<title id="[^"]+">/);
    expect(painted).toMatch(/<desc id="[^"]+">/);
    for (const step of FLOW_STEPS) expect(painted).toContain(text.steps[step.id].name);
    expect(painted.split(text.forcible)).toHaveLength(2);
  });
});
