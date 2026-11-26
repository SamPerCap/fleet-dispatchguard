# Business Rules: Fleet DispatchGuard

Central document of all the business rules and regulations that apply to the Fleet DispatchGuard project.

## CE 561/2006: Regulation (EC) on Driving Times and Rest Periods

Regulation **CE 561/2006** is mandatory in Spain and the whole European Union for commercial transport vehicles.

**Official Source**: [Regulation (EC) No 561/2006](https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=celex%3A32006R0561)

---

## Driving Rules

### Rule 1: Driving Period (Article 7)
- **Maximum**: 4.5 hours of continuous driving without interruption
- **Mandatory Break**: 45 minutes (can be split: 15min + 30min)
- **Reset**: The counter restarts when a break of ≥45 minutes is taken
- **Implementation**: `DrivingLimits.CanDriveContinuous()`, `DrivingLimits.MaxContinuousDriving = 4.5h`
- **Origin in Code**: SPEC 0005 - Enhance DrivingLimits with Continuous Driving Rule

### Rule 2: Daily Driving Time (Article 6)
- **Standard Maximum**: 9 hours of driving in the working day: between the end of one daily rest period and the beginning of the next (Art. 4.k; rest period = 9 h or more without driving). It is not the calendar day (SPEC 0064)
- **Exception**: Maximum 10 hours, but only 2 times per week. Own criterion, not legal text: the working day that crosses Monday counts in the week in which it starts
- **Rest Reset**: 11 hours of uninterrupted rest
- **Implementation**: `DrivingLimits.AvailableToday()`, `DrivingLimits.MaxDailyDriving = 9h`, `MaxDailyDrivingExtended = 10h`

### Rule 3: Weekly Driving Time (Article 6)
- **Maximum**: 56 hours from 00:00 on Monday to 24:00 on Sunday (Art. 4.i), counted in Madrid time and in UTC: both must be met. A service that crosses Monday counts in each week the part that falls in it (SPEC 0064, ADR 0017)
- **Rest Reset**: Weekend (weekly rest period)
- **Implementation**: `DrivingLimits.AvailableThisWeek()`, `DrivingLimits.MaxWeeklyDriving = 56h`

### Rule 4: Driving in two calendar weeks (Article 6)
- **Maximum**: 90 hours in two consecutive calendar weeks (Art. 6.3 with 4.i), in both time zones (SPEC 0064)
- **Purpose**: Avoid accumulation of fatigue over extended periods
- **Implementation**: `DrivingLimits.AvailableInTwoWeeks()`, `DrivingLimits.MaxBiWeeklyDriving = 90h`

---

## Rest Rules

### Rule 5: Daily Rest Period (Article 8)
- **Minimum**: 11 hours in a row every 24 hours
- **Reduced Exception**: 9 hours, maximum 3 times between weekly rest periods
- **Compensation**: The missing hours must be made up before the next weekly rest period
- **Split (Art. 4(g))**: 3h + 9h counts as a regular rest period
- **Window (Art. 8.2)**: the rest period must be completed within the 24h following the end of the previous one; a break < 9h inside the working day (split shift) is not a daily rest period
- **Implementation** (SPEC 0041): `LegalComplianceEvaluation.EvaluateDailyRest` over the driver's full calendar (it includes services already allocated after the trip, so the result does not depend on the order of allocation). Violations `CE_561_2006.DailyRest.11hours` (not even 9h fits in the window) and `CE_561_2006.DailyRest.ReducedLimit` (more than 3 reduced between weekly ones). Figures in `DrivingLimits` (`DailyRestNormal`, `DailyRestReduced`, `SplitDailyRestFirstPart`, `DailyRestWindow`, `MaxReducedDailyRests`)

