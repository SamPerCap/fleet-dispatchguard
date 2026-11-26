# ADR 0015 — Forcing an allocation: CE 561/2006 only, with a reason, audited and visible

- **Status:** accepted (the user, 2026-11-18) · **Origin:** SPEC 0056 · **Qualifies:** ADR 0001 (legal validation before allocating)

## Context
In real operation there is sometimes no alternative: there is no other driver left, or there is an emergency. If the system only
knows how to say “no”, the dispatcher allocates outside the tool and the decision is lost. An explicit way out
was needed that would not weaken the central rule.

## Decision
- The engine **always** validates the same way. Forcing skips no check: it only allows going on when the only thing
  that fails is CE 561/2006 (`ComplianceException`).
- An ineligible vehicle (seats, inactive, workshop) and an overlap of driver or vehicle **are never forced**,
  because they are physically impossible. With `force=true` they still answer 422/409.
- `POST /api/trips/{id}/allocate` with `Force`, `Reason` and `AssignedBy` (in the body since SPEC 0089): a free-text reason of 10 to 500 characters and
  a name, both mandatory (`AllocationOverride`), or 400. The compliance 422 carries `forcible: true`, so that the
  web offers forcing only when it applies.
- The trip stores `ForcedReason`/`ForcedBy` and the non-compliant validation. The attempt log records `Forced`
  with the reason and the infringed rules. The previous rejected attempt still counts as a block. A
  normal reallocation clears the mark.
- It is visible: “Forced” badge, `ForcedAssignment` warning in the Control Centre, “Forced” figure and the
  ⓘ explanation with the infringed rules.

## Consequences
- Rule 1 of `CLAUDE.md` now has a single documented exception.
- Without users (M1), anyone can force. The control is the audit, not a permission. When there is
  authentication, forcing will be a role capability.
- The demo never forces: forcing is a human decision.
