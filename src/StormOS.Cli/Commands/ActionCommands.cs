using System.CommandLine;
using System.ServiceProcess;
using Microsoft.Extensions.DependencyInjection;
using StormOS.Benchmark.Engine;
using StormOS.Core.Benchmark;
using StormOS.Core.Network;
using StormOS.Core.Optimization;
using StormOS.Infrastructure.Logging;
using StormOS.Infrastructure.Paths;
using StormOS.Services.Client;
using StormOS.Services.History;
using StormOS.Services.Optimization;
using StormOS.Services.Scan;
using StormOS.Windows.Platform;

namespace StormOS.Cli.Commands;

/// <summary>Commands that measure or change the system. Changes always require confirmation.</summary>
internal static class ActionCommands
{
    public static Command Benchmark(IServiceProvider services)
    {
        var type = new Argument<BenchmarkType>("type") { Description = "cpu, memory, disk, gpu or network." };
        var duration = new Option<int>("--duration") { Description = "Approximate duration in seconds.", DefaultValueFactory = _ => 10 };
        var label = new Option<string?>("--label") { Description = "Label such as before/after." };
        var command = new Command("benchmark", "Run a benchmark and store the result.") { type, duration, label };
        command.SetAction(async (parse, cancellationToken) =>
        {
            var benchmarkType = parse.GetValue(type);
            if (benchmarkType == BenchmarkType.Gaming)
            {
                return Output.Error("Gaming benchmarks capture a running game; start them from the desktop app.");
            }

            var engine = services.GetRequiredService<BenchmarkEngine>();
            var progress = new Progress<BenchmarkProgress>(p => Output.Line($"  {p.Percent,3:0}%  {p.Stage}"));
            var result = await engine.RunAsync(benchmarkType, new BenchmarkRunOptions { Duration = TimeSpan.FromSeconds(Math.Clamp(parse.GetValue(duration), 3, 120)), Label = parse.GetValue(label) }, progress, cancellationToken);
            Output.Data(result);
            if (!result.Completed)
            {
                return Output.Error(result.Error ?? "The benchmark failed.");
            }

            Output.Title($"{result.Type} benchmark");
            foreach (var metric in result.Metrics)
            {
                Output.Row(metric.Name, $"{metric.Value:0.##} {metric.Unit}");
            }

            if (result.Score is { Score: { } score } breakdown)
            {
                Output.Row("Score (partial)", $"{score:0} — {breakdown.Rating}");
            }

            return 0;
        });
        return command;
    }

    public static Command Optimize(IServiceProvider services)
    {
        var list = new Command("list", "List optimizations and their current state.");
        list.SetAction(async (_, cancellationToken) =>
        {
            var (items, note) = await services.GetRequiredService<OptimizationCoordinator>().ListAsync(cancellationToken);
            Output.Data(items);
            Output.Title("Optimizations");
            foreach (var item in items)
            {
                var d = item.Descriptor;
                Output.Row(d.Id, $"{d.Name} · risk {d.RiskLevel} · {(d.Detection is null ? "needs parameters" : $"{d.Detection.State}: {d.Detection.CurrentValue}")}{(d.RequiresAdmin ? " · admin" : string.Empty)}");
            }

            if (note is not null)
            {
                Output.Line("  " + note);
            }

            return 0;
        });

        var ruleId = new Argument<string>("rule") { Description = "Rule id, for example windows.game-mode." };
        var parameters = new Option<string[]>("--param") { Description = "Rule parameter as key=value (repeatable).", AllowMultipleArgumentsPerToken = true };
        var yes = new Option<bool>("--yes") { Description = "Confirm without prompting." };
        var apply = new Command("apply", "Apply an optimization (creates a backup first).") { ruleId, parameters, yes };
        apply.SetAction(async (parse, cancellationToken) =>
        {
            var coordinator = services.GetRequiredService<OptimizationCoordinator>();
            var (items, _) = await coordinator.ListAsync(cancellationToken);
            var item = items.FirstOrDefault(i => i.Descriptor.Id == parse.GetValue(ruleId));
            if (item is null)
            {
                return Output.Error("Unknown optimization. Run 'storm optimize list'.");
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in parse.GetValue(parameters) ?? [])
            {
                var eq = pair.IndexOf('=', StringComparison.Ordinal);
                if (eq <= 0)
                {
                    return Output.Error($"Invalid parameter '{pair}'. Use key=value.");
                }

                values[pair[..eq]] = pair[(eq + 1)..];
            }

            var detection = await coordinator.DetectAsync(item.Executor, item.Descriptor.Id, values, cancellationToken);
            if (!detection.IsSuccess)
            {
                return Output.Error(detection.Error.Message);
            }

            Output.Title(item.Descriptor.Name);
            Output.Line("  " + item.Descriptor.Description);
            Output.Row("Current", detection.Value!.CurrentValue);
            Output.Row("Target", detection.Value.TargetValue);
            Output.Row("Risk", item.Descriptor.RiskLevel.ToString());
            Output.Row("Reversible", item.Descriptor.CanRollback ? "Yes (storm restore)" : "NO — this change cannot be undone");
            if (detection.Value.State != DetectionState.Applicable)
            {
                Output.Line("  Nothing to do: " + detection.Value.Explanation);
                return 0;
            }

            if (!Output.Confirm("Apply this change?", parse.GetValue(yes)))
            {
                Output.Line("Cancelled. Nothing was changed.");
                return 2;
            }

            var result = await coordinator.ApplyAsync(item.Executor, item.Descriptor.Id, values, Elevation.CurrentUserName, cancellationToken);
            if (!result.IsSuccess)
            {
                return Output.Error(result.Error.Message);
            }

            Output.Data(result.Value!);
            Output.Row("Result", $"{result.Value!.Outcome}: {result.Value.Message}");
            Output.Row("Change id", result.Value.Id.ToString());
            return result.Value.Outcome == OptimizationOutcome.Applied ? 0 : 1;
        });

        return new Command("optimize", "List or apply reversible optimizations.") { list, apply };
    }

