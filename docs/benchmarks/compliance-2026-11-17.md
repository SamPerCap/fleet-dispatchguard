# Benchmark: CE 561/2006 validation (2026-11-17)

SPEC 0039. It measures `LegalComplianceEvaluation.Validate` — the legal check that runs for each candidate before proposing or allocating it. Scenarios in `tools/Benchmarks/ComplianceScenarios.cs`; their expected result is fixed by the `ComplianceScenariosTests` tests.

- **NewDriver:** no history → compliant with a warning of a weekly rest period that cannot be verified.
- **RegularTwoWeeks:** 20 periods (Mon–Fri, 2×4h with a 45 min break, two weeks = 80h) and a 2h service the following Monday → compliant without violations.
- **ContinuousDrivingExceeded** (`Conti(...)eeded`): same history, a 2h service right when the last stretch ends → not compliant (6h continuous).

Command: `dotnet run -c Release --project tools/Benchmarks -- --filter "*"`

Unedited output (GitHub exporter of BenchmarkDotNet):

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9974/25H2/2025Update/HudsonValley2)
AMD Ryzen 7 7800X3D 4.20GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v4


```
| Method   | Scenario             | Mean       | Error    | StdDev   | Gen0   | Allocated |
|--------- |--------------------- |-----------:|---------:|---------:|-------:|----------:|
| Validate | Conti(...)eeded [25] | 1,714.1 ns | 27.89 ns | 24.72 ns | 0.0858 |   4.23 KB |
| Validate | NewDriver            |   349.2 ns |  6.19 ns |  7.37 ns | 0.0420 |   2.08 KB |
| Validate | RegularTwoWeeks      |   937.7 ns | 15.68 ns | 21.98 ns | 0.0572 |   2.83 KB |

1 ns = 0.000000001 s. A single development machine; the figures give the order of magnitude, they are not an SLA.
