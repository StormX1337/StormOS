using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StormOS.App.Services;
using StormOS.Core.Optimization;
using StormOS.Core.Power;

namespace StormOS.App.ViewModels;

/// <summary>A power plan row.</summary>
public sealed record PowerPlanRow(PowerPlan Plan)
{
    public string Name => Plan.Name;

    public string Description => Plan.Description ?? KnownDescription(Plan.Id);

    public bool IsActive => Plan.IsActive;

    public string Status => Plan.IsActive ? "ACTIVE" : string.Empty;

    public string PlanId => Plan.Id.ToString("D", CultureInfo.InvariantCulture);

    private static string KnownDescription(Guid id) =>
        id == KnownPowerSchemes.Balanced ? "Windows default. Boosts clocks on demand and saves power when idle; the right choice for most gaming PCs."
        : id == KnownPowerSchemes.HighPerformance ? "Keeps the CPU at higher minimum states. Can help older CPUs; increases idle power and heat."
        : id == KnownPowerSchemes.UltimatePerformance ? "Removes power-saving micro-latencies. Meant for desktops; clearly higher idle power."
        : id == KnownPowerSchemes.PowerSaver ? "Limits performance to save energy."
        : "Custom plan.";
}

/// <summary>Power plans: activate (reversible) and create Ultimate Performance.</summary>
public sealed partial class PowerViewModel(IPowerPlanService power, ChangeService changes, NotificationService notifications) : PageViewModel
{
    public ObservableCollection<PowerPlanRow> Plans { get; } = [];

    [ObservableProperty]
    public partial string ActivePlan { get; set; } = "—";

    [ObservableProperty]
    public partial string PowerMode { get; set; } = "—";

    [ObservableProperty]
    public partial bool HasUltimate { get; set; }

    public override void Activate(object? parameter) => Refresh();

    [RelayCommand]
    private void Refresh()
    {
        try
        {
            var plans = power.GetPlans();
            Plans.Clear();
            foreach (var plan in plans.OrderByDescending(p => p.IsActive).ThenBy(p => p.Name, StringComparer.CurrentCulture))
            {
                Plans.Add(new PowerPlanRow(plan));
            }

            ActivePlan = plans.FirstOrDefault(p => p.IsActive)?.Name ?? "Unknown";
            HasUltimate = plans.Any(p => p.Id == KnownPowerSchemes.UltimatePerformance || p.Name.Contains("Ultimate", StringComparison.OrdinalIgnoreCase));
            PowerMode = power.GetEffectivePowerMode() ?? "Not reported by this Windows version";
        }
        catch (InvalidOperationException ex)
        {
            notifications.Warning(ex.Message, "Power plans");
        }
    }

    [RelayCommand]
    private async Task CreateUltimateAsync()
    {
        if (await changes.ApplyWithConsentAsync("power.ultimate-plan", null) is { Outcome: OptimizationOutcome.Applied })
        {
            Refresh();
        }
    }

    public async Task ActivateAsync(PowerPlanRow row)
    {
        if (row.IsActive)
        {
            return;
        }

        var reason = $"Switches from \"{ActivePlan}\" to \"{row.Name}\". {row.Description}";
        if (await changes.ApplyWithConsentAsync("power.plan", new Dictionary<string, string> { ["plan"] = row.PlanId }, reason) is { Outcome: OptimizationOutcome.Applied })
        {
            Refresh();
        }
    }
}
