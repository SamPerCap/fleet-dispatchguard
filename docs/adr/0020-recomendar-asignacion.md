# ADR 0020 — Recommend an allocation: simulate with the same classes and apply through `AllocateDriver`

- **Status:** accepted (scope by the user, 2026-11-23; mechanism, technical decision) · **Origin:** SPEC 0062 · **Builds on:** ADR 0013, 0016, 0018 and 0019

## Context
A proposal of many trips has to count each choice in the following ones without saving anything, and what it proposes has
to be what the API accepts on applying. Two engines (one to simulate and another to allocate) would end up disagreeing.

## Decision
- **An in-memory copy** (`PlanningSnapshot`): what the rules look at around the requested trips is read once, without EF
  tracking. On top of it there are in-memory repositories that implement the same interfaces, and with them
  **the same classes** are built: searches, `LegalValidationService`, `ScheduleConflictService`, `VehicleEligibilityService`.
  Choosing a pair calls `Trip.AssignResources` on the copy. No writing is possible: the write methods throw.
- **The steps of `AllocateCore` move to `AllocationEvaluator`**, used by the real allocation and the simulation.
- **No state on the server.** The proposal lives on the screen; changing a line sends the lines and they are all revalidated
  on a new copy.
- **Applying = `POST /allocate` per trip**, in the order of the proposal, with `origin` and `onlyIfUnassigned`. The condition
  (`Trip.WhyNotRecommendable`, the same when proposing and when revalidating) is checked inside the exclusive section: without it, applying
  an old proposal would reallocate what someone else just allocated or would allocate a trip that has already departed.
- **Revalidating:** an untouched line that no longer arrives on time is no longer applied; one changed by hand only goes through the rules
  of the engine and shows the warning. Putting the proposed pair back marks it as recommended again.
- **Who is chosen:** the smallest step (5, 15, 30 km from the position) with a legal pair that arrives; within it, the order
  of the searches, the same as in the panel (user decision). The crew of the same booking goes first and without a distance cap
  (user decision). A single place: `RecommendationPick`, which also decides the panel badge.
- **The origin goes in the attempt** (`AllocationAttempt.Origin`), not in the result: the counts by result do not change.

## Discarded
- **A transaction that is rolled back:** it would hold locks while calculating and would make whoever really allocates wait.
- **Simplified rules of its own in the proposal:** faster, but they would propose things that the API rejects.
- **The nearest within the step:** the steps would be only a label and a 30 km cap.
- **Applying everything in one request:** if the connection drops, nobody knows what was done; per trip there is progress and retry.

## Measures
API published in Release against a SQL Server 2022 in a container, with the demo (1112 services, 54 drivers, 36 vehicles).

| Case | Proposal | Drivers per step 5 / 15 / 30 | Vehicles per step 5 / 15 / 30 | Same booking (more than 30 km) |
|---|---|---|---|---|
| The demo as it is (12 pending) | 12 of 12 | 7 / 0 / 0 | 6 / 0 / 0 | 5 (4) drivers, 6 (3) vehicles |
| Everything future unallocated: the first 100 of 221 | 100 of 100 | 27 / 4 / 0 | 23 / 6 / 2 | 69 (15) drivers, 69 (10) vehicles |
| The next 100, with the previous ones applied | 100 of 100 | 38 / 3 / 0 | 33 / 4 / 0 | 59 (16) drivers, 63 (12) vehicles |

The steps count only what is not kept from the same booking; no proposal was left without a solution and the
third step was hardly used (2 vehicles). Proposing 100 trips takes 0.3–0.4 s (1.9 s the first time after starting), revalidating 100 lines
less than 0.1 s and applying 98, 2.7 s. There was no need to precompute the scope.

## Consequences
- A new repository method used by the rules must also be provided in the copy, and the copy has to read as far
  as they do (`LegalValidationService.CalendarFor`).
- Something may change between calculating and applying: the line is rejected with its reason. The proposal reserves nothing.
- Greedy heuristic: an early choice may leave a later trip without a solution.