    public static Command Restore(IServiceProvider services)
    {
        var changeId = new Argument<Guid?>("change") { Description = "Change id from 'storm optimize apply' or the history.", Arity = ArgumentArity.ZeroOrOne };
        var all = new Option<bool>("--all") { Description = "Restore every active change (newest first)." };
        var yes = new Option<bool>("--yes") { Description = "Confirm without prompting." };
        var command = new Command("restore", "Restore a previous state from its snapshot.") { changeId, all, yes };
        command.SetAction(async (parse, cancellationToken) =>
        {
            var coordinator = services.GetRequiredService<OptimizationCoordinator>();
            if (parse.GetValue(all))
            {
                if (!Output.Confirm("Restore ALL active changes?", parse.GetValue(yes)))
                {
                    return 2;
                }

                var (records, note) = await coordinator.RestoreAllAsync(Elevation.CurrentUserName, cancellationToken);
                Output.Data(records);
                foreach (var record in records)
                {
                    Output.Row(record.RuleName, record.Rollback.ToString());
                }

                if (note is not null)
                {
                    Output.Line("  " + note);
                }

                return records.All(r => r.Rollback == RollbackStatus.RolledBack) ? 0 : 1;
            }

            if (parse.GetValue(changeId) is not { } id)
            {
                return Output.Error("Specify a change id or --all.");
            }

            var change = (await services.GetRequiredService<HistoryService>().ListChangesAsync(cancellationToken)).FirstOrDefault(r => r.Id == id);
            if (change is null)
            {
                return Output.Error("The change was not found.");
            }

            Output.Row(change.RuleName, $"{change.After} → {change.Before}");
            if (!Output.Confirm("Restore the previous state?", parse.GetValue(yes)))
            {
                return 2;
            }

            var result = await coordinator.RestoreAsync(change, Elevation.CurrentUserName, cancellationToken);
            if (!result.IsSuccess)
            {
                return Output.Error(result.Error.Message);
            }

            Output.Data(result.Value!);
            Output.Row("Result", $"{result.Value!.Rollback}: {result.Value.Message}");
            return result.Value.Rollback == RollbackStatus.RolledBack ? 0 : 1;
        });
        return command;
    }

