# ADR 0007 — Candidates: hard legal filters, a ranking by proximity that never excludes

- **Status:** accepted · **Origin:** SPECs 0031, 0032 (user decision, 2026-11-16)

## Context
Faced with a sick leave, the dispatcher needs few options, all legal and with an understandable reason.

## Decision
- **3** candidates are proposed by default (configurable).
- Hard filters: active, no unavailability, no overlap, availability (ADR 0006) and CE 561/2006. Warnings are kept.
- Order by proximity of the base to the origin/destination of the service in bands of 5/10/20/40 km (Euclidean distance in M1). The 40 km radius **does not exclude**: the distant ones and those without a base go at the end, with their reason.
- Vehicles: position = destination of their last trip (7 days) → base → unknown.
- The ranking comes first, and CE 561 is validated in that order only until N are found.

## Consequences
- The dispatcher is never left without options because of a proximity rule.
- Checking that they arrive on time requires real distances (ADR 0009, M2).
