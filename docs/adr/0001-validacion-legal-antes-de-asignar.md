# ADR 0001 — Legal validation before allocating, fail-fast

- **Status:** accepted · **Origin:** SPECs 0013, 0034, 0035

## Context
A dispatcher who allocates a driver who cannot legally drive exposes the company to penalties and immobilises the service on the road. Detecting it afterwards (audit) is too late.

## Decision
`DriverAllocationService.AllocateDriver` checks, in this order and before touching anything: CE 561/2006 compliance → vehicle eligibility → overlap of the driver's schedule → overlap of the vehicle. The first failure throws a domain exception (`ComplianceException`, `VehicleIneligibleException`, `ScheduleConflictException`) and nothing is persisted. The API translates them into `ProblemDetails` (422 / 409).

## Consequences
- A saved allocation is, by construction, legal according to the information available.
- The validators return result objects (`XxxCheck`); only the orchestrator throws. They are reused to suggest candidates without exceptions.
- The order is a contract: changing it changes which error the user sees.
