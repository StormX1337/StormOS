using StormOS.Core.Optimization;
using StormOS.Core.Power;
using StormOS.Core.Startup;
using StormOS.Windows.RegistryAccess;

namespace StormOS.Optimization.Tests;

internal sealed class InMemoryRegistry : IRegistryAccess
{
    private readonly Dictionary<(RegistryHive, string, string), RegistryValue> _values = [];

    public bool FailWrites { get; set; }

    public RegistryValue? GetValue(RegistryHive hive, string keyPath, string valueName) =>
        _values.TryGetValue((hive, keyPath.ToUpperInvariant(), valueName.ToUpperInvariant()), out var value) ? value : null;

    public void SetValue(RegistryHive hive, string keyPath, string valueName, RegistryValue value)
    {
        if (FailWrites)
        {
            throw new UnauthorizedAccessException("denied");
        }

        _values[(hive, keyPath.ToUpperInvariant(), valueName.ToUpperInvariant())] = value;
    }

    public void DeleteValue(RegistryHive hive, string keyPath, string valueName) =>
        _values.Remove((hive, keyPath.ToUpperInvariant(), valueName.ToUpperInvariant()));

    public IReadOnlyList<string> GetValueNames(RegistryHive hive, string keyPath) =>
        _values.Keys.Where(k => k.Item1 == hive && string.Equals(k.Item2, keyPath, StringComparison.OrdinalIgnoreCase)).Select(k => k.Item3).ToList();

    public IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string keyPath) => [];
}

internal sealed class InMemoryJournal : IOptimizationJournal
{
    private readonly Dictionary<Guid, OptimizationRecord> _records = [];

    public IReadOnlyCollection<OptimizationRecord> All => _records.Values;

    public Task SaveAsync(OptimizationRecord record, CancellationToken cancellationToken = default)
    {
        _records[record.Id] = record;
        return Task.CompletedTask;
    }

    public Task<OptimizationRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_records.GetValueOrDefault(id));

    public Task<IReadOnlyList<OptimizationRecord>> ListAsync(int limit = 200, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<OptimizationRecord>>(_records.Values.OrderByDescending(r => r.Timestamp).Take(limit).ToList());

    public Task<IReadOnlyList<OptimizationRecord>> ListActiveAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<OptimizationRecord>>(_records.Values.Where(r => r.Rollback == RollbackStatus.Available).OrderByDescending(r => r.Timestamp).ToList());
}

internal sealed class FakePowerPlans : IPowerPlanService
{
    public List<PowerPlan> Plans { get; } =
    [
        new() { Id = KnownPowerSchemes.Balanced, Name = "Balanced" },
        new() { Id = KnownPowerSchemes.HighPerformance, Name = "High performance" },
    ];

    public Guid Active { get; set; } = KnownPowerSchemes.Balanced;

    public IReadOnlyList<PowerPlan> GetPlans() => Plans.Select(p => p with { IsActive = p.Id == Active }).ToList();

    public Guid GetActivePlanId() => Active;

    public void SetActivePlan(Guid schemeId) => Active = Plans.Any(p => p.Id == schemeId) ? schemeId : throw new InvalidOperationException("unknown plan");

    public Guid DuplicatePlan(Guid templateId)
    {
        var id = Guid.NewGuid();
        Plans.Add(new PowerPlan { Id = id, Name = "Ultimate Performance" });
        return id;
    }

    public void DeletePlan(Guid schemeId) => Plans.RemoveAll(p => p.Id == schemeId);

    public string? GetEffectivePowerMode() => null;
}

internal sealed class FakeStartup : IStartupManager
{
    public Dictionary<string, bool> Entries { get; } = new() { ["RegistryUserRun|Discord"] = true, ["RegistryMachineRun|Vendor"] = true };

    public Task<IReadOnlyList<StartupEntry>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StartupEntry>>(Entries.Select(e => new StartupEntry { Id = e.Key, IsEnabled = e.Value }).ToList());

    public bool? IsEnabled(string entryId) => Entries.TryGetValue(entryId, out var enabled) ? enabled : null;

    public void SetEnabled(string entryId, bool enabled) => Entries[entryId] = enabled;
}
