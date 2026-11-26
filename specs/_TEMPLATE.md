# [NNNN] SPEC: {{Feature name}}

> Copy to `specs/backlog/[NNNN]_FEATURE_[Name]_[Brief].md`. Short: if it goes over ~1 page, it is probably two features.

## Objective
One sentence: what can be done afterwards that could not be done before.

## Context
Why now. Which previous SPECs or debt (`.claude/ARCHITECTURE_REVIEW.md`) it touches.

## Rules
Each rule with its **article** (if it is regulatory) and **at least one numeric example** including the edge case.
- R1 (CE 561/2006 Art. X): … E.g.: 4h30 continuous → OK; 4h31 without a 45 min break → infringement.

## Acceptance criteria (verifiable)
- [ ] CA1: …
- [ ] CA-unit: unit tests of each rule and its limits.
- [ ] CA-integration (if it touches persistence/HTTP): test against real PostgreSQL or a real call to the endpoint with the expected result.

## Out of scope
What is NOT done here (keeps the implementation from expanding).

## Reconciliation with the current domain
Cited classes/properties that **already exist** (verified with grep, including inheritance) and those that are new. See `docs/LESSONS.md §1`.

## Likely files
Taken from `.claude/PROJECT_MAP.md` (routing table), not assumed.

## Closing notes
_(filled in when finished)_ Estimated size S/M → actual. Friction, if any, in one or two sentences; if it was a new lesson → `docs/LESSONS.md`.
