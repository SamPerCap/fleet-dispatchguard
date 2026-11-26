# ADR 0012 — Sample data with real stops, loaded through the API

- **Status:** accepted · **Origin:** SPEC 0038

## Context
A demo with data made up at random does not convince; one with data copied from a real company is not publishable.

## Decision
A fictitious company with 15 real stops of the Madrid interurban network (CRTM), transcribed by hand with the source cited and without downloading GTFS. They are loaded only through the public API, allocating each service with `allocate` in chronological order; the load is idempotent.

## Consequences
- The demo data go through the same rules as a real user: if an allocation were illegal, the seeder fails.
