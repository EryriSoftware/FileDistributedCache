using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Redis;

namespace Eryri.Extensions.Caching.FileSystem.Tests.Benchmarking;

public class StackExchange_Redis_Benchmarks : Benchmarks
{
    [ParamsAllValues]
    public bool RunAtCapacity;
    private RedisContainer? _redis;

    public override async Task Setup()
    {
        var size = RunAtCapacity ? InitialSize : 10L << 30;
        _redis = new RedisBuilder("redis:7.4")
            .WithCommand(
                "redis-server",
                "--maxmemory", $"{size / (1L << 20)}mb",
                "--maxmemory-policy", "allkeys-lru",
                "--save", "",
                "--appendonly", "no")
            .Build();
        await _redis.StartAsync(CancellationToken);
        Services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = _redis.GetConnectionString();
            options.InstanceName = "Benchmarking:";
        });
        await base.Setup();
    }

    protected override void Dispose(bool isDisposing)
    {
        if (isDisposing)
        {
            _redis?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        base.Dispose(isDisposing);
    }
}
