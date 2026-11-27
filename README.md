# Fleet DispatchGuard

Allocation of drivers and buses that checks the driving-time regulations **before** it saves anything.

A bus company allocates a driver and a vehicle every day to regular services, discretionary services, circuits and airport
transfers. The hard part is not finding somebody free, but finding somebody free **who may legally drive**: Regulation (EC)
No 561/2006 limits the driving period, the daily, weekly and two-week driving time, and requires daily and weekly rest periods.
A mistake is found at a roadside check or with the bus stopped.

Fleet DispatchGuard validates every allocation against those rules and rejects it, saying why, before it is stored.

**Live demo:** <https://lof-web-nu.vercel.app/> — the cover explains the case and “Try” opens the dispatcher's tool on fictitious
data that resets every day.

## Thesis: it never saves an illegal allocation

Each figure below is a measurement with a date and the test or benchmark that prints it. Running the command again is the way to
check it; a different result changes the figure and the date.

| Figure | What it measures | Where | Command | Measured |
|---|---|---|---|---|
| 60 of 480 | Clashing allocations accepted: 60 bursts of 8 simultaneous requests giving one driver eight services at the same hour, over two API instances on one real PostgreSQL 17. Exactly one per burst; the other 420 are rejected as an overlap. | [`ConcurrentBurstTests`](Tests/Infrastructure.Tests/Integration/ConcurrentBurstTests.cs) | `dotnet test --filter "FullyQualifiedName~ConcurrentBurstTests"` (needs Docker) | 2026-11-26 |
| 0 of 10,102 | Disagreements between the engine and an oracle written from the text of Article 6 (driving times) that shares no code with it, over random four-week calendars around both clock changes of 2026 and an ordinary week. Article 6 only. | [`Article6Tests`](Tests/Core.Tests/ValueObjects/Article6Tests.cs) | `dotnet test --filter "FullyQualifiedName~Engine_AgreesWithAnIndependentReadingOfArticle6"` | 2026-11-26 |
| 4.4 µs | One EC 561/2006 check of one driver with two weeks of history (1.3 µs for a new driver). BenchmarkDotNet, Release, .NET 10, AMD Ryzen 7 7800X3D. | [`ComplianceBenchmarks`](tools/Benchmarks/ComplianceBenchmarks.cs) | `dotnet run -c Release --project tools/Benchmarks -- --filter "*"` | 2026-11-26 |

Earlier benchmark runs, with their scenarios: [`docs/benchmarks/`](docs/benchmarks/compliance-2026-11-23.md).

## What it checks, and in which order

`DriverAllocationService.AllocateDriver` checks, in this order, and throws without persisting at the first failure
([ADR 0001](docs/adr/0001-validacion-legal-antes-de-asignar.md)):

1. **Driver available**: active, and not on sick leave, annual leave or another unavailability during the service
   ([ADR 0019](docs/adr/0019-conductor-de-baja-o-inactivo.md)).
2. **EC 561/2006 compliance** (Articles 6–8: driving period and break, daily and weekly driving time, two consecutive weeks,
   daily and weekly rest periods, reduced ones included), **including the later services the driver already has**, which the new
   one must not make illegal ([ADR 0016](docs/adr/0016-servicios-posteriores-ya-asignados.md)). Too little history to verify the
   weekly rest period is a warning, not a block ([ADR 0008](docs/adr/0008-descanso-semanal-no-verificable.md)).
3. **Vehicle**: active, enough seats, not in the workshop or otherwise unavailable.
4. **Overlap**: driver or vehicle already busy at that time → conflict. Simultaneous requests are serialised with a database lock per service, driver and vehicle
   ([ADR 0018](docs/adr/0018-asignaciones-simultaneas.md)).

The one exception: a person can force **only** an EC 561/2006 infringement, with a mandatory reason and name. The engine validates
all the same, the attempt is audited with the rules skipped, and the service is marked “Forced”
([ADR 0015](docs/adr/0015-forzar-asignacion-con-motivo.md)). Overlap, an unsuitable vehicle and an unavailable driver are never
forced.

Every legal figure lives in one file, [`DrivingLimits.cs`](src/Core/ValueObjects/DrivingLimits.cs)
([ADR 0002](docs/adr/0002-cifras-ce561-en-una-sola-fuente.md)). Article by article, with the test of each rule:
[`docs/REGULATORY_MATRIX.md`](docs/REGULATORY_MATRIX.md).

Every allocation, rejection and ranking returns an explanation in two layers: the business one, with the figures computed, and
the technical one, with the path through the code and the ADR behind each step.

## Run it locally

Needs .NET 10, Node 24 (the CI version) and Docker.

```bash
docker-compose up -d postgres                                     # PostgreSQL 17 (needs .env with POSTGRES_PASSWORD)
dotnet ef database update --project src/Infrastructure --startup-project src/Web.Api
dotnet run --project src/Web.Api                                  # API on http://localhost:5171
dotnet run --project tools/SampleData -- --api http://localhost:5171   # demo week, idempotent
npm --prefix web install && npm --prefix web run dev              # web on http://localhost:5173
```

