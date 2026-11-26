# ADR 0010 — API: Minimal APIs only and errors as ProblemDetails

- **Status:** accepted · **Origin:** SPECs 0035, 0036; reason and shape completed in 0073 (user decision, 2026-11-24)

## Context
MVC controllers and Minimal APIs coexisted, and each endpoint returned errors in its own way. SPEC 0036 kept Minimal APIs without recording why; the resulting code was hard to read and lost the automatic validation of `[ApiController]`.

## Alternatives
- **Controllers:** familiar, conventions imposed by the framework, validation on by default, extensible model binding. Still fully supported. Cost: more ceremony per endpoint and no Native AOT.
- **Minimal APIs (chosen):** Microsoft's recommendation for new projects, less overhead, each endpoint declares only what it needs. Cost: structure and validation depend on the project.

Neither is safer: they share routing, middleware and authorization. With authentication (M2), a `FallbackPolicy` protects both.

## Decision
- Every endpoint is a Minimal API in `src/Web.Api/Endpoints/`: one file per resource, route table on top, one named handler per endpoint returning `TypedResults`. Handlers do HTTP only.
- `AddValidation()` enforces the DTO annotations.
- A global handler translates exceptions: compliance/eligibility → 422 with `violations`, overlap → 409 with `conflictingTripIds`, validation → 400, not found → 404, the rest → 500 without internal details. New DTOs use manual projection; JSON in PascalCase.

## Consequences
- Uniform errors that the web shows as they come; endpoints carry no `try/catch`.
- Input is validated before existence: an invalid body for an unknown id answers 400, not 404.
- The structure is a convention (`CLAUDE.md §4.1`), not something the framework enforces.
