# ADR 0014 — Demo reset: endpoint with a key from configuration + reseeding through the API

- **Status:** accepted · **Origin:** SPEC 0044

## Context
The public demo has no authentication: anyone can allocate, cancel or register a sick leave. It is necessary to go back to
the week of 0038 without touching the DB by hand. Options: a scheduled task inside the API, or a protected endpoint.

## Decision
`POST /api/admin/demo/reset` deletes the business data (not the distance cache) in a transaction. It only
exists if `Demo:ResetKey` is configured (host environment variable; never in the repo) and requires the header
`X-Demo-Reset-Key`. The reseeding is done by `tools/SampleData --reset-key <k>`, **through the public API**, so that
each allocation goes through the legal validation again (ADR 0001, 0012). The frequency (e.g. a nightly cron) is
configured in the host when deploying.

## Consequences
- No hosted service and no dependency of the API on `tools/SampleData`; a manual reset and a scheduled one are the same command.
- If the key is not configured the endpoint answers 404: locally and in CI it does not exist.
- Between the deletion and the reseeding the demo is empty for a few seconds; acceptable for a demo.