The demo data are a fictitious company with 15 real stops of the Madrid interurban network (CRTM)
([ADR 0012](docs/adr/0012-datos-de-ejemplo-crtm.md)). They are loaded through the public API, so they go through the same
validations as a person.

The image as it is deployed, with a database of its own (Git Bash and Docker):

```bash
docker build -t dispatchguard:local .
DISPATCHGUARD_IMAGE=dispatchguard:local DISPATCHGUARD_PORT=8491 bash deploy/test/start.sh
```

Tests:

```bash
dotnet build DispatchGuard.slnx                   # warnings are errors
dotnet test --filter "Category!=Integration"      # no Docker
dotnet test --filter "Category=Integration"       # real PostgreSQL through Testcontainers
npm --prefix web run lint && npm --prefix web test
```

## Architecture

- **.NET 10 backend**, Clean Architecture + DDD. Dependencies point inwards: `Web.Api → Application → Core ← Infrastructure`.
  - `src/Core`: entities, value objects and the pure EC 561/2006 logic (`LegalComplianceEvaluation`), with no infrastructure.
  - `src/Application`: validators that return results, the orchestrator that turns a negative result into a domain exception,
    candidate search and ranking ([ADR 0007](docs/adr/0007-ranking-de-candidatos.md)), the heuristic optimiser
    ([ADR 0013](docs/adr/0013-optimizador-heuristico-sincrono.md)).
  - `src/Infrastructure`: EF Core on PostgreSQL ([ADR 0024](docs/adr/0024-postgresql.md)), repositories, migrations.
  - `src/Web.Api`: Minimal APIs, errors as `ProblemDetails` ([ADR 0010](docs/adr/0010-api-minimal-y-problemdetails.md)),
    anonymous session ([ADR 0021](docs/adr/0021-anonymous-session.md)) and perimeter
    ([ADR 0022](docs/adr/0022-perimeter.md)).
- **React + TypeScript web** (Vite) in `web/`, with no Gantt library: legality is always decided by the backend
  ([ADR 0011](docs/adr/0011-web-react-sin-librerias-pesadas.md)). English by default, Spanish on request.
- **One repository** for the API and the web, on purpose: one deployment, one origin, changes that land whole
  ([ADR 0027](docs/adr/0027-one-repository.md)).
- **One container** that serves the API and the web ([ADR 0023](docs/adr/0023-cloud-ready.md)), deployed as described in
  [ADR 0025](docs/adr/0025-public-deployment.md).
- **Tests**: xUnit with hand-written fakes for the rules, integration tests against a real PostgreSQL 17 (Testcontainers),
  Vitest for the web rules, Playwright E2E and OWASP ZAP against the image in CI.

## Documentation

- [`docs/adr/`](docs/adr/README.md): the architecture decisions, with the alternatives discarded.
- [`docs/REGULATORY_MATRIX.md`](docs/REGULATORY_MATRIX.md): article → rule → test. A test
  ([`RegulatoryMatrixTests`](Tests/Core.Tests/Docs/RegulatoryMatrixTests.cs)) fails if a cited test does not exist, if a tagged rule
  test is missing from the matrix or if a figure differs from `DrivingLimits`. [`docs/RULES_OF_BUSINESS.md`](docs/RULES_OF_BUSINESS.md):
  rule by rule, what is implemented and what is not.
- [`docs/MUTATION.md`](docs/MUTATION.md): mutation testing of the EC 561/2006 engine (Stryker.NET), with its score and date.
- [`docs/THREAT_MODEL.md`](docs/THREAT_MODEL.md): STRIDE over what exists, each mitigation with its test; how to report a
  vulnerability: [`SECURITY.md`](SECURITY.md).
- [`docs/PROJECT_DEFINITION.md`](docs/PROJECT_DEFINITION.md): scope, decisions in force and roadmap.
- [`specs/samples/`](specs/samples/): three real SPECs, as they were closed, to show how a change is specified and verified.
- [`docs/LESSONS.md`](docs/LESSONS.md) and [`docs/RETROSPECTIVE.md`](docs/RETROSPECTIVE.md): what went wrong and what changed
  because of it.

## How it is built

With Claude Code as a coding assistant; the commits it co-wrote say so in their trailers. Nothing is accepted because the assistant
says so: one feature per session with a short SPEC, legal rules tested with their edge cases before they are written, warnings as
errors, tests against a real PostgreSQL, a review of each change, a hook that blocks the end of a session when the build or the tests
fail, and CI on every push and pull request, which also publishes a CycloneDX SBOM of the .NET and npm dependencies. The rules the
sessions follow and the full history of SPECs stay in the private working repository; three of those SPECs, as they were closed,
are in [`specs/samples/`](specs/samples/).

## Status

M1, the dispatcher's tool for one company, closed on 2026-11-17. The product closing (explanations, heuristic optimiser, hardening,
public deployment) is tracked in [`docs/PROJECT_DEFINITION.md`](docs/PROJECT_DEFINITION.md), section 5, together with the future
work: the rest of bookings, several companies with authentication, real road distances, import from an operator's existing system and a full
optimiser.

## License

This project is not open source. The code is provided for reference only; see [`LICENSE`](LICENSE).
