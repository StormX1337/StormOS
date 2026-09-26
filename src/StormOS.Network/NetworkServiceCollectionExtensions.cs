using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StormOS.Core.Network;
using StormOS.Network.Diagnostics;

namespace StormOS.Network;

/// <summary>Dependency injection registration for network diagnostics.</summary>
public static class NetworkServiceCollectionExtensions
{
    /// <summary>Registers network diagnostics.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddStormNetwork(this IServiceCollection services)
    {
        services.AddHttpClient("storm-throughput");
        services.TryAddSingleton<NetworkDiagnostics>();
        services.TryAddSingleton<INetworkDiagnostics>(sp => sp.GetRequiredService<NetworkDiagnostics>());
        return services;
    }
}
