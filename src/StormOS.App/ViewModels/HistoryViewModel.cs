using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using StormOS.App.Services;
using StormOS.Core.History;
using StormOS.Core.Network;
using StormOS.Core.Optimization;
using StormOS.Services.History;

namespace StormOS.App.ViewModels;

/// <summary>A game session row.</summary>
public sealed record SessionRow(GameSession Session)
{
    private static string N(double? value, string format, string unit) => value is { } v ? v.ToString(format, CultureInfo.CurrentCulture) + unit : "—";

    public string Game => Session.GameName;

    public string Started => Session.StartedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public string Duration => Session.EndedAt is null ? "Running" : Core.Common.Units.FormatDuration(Session.Duration);

    public string Fps => Session.AverageFps is null ? "No frame data" : $"{N(Session.AverageFps, "0", " FPS")} · 1% low {N(Session.OnePercentLowFps, "0", string.Empty)} · {N(Session.AverageFrameTimeMs, "0.0", " ms")}";

    public string Load => $"CPU {N(Session.AverageCpuUsage, "0", " %")} · GPU {N(Session.AverageGpuUsage, "0", " %")}";

    public string Temps => $"max CPU {N(Session.MaxCpuTemperature, "0", " °C")} · GPU {N(Session.MaxGpuTemperature, "0", " °C")}";

    public string Source => Session.FrameSource ?? "—";
}

/// <summary>An optimization journal row.</summary>
public sealed record ChangeRow(OptimizationRecord Record)
{
    public string Time => Record.Timestamp.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public string Name => Record.RuleName;

    public string Change => $"{Record.Before} → {Record.After}";

    public string Outcome => Record.Outcome switch
    {
        OptimizationOutcome.Applied => "Applied & verified",
        OptimizationOutcome.Skipped => "Skipped",
        OptimizationOutcome.FailedRolledBack => "Failed — rolled back",
        OptimizationOutcome.FailedRollbackFailed => "Failed — rollback failed",
        _ => "Failed",
    };

    public string Rollback => Record.Rollback switch
    {
        RollbackStatus.Available => "Restorable",
        RollbackStatus.RolledBack => $"Restored {(Record.RolledBackAt is { } at ? at.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : string.Empty)}",
        RollbackStatus.Failed => "Restore failed",
        RollbackStatus.Unavailable => "Not reversible",
        _ => "—",
    };

    public string Who => $"{Record.User} · {(Record.Executor == "service" ? "service" : "app")}";

    public bool Succeeded => Record.Outcome is OptimizationOutcome.Applied or OptimizationOutcome.Skipped;

    public bool CanRestore => Record.Rollback == RollbackStatus.Available;
}

/// <summary>A network report row.</summary>
public sealed record NetworkReportRow(NetworkDiagnosticsReport Report)
{
    public string Time => Report.Timestamp.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public string Score => Report.Score?.Score is { } s ? s.ToString("0", CultureInfo.CurrentCulture) : "—";

    public string Summary => Report.Internet is { Received: > 0 } p
        ? string.Create(CultureInfo.CurrentCulture, $"{p.AverageMs:0.0} ms · jitter {p.JitterMs:0.0} ms · loss {p.LossPercent:0.#} %")
        : "No internet reply";

    public string Adapter => Report.ActiveInterface?.Name ?? "—";
}

/// <summary>A generic history event row.</summary>
public sealed record EventRow(HistoryEvent Event)
{
    public string Time => Event.Timestamp.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public string Category => Event.Category.ToString();

    public string Action => Event.Action;

    public string Result => Event.Result.ToString();

    public string Details => Event.Details;

    public bool Succeeded => Event.Result is EventResult.Success or EventResult.Info;
}

/// <summary>Sessions, optimizations, benchmarks, network tests and system changes.</summary>
public sealed partial class HistoryViewModel(HistoryService history, IHistoryStore store, ChangeService changes) : PageViewModel
{
    public ObservableCollection<SessionRow> Sessions { get; } = [];

    public ObservableCollection<ChangeRow> Changes { get; } = [];

    public ObservableCollection<BenchmarkRow> Benchmarks { get; } = [];

    public ObservableCollection<NetworkReportRow> NetworkTests { get; } = [];

    public ObservableCollection<EventRow> Events { get; } = [];

    public override void Activate(object? parameter) => _ = RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            Fill(Sessions, (await history.ListSessionsAsync()).Select(s => new SessionRow(s)));
            Fill(Changes, (await history.ListChangesAsync()).Select(c => new ChangeRow(c)));
            Fill(Benchmarks, (await history.ListBenchmarksAsync()).Select(b => new BenchmarkRow(b)));
            Fill(NetworkTests, (await store.ListNetworkReportsAsync(100)).Select(r => new NetworkReportRow(r)));
            Fill(Events, (await history.ListEventsAsync()).Select(e => new EventRow(e)));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RestoreAllAsync()
    {
        if (await changes.RestoreAllWithConsentAsync() > 0)
        {
            await RefreshAsync();
        }
    }

    public async Task RestoreAsync(ChangeRow row)
    {
        if (await changes.RestoreWithConsentAsync(row.Record))
        {
            changes.Invalidate();
            await RefreshAsync();
        }
    }

    private static void Fill<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
