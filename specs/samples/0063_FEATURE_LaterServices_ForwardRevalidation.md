> English version of a real SPEC of this project, written in Spanish and closed on 2026-11-23. Translated without changes to its <!-- sample-note -->
> rules, figures or closing notes; it describes the code of that date (the database was SQL Server then, PostgreSQL since SPEC 0092). <!-- sample-note -->
> P3–P7 are the numbers of the questions the user answered before the SPEC was approved. <!-- sample-note -->

# 0063 SPEC: Later services already allocated

## Objective
Allocating a service can no longer make illegal another, later service that the driver already had allocated.

## Context
`LegalComplianceEvaluation.Validate` only looks backwards (except the daily rest period, SPEC 0041). Checked by running the engine: with a new service T **before** another one L already allocated, T was accepted and L became non-compliant (Art. 7: 6 h continuous; Art. 6.1: 11 h; third extended day: 9.6 h; Art. 6.3: 90.5 h; Art. 8.6: no weekly rest period). Wording and questions approved by the user on 2026-11-23. It comes before 0062 (Recommend).

## Business rules
- **R1.** Allocating T requires, besides T complying, that every later service L already allocated to the driver (it starts after the start of T and does not overlap it: overlap is another rule) still complies. L is revalidated with the same `LegalComplianceEvaluation.Validate`, without T and with T. No new rules or figures. E.g.: T 08:00–12:00, L 18:00–21:00 after another one 13:00–17:00 → L goes from 7 to 11 h (max. 10) → rejection.
- **R2. Horizon.** Every L whose own evaluation sees T: it starts less than 14 days after the end of T (`LegalComplianceEvaluation.LookbackWindow`, the only figure). E.g.: L at 13 d 23 h 59 min → evaluated; at 14 d → not.
- **R3. Only if it gets worse.** It blocks if, because of T, a critical rule of L (a) goes from compliant to non-compliant, or (b) was already non-compliant and its distance to the limit grows. An L already illegal or forced that does not get worse does not block. A warning does not block; if because of T it becomes critical, that is (a). E.g.: L at 10.4 h, forced; T on another day → does not block; T on the same day, 2 h → 12.4 h → blocks.
- **R4.** It lives in `LegalValidationService`: allocating, forcing and the candidates use the same check. Reallocating the same driver (e.g. another vehicle) does not change their calendar and never blocks under this rule.
- **R5.** It is CE 561/2006: `ComplianceException` → 422 and the same order. It is always evaluated, even if T is already non-compliant on its own (P6).
- **R6. Explanation.** “Later services already allocated”: complied → how many and until when; not complied → per service and rule, figure with T, figure without T and limit.
- **R7. Forcing** (P3): allowed, with a reason (0056). The 422 lists the later services affected (`laterServices`) and the form shows them before confirming.
- **R8. Mark on the later service** (P4): when T is forced, each affected L is marked “Forced by service #T” with the reason and the name, storing the id of T. The mark clears itself if T is cancelled (itself or its booking) or reallocated to another driver, and then L is revalidated. L keeps its own forced mark and the marks of other T. Reallocating T to the same driver keeps the marks of L and, if L was still affected, the one of T.
- **R9. Log** (P5): 1 “Forced” attempt (the one of T) that lists the L. Rules of the attempt: `later-services` plus the id of the article broken.

## Acceptance criteria
- [x] CA1: limits per article (7, 6.1, 6.1 extended, 6.2, 6.3, 8.4, 8.6) and R2/R3 in Core tests.
- [x] CA2: real allocation against SQL Server: 422 rejection, a forced allocation that marks L, and the five clearing cases of R8.
- [x] CA3: full demo loaded through the API with no new rejections, with the time measured.
- [x] CA4: web: list of later services before forcing and badge on L, in a real browser.

## Out of scope
Counting the day and the week in UTC (0064). Checking when driving is removed (cancelling or reallocating to another driver): it adds no hours (P7). Two simultaneous allocations to the same driver: no concurrency control, as today.

## Reconciliation with the current domain
Existing: `LegalComplianceEvaluation.Validate`, `DrivingPeriod`, `ComplianceCheckResult`, `ComplianceRuleCheck`, `LegalValidationService`, `Trip.ForceAssign`, `AllocationOverride`, `AttemptRules`, `RulesCatalog`. New: `LaterServicesEvaluation`, `LaterServicesCheck`, `LaterServiceImpact`, `ForcedByTrip`, `ForcedConsequences`. Checked (P7): the time of a trip is only set in its constructor and nothing allocates outside `AllocateDriver`.

## Likely files
`Core/ValueObjects/{LaterServicesEvaluation,DrivingPeriod,ComplianceCheckResult}.cs`, `Core/Entities/Trip.cs`, `Application/Services/{LegalValidationService,DriverAllocationService,ForcedConsequences}.cs`, `Explanations/DecisionExplainer.cs`, `Rules/*`, `Errors/DomainExceptionHandler.cs`, `web/src/{force.ts,BookingPanel.tsx}`.

## Closing notes
Size M. Invariant with fixed seeds (300 calendars in random order): no accepted service ends up illegal. Demo loaded through the API, before and after: 1,112 services, 1,100 allocated, 21 rejections, 19 s and 18 s. In 71 services of the demo, 24 driver options (10 services) are now discarded because of a later service; median of `driver-options` 251 → 253 ms. `LegalValidationViolations` no longer has a cap of 1,000 characters: there is one violation per later service and rule. Friction: nothing new. Unchanged: two simultaneous allocations to the same driver.
