// Each figure of the cover points to what reproduces it (SPEC 0096 R2). They are measurements with a date, not counts: whoever
// measures again and gets another figure changes the text and the date together. `measurements.test.ts` checks that the file and
// the test still exist.
import { locale } from '../language';

export type EvidenceId = 'bursts' | 'oracle' | 'speed';

export interface EvidenceSource {
  id: EvidenceId;
  /** From the root of the repository. */
  file: string;
  /** The test or benchmark method that prints the figure. */
  member: string;
  /** How to run it from the root of the repository. */
  command: string;
  measuredOn: string;
}

export const EVIDENCE: EvidenceSource[] = [
  {
    id: 'bursts',
    file: 'Tests/Infrastructure.Tests/Integration/ConcurrentBurstTests.cs',
    member: 'SixtyBurstsOfEightClashingAssignments_AcceptOnePerBurst',
    command: 'dotnet test --filter "FullyQualifiedName~ConcurrentBurstTests"',
    measuredOn: '2026-11-26',
  },
  {
    id: 'oracle',
    file: 'Tests/Core.Tests/ValueObjects/Article6Tests.cs',
    member: 'Engine_AgreesWithAnIndependentReadingOfArticle6',
    command: 'dotnet test --filter "FullyQualifiedName~Engine_AgreesWithAnIndependentReadingOfArticle6"',
    measuredOn: '2026-11-26',
  },
  {
    id: 'speed',
    file: 'tools/Benchmarks/ComplianceBenchmarks.cs',
    member: 'Validate',
    command: 'dotnet run -c Release --project tools/Benchmarks -- --filter "*"',
    measuredOn: '2026-11-26',
  },
];

/** A date of the form 2026-11-26, written as its reader writes dates. */
export function dateOf(isoDay: string): string {
  return new Date(`${isoDay}T12:00:00Z`).toLocaleDateString(locale(), { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' });
}

/**
 * The last cold start measured with the database asleep (SPEC 0086 CA1): three visits after 7 minutes idle, before the bot
 * protection. Measuring again means waking production, so it is not redone here (SPEC 0096 R8).
 */
export const COLD_START = { fromSeconds: 6.5, toSeconds: 6.7, measuredOn: '2026-11-25' };

export function coldStartWait(): { wait: string; measuredOn: string } {
  const seconds = (value: number) => value.toLocaleString(locale(), { minimumFractionDigits: 1 });
  return { wait: `${seconds(COLD_START.fromSeconds)}–${seconds(COLD_START.toSeconds)} s`, measuredOn: dateOf(COLD_START.measuredOn) };
}
