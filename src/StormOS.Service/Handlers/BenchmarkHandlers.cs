using StormOS.Benchmark.Engine;
using StormOS.Core.Benchmark;
using StormOS.Core.Games;
using StormOS.Core.Hardware;
using StormOS.Core.Ipc;
using StormOS.Infrastructure.Ipc;
using StormOS.Security.Validation;

namespace StormOS.Service.Handlers;

/// <summary>benchmark.gaming: captures frame timing of a running game in the service (requires ETW access).</summary>
public sealed class GamingBenchmarkHandler(GamingBenchmarkRunner runner, IRunningGameDetector detector, IHardwareInventoryProvider inventory) : IpcHandler<GamingBenchmarkRequest>
{
    /// <inheritdoc />
    public override string Operation => IpcOperations.BenchmarkGaming;

    /// <inheritdoc />
    protected override string? Validate(GamingBenchmarkRequest payload) =>
        !InputValidator.IsProcessId(payload.ProcessId) ? "Invalid process id."
        : payload.DurationSeconds is < 10 or > 600 ? "Duration must be 10–600 seconds."
        : payload.WarmupSeconds is < 0 or > 120 ? "Warm-up must be 0–120 seconds."
        : payload.Label is { Length: > 32 } ? "Label is too long."
        : null;

    /// <inheritdoc />
    protected override async Task<object?> HandleAsync(GamingBenchmarkRequest payload, IIpcSession session, CancellationToken cancellationToken)
    {
        var game = detector.Running.FirstOrDefault(g => g.ProcessId == payload.ProcessId);
        var inventoryData = await inventory.GetInventoryAsync(cancellationToken).ConfigureAwait(false);
        var refresh = inventoryData.Monitors.FirstOrDefault(m => m.IsPrimary)?.RefreshRateHz;
        var result = await runner.RunAsync(new BenchmarkRunOptions
        {
            ProcessId = payload.ProcessId,
            Duration = TimeSpan.FromSeconds(payload.DurationSeconds),
            Warmup = TimeSpan.FromSeconds(payload.WarmupSeconds),
            GameId = game?.Game?.GameId,
            GameName = game?.DisplayName,
            Label = payload.Label,
        }, refresh, cancellationToken).ConfigureAwait(false);

        var gpu = inventoryData.Gpus.OrderByDescending(g => g.DedicatedMemoryBytes).FirstOrDefault()?.Name ?? "Unknown GPU";
        // The desktop app stores the result in the user's history; the service only measures.
        return result with { Hardware = $"{inventoryData.Cpu.Name} · {gpu}" };
    }
}
