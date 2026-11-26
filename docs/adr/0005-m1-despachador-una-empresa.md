# ADR 0005 — First milestone: the dispatcher's tool for one company

- **Status:** accepted · **Origin:** `docs/PROJECT_DEFINITION.md §4–5` (2026-11-13/14)

## Context
The project is a portfolio: one finished, demonstrable use case is worth more than many half-done pieces.

## Decision
M1 revolves around the **"last-minute sick leave"** case: one company, data through the API (no import from other systems), React + TypeScript web, no authentication (local/demo use). Bookings (`Booking`), multi-company, Google Maps and the optimiser are left for M2–M4.

## Consequences
- In M1 services are created directly as `Trip`.
- Authentication arrives in M2 together with the tenant in the claim.
