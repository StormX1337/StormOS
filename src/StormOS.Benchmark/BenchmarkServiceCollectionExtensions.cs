using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StormOS.Benchmark.Engine;
using StormOS.Benchmark.Workloads;
using StormOS.Core.Benchmark;

namespace StormOS.Benchmark;

/// <summary>Dependency injection registration for benchmarks.</summary>
public static class BenchmarkServiceCollectionExtensions
{
    /// <summary>Registers synthetic benchmarks and the engine.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="includeGaming">Whether the gaming benchmark runner (needs frame capture) is registered.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddStormBenchmarks(this IServiceCollection services, bool includeGaming)
    {
        services.AddSingleton<IBenchmark, CpuBenchmark>();
        services.AddSingleton<IBenchmark, MemoryBenchmark>();
        services.AddSingleton<IBenchmark, DiskBenchmark>();
        services.AddSingleton<IBenchmark, GpuBenchmark>();
        services.AddSingleton<IBenchmark, NetworkBenchmark>();
        services.TryAddSingleton<BenchmarkEngine>();
        if (includeGaming)
        {
            services.TryAddSingleton<GamingBenchmarkRunner>();
        }

        return services;
    }
}
