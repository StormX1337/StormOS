using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StormOS.App.Services;
using StormOS.Core.Common;
using StormOS.Core.History;
using StormOS.Core.Network;
using StormOS.Core.Optimization;
using StormOS.Core.Scoring;
using StormOS.Core.Settings;
using StormOS.Services.History;

namespace StormOS.App.ViewModels;

/// <summary>A DNS test row.</summary>
public sealed record DnsRow(DnsTestResult Result, double? ConfiguredLatencyMs)
{
    public string Server => Result.Server;

    public string Role => Result.IsConfigured ? "In use" : "Alternative";

    public string Latency => Result.Success && Result.LatencyMs is { } ms ? ms.ToString("0.0", CultureInfo.CurrentCulture) + " ms" : "Failed";

    public string Detail => Result.Success ? string.Join(", ", Result.Addresses.Take(2)) : Result.Error ?? "No answer";

    public bool Success => Result.Success;

    public bool CanUse => !Result.IsConfigured && Result.Success && Result.LatencyMs is { } ms && (ConfiguredLatencyMs is null || ms < ConfiguredLatencyMs);
}

/// <summary>A TCP test row.</summary>
public sealed record TcpRow(TcpTestResult Result)
{
    public string Endpoint => $"{Result.Host}:{Result.Port}";

    public string Value => Result.Success && Result.ConnectMs is { } ms ? ms.ToString("0.0", CultureInfo.CurrentCulture) + " ms" : Result.Error ?? "Failed";

    public bool Success => Result.Success;
}

/// <summary>A route hop row.</summary>
public sealed record HopRow(TraceHop Hop)
{
    public string Number => Hop.Hop.ToString(CultureInfo.CurrentCulture);

    public string Address => Hop.TimedOut ? "* (no reply)" : Hop.Address ?? "—";

    public string Rtt => Hop.RttMs is { } ms ? ms.ToString("0", CultureInfo.CurrentCulture) + " ms" : "—";
}

