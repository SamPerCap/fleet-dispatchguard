# ADR 0009 — Real road distances with a cache

- **Status:** accepted (implementation in M2) · **Origin:** a backlog SPEC (not published)

## Context
The straight-line distance is enough to order candidates, but not to know whether they arrive on time.

## Decision
A road-routing provider (to be chosen), with a persistent cache (`CachedRoute`) and a Euclidean prefilter so as not to query impossible routes. In M1 the ranking uses only Euclidean distance.

## Consequences
- Cost per query bounded by the cache and the prefilter.
