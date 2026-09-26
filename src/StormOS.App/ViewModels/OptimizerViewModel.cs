using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StormOS.App.Services;
using StormOS.Core.Analysis;
using StormOS.Core.Optimization;
using StormOS.Core.Power;
using StormOS.Core.Processes;
using StormOS.Core.Scan;
using StormOS.Services.Analysis;
using StormOS.Services.Optimization;
using StormOS.Services.Scan;
using StormOS.Windows.Display;

namespace StormOS.App.ViewModels;

/// <summary>An optimization rule with its detected state.</summary>
public sealed partial class RuleItem(OptimizationItem item) : ObservableObject
{
    public OptimizationItem Item => item;

    public RuleDescriptor Descriptor => item.Descriptor;

    public string Name => Descriptor.Name;

    public string Description => Descriptor.Description;

    public RiskLevel Risk => Descriptor.RiskLevel;

    public string RiskText => $"Risk: {Descriptor.RiskLevel}";

    public string Facts => string.Join(" · ", new[]
    {
        item.Executor == "service" ? "Runs in service (admin)" : "Runs as you",
        Descriptor.CanRollback ? "Reversible" : "Not reversible",
        Descriptor.RequiresRestart ? "Restart required" : null,
    }.Where(s => s is not null));

    public bool NeedsParameters => Descriptor.Parameters.Any(p => p.Required);

    public string ParameterHint => Descriptor.Id switch
    {
        "power.plan" => "Choose a plan on the Power page.",
        "startup.entry" or "startup.entry-machine" => "Toggle entries on the Startup page.",
        "network.dns" => "Test and choose DNS servers on the Network page.",
        "service.start-type" => "Suggested by the System Scan for specific services only.",
        "background.lower-priority" => "Suggested by recommendations for a specific busy program.",
        "process.game-priority" => "Applied per game from its profile while playing.",
        _ => "Needs parameters.",
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(CanApply), nameof(IsApplied), nameof(Explanation))]
    public partial RuleDetection? Detection { get; set; } = item.Descriptor.Detection;

    public string StateText => NeedsParameters ? "Per item" : Detection?.State switch
    {
        DetectionState.Applicable => $"{Detection.CurrentValue} → {Detection.TargetValue}",
        DetectionState.AlreadyApplied => "Already optimized",
        DetectionState.NotApplicable => "Not applicable",
        DetectionState.Unsupported => "Not supported",
        DetectionState.Error => "Check failed",
        _ => "Not checked",
    };

    public string Explanation => NeedsParameters ? ParameterHint : Detection?.Explanation ?? string.Empty;

    public bool CanApply => !NeedsParameters && Detection?.State == DetectionState.Applicable;

    public bool IsApplied => Detection?.State == DetectionState.AlreadyApplied;
}

/// <summary>Rules of one category.</summary>
public sealed record RuleGroup(string Title, IReadOnlyList<RuleItem> Rules);

/// <summary>A scan finding row.</summary>
public sealed record FindingItem(ScanFinding Finding)
{
    public string Title => Finding.Title;

    public string Area => Finding.Area.ToUpperInvariant();

    public ScanSeverity Severity => Finding.Severity;

    public string SeverityText => Finding.Severity.ToString().ToUpperInvariant();

    public string Description => Finding.Description;

    public string Evidence => string.Join(Environment.NewLine, Finding.Evidence.Select(e => "• " + e));

    public string Impact => Finding.Impact;

    public string SuggestedAction => Finding.SuggestedAction;

    public string RiskText => $"Risk of suggested action: {Finding.Risk}";

    public bool HasAction => Finding.RuleId is not null;
}

/// <summary>A recommendation row (Why / Evidence / Risk / Recommendation / Action).</summary>
public sealed record RecommendationItem(Recommendation Recommendation)
{
    public string Title => Recommendation.Title;

    public string Why => Recommendation.Why;

    public string Evidence => string.Join(Environment.NewLine, Recommendation.Evidence.Select(e => "• " + e));

    public string Advice => Recommendation.Advice;

    public RiskLevel Risk => Recommendation.Risk;

    public string Meta => $"Risk {Recommendation.Risk} · confidence {Recommendation.Confidence} · source {(Recommendation.Source == "ai" ? "AI analysis (cloud)" : "local rules")}";

    public bool HasAction => Recommendation.Action is not null;

    public string ActionLabel => Recommendation.Action?.Label ?? string.Empty;
}

