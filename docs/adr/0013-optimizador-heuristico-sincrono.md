# ADR 0013 — Synchronous heuristic optimiser in Application, read-only

- **Status:** accepted · **Origin:** SPEC 0043

## Context
Closing the product asks to reduce empty km by chaining trips. The optimisation worker was an empty stub and
a worker would require a queue, job state and polling from the web, too much for a one-company demo.

## Decision
Greedy heuristic (by departure time, nearest eligible vehicle that arrives on time) as a **pure function**
(`VehicleChainingPlanner`) in Application, exposed by a synchronous service and `GET /api/optimization/vehicle-chaining`.
Straight-line distance and 70 km/h, in a single source (`TravelAssumptions`). It only **proposes**: it changes no allocation.

## Consequences
- A demo week is calculated in milliseconds within the request; if the volume grows, the same pure function
  can be called from a worker without rewriting it.
- It is not the global optimum; the response says so and returns the current plan if it does not improve.
- Applying the proposal is left out: changing vehicles in a chain needs a multi-trip transaction that
  allocation (ADR 0001) does not offer today; each change applied by hand goes through `allocate` and is revalidated in full.

## Note (SPEC 0070, 2026-11-24)
The calculation depends on the instant: a service with departure ≤ now (injected clock, UTC) is a fact. It keeps its bus without going through the eligibility
or arrival filter and still counts for the position and for the km, before and after; the saving comes only from what can still be changed.
The function is still pure: it receives `now`. The report and the efficiency of the Control Centre use it in the same way.

## Note (SPEC 0071, 2026-11-24)
Nobody leaves before now: for what has not left, `max(now, end of its previous service) + distance/70 km/h ≤ departure`, from where its last
completed or in-progress service left the bus, also from before the period or from another base (`IVehicleLastServiceReader`), or from its base. That decides
whether it arrives; the km still start at the base. The estimate does not question what the dispatcher already allocated nor invalidate the report: the current bus
competes on km even if it does not arrive; if it does not arrive because of another service of its own in the plan, it is the last option; if the proposal took it away, it is recalculated
reserving that service for it; and if it is no longer eligible and the others are discarded only by the estimate, it is kept and that is said. If there is no eligible vehicle,
R6 of 0043 still applies. The days and the week are those of Madrid, with `OperationalDay` as the single source: in the URL, a date is Madrid
midnight (`DayOrInstant`) and with a time it is an instant, with the criterion of 0067.