### Rule 6: Weekly Rest Period (Article 8)
- **Minimum**: 45 hours in a row every 2 weeks
- **Reduced Exception**: 24 hours, but it must be compensated within 3 weeks
- **Purpose**: Guarantee periodic deep rest for safety
- **Blocking**: Since SPEC 0015, if there is no gap ≥24h between consecutive `DrivingPeriod`s within the last 6×24h (relative to `tripStart`), `LegalComplianceEvaluation.Validate()` emits a `ComplianceViolation.Critical` (`CE_561_2006.WeeklyRest.45hours`) that blocks the allocation — before, it was only detected, not blocked
- **Not verifiable (SPEC 0040)**: with no rest period found, it is only critical if the known history covers at least 6×24h before the service (`DrivingLimits.MaxIntervalBetweenWeeklyRests`). If it does not cover it (new hire, a single record, only recent activity) a non-blocking warning `CE_561_2006.WeeklyRest.Unverifiable` is emitted ("Historial insuficiente para verificar el descanso semanal: confirma el tacógrafo…" — insufficient history to verify the weekly rest period: check the tachograph), which is stored in `LegalValidation.Warnings` and comes out in `warnings` of `/allocate`
- **Implementation**: `DrivingLimits.NeedsWeeklyRest(DateTime asOf)` (deterministic, it no longer uses `DateTime.UtcNow` internally), `LegalComplianceEvaluation.EvaluateWeeklyRest()`, `DrivingLimits.WeeklyRestNormal = 45h`, `WeeklyRestReduced = 24h`
- **Out of scope (RF4)**: the validation of compensation for a reduced rest period (Art. 8.6) was not implemented — it requires a "rest ledger" entity that does not exist in the domain today; it does not block false positives
- **Origin in Code**: SPEC 0015 - Weekly Rest Violation Enforcement

### Later services already allocated (Articles 6 to 8)
- **Requirement**: allocating a service requires that it complies and that each later service already allocated to the driver keeps complying. Each later one is revalidated with the same rules, without and with the new one.
- **Horizon**: those whose own evaluation sees the new one: two calendar weeks in both time zones (`LegalComplianceEvaluation.LookbackWindow`).
- **Blocks** only if, because of the new one, a critical rule starts being infringed, or it was already infringed and the figure moves further from the limit. A later one that is already illegal or forced and does not get worse does not block. Warnings do not block.
- **Force**: it can be done with a reason (ADR 0015). The affected later one is marked “Forzado por el servicio #T” (Forced by service #T); the mark clears itself if T is cancelled or changes driver.
- **Implementation**: `LaterServicesEvaluation.Evaluate` ← `LegalValidationService`; `ForcedConsequences`; `Trip.ForcedByTrips`
- **Origin in Code**: SPEC 0063, ADR 0016

---

## Rule 7: Return to Operating Base (Amendment 2020/1054)

### Rule 7: Return to Operating Base
- **Requirement**: The driver must be able to return to their operating base (`Driver.HomeLocation`) or complete a trip whose origin/destination is that location, at least once every 28 days (4 weeks) in a row.
- **Source**: Amendment (UE) 2020/1054 to Regulation CE 561/2006 (international passenger transport).
- **Status**: Non-blocking — verification service (`IReturnToBaseService.CheckCompliance`) available for reporting/alerting, it does not block `AllocateDriver`. Blocking integration is left for a future SPEC.
- **Possible states**: Compliant, NonCompliant, NotVerifiable (if the driver has no `HomeLocation` set, or does not exist).
- **Implementation**: `ReturnToBaseService.CheckCompliance(driverId, asOf)`, `ReturnToBaseCheck` (VO), `ReturnToBaseStatus` (enum).
- **Origin in Code**: SPEC 0016 - Return-to-Base Tracking

---

## Summary of Time Limits

| Type | Maximum | Rest Reset | Article |
|------|--------|----------------|---------|
| Driving Period | 4.5 h | 45 min | Article 7 |
| Daily Driving Time | 9 h (10 h, 2x/week) | 11 h | Article 6 |
| Weekly Driving Time | 56 h | Weekend | Article 6 |
| Driving in two calendar weeks | 90 h | Following week | Article 6 |
| Daily Rest Period | Min 11 h (9 h reduced, 3x) | N/A | Article 8 |
| Weekly Rest Period | Min 45 h (24 h reduced + comp.) | 2 weeks | Article 8 |

---

## Implementation in the Code

All these rules are centralised in the Value Object `DrivingLimits` (located in `src/Core/ValueObjects/DrivingLimits.cs`):