/// <summary>System scan, explainable recommendations and the optimization catalog.</summary>
public sealed partial class OptimizerViewModel(
    ChangeService changes,
    SystemScanService scanner,
    AnalysisService analysis,
    TelemetryFeed feed,
    IPowerPlanService power,
    IProcessInspector processes,
    NotificationService notifications,
    DialogService dialogs) : PageViewModel
{
    public ObservableCollection<RuleGroup> Groups { get; } = [];

    public ObservableCollection<FindingItem> Findings { get; } = [];

    public ObservableCollection<ScanCheck> Checks { get; } = [];

    public ObservableCollection<RecommendationItem> Recommendations { get; } = [];

    [ObservableProperty]
    public partial string? CatalogNote { get; set; }

    [ObservableProperty]
    public partial bool HasScan { get; set; }

    [ObservableProperty]
    public partial string ScanSummary { get; set; } = "Run a System Scan to check Windows, drivers, storage, power, startup, network and gaming settings.";

    [ObservableProperty]
    public partial ScanSeverity ScanOverall { get; set; }

    [ObservableProperty]
    public partial string RecommendationSummary { get; set; } = "Recommendations are derived from the last minutes of measured telemetry.";

    [ObservableProperty]
    public partial bool IncludeAi { get; set; }

    public bool AiAvailable => analysis.AiAvailable;

    public override void Activate(object? parameter)
    {
        _ = parameter switch
        {
            "scan" => ScanThenLoadAsync(),
            "quick" => QuickThenLoadAsync(),
            _ => LoadCatalogAsync(false),
        };
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadCatalogAsync(true);

    [RelayCommand]
    private async Task ScanAsync()
    {
        IsBusy = true;
        try
        {
            var progress = new Progress<string>(stage => StatusMessage = stage);
            var result = await Task.Run(() => scanner.RunAsync(progress));
            Findings.Clear();
            foreach (var finding in result.Findings.OrderByDescending(f => f.Severity).ThenBy(f => f.Area, StringComparer.Ordinal))
            {
                Findings.Add(new FindingItem(finding));
            }

            Checks.Clear();
            foreach (var check in result.Checks)
            {
                Checks.Add(check);
            }

            HasScan = true;
            ScanOverall = result.Overall;
            ScanSummary = result.Findings.Count == 0
                ? $"Healthy. {result.Checks.Count} checks passed."
                : $"{ScanOverall}: {result.Findings.Count(f => f.Severity == ScanSeverity.Attention)} need attention, {result.Findings.Count(f => f.Severity == ScanSeverity.Warning)} warnings · {result.Checks.Count(c => c.Passed)} of {result.Checks.Count} checks passed.";
        }
        finally
        {
            IsBusy = false;
            StatusMessage = null;
        }

        await AnalyzeAsync();
    }

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        var snapshots = feed.History(300);
        if (snapshots.Count < 10)
        {
            RecommendationSummary = "Not enough telemetry yet. Keep STORM OS open for a minute, then analyze again.";
            return;
        }

        string? plan = null;
        try
        {
            var active = power.GetActivePlanId();
            plan = KnownPowerSchemes.Describe(active) ?? power.GetPlans().FirstOrDefault(p => p.Id == active)?.Name;
        }
        catch (InvalidOperationException)
        {
        }

        var refresh = DisplayEnumerator.GetMonitors().FirstOrDefault(m => m.IsPrimary)?.RefreshRateHz;
        var busy = processes.Snapshot().Count(p => p.CpuPercent >= 5 && p.SessionId != 0);
        var window = AnalysisWindowBuilder.Build(snapshots, powerPlan: plan, refreshRateHz: refresh, busyBackgroundProcesses: busy);
        var results = await analysis.AnalyzeAsync(window, IncludeAi);
        Recommendations.Clear();
        foreach (var recommendation in results.OrderByDescending(r => r.Confidence))
        {
            Recommendations.Add(new RecommendationItem(recommendation));
        }

        RecommendationSummary = results.Count == 0
            ? $"No issues found in the last {Core.Common.Units.FormatDuration(window.To - window.From)} of measurements."
            : $"{results.Count} recommendation(s) from {window.SampleCount} samples ({Core.Common.Units.FormatDuration(window.To - window.From)}). Nothing is changed without your confirmation.";
    }

    [RelayCommand]
    private async Task QuickOptimizeAsync()
    {
        IsBusy = true;
        StatusMessage = "Checking safe optimizations…";
        List<RuleItem> plan;
        try
        {
            await LoadCatalogAsync(true);
            plan = Groups.SelectMany(g => g.Rules)
                .Where(r => r.CanApply && r.Descriptor.CanRollback && !r.Descriptor.RequiresRestart && r.Risk <= RiskLevel.Low)
                .ToList();
        }
        finally
        {
            IsBusy = false;
            StatusMessage = null;
        }

        if (plan.Count == 0)
        {
            notifications.Success("All low-risk, reversible optimizations are already in place.", "Quick optimize");
            return;
        }

        var text = "These low-risk changes are reversible and need no restart:" + Environment.NewLine + Environment.NewLine
            + string.Join(Environment.NewLine, plan.Select(p => $"• {p.Name}: {p.Detection!.CurrentValue} → {p.Detection.TargetValue}"))
            + Environment.NewLine + Environment.NewLine + "A backup is taken before each change; restore any time from History.";
        if (!await dialogs.ConfirmAsync("Quick optimize", new Microsoft.UI.Xaml.Controls.TextBlock { Text = text, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap, MaxWidth = 480 }, "Apply all"))
        {
            return;
        }

        IsBusy = true;
        var applied = 0;
        try
        {
            foreach (var rule in plan)
            {
                StatusMessage = $"Applying {rule.Name}…";
                if (await changes.ApplyConfirmedAsync(rule.Item, null) is { Outcome: OptimizationOutcome.Applied })
                {
                    applied++;
                }
            }
        }
        finally
        {
            IsBusy = false;
            StatusMessage = null;
        }

        notifications.Success($"{applied} of {plan.Count} changes applied and verified.", "Quick optimize");
        await LoadCatalogAsync(true);
    }

    [RelayCommand]
    private async Task RestoreAllAsync()
    {
        if (await changes.RestoreAllWithConsentAsync() > 0)
        {
            await LoadCatalogAsync(true);
        }
    }

    public async Task ApplyRuleAsync(RuleItem rule)
    {
        if (await changes.ApplyWithConsentAsync(rule.Descriptor.Id, null) is not null)
        {
            await RedetectAsync(rule);
        }
    }

    public async Task FixFindingAsync(FindingItem finding)
    {
        if (finding.Finding.RuleId is not { } ruleId)
        {
            return;
        }

        if (await changes.ApplyWithConsentAsync(ruleId, finding.Finding.RuleParameters, finding.Finding.Description) is { Outcome: OptimizationOutcome.Applied })
        {
            Findings.Remove(finding);
            changes.Invalidate();
        }
    }

    public async Task ApplyRecommendationAsync(RecommendationItem item)
    {
        if (item.Recommendation.Action is not { } action)
        {
            return;
        }

        if (await changes.ApplyWithConsentAsync(action.RuleId, action.Parameters, item.Why) is { Outcome: OptimizationOutcome.Applied })
        {
            Recommendations.Remove(item);
            changes.Invalidate();
        }
    }

    private async Task RedetectAsync(RuleItem rule)
    {
        changes.Invalidate();
        var (items, _) = await changes.CatalogAsync(true);
        rule.Detection = items.FirstOrDefault(i => i.Descriptor.Id == rule.Descriptor.Id && i.Executor == rule.Item.Executor)?.Descriptor.Detection;
    }

    private async Task ScanThenLoadAsync()
    {
        await LoadCatalogAsync(false);
        await ScanAsync();
    }

    private async Task QuickThenLoadAsync() => await QuickOptimizeAsync();

    private async Task LoadCatalogAsync(bool refresh)
    {
        var (items, note) = await changes.CatalogAsync(refresh);
        CatalogNote = note;
        Groups.Clear();
        foreach (var group in items.GroupBy(i => i.Descriptor.Category).OrderBy(g => g.Key))
        {
            Groups.Add(new RuleGroup(CategoryTitle(group.Key), group.Select(i => new RuleItem(i)).OrderBy(r => r.NeedsParameters).ThenBy(r => r.Name, StringComparer.CurrentCulture).ToList()));
        }
    }

    private static string CategoryTitle(OptimizationCategory category) => category switch
    {
        OptimizationCategory.Power => "POWER",
        OptimizationCategory.GamingServices => "GAMING SERVICES",
        OptimizationCategory.Startup => "STARTUP",
        OptimizationCategory.BackgroundProcesses => "BACKGROUND PROCESSES",
        OptimizationCategory.Network => "NETWORK",
        OptimizationCategory.WindowsSettings => "WINDOWS SETTINGS",
        OptimizationCategory.Game => "GAME",
        OptimizationCategory.Storage => "STORAGE",
        OptimizationCategory.Process => "PROCESS",
        OptimizationCategory.Services => "SERVICES",
        _ => category.ToString().ToUpperInvariant(),
    };
}
