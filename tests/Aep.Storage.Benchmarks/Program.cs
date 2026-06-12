using Aep.Storage.Benchmarks;
using BenchmarkDotNet.Running;

// Run all benchmarks (or filter): dotnet run -c Release --project tests/Aep.Storage.Benchmarks -- --filter '*Read*'
BenchmarkSwitcher.FromAssembly(typeof(StoreReadBenchmarks).Assembly).Run(args);