/// <summary>Network diagnostics, STORM Network Score and opt-in DNS changes.</summary>
public sealed partial class NetworkViewModel(
    INetworkDiagnostics diagnostics,
    IHistoryStore history,
    HistoryService historyService,
    ISettingsStore settings,
    ChangeService changes,
    NotificationService notifications) : PageViewModel
{
    private static readonly Dictionary<string, string> SecondaryDns = new(StringComparer.Ordinal)
    {
        ["1.1.1.1"] = "1.0.0.1",
        ["1.0.0.1"] = "1.1.1.1",
        ["8.8.8.8"] = "8.8.4.4",
        ["8.8.4.4"] = "8.8.8.8",
        ["9.9.9.9"] = "149.112.112.112",
        ["149.112.112.112"] = "9.9.9.9",
    };

    private CancellationTokenSource? _cts;

    public ObservableCollection<DnsRow> Dns { get; } = [];

    public ObservableCollection<TcpRow> Tcp { get; } = [];

    public ObservableCollection<HopRow> Route { get; } = [];

    public ObservableCollection<string> Issues { get; } = [];

    [ObservableProperty]
    public partial NetworkDiagnosticsReport? Report { get; set; }

    [ObservableProperty]
    public partial ScoreBreakdown? Score { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<double?> PingSamples { get; set; } = [];

    [ObservableProperty]
    public partial bool IncludeThroughput { get; set; }

    [ObservableProperty]
    public partial string InterfaceText { get; set; } = "—";

    [ObservableProperty]
    public partial string AddressText { get; set; } = "—";

    [ObservableProperty]
    public partial string ReportTime { get; set; } = "No test has run yet.";

    [ObservableProperty]
    public partial string InternetText { get; set; } = "—";

    [ObservableProperty]
    public partial string GatewayText { get; set; } = "—";

    [ObservableProperty]
    public partial string ThroughputText { get; set; } = "Not measured";

    [ObservableProperty]
    public partial bool HasDnsChange { get; set; }

    public override void Activate(object? parameter)
    {
        if (parameter is "run")
        {
            _ = RunAsync();
        }
        else
        {
            _ = LoadLastAsync();
        }
    }

    public override void Deactivate() => _cts?.Cancel();

    [RelayCommand]
    private async Task RunAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var network = settings.Current.Network;
        var options = new NetworkDiagnosticsOptions
        {
            InternetTarget = network.LatencyTarget,
            AlternativeDnsServers = network.AlternativeDnsServers,
            IncludeThroughput = IncludeThroughput,
            ThroughputUrl = network.ThroughputUrl,
        };
        _cts = new CancellationTokenSource();
        IsBusy = true;
        try
        {
            var progress = new Progress<string>(stage => StatusMessage = stage);
            var report = await Task.Run(() => diagnostics.RunAsync(options, progress, _cts.Token), _cts.Token);
            await history.SaveNetworkReportAsync(report, _cts.Token);
            await history.AddEventAsync(
                new HistoryEvent
                {
                    Timestamp = report.Timestamp,
                    Category = HistoryCategory.NetworkTest,
                    Action = "Network test",
                    Result = EventResult.Success,
                    Details = report.Score?.Score is { } s ? $"Network Score {s:0}" : "Network Score: insufficient data",
                    RelatedId = report.Id.ToString(),
                },
                _cts.Token);
            Show(report);
        }
        catch (OperationCanceledException)
        {
            notifications.Info("Network test cancelled.");
        }
        finally
        {
            IsBusy = false;
            StatusMessage = null;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private async Task RestoreDnsAsync()
    {
        var change = (await historyService.ListChangesAsync()).FirstOrDefault(c => c.RuleId == "network.dns" && c.Rollback == RollbackStatus.Available);
        if (change is null)
        {
            HasDnsChange = false;
            notifications.Info("STORM OS has not changed your DNS servers.");
            return;
        }

        if (await changes.RestoreWithConsentAsync(change))
        {
            HasDnsChange = false;
        }
    }

    public async Task UseDnsAsync(DnsRow row)
    {
        if (Report?.ActiveInterface is not { } adapter)
        {
            notifications.Warning("No active network adapter was found.");
            return;
        }

        var servers = SecondaryDns.TryGetValue(row.Server, out var secondary) ? $"{row.Server},{secondary}" : row.Server;
        var reason = $"Measured DNS response: {row.Latency} for {row.Server} versus {(row.ConfiguredLatencyMs is { } ms ? ms.ToString("0.0", CultureInfo.CurrentCulture) + " ms" : "unavailable")} for the servers in use. Applies to \"{adapter.Name}\" only.";
        var record = await changes.ApplyWithConsentAsync("network.dns", new Dictionary<string, string> { ["interfaceId"] = adapter.Id, ["servers"] = servers }, reason);
        if (record is { Outcome: OptimizationOutcome.Applied })
        {
            HasDnsChange = true;
        }
    }

    private async Task LoadLastAsync()
    {
        HasDnsChange = (await historyService.ListChangesAsync()).Any(c => c.RuleId == "network.dns" && c.Rollback == RollbackStatus.Available);
        var reports = await history.ListNetworkReportsAsync(1);
        if (Report is null && reports.Count > 0)
        {
            Show(reports[0]);
        }
    }

    private void Show(NetworkDiagnosticsReport report)
    {
        Report = report;
        Score = report.Score;
        ReportTime = $"Measured {report.Timestamp.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}";
        if (report.ActiveInterface is { } nic)
        {
            InterfaceText = $"{nic.Name} · {nic.InterfaceType} · {(nic.SpeedBitsPerSecond > 0 ? Units.FormatBitRate(nic.SpeedBitsPerSecond) : "speed unknown")}";
            AddressText = $"IPv4 {string.Join(", ", nic.IPv4Addresses.DefaultIfEmpty("—"))} · gateway {string.Join(", ", nic.Gateways.DefaultIfEmpty("—"))} · DNS {string.Join(", ", nic.DnsServers.DefaultIfEmpty("—"))}{(nic.DhcpEnabled ? " (DHCP)" : string.Empty)}";
        }
        else
        {
            InterfaceText = "No active network adapter";
            AddressText = "—";
        }

        InternetText = Describe(report.Internet);
        GatewayText = Describe(report.Gateway);
        PingSamples = report.Internet?.Samples ?? [];
        ThroughputText = report.Throughput switch
        {
            null => "Not measured (enable the download test)",
            { DownloadBitsPerSecond: { } down } t => $"{Units.FormatBitRate(down)} download · {Units.FormatBytes(t.BytesTransferred)} from {new Uri(t.Endpoint).Host}",
            { Error: { } error } => error,
            _ => "Unavailable",
        };

        var configured = report.Dns.Where(d => d.IsConfigured && d.Success && d.LatencyMs.HasValue).Select(d => d.LatencyMs!.Value).ToList();
        double? configuredAverage = configured.Count > 0 ? configured.Average() : null;
        Dns.Clear();
        foreach (var dns in report.Dns.OrderByDescending(d => d.IsConfigured).ThenBy(d => d.LatencyMs ?? double.MaxValue))
        {
            Dns.Add(new DnsRow(dns, configuredAverage));
        }

        Tcp.Clear();
        foreach (var tcp in report.Tcp)
        {
            Tcp.Add(new TcpRow(tcp));
        }

        Route.Clear();
        foreach (var hop in report.Route)
        {
            Route.Add(new HopRow(hop));
        }

        Issues.Clear();
        foreach (var issue in report.Issues)
        {
            Issues.Add(issue);
        }
    }

    private static string Describe(PingStatistics? ping) => ping switch
    {
        null => "Not measured",
        { Received: 0 } p => $"{p.Target}: no replies{(p.Error is null ? string.Empty : " — " + p.Error)}",
        var p => string.Create(CultureInfo.CurrentCulture, $"{p.Target}: {p.AverageMs:0.0} ms avg · {p.MinMs:0}–{p.MaxMs:0} ms · jitter {p.JitterMs:0.0} ms · loss {p.LossPercent:0.#} %"),
    };
}
