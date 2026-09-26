using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StormOS.Core.Frames;
using StormOS.Core.Telemetry;
using StormOS.Performance.Frames;
using StormOS.Performance.Sessions;
using StormOS.Performance.Telemetry;

namespace StormOS.Performance;

/// <summary>Dependency injection registration for the performance engine.</summary>
public static class PerformanceServiceCollectionExtensions
{
    /// <summary>Registers the telemetry hub and, optionally, frame capture and session recording.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Configuration root.</param>
    /// <param name="includeFrameCapture">Whether frame capture providers are registered (service process only).</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddStormPerformance(this IServiceCollection services, IConfiguration configuration, bool includeFrameCapture)
    {
        services.AddOptions<SamplingOptions>().Bind(configuration.GetSection(SamplingOptions.SectionName));
        services.AddOptions<FrameCaptureOptions>().Bind(configuration.GetSection(FrameCaptureOptions.SectionName));
        services.TryAddSingleton<LatencyProbe>();
        if (includeFrameCapture)
        {
            services.AddSingleton<IFrameCaptureProvider, PresentMonFrameCaptureProvider>();
            services.AddSingleton<IFrameCaptureProvider, EtwPresentFrameCaptureProvider>();
            services.TryAddSingleton<FrameCaptureCoordinator>();
            services.TryAddSingleton<SessionRecorder>();
        }

        services.TryAddSingleton(sp => ActivatorUtilities.CreateInstance<TelemetryHub>(sp));
        services.TryAddSingleton<ITelemetryHub>(sp => sp.GetRequiredService<TelemetryHub>());
        services.TryAddSingleton<MetricHistoryRecorder>();
        return services;
    }
}
