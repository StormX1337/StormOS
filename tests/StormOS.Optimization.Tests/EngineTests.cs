using Microsoft.Extensions.Logging.Abstractions;
using StormOS.Core.Optimization;
using StormOS.Optimization.Engine;
using StormOS.Optimization.Rules.Power;
using StormOS.Optimization.Rules.Startup;
using StormOS.Optimization.Rules.WindowsSettings;
using StormOS.Windows.RegistryAccess;

namespace StormOS.Optimization.Tests;

public sealed class EngineTests
{
    private static OptimizationEngine Engine(IEnumerable<IOptimizationRule> rules, IOptimizationJournal journal, bool service = false) =>
        new(rules, journal, new OptimizationExecutor(service ? OptimizationExecutor.Service : OptimizationExecutor.App, IsElevated: service, WindowsBuild: 26100), NullLogger<OptimizationEngine>.Instance);

    [Fact]
    public async Task Apply_Snapshot_Verify_Record_And_Restore()
    {
        var registry = new InMemoryRegistry();
        registry.SetValue(RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", new RegistryValue(RegistryKind.DWord, 1));
        var journal = new InMemoryJournal();
        using var engine = Engine([new GameDvrRule(registry)], journal);

        var applied = await engine.ApplyAsync("windows.game-dvr", null, "tester", TestContext.Current.CancellationToken);

        Assert.Equal(OptimizationOutcome.Applied, applied.Outcome);
        Assert.Equal(RollbackStatus.Available, applied.Rollback);
        Assert.Equal("On", applied.Before);
        Assert.Equal("Off", applied.After);
        Assert.Equal("tester", applied.User);
        Assert.Equal(0, registry.GetValue(RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled")!.Value);
        Assert.Equal("true", applied.Snapshot!.Get("t0.exists"));
        Assert.Equal("false", applied.Snapshot.Get("t1.exists"));

        var restored = await engine.RollbackAsync(applied.Id, "tester", TestContext.Current.CancellationToken);

        Assert.Equal(RollbackStatus.RolledBack, restored.Rollback);
        Assert.Equal(1, registry.GetValue(RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled")!.Value);
        Assert.Null(registry.GetValue(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled"));
        Assert.Empty(await journal.ListActiveAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AlreadyApplied_IsSkipped_WithoutChanges()
    {
        var registry = new InMemoryRegistry();
        var journal = new InMemoryJournal();
        using var engine = Engine([new GameModeRule(registry)], journal);

        var record = await engine.ApplyAsync("windows.game-mode", null, "tester", TestContext.Current.CancellationToken);

        Assert.Equal(OptimizationOutcome.Skipped, record.Outcome);
        Assert.Null(registry.GetValue(RegistryHive.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled"));
    }

    [Fact]
    public async Task FailedVerification_RollsBackAutomatically()
    {
        var journal = new InMemoryJournal();
        var rule = new FlakyRule();
        using var engine = Engine([rule], journal);

        var record = await engine.ApplyAsync("test.flaky", null, "tester", TestContext.Current.CancellationToken);

        Assert.Equal(OptimizationOutcome.FailedRolledBack, record.Outcome);
        Assert.Equal(RollbackStatus.RolledBack, record.Rollback);
        Assert.Equal("original", rule.State);
    }

    [Fact]
    public async Task ApplyException_RollsBack_AndNeverThrows()
    {
        var registry = new InMemoryRegistry();
        registry.SetValue(RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", new RegistryValue(RegistryKind.DWord, 1));
        registry.FailWrites = true;
        using var engine = Engine([new GameDvrRule(registry)], new InMemoryJournal());

        var record = await engine.ApplyAsync("windows.game-dvr", null, "tester", TestContext.Current.CancellationToken);

        Assert.True(record.Outcome is OptimizationOutcome.FailedRolledBack or OptimizationOutcome.FailedRollbackFailed);
        Assert.Equal(1, registry.GetValue(RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled")!.Value);
    }

    [Fact]
    public async Task Executors_OnlyExposeTheirRules()
    {
        var registry = new InMemoryRegistry();
        IOptimizationRule[] rules = [new GameModeRule(registry), new HardwareGpuSchedulingRule(registry)];
        using var app = Engine(rules, new InMemoryJournal());
        using var service = Engine(rules, new InMemoryJournal(), service: true);

        Assert.Equal(["windows.game-mode"], app.Rules.Select(r => r.Id));
        Assert.Equal(["windows.hags"], service.Rules.Select(r => r.Id));
        var record = await app.ApplyAsync("windows.hags", null, "tester", TestContext.Current.CancellationToken);
        Assert.Equal(OptimizationOutcome.Failed, record.Outcome);
    }

    [Theory]
    [InlineData("plan", "ultra", false)]
    [InlineData("other", "x", false)]
    [InlineData("plan", "high-performance", true)]
    public async Task Parameters_AreValidatedBeforeRuleCode(string key, string value, bool valid)
    {
        var power = new FakePowerPlans();
        using var engine = Engine([new PowerPlanRule(power)], new InMemoryJournal());

        var record = await engine.ApplyAsync("power.plan", new Dictionary<string, string> { [key] = value }, "tester", TestContext.Current.CancellationToken);

        if (valid)
        {
            Assert.Equal(OptimizationOutcome.Applied, record.Outcome);
            Assert.Equal(KnownPowerSchemesHigh, power.Active);
        }
        else
        {
            Assert.NotEqual(OptimizationOutcome.Applied, record.Outcome);
            Assert.Equal(StormOS.Core.Power.KnownPowerSchemes.Balanced, power.Active);
        }
    }

    [Fact]
    public async Task PowerPlan_UnavailablePlan_IsNotApplicable()
    {
        var power = new FakePowerPlans();
        power.Plans.RemoveAll(p => p.Id == KnownPowerSchemesHigh);
        using var engine = Engine([new PowerPlanRule(power)], new InMemoryJournal());

        var detection = await engine.DetectAsync("power.plan", new Dictionary<string, string> { ["plan"] = "high-performance" }, TestContext.Current.CancellationToken);

        Assert.Equal(DetectionState.NotApplicable, detection.State);
    }

    [Fact]
    public async Task RollbackAll_RestoresNewestFirst()
    {
        var power = new FakePowerPlans();
        var journal = new InMemoryJournal();
        using var engine = Engine([new PowerPlanRule(power)], journal);
        var first = await engine.ApplyAsync("power.plan", new Dictionary<string, string> { ["plan"] = "high-performance" }, "t", TestContext.Current.CancellationToken);
        await Task.Delay(5, TestContext.Current.CancellationToken);
        var second = await engine.ApplyAsync("power.plan", new Dictionary<string, string> { ["plan"] = "balanced" }, "t", TestContext.Current.CancellationToken);
        Assert.Equal(OptimizationOutcome.Applied, first.Outcome);
        Assert.Equal(OptimizationOutcome.Applied, second.Outcome);

        var restored = await engine.RollbackAllAsync("t", TestContext.Current.CancellationToken);

        Assert.Equal([second.Id, first.Id], restored.Select(r => r.Id));
        Assert.Equal(StormOS.Core.Power.KnownPowerSchemes.Balanced, power.Active);
    }

    [Fact]
    public async Task UltimatePlan_CreateAndRemove()
    {
        var power = new FakePowerPlans();
        using var engine = Engine([new UltimatePerformancePlanRule(power)], new InMemoryJournal(), service: true);

        var record = await engine.ApplyAsync("power.ultimate-plan", null, "t", TestContext.Current.CancellationToken);
        Assert.Equal(OptimizationOutcome.Applied, record.Outcome);
        Assert.Equal(3, power.Plans.Count);

        var restored = await engine.RollbackAsync(record.Id, "t", TestContext.Current.CancellationToken);
        Assert.Equal(RollbackStatus.RolledBack, restored.Rollback);
        Assert.Equal(2, power.Plans.Count);
    }

    [Fact]
    public async Task StartupRule_ScopesEntries()
    {
        var startup = new FakeStartup();
        using var app = Engine([new StartupEntryRule(startup, machine: false), new StartupEntryRule(startup, machine: true)], new InMemoryJournal());

        var user = await app.ApplyAsync("startup.entry", new Dictionary<string, string> { ["entryId"] = "RegistryUserRun|Discord", ["state"] = "disabled" }, "t", TestContext.Current.CancellationToken);
        var wrongScope = await app.ApplyAsync("startup.entry", new Dictionary<string, string> { ["entryId"] = "RegistryMachineRun|Vendor", ["state"] = "disabled" }, "t", TestContext.Current.CancellationToken);

        Assert.Equal(OptimizationOutcome.Applied, user.Outcome);
        Assert.False(startup.Entries["RegistryUserRun|Discord"]);
        Assert.Equal(OptimizationOutcome.Skipped, wrongScope.Outcome);
        Assert.True(startup.Entries["RegistryMachineRun|Vendor"]);

        await app.RollbackAsync(user.Id, "t", TestContext.Current.CancellationToken);
        Assert.True(startup.Entries["RegistryUserRun|Discord"]);
    }

    private static readonly Guid KnownPowerSchemesHigh = StormOS.Core.Power.KnownPowerSchemes.HighPerformance;

    private sealed class FlakyRule : IOptimizationRule
    {
        public string State { get; private set; } = "original";

        public string Id => "test.flaky";

        public string Name => "Flaky";

        public string Description => "Applies but never verifies.";

        public OptimizationCategory Category => OptimizationCategory.WindowsSettings;

        public RiskLevel RiskLevel => RiskLevel.Low;

        public bool RequiresAdmin => false;

        public bool CanRollback => true;

        public bool RequiresRestart => false;

        public OsSupport SupportedOs => OsSupport.Windows10OrLater;

        public IReadOnlyList<RuleParameter> Parameters => [];

        public Task<RuleDetection> DetectAsync(OptimizationContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RuleDetection(DetectionState.Applicable, State, "changed", "test"));

        public Task<RuleSnapshot> CaptureAsync(OptimizationContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RuleSnapshot { RuleId = Id, Values = new Dictionary<string, string?> { ["state"] = State } });

        public Task ApplyAsync(OptimizationContext context, CancellationToken cancellationToken = default)
        {
            State = "changed";
            return Task.CompletedTask;
        }

        public Task<RuleVerification> VerifyAsync(OptimizationContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RuleVerification(false, State, "never verifies"));

        public Task<RuleVerification> RollbackAsync(OptimizationContext context, RuleSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            State = snapshot.Get("state")!;
            return Task.FromResult(new RuleVerification(State == "original", State));
        }
    }
}
