using System.Diagnostics;
using System.Globalization;
using StormOS.Core.Optimization;
using StormOS.Core.Processes;

namespace StormOS.Optimization.Rules.Processes;

/// <summary>Lowers the CPU priority of a background program while you play (session scoped, restored on rollback).</summary>
public sealed class BackgroundPriorityRule(IProcessController controller, IProcessInspector inspector) : IOptimizationRule
{
    /// <inheritdoc />
    public string Id => "background.lower-priority";

    /// <inheritdoc />
    public string Name => "Lower background program priority";

    /// <inheritdoc />
    public string Description => "Sets a busy background program to below-normal priority so the game gets CPU time first. Nothing is closed; the priority is restored on rollback and ends when the program exits.";

    /// <inheritdoc />
    public OptimizationCategory Category => OptimizationCategory.BackgroundProcesses;

    /// <inheritdoc />
    public RiskLevel RiskLevel => RiskLevel.Low;

    /// <inheritdoc />
    public bool RequiresAdmin => false;

    /// <inheritdoc />
    public bool CanRollback => true;

    /// <inheritdoc />
    public bool RequiresRestart => false;

    /// <inheritdoc />
    public OsSupport SupportedOs => OsSupport.Windows10OrLater;

    /// <inheritdoc />
    public IReadOnlyList<RuleParameter> Parameters { get; } = [new("processName", "Process name without .exe.")];

    /// <inheritdoc />
    public Task<RuleDetection> DetectAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var name = context.Require("processName");
        if (inspector.IsProtected(name))
        {
            return Task.FromResult(new RuleDetection(DetectionState.NotApplicable, "-", "Below normal", $"{name} is a protected system, security or anti-cheat process."));
        }

        var ids = ProcessIds(name);
        if (ids.Count == 0)
        {
            return Task.FromResult(new RuleDetection(DetectionState.NotApplicable, "Not running", "Below normal", $"{name} is not running."));
        }

