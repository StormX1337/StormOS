using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StormOS.Core.Optimization;
using StormOS.Optimization.Engine;
using StormOS.Optimization.Rules.Network;
using StormOS.Optimization.Rules.Power;
using StormOS.Optimization.Rules.Processes;
using StormOS.Optimization.Rules.Services;
using StormOS.Optimization.Rules.Startup;
using StormOS.Optimization.Rules.Storage;
using StormOS.Optimization.Rules.WindowsSettings;
using StormOS.Optimization.Sessions;

namespace StormOS.Optimization;

/// <summary>Dependency injection registration for the optimization engine.</summary>
public static class OptimizationServiceCollectionExtensions
{
    /// <summary>Registers all rules and an engine for the given executor.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="executor">Executor description (app or service).</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddStormOptimization(this IServiceCollection services, OptimizationExecutor executor)
    {
        services.TryAddSingleton(executor);
        services.TryAddSingleton(ServiceKnowledgeBase.Default);
        services.TryAddSingleton<IDnsConfigurator, WindowsDnsConfigurator>();

        services.AddSingleton<IOptimizationRule, GameModeRule>();
        services.AddSingleton<IOptimizationRule, GameDvrRule>();
        services.AddSingleton<IOptimizationRule, HardwareGpuSchedulingRule>();
        services.AddSingleton<IOptimizationRule, WindowedOptimizationsRule>();
        services.AddSingleton<IOptimizationRule, MousePrecisionRule>();
        services.AddSingleton<IOptimizationRule, TrimRule>();
        services.AddSingleton<IOptimizationRule, PowerPlanRule>();
        services.AddSingleton<IOptimizationRule, UltimatePerformancePlanRule>();
        services.AddSingleton<IOptimizationRule>(sp => new StartupEntryRule(sp.GetRequiredService<Core.Startup.IStartupManager>(), machine: false));
        services.AddSingleton<IOptimizationRule>(sp => new StartupEntryRule(sp.GetRequiredService<Core.Startup.IStartupManager>(), machine: true));
        services.AddSingleton<IOptimizationRule, ServiceStartTypeRule>();
        services.AddSingleton<IOptimizationRule, DnsServersRule>();
        services.AddSingleton<IOptimizationRule, BackgroundPriorityRule>();
        services.AddSingleton<IOptimizationRule, GamePriorityRule>();
        services.AddSingleton<IOptimizationRule>(sp => new TempCleanupRule(null, sp.GetService<TimeProvider>()));

        services.TryAddSingleton<OptimizationEngine>();
        services.TryAddSingleton<IOptimizationEngine>(sp => sp.GetRequiredService<OptimizationEngine>());
        services.TryAddSingleton<ProfileSessionOptimizer>();
        return services;
    }
}
