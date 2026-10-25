using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Eryri.Extensions.Caching.FileSystem.Tests.Benchmarking;
using Perfolizer.Horology;

namespace Eryri.Extensions.Caching.FileSystem.Tests;

[TestFixture]
public class BenchmarkTests
{
    private readonly IConfig config = ManualConfig
        .Create(DefaultConfig.Instance)
        .WithSummaryStyle(SummaryStyle.Default.WithTimeUnit(TimeUnit.Microsecond));

    [TestCase(typeof(Eryri_FileDistributedCache_Benchmarks), TestName = "Eryri_FileDistributedCache")]
    [TestCase(typeof(DamianH_FileDistributedCache_Benchmarks), TestName = "DamianH_FileDistributedCache")]
    [TestCase(typeof(DNeoSmart_Caching_Sqlite_Benchmarks), TestName = "DNeoSmart_Caching_Sqlite")]
    [TestCase(typeof(LiteDb_Extensions_Caching_Benchmarks), TestName = "LiteDb_Extensions_Caching")]
    [TestCase(typeof(Caching_FileBackedDistributedCache_Benchmarks), TestName = "Caching_FileBackedDistributedCache")]
    [TestCase(typeof(Net_DistributedFileStoreCache_Benchmarks), TestName = "Net_DistributedFileStoreCache")]
    public void Run_Benchmark(Type benchmarkType) => BenchmarkRunner.Run(benchmarkType, config);
}
