using BenchmarkDotNet.Running;
using FlowEngine.Benchmarks;

// 入口：运行管线额外开销基准。
// 短跑（fast/非统计严谨）可用：
//   dotnet run --project tests/FlowEngine.Benchmarks -- --filter *PipelineOverheadBenchmark* --job short
// 若 --job short 不被支持，改用：
//   dotnet run --project tests/FlowEngine.Benchmarks -- --filter *PipelineOverheadBenchmark* --iterationCount 3 --warmupCount 1
BenchmarkRunner.Run<PipelineOverheadBenchmark>();