```
DrivingLimits
├── Current Properties
│   ├── DrivingToday: TimeSpan
│   ├── DrivingThisWeek: TimeSpan
│   ├── DrivingLastTwoWeeks: TimeSpan
│   └── DrivingSinceLastBreak: TimeSpan (new in SPEC 0005)
│
├── Availability Methods
│   ├── AvailableToday(): TimeSpan
│   ├── AvailableThisWeek(): TimeSpan
│   ├── AvailableInTwoWeeks(): TimeSpan
│   └── RemainingContinuousDrivingTime: TimeSpan (new in SPEC 0005)
│
├── Validation Methods
│   ├── CanDrive(TimeSpan tripDuration): bool
│   ├── CanDriveContinuous(TimeSpan tripDuration): bool (new in SPEC 0005)
│   ├── NeedsDailyRest(): bool
│   └── NeedsWeeklyRest(): bool
│
└── CE 561/2006 Constants
    ├── MaxDailyDriving = 9h
    ├── MaxDailyDrivingExtended = 10h
    ├── MaxWeeklyDriving = 56h
    ├── MaxBiWeeklyDriving = 90h
    ├── MaxContinuousDriving = 4.5h (new in SPEC 0005)
    ├── MandatoryContinuousBreak = 45min (new in SPEC 0005)
    ├── DailyRestNormal = 11h
    ├── DailyRestReduced = 9h
    ├── WeeklyRestNormal = 45h
    └── WeeklyRestReduced = 24h
```

---

## Validation in the Allocation Pipeline

The driver allocation flow (`DriverAllocationService`) uses these rules:

1. **Fetch History**: Gets the driver's driving history (two calendar weeks in both time zones) via `DrivingRecordRepository`
2. **Calculate Limits**: Builds a `DrivingLimits` object from the history
3. **Validate Trip**: Calls `limits.CanDrive(tripDuration)` (which internally checks all 6 rules)
4. **Decision**:
   - If `CanDrive() == true`: Allocates driver and persists
   - If `CanDrive() == false`: Throws `ComplianceException` with specific violations

---

## Testing Strategy

Each rule has unit test coverage in `Tests/Core.Tests/ValueObjects/DrivingLimitsTests.cs`:

- Tests for maximum limits (boundary conditions)
- Tests for resets after rest periods
- Tests for several rules at the same time
- Tests for the 10h daily exception
- **Goal**: >95% coverage in critical code

---

## Service Type Classification (Trip Service Type)

Each `Trip` must be classified at the moment it is created with one of the service types defined in the `ServiceType` enum:

### ServiceType: Regular
- Regular point-to-point route, non-recurring or recurring but independent
- A `Trip` with `ServiceType.Regular` is NOT linked to a `Circuit`

### ServiceType: Discretionary
- On-demand service, transport without a predefined fixed route
- Classification available for future refinements of validation

### ServiceType: Circuit
- Part of a multi-stage trip (multi-day or a sequence of stops)
- **Invariant rule**: A `Trip` with `ServiceType.Circuit` MUST have a `CircuitId` set
- `Circuit.AddTrip(trip)` validates and throws `InvalidOperationException` if `trip.ServiceType != ServiceType.Circuit`
- Ensures two-way consistency between the Trip's property and its membership of the Circuit

### ServiceType: AirportTransfer
- Transport from/to an airport
- Classification available for future refinements of validation

**Rule 1 (Trip Type Mandatory)**: Every `Trip` is born classified — there is no trip "without a type". The `Trip.ServiceType` property is non-nullable and required in the constructor.

**Rule 2 (Circuit Invariant)**: If a `Trip` belongs to a `Circuit`, its `ServiceType` MUST be `Circuit`. The validation happens in `Circuit.AddTrip` as the joining point between both entities.

**Rule 3 (Vehicle Homologation - Deferred)**: The question "which `Vehicle` can perform which `ServiceType`?" is explicitly out of scope (same as SPEC 0001). There is no domain model for the homologation of vehicles by service type. This is documented as known debt, not as a silent omission — see the note in RF5 of SPEC 0017.

**Implementation**: `src/Core/Enums/ServiceType.cs`, `Trip.ServiceType` property, `Circuit.AddTrip` invariant check.

