# ADR 0002 — CE 561/2006 figures in a single source

- **Status:** accepted · **Origin:** SPECs 0005, 0015

## Context
The limits (4h30, 9h/10h, 56h, 90h, 11h/9h, 45h/24h) appeared repeated in services and tests; a regulatory change or a mistake in one copy produced inconsistent validations.

## Decision
All the figures live in `src/Core/ValueObjects/DrivingLimits.cs`. The evaluation (`LegalComplianceEvaluation`) is pure, static logic over the history of driving periods.

## Consequences
- A regulatory change is made in a single file and the tests confirm it.
- Each company's own rules (M2) will be thresholds that only tighten these figures (ADR 0004).
