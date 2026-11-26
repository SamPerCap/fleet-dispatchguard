# ADR 0016 — Allocating cannot make a later, already allocated service illegal

- **Status:** accepted (the user, 2026-11-23) · **Origin:** SPEC 0063 · **Completes:** ADR 0001 · **Extends:** ADR 0015 (forcing)

## Context
The CE 561/2006 engine validates a service by looking at what was driven before (and, for the daily rest period, the working day that
contains it). If a service was allocated before another one already allocated, the later one was not validated again: it could end up
above the driving period, daily, weekly or two-week driving time, or without a weekly rest period, without anyone seeing it. The
verdict depended on the order of allocation.

## Decision
- Allocating requires the service to comply **and** the driver's later, already allocated services to keep complying. Each one
  is revalidated with the same `LegalComplianceEvaluation.Validate`, without and with the new one: no new rules or figures.
- A single horizon: the services whose own evaluation sees the new one, that is, those that start less than
  `LegalComplianceEvaluation.LookbackWindow` (two calendar weeks, ADR 0017) after its end.
- It only blocks what the new one breaks or worsens, rule by rule. A later service that is already illegal or forced and does not get worse does not block.
- It lives in `LegalValidationService`, so allocating, forcing and searching for candidates share the check. The rejection is
  CE 561/2006 (422), in the same step.
- **It can be forced** with a reason. The response lists the affected later services and the web shows them before confirming.
  Each affected later service is marked “Forced by service #T” (with the reason and who) and stores the id of T. The mark is
  cleared by itself when T is cancelled or changes driver, and the later service is revalidated; it does not touch its own forcing or the
  marks of other services. The log stores a single attempt: that of T.

## Consequences
- The verdict no longer depends on the order in which a driver's services are allocated.
- Reallocating the same driver (another vehicle) does not change their calendar and never blocks because of this rule; if the service
  was forced because of later services that are still affected, it keeps its mark.
- Cancelling or reallocating to another driver is not checked: it removes driving, it does not add it.
- Two simultaneous allocations: they were left without concurrency control (just like overlaps); ADR 0018 solves it.
