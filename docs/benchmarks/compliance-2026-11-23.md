# Benchmark: CE 561/2006 validation (2026-11-23)

SPEC 0064. The same scenarios as [`compliance-2026-11-17.md`](compliance-2026-11-17.md), after counting the week in Madrid time and in UTC and the working day between rest periods. It measures one evaluation of `LegalComplianceEvaluation.Validate`; later services (SPEC 0063) add two evaluations for each one.

Command: `dotnet run -c Release --project tools/Benchmarks -- --filter "*"`

Output of the GitHub exporter of BenchmarkDotNet, without the bold:

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9974)
AMD Ryzen 7 7800X3D 4.20GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v4


```
| Method   | Scenario             | Mean     | Error     | StdDev    | Gen0   | Allocated |
|--------- |--------------------- |---------:|----------:|----------:|-------:|----------:|
| Validate | Conti(...)eeded [25] | 6.475 μs | 0.0881 μs | 0.0781 μs | 0.2975 |   14.7 KB |
| Validate | NewDriver            | 2.213 μs | 0.0420 μs | 0.0501 μs | 0.1411 |   6.94 KB |
| Validate | RegularTwoWeeks      | 5.982 μs | 0.0928 μs | 0.0868 μs | 0.2899 |   14.4 KB |

1 μs = 0.000001 s. A single development machine; the figures give the order of magnitude, they are not an SLA.
