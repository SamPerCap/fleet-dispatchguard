// Only what can be seen in the demo, or is explained with its reason (SPEC 0093 R5). Figures are counted by the build, never here.
import type { EvidenceId } from './measurements';

export interface Point {
  title: string;
  why: string;
}

export interface EngineeringSection {
  id: string;
  title: string;
  intro: string;
  points: Point[];
}

/** The pages that ask nothing of the server, in one language. The English one comes with the page; the Spanish one, when chosen. */
export interface ShowcaseText {
  tagline: string;
  cover: {
    /** The thesis: one sentence the visitor can check in the demo itself (SPEC 0096 R1). */
    title: string;
    problem: string;
    tryIt: string;
    howItIsBuilt: string;
    about: string;
    technologies: string;
    /** The last measurement of the cold start, with its date (SPEC 0096 R8). */
    notes: (wait: string, measuredOn: string) => string[];
  };
  evidence: {
    title: string;
    intro: string;
    items: Record<EvidenceId, { figure: string; claim: string; method: string }>;
    measuredOn: (date: string) => string;
    reproduce: string;
    readTheCode: string;
  };
  flow: AllocationFlowText;
  about: AboutText;
  engineering: {
    back: string;
    title: string;
    intro: string;
    sections: EngineeringSection[];
  };
  loading: string;
  pageFailed: string;
  appFailed: string;
  tryAgain: string;
}

export type FlowStepId = 'driver' | 'compliance' | 'vehicle' | 'overlap';

/** Lines of the diagram are short on purpose: SVG does not wrap, and it must be read on a 360 px phone. */
export interface AllocationFlowText {
  title: string;
  /** For whoever cannot see the drawing: the whole flow in words. */
  description: string;
  request: { name: string; detail: string };
  steps: Record<FlowStepId, { name: string; detail: string }>;
  forcible: string;
  rejected: [string, string];
  saved: { name: string; detail: string };
  caption: string;
}

export interface TechnologyGroup {
  group: string;
  items: string[];
}

export interface AboutText {
  back: string;
  title: string;
  contact: string;
  story: string[];
  architecture: { title: string; intro: string; points: Point[]; technologies: TechnologyGroup[] };
  ai: {
    title: string;
    intro: string;
    parts: Point[];
  };
  code: { title: string; pending: string; link: string };
}

export const TECHNOLOGIES = ['.NET 10', 'PostgreSQL 17', 'React + TypeScript', 'Docker', 'Vercel + Neon'];