**Origin in Code**: SPEC 0017 - Trip Service Type Classification

---

## Operational Rules (Non-Regulatory)

Internal business rules of Fleet DispatchGuard, not derived from CE 561/2006 but just as blocking in the allocation flow.

### Rule: No Double-Booking of Driver/Vehicle

A `Driver` or a `Vehicle` cannot be allocated to two active (not `Cancelled`) `Trip`s whose schedule (`TimeWindow`) overlaps. The check happens in `ScheduleConflictService`, invoked by `DriverAllocationService.AllocateDriver` AFTER validating CE561 compliance/vehicle eligibility and BEFORE persisting the allocation (fail-fast, no partial allocations). If there is a conflict, `ScheduleConflictException` is thrown with the IDs of the trips in conflict.

Adjacent trips (the end of one coincides with the start of another, with no real overlap) do NOT count as a conflict — it uses the same semantics of `TimeWindow.OverlapsWith` that already governs `Circuit.AddTrip`.

Two requests at the same time do not skip the rule: reading, validating and saving is atomic per service, driver and vehicle in the database, and the second one is validated against what the first one saved (SPEC 0065, ADR 0018).

**Implemented in**: SPEC 0013 (`src/Application/Services/ScheduleConflictService.cs`, `src/Core/ValueObjects/ScheduleConflictCheck.cs`, `src/Core/Exceptions/ScheduleConflictException.cs`).

---

### Rule: Active and Available Driver

A `Driver` is not allocated if they are inactive or if they have an unavailability, of whatever type, that overlaps with the service's `[departure, arrival)`. No margin before or after: if it ends exactly at departure or starts exactly at arrival, it can be done. It is checked before CE 561/2006, against what is saved, and it cannot be forced. The same criterion decides the candidate searches and the warnings of the Control centre. Putting on leave or deactivating does not remove the services already allocated.

**Implemented in**: SPEC 0066, ADR 0019 (`src/Core/ValueObjects/DriverEligibilityCheck.cs`, `src/Core/Exceptions/DriverUnavailableException.cs`).

---

### Rule: Agenda of Drivers and Vehicles

The agenda is the unavailabilities of a driver (annual leave, sick leave, weekly rest period, training, personal matters, other) or of a vehicle (ITV, maintenance, repair, cleaning, administrative errand, other). Each entry is a period `[start, end)`.

- Saving an entry that catches services already allocated is allowed: it warns first, removes nothing and the conflict stays visible until a person resolves it. Cancelled services do not count.
- A finished entry (`end ≤ now`) is history: it is not changed or deleted. Adding one already in the past is allowed. One in progress can be shortened, lengthened or deleted.
- “Descanso semanal” (weekly rest period) as an entry only prevents allocating: it does not declare the CE 561/2006 rest period nor change any calculation.
- It does not calculate when the ITV is due nor its frequency (RD 920/2017).

**Implemented in**: SPEC 0078 (`src/Core/Entities/Base/AgendaEntryRules.cs`, `src/Application/Services/Resources/AffectedTripsQuery.cs`).

---

## Historical Updates

| SPEC | Date | Change |
|------|-------|--------|
| 0005 | 2026-07-01 | Added Rule 1 (Driving Period) - Article 7 |
| 0013 | 2026-07-02 | Added Operational Rule: No Double-Booking of Driver/Vehicle |
| 0078 | 2026-11-24 | Added Operational Rule: Agenda of Drivers and Vehicles (eligibility does not change) |
| 0066 | 2026-11-23 | Added Operational Rule: Active and Available Driver |
| 0065 | 2026-11-23 | Simultaneous allocations: no rule changes; the second request is validated against what the first one saved |
| 0064 | 2026-11-23 | Week and two calendar weeks in Madrid time and UTC; daily driving between rest periods (Articles 4 and 6) |
| 0063 | 2026-11-23 | Added revalidation of the later services already allocated (Articles 6 to 8) |

---

**Reference document**: Every developer must read this document before modifying `DrivingLimits` or adding new compliance rules.

**Compliance**: 100% compliant with CE 561/2006 as applied in Spain. Infringement = penalties of €6,000+ for the company.
