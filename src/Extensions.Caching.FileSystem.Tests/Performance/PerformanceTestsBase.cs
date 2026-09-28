using System.Diagnostics;
using System.Security.Cryptography;

namespace Eryri.Extensions.Caching.FileSystem.Tests.Performance;

[TestFixture, Parallelizable(ParallelScope.All)]
public abstract class PerformanceTestsBase
{
    protected const int EntriesCount = 1000;
    protected void PrintLatency(long[] ticks)
    {
        Array.Sort(ticks);

        Console.WriteLine($"P01: {MsAt(0.01):F3} ms");
        Console.WriteLine($"P10: {MsAt(0.10):F3} ms");
        Console.WriteLine($"P50: {MsAt(0.50):F3} ms");
        Console.WriteLine($"P95: {MsAt(0.95):F3} ms");
        Console.WriteLine($"P99: {MsAt(0.99):F3} ms");

        double MsAt(double percentile) => ticks[(int)Math.Ceiling(percentile * ticks.Length) - 1] * 1000.0 / Stopwatch.Frequency;
    }
}
