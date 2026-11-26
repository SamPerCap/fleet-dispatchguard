import { createElement } from 'react';
import { renderToString } from 'react-dom/server';
import { afterEach, describe, expect, it } from 'vitest';
import { setLanguage } from './language';
import { t } from './i18n/t';
import { XRayBody, XRayPanel } from './XRayPanel';
import { createXRayStore, kindOf, xrays, type XRay } from './xray';

/** As the API answers a rejected allocation (SPEC 0097): every figure here is the server's. */
const REJECTED: XRay = {
  Kind: 'allocation',
  Status: 422,
  TotalMs: 41.3,
  Queries: 12,
  QueriesMs: 18.6,
  Evaluations: 1,
  Checks: [
    { Step: 'driver-available', Outcome: 'passed', Rules: [] },
    {
      Step: 'compliance',
      Outcome: 'rejected',
      Rules: [
        { Rule: 'CE_561_2006.DailyDriving.9hours', Name: 'Daily driving time', Article: 'Art. 6.1', Passed: true, Actual: 4, Limit: 9, Unit: 'hours' },
        { Rule: 'CE_561_2006.WeeklyRest', Name: 'Weekly rest period', Article: 'Art. 8.6', Passed: false, Actual: 12.5, Limit: 45, Unit: 'hours' },
      ],
    },
    { Step: 'vehicle', Outcome: 'not-reached', Rules: [] },
    { Step: 'driver-overlap', Outcome: 'not-reached', Rules: [] },
    { Step: 'vehicle-overlap', Outcome: 'not-reached', Rules: [] },
  ],
  Rules: [],
  Stages: [
    { Stage: 'locks', Times: 1, Ms: 2.1, Queries: 1 },
    { Stage: 'compliance', Times: 1, Ms: 9.4, Queries: 3 },
  ],
  OtherMs: 5.2,
  Locks: [{ Resource: 'trip', Count: 1 }, { Resource: 'driver', Count: 1 }, { Resource: 'vehicle', Count: 1 }],
  Writes: [{ Record: 'attempt', Count: 1, Saved: true }],
};

describe('which answers carry an X-ray', () => {
  it.each([
    ['/api/trips/7/allocate', 'allocation'],
    ['/api/recommendations', 'recommendation'],
    ['/api/recommendations/check', 'recommendation'],
  ] as const)('%s is %s', (url, kind) => expect(kindOf(url)).toBe(kind));

  it.each(['/api/trips/7', '/api/xray', '/api/trips/7/recommendation', '/api/trips/7/driver-options', '/api/audit/visits'])('%s is not', url =>
    expect(kindOf(url)).toBeNull());

  it('keeps only the last token of each kind and says so to who listens', () => {
    const store = createXRayStore();
    let heard = 0;
    store.subscribe(() => heard++);

    store.remember('/api/trips/1/allocate', 'first');
    store.remember('/api/trips/2/allocate', 'second');
    store.remember('/api/trips/2', 'ignored');
    store.remember('/api/trips/2/allocate', null);

    expect(store.latest('allocation')?.token).toBe('second');
    expect(store.latest('recommendation')).toBeNull();
    expect(heard).toBe(2);
  });
});

describe('the panel', () => {
  afterEach(() => setLanguage('es'));

  it('does not exist until an answer of its kind brought an X-ray', () => {
    expect(renderToString(createElement(XRayPanel, { kind: 'recommendation' }))).toBe('');
  });

  it('is a closed button that a keyboard reaches and that says what it controls', () => {
    xrays.remember('/api/trips/3/allocate', 'token');

    const painted = renderToString(createElement(XRayPanel, { kind: 'allocation' }));

    expect(painted).toMatch(/^<div[^>]*><button type="button"/);
    expect(painted).toContain('aria-expanded="false"');
    expect(painted).toMatch(/aria-controls="[^"]+"/);
    expect(painted).toContain(t.xray.open);
  });

  it.each(['en', 'es'] as const)('in %s shows the server figures and the codes by name', async chosen => {
    await setLanguage(chosen);

    const painted = renderToString(createElement(XRayBody, { xray: REJECTED }));

    expect(painted).toContain(t.xray.summary(422, (41.3).toLocaleString(chosen), 12, (18.6).toLocaleString(chosen)));
    for (const step of ['driver-available', 'compliance', 'vehicle'] as const) expect(painted).toContain(t.xrayStep[step]);
    expect(painted).toContain(t.xrayOutcome.rejected);
    expect(painted).toContain(t.xrayOutcome['not-reached']);
    expect(painted).toContain('Art. 8.6 · Weekly rest period');
    expect(painted).toContain(t.xray.figure((12.5).toLocaleString(chosen), '45', t.xrayUnit.hours));
    expect(painted).toContain(t.xray.limitSource);
    expect(painted).toContain(t.xray.lock(t.xrayResource.driver, 1));
    expect(painted).toContain(t.xray.write(t.xrayRecord.attempt, 1, t.xray.saved));
    expect(painted).toContain('<caption');
  });

  it('with many evaluations counts the rules instead of an order that does not exist', () => {
    const recommended: XRay = {
      ...REJECTED,
      Kind: 'recommendation',
      Status: 200,
      Evaluations: 240,
      Checks: [],
      Rules: [{ Rule: 'daily-driving', Passed: 230, Failed: 10 }],
      Locks: [],
      Writes: [],
    };

    const painted = renderToString(createElement(XRayBody, { xray: recommended }));

    expect(painted).toContain(t.xray.evaluations(240));
    expect(painted).toContain(t.xray.ruleCount(230, 10));
    expect(painted).toContain(t.xray.noLocks);
    expect(painted).toContain(t.xray.noWrites);
    expect(painted).not.toContain(t.xray.checks);
  });
});
