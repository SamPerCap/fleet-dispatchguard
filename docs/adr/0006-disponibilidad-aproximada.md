# ADR 0006 — Approximate daily availability

- **Status:** accepted · **Origin:** SPEC 0030

## Context
The real working day includes periods of availability and other work that the system does not know about.

## Decision
The availability of a day is the span from the first start to the last finish, which never exceeds 24 h minus the applicable daily rest period (13 h or 15 h). It is calculated only from driving and unavailabilities.

## Consequences
- It is a fast and explainable filter to discard candidates; it does not replace the CE 561/2006 validation.
