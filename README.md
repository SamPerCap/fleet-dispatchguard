# Fleet DispatchGuard

Allocates drivers and buses, and rejects any allocation that breaks the driving-time rules of Regulation (EC) No 561/2006
**before** it is saved, saying why.

**Live demo:** <https://fleet-dispatchguard.vercel.app/> — press “Try”, allocate a driver to a service and see why an illegal
allocation is rejected. Fictitious data, reset every day.

## Three measured figures

| Figure | What it measures | Command (test) | Measured |
|---|---|---|---|
| 60 of 480 | Clashing allocations accepted from 60 bursts of 8 simultaneous requests, two API instances, one real PostgreSQL 17: exactly one per burst. | `dotnet test --filter "FullyQualifiedName~ConcurrentBurstTests"` ([`ConcurrentBurstTests`](Tests/Infrastructure.Tests/Integration/ConcurrentBurstTests.cs), needs Docker) | 2026-11-26 |
| 0 of 10,102 | Disagreements with an independent oracle of Article 6 over random four-week calendars. | `dotnet test --filter "FullyQualifiedName~Engine_AgreesWithAnIndependentReadingOfArticle6"` ([`Article6Tests`](Tests/Core.Tests/ValueObjects/Article6Tests.cs)) | 2026-11-26 |
| 4.4 µs | One EC 561/2006 check of a driver with two weeks of history (BenchmarkDotNet, AMD Ryzen 7 7800X3D). | `dotnet run -c Release --project tools/Benchmarks -- --filter "*"` ([`ComplianceBenchmarks`](tools/Benchmarks/ComplianceBenchmarks.cs)) | 2026-11-26 |

## Architecture

```
web (React + TS) ──HTTP──► Web.Api (Minimal APIs) ──► Application ──► Core (EC 561/2006 rules, no I/O)
                                                                  ▲
                                         Infrastructure (EF Core, PostgreSQL) ─┘
```

Each allocation checks, in order: driver available → EC 561/2006 (including the driver's later services) → vehicle → overlap.
The first failure throws and nothing is stored.

**Stack:** .NET 10, EF Core, PostgreSQL 17, React + TypeScript (Vite), xUnit + Testcontainers, Vitest, Playwright, one container.

## Read more

- [`docs/overview.md`](docs/overview.md): the full picture — the checks and the one exception, running it locally, architecture,
  how it is built, status.
- [`docs/REGULATORY_MATRIX.md`](docs/REGULATORY_MATRIX.md): article → rule → test.
- [`docs/adr/`](docs/adr/README.md): the architecture decisions, with the alternatives discarded.
- [`docs/THREAT_MODEL.md`](docs/THREAT_MODEL.md) and [`SECURITY.md`](SECURITY.md).

Not open source; the code is provided for reference only, see [`LICENSE`](LICENSE).
