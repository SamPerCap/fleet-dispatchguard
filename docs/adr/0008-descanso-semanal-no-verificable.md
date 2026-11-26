# ADR 0008 — Insufficient history: a "not verifiable" weekly rest period is a warning

- **Status:** accepted · **Origin:** SPEC 0040 (revokes 0015 CA4)

## Context
With the history derived from services (ADR 0003), every new driver lacked a recorded weekly rest period and was blocked.

## Decision
If the known history does not cover 6×24 h, the absence of a weekly rest period cannot be proved: it is a **warning** that asks to confirm with the tachograph, not a block. It is a critical infringement only when the history spans the whole window without a rest.

## Consequences
- New drivers can be allocated and the warning reaches the web.
- Only `Critical` infringements block; `Warning`s travel with the result.
