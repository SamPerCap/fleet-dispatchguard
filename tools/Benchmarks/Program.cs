using BenchmarkDotNet.Running;
using DispatchGuard.Benchmarks;

// Run in Release: dotnet run -c Release --project tools/Benchmarks -- --filter "*"
BenchmarkSwitcher.FromAssembly(typeof(ComplianceBenchmarks).Assembly).Run(args);
