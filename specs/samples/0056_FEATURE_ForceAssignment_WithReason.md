> English version of a real SPEC of this project, written in Spanish and closed on 2026-11-18. Translated without changes to its <!-- sample-note -->
> rules, figures or closing notes; it describes the code of that date (the database was SQL Server then, PostgreSQL since SPEC 0092). <!-- sample-note -->

# [0056] SPEC: Force an allocation with a reason

## Objective
A dispatcher can force an allocation that the engine rejects under CE 561/2006, giving a reason and their name. It is audited, visible and marked as “Forced”.

## Context
Until now, a rejection was final. The user decided it on 2026-11-18: forcing is an explicit exception, never a silent one. The engine always validates the same way. It builds on the attempt log (0050), the Control centre (0051) and the explanations (0042). The decision is recorded in ADR 0015.

## Business rules (approved by the user, 2026-11-18)
- R1 Only a CE 561/2006 rejection (`ComplianceException`) can be forced. An unsuitable vehicle (seats, inactive, in the workshop) and an overlap of driver or vehicle **cannot** be forced, even if `force=true` is sent: they still answer 422/409. E.g.: driver with 9h15 and a 1 h trip, forced → allocated. Vehicle with 19 seats for 30 passengers, forced → 422.
- R2 The reason is mandatory free text of 10 to 500 characters, counted after trimming spaces. The name of whoever forces is also mandatory. E.g.: a reason of 9 characters → 400; of 10 → OK.
- R3 The engine always validates. If with `force=true` the trip turns out to be legal, it is allocated normally, without a forced mark.
- R4 The forced allocation stores the reason and the name in the trip (`ForcedReason`, `ForcedBy`). A later normal reallocation clears the mark.
- R5 The attempt log keeps an attempt with outcome `Forced` and a detail with the reason and the rules broken. The previous rejected attempt still counts as a block. `Forced` is not a rejection.
- R6 In the web: amber “Forced” badge on the trip (Planning and booking panel). The Control centre alert (`ForcedAssignment`, Warning) shows the reason, and the “Forced” figure appears in the engine. The ⓘ explanation lists the rules broken.
- R7 In the web, when the API rejects on regulatory grounds, the panel offers “Force with a reason…” (reason + name), which retries with `force=true`.

## Acceptance criteria
- [x] CA1: forcing without a reason, with one of fewer than 10 characters or without a name → `ArgumentException` (400) and nothing persisted.
- [x] CA2: forcing with the unsuitable vehicle or with an overlap → same exception as without forcing and nothing allocated.
- [x] CA3 (integration, SQL Server): forcing a compliance rejection → trip allocated with `ForcedReason`/`ForcedBy` and a `Forced` attempt with the rules broken.
- [x] CA4: a normal reallocation clears the mark. With `force` and a legal trip, there is no mark.
- [x] CA5: the Control centre gives the `ForcedAssignment` alert and the `Forced` figure in the engine.
- [x] CA-web: Vitest for the validation of the reason; force dialog in the booking panel.

## Out of scope
Users and permissions (who *may* force), a closed list of reasons, undoing a forced allocation as an action of its own, and local E2E.

## Reconciliation with the current domain
Existing: `DriverAllocationService.AllocateDriver(…, attemptedBy)`, `AllocationAttempt`, `AllocationOutcome`, `Trip.AssignResources`/`SetLegalValidation`, `ControlAlertType`, `EngineMetricsDto` and `TripDto`. New: `Core/ValueObjects/AllocationOverride`, `Trip.ForceAssign`, `Trip.ForcedReason`/`ForcedBy`/`IsForced`, `AllocationOutcome.Forced`, the migration `AddForcedAllocation`, `ControlAlertType.ForcedAssignment` and `EngineMetricsDto.Forced`.

## Likely files
`DriverAllocationService.cs`, `Trip.cs`, `TripConfiguration.cs`, `AllocationAttempt.cs`, `TripsEndpointsV2.cs:AllocateDriverToTrip`, `MasterDataDtos.cs:TripDto`, `ControlCenterService.cs`, `ControlCenterDtos.cs`, `web/src/BookingPanel.tsx`, `web/src/api.ts`, `web/src/pages/ControlPage.tsx`, `Timeline.tsx`.

## Closing notes
Estimated M → actual M (2026-11-18). The compliance 422 carries `forcible: true`, so the web does not guess from the title. `SampleDataSeederHttpTests` required every alert type in the demo: `ForcedAssignment` is excluded, because the demo never forces. The local API, while running, locks `bin/Debug`: built and tested in Release, and for QA :5171 has to be restarted (it applies the migration `AddForcedAllocation`). No local E2E.