    public static Command Network(IServiceProvider services)
    {
        var throughput = new Option<bool>("--throughput") { Description = "Also measure download speed (downloads about 25 MB)." };
        var command = new Command("network", "Run network diagnostics.") { throughput };
        command.SetAction(async (parse, cancellationToken) =>
        {
            var diagnostics = services.GetRequiredService<INetworkDiagnostics>();
            var report = await diagnostics.RunAsync(new NetworkDiagnosticsOptions { IncludeThroughput = parse.GetValue(throughput), ThroughputUrl = "https://speed.cloudflare.com/__down?bytes=25000000", AlternativeDnsServers = ["1.1.1.1", "8.8.8.8", "9.9.9.9"] }, new Progress<string>(s => Output.Line("  … " + s)), cancellationToken);
            Output.Data(report);
            Output.Title("Network");
            Output.Row("Interface", report.ActiveInterface is { } nic ? $"{nic.Name} · {nic.Description}" : "None");
            Output.Row("IP / gateway", report.ActiveInterface is { } n ? $"{string.Join(", ", n.IPv4Addresses)} / {string.Join(", ", n.Gateways)}" : "-");
            Output.Row("Gateway latency", report.Gateway?.AverageMs is { } g ? $"{g:0.0} ms" : "Not measured");
            Output.Row("Internet latency", report.Internet?.AverageMs is { } l ? $"{l:0.0} ms" : "No reply");
            Output.Row("Jitter", report.Internet?.JitterMs is { } j ? $"{j:0.0} ms" : "Not measured");
            Output.Row("Packet loss", report.Internet is { Sent: > 0 } p ? $"{p.LossPercent:0.#} %" : "Not measured");
            foreach (var dns in report.Dns)
            {
                Output.Row($"DNS {dns.Server}{(dns.IsConfigured ? " *" : string.Empty)}", dns.Success ? $"{dns.LatencyMs:0.0} ms" : dns.Error);
            }

            if (report.Throughput?.DownloadBitsPerSecond is { } down)
            {
                Output.Row("Download", StormOS.Core.Common.Units.FormatBitRate(down));
            }

            Output.Row("STORM network score", report.Score?.Score is { } s ? $"{s:0} / 100 ({report.Score.Rating})" : "Insufficient data");
            foreach (var issue in report.Issues)
            {
                Output.Line("  ! " + issue);
            }

            return 0;
        });
        return command;
    }

    public static Command Scan(IServiceProvider services)
    {
        var command = new Command("scan", "Run the STORM system scan.");
        command.SetAction(async (_, cancellationToken) =>
        {
            var result = await services.GetRequiredService<SystemScanService>().RunAsync(new Progress<string>(a => Output.Line("  … " + a)), cancellationToken);
            Output.Data(result);
            Output.Title($"System scan: {result.Overall}");
            foreach (var check in result.Checks)
            {
                Output.Row((check.Passed ? "✓ " : "✗ ") + check.Name, check.Detail);
            }

            foreach (var finding in result.Findings)
            {
                Output.Line($"\n  [{finding.Severity}] {finding.Title}\n    {finding.Description}\n    Evidence: {string.Join("; ", finding.Evidence)}\n    Suggested: {finding.SuggestedAction} (risk {finding.Risk})");
            }

            return 0;
        });
        return command;
    }

    public static Command Logs(IServiceProvider services)
    {
        var service = new Option<bool>("--service") { Description = "Show the service log instead of the app log." };
        var lines = new Option<int>("--lines") { Description = "Number of lines.", DefaultValueFactory = _ => 50 };
        var command = new Command("logs", "Show recent log entries.") { service, lines };
        command.SetAction(async (parse, cancellationToken) =>
        {
            var count = Math.Clamp(parse.GetValue(lines), 1, 1000);
            if (parse.GetValue(service))
            {
                var client = services.GetRequiredService<IStormServiceClient>();
                var connect = await client.ConnectAsync(cancellationToken);
                if (!connect.IsSuccess)
                {
                    return Output.Error(connect.Error.Message);
                }

                var tail = await client.TailLogsAsync(count, cancellationToken);
                if (!tail.IsSuccess)
                {
                    return Output.Error(tail.Error.Message);
                }

                tail.Value!.Lines.ToList().ForEach(Console.WriteLine);
                return 0;
            }

            var (_, entries) = StormLogging.Tail(services.GetRequiredService<IStormPaths>().Logs, LogCategories.Application, count);
            entries.ToList().ForEach(Console.WriteLine);
            return 0;
        });
        return command;
    }

    public static Command Service()
    {
        var action = new Argument<string>("action") { Description = "status, start, stop or restart." };
        action.AcceptOnlyFromAmong("status", "start", "stop", "restart");
        var command = new Command("service", "Control StormOSService (start/stop/restart require administrator rights).") { action };
        command.SetAction(async (parse, cancellationToken) =>
        {
            try
            {
                using var controller = new ServiceController("StormOSService");
                var verb = parse.GetValue(action)!;
                if (verb != "status" && !Elevation.IsElevated)
                {
                    return Output.Error("Run this command from an elevated terminal (Run as administrator).");
                }

                if (verb is "stop" or "restart" && controller.Status != ServiceControllerStatus.Stopped)
                {
                    controller.Stop();
                    await Task.Run(() => controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30)), cancellationToken);
                }

                if (verb is "start" or "restart")
                {
                    controller.Start();
                    await Task.Run(() => controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30)), cancellationToken);
                }

                controller.Refresh();
                Output.Row("StormOSService", controller.Status.ToString());
                return 0;
            }
            catch (InvalidOperationException)
            {
                return Output.Error("StormOSService is not installed.");
            }
            catch (System.ServiceProcess.TimeoutException)
            {
                return Output.Error("The service did not change state in time.");
            }
        });
        return command;
    }
}
