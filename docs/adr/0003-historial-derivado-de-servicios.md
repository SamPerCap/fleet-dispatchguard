# ADR 0003 — Driving history derived from the allocated services

- **Status:** accepted, open to review · **Origin:** SPECs 0004, 0034

## Context
Validating CE 561/2006 requires knowing what was driven in the last 14 days. In M1 there is no integration with the tachograph.

## Decision
`DrivingRecordRepository` rebuilds the driving periods from the driver's allocated, in-progress or completed `Trip`s. There is no table of records of its own.

## Consequences
- Zero duplicated data and nothing to synchronise in M1.
- A newly registered driver has no history: their weekly rest period is "not verifiable" (ADR 0008).
- It will be reviewed with the import from existing systems (M3) or with real tachograph data.
