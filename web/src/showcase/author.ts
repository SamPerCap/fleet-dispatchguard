// Only what the author wrote, word for word (SPEC 0096 R4, 2026-11-26): no e-mail, no photo, no sentence about the origin.
export const AUTHOR = {
  name: 'Samuel P.C.',
  // Waiting for the author: nothing is shown while it is null.
  headline: null as string | null,
  // Names of services, the same in every language.
  links: [
    { name: 'GitHub', url: 'https://github.com/SamPerCap' },
    { name: 'LinkedIn', url: 'https://www.linkedin.com/in/sampercap/' },
  ],
};

/** The guides the use of AI follows, as their official pages name them (verified on 2026-11-26). */
export type GuideId = 'nist-sp-800-218a' | 'nist-ai-600-1' | 'openssf-ai-assistants';

export const GUIDES: { id: GuideId; name: string; publisher: string; published: string; url: string }[] = [
  {
    id: 'nist-sp-800-218a',
    name: 'SP 800-218A, Secure Software Development Practices for Generative AI and Dual-Use Foundation Models: An SSDF Community Profile',
    publisher: 'NIST',
    published: '2024-07',
    url: 'https://csrc.nist.gov/pubs/sp/800/218/a/final',
  },
  {
    id: 'nist-ai-600-1',
    name: 'NIST AI 600-1, Artificial Intelligence Risk Management Framework: Generative Artificial Intelligence Profile',
    publisher: 'NIST',
    published: '2024-07',
    url: 'https://www.nist.gov/publications/artificial-intelligence-risk-management-framework-generative-artificial-intelligence',
  },
  {
    id: 'openssf-ai-assistants',
    name: 'Security-Focused Guide for AI Code Assistant Instructions',
    publisher: 'OpenSSF',
    published: '2025-08',
    url: 'https://best.openssf.org/Security-Focused-Guide-for-AI-Code-Assistant-Instructions',
  },
];

/**
 * Counted on the private history with
 * `git log origin/master --format='%(trailers:key=Co-Authored-By,valueonly)'` (lines naming Claude) and `git rev-list --count`.
 * The public repository will start with a clean history (SPEC 0099), where this count cannot be redone.
 */
export const COAUTHORSHIP = { coauthored: 174, total: 204, commit: '1913125', countedOn: '2026-11-26' };