        var all = ids.All(id => controller.GetPriority(id) is ProcessPriority.BelowNormal or ProcessPriority.Idle);
        return Task.FromResult(new RuleDetection(all ? DetectionState.AlreadyApplied : DetectionState.Applicable, all ? "Below normal" : "Normal", "Below normal", Description));
    }

    /// <inheritdoc />
    public Task<RuleSnapshot> CaptureAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var entries = ProcessIds(context.Require("processName"))
            .Select(id => (id, controller.GetPriority(id)))
            .Where(p => p.Item2 is not null)
            .Select(p => $"{p.id.ToString(CultureInfo.InvariantCulture)}:{p.Item2}");
        return Task.FromResult(new RuleSnapshot { RuleId = Id, CapturedAt = DateTimeOffset.UtcNow, Description = "Previous priorities", Values = new Dictionary<string, string?> { ["processes"] = string.Join(';', entries) } });
    }

    /// <inheritdoc />
    public Task ApplyAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        foreach (var id in ProcessIds(context.Require("processName")))
        {
            var result = controller.SetPriority(id, ProcessPriority.BelowNormal);
            if (!result.IsSuccess && result.Error.Code != Core.Common.StormErrorCodes.NotFound)
            {
                throw new InvalidOperationException(result.Error.Message);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<RuleVerification> VerifyAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var ids = ProcessIds(context.Require("processName"));
        var ok = ids.All(id => controller.GetPriority(id) is ProcessPriority.BelowNormal or null);
        return Task.FromResult(new RuleVerification(ok, ok ? "Below normal" : "Unchanged"));
    }

    /// <inheritdoc />
    public Task<RuleVerification> RollbackAsync(OptimizationContext context, RuleSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var ok = true;
        foreach (var entry in (snapshot.Get("processes") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || !Enum.TryParse<ProcessPriority>(parts[1], out var priority))
            {
                continue;
            }

            var result = controller.SetPriority(id, priority);
            ok &= result.IsSuccess || result.Error.Code == Core.Common.StormErrorCodes.NotFound;
        }

        return Task.FromResult(new RuleVerification(ok, ok ? "Restored" : "Partially restored"));
    }

    private static List<int> ProcessIds(string name)
    {
        var processes = Process.GetProcessesByName(name);
        try
        {
            return processes.Select(p => p.Id).ToList();
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }
}

/// <summary>Sets the priority of a running game process (session scoped, from its profile).</summary>
public sealed class GamePriorityRule(IProcessController controller) : IOptimizationRule
{
    /// <inheritdoc />
    public string Id => "process.game-priority";

    /// <inheritdoc />
    public string Name => "Game process priority";

    /// <inheritdoc />
    public string Description => "Raises the game's CPU priority while it runs. Games protected by anti-cheat may refuse the change; nothing else is attempted in that case.";

    /// <inheritdoc />
    public OptimizationCategory Category => OptimizationCategory.Process;

    /// <inheritdoc />
    public RiskLevel RiskLevel => RiskLevel.Low;

    /// <inheritdoc />
    public bool RequiresAdmin => false;

    /// <inheritdoc />
    public bool CanRollback => true;

    /// <inheritdoc />
    public bool RequiresRestart => false;

    /// <inheritdoc />
    public OsSupport SupportedOs => OsSupport.Windows10OrLater;

    /// <inheritdoc />
    public IReadOnlyList<RuleParameter> Parameters { get; } =
    [
        new("processId", "Game process id."),
        new("priority", "Target priority.", AllowedValues: ["normal", "aboveNormal", "high"]),
    ];

    /// <inheritdoc />
    public Task<RuleDetection> DetectAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var current = controller.GetPriority(ParseId(context));
        var target = ParsePriority(context);
        if (current is null)
        {
            return Task.FromResult(new RuleDetection(DetectionState.NotApplicable, "-", target.ToString(), "The game process is not accessible (not running or protected by anti-cheat)."));
        }

        return Task.FromResult(new RuleDetection(current == target ? DetectionState.AlreadyApplied : DetectionState.Applicable, current.Value.ToString(), target.ToString(), Description));
    }

    /// <inheritdoc />
    public Task<RuleSnapshot> CaptureAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        var current = controller.GetPriority(ParseId(context)) ?? throw new InvalidOperationException("The game process is not accessible.");
        return Task.FromResult(new RuleSnapshot { RuleId = Id, CapturedAt = DateTimeOffset.UtcNow, Description = current.ToString(), Values = new Dictionary<string, string?> { ["priority"] = current.ToString() } });
    }

    /// <inheritdoc />
    public Task ApplyAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        var result = controller.SetPriority(ParseId(context), ParsePriority(context));
        return result.IsSuccess ? Task.CompletedTask : throw new InvalidOperationException(result.Error.Message);
    }

    /// <inheritdoc />
    public Task<RuleVerification> VerifyAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        var current = controller.GetPriority(ParseId(context));
        return Task.FromResult(new RuleVerification(current == ParsePriority(context), current?.ToString() ?? "-"));
    }

    /// <inheritdoc />
    public Task<RuleVerification> RollbackAsync(OptimizationContext context, RuleSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var original = Enum.Parse<ProcessPriority>(snapshot.Get("priority") ?? nameof(ProcessPriority.Normal));
        var result = controller.SetPriority(ParseId(context), original);
        var exited = !result.IsSuccess && result.Error.Code == Core.Common.StormErrorCodes.NotFound;
        return Task.FromResult(new RuleVerification(result.IsSuccess || exited, exited ? "Process exited" : original.ToString()));
    }

    private static int ParseId(OptimizationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return int.TryParse(context.Require("processId"), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 4
            ? id
            : throw new ArgumentException("Invalid process id.");
    }

    private static ProcessPriority ParsePriority(OptimizationContext context) => context.Require("priority") switch
    {
        "aboveNormal" => ProcessPriority.AboveNormal,
        "high" => ProcessPriority.High,
        _ => ProcessPriority.Normal,
    };
}
