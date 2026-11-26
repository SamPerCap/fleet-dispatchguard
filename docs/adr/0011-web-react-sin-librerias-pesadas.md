# ADR 0011 — Dispatcher web: React + TS without a Gantt or state library

- **Status:** accepted · **Origin:** SPEC 0037

## Context
The web only needs a Gantt of the day and the incident flow; Gantt libraries weigh more than the problem.

## Decision
Vite + React 18 + TypeScript. The Gantt is drawn by hand; no state manager. Vite proxy to the API instead of CORS. A sick leave is recorded as a `SickLeave` unavailability. **Legality is always decided by the backend**: the web neither filters nor retries.

## Consequences
- Few dependencies; pure presentation rules tested with Vitest.
