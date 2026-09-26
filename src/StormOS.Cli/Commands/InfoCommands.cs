using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using StormOS.Core.Common;
using StormOS.Core.Games;
using StormOS.Core.Hardware;
using StormOS.Core.Telemetry;
using StormOS.Services.Client;

namespace StormOS.Cli.Commands;

/// <summary>Read-only commands: status, hardware, games.</summary>
internal static class InfoCommands
{
    public static Command Status(IServiceProvider services)
    {
        var command = new Command("status", "Show the STORM OS service, system and live resource status.");
        command.SetAction(async (_, cancellationToken) =>
        {
            var client = services.GetRequiredService<IStormServiceClient>();
            var os = services.GetRequiredService<IOperatingSystemInfoProvider>().GetOsInfo();
            var connected = await client.ConnectAsync(cancellationToken);
            var health = connected.IsSuccess ? await client.GetHealthAsync(cancellationToken) : null;
            var snapshot = connected.IsSuccess ? await client.GetSnapshotAsync(cancellationToken) : null;
            var live = snapshot is { IsSuccess: true } ? snapshot.Value! : await services.GetRequiredService<ITelemetryHub>().SampleOnceAsync(cancellationToken);

            Output.Data(new { service = connected.IsSuccess ? health?.Value : null, os, snapshot = live });
            Output.Title("STORM OS");
            Output.Row("Service", connected.IsSuccess ? $"Running · v{health?.Value?.Version} · {health?.Value?.Status}" : connected.Error.Message);
            Output.Row("Windows", $"{os.ProductName} {os.DisplayVersion} ({os.VersionString})");
            Output.Row("Host", os.HostName);
            Output.Row("Uptime", Units.FormatDuration(DateTimeOffset.Now - os.BootTime));
            Output.Title("Live");
            Output.Row("CPU", Output.Reading(live.Cpu.Usage, "0", " %"));
            var gpu = live.PrimaryGpu();
            Output.Row("GPU", gpu is null ? "No GPU telemetry" : $"{gpu.Name}: {Output.Reading(gpu.Usage, "0", " %")}");
            Output.Row("RAM", $"{Units.FormatBytes(live.Memory.UsedBytes)} / {Units.FormatBytes(live.Memory.TotalBytes)} ({live.Memory.UsagePercent:0} %)");
            Output.Row("Latency", Output.Reading(live.System.LatencyMs, "0", " ms"));
            Output.Row("Active game", live.System.ActiveGame ?? "None");
            return 0;
        });
        return command;
    }

    public static Command Hardware(IServiceProvider services)
    {
        var command = new Command("hardware", "Show the detected hardware.");
        command.SetAction(async (_, cancellationToken) =>
        {
            var inventory = await services.GetRequiredService<IHardwareInventoryProvider>().GetInventoryAsync(cancellationToken);
            Output.Data(inventory);
            Output.Title("Hardware");
            Output.Row("CPU", $"{inventory.Cpu.Name} · {inventory.Cpu.Cores}C/{inventory.Cpu.LogicalProcessors}T");
            foreach (var gpu in inventory.Gpus)
            {
                Output.Row("GPU", $"{gpu.Name} · {Units.FormatBytes(gpu.DedicatedMemoryBytes, 0)} · driver {gpu.DriverVersion ?? "unknown"}");
            }

            Output.Row("Memory", $"{Units.FormatBytes(inventory.Memory.TotalBytes, 0)} ({inventory.Memory.Modules.Count} modules)");
            foreach (var module in inventory.Memory.Modules)
            {
                Output.Row("  " + module.Slot, $"{Units.FormatBytes(module.CapacityBytes, 0)} {module.MemoryType} {module.ConfiguredSpeedMts}/{module.SpeedMts} MT/s {module.Manufacturer} {module.PartNumber}");
            }

            Output.Row("Motherboard", $"{inventory.Motherboard.Manufacturer} {inventory.Motherboard.Product}".Trim());
            Output.Row("BIOS", $"{inventory.Bios.Vendor} {inventory.Bios.Version} ({inventory.Bios.ReleaseDate})");
            foreach (var disk in inventory.Storage)
            {
                Output.Row("Disk", $"{disk.Model} · {disk.BusType} {disk.MediaType} · {Units.FormatBytes(disk.SizeBytes, 0)} · {disk.HealthStatus ?? "health n/a"}");
            }

            foreach (var monitor in inventory.Monitors)
            {
                Output.Row("Display", $"{monitor.FriendlyName ?? monitor.DeviceName} · {monitor.Width}×{monitor.Height} @ {monitor.RefreshRateHz:0} Hz{(monitor.IsPrimary ? " (primary)" : string.Empty)}");
            }

            foreach (var issue in inventory.Issues)
            {
                Output.Row("Note", $"{issue.Component}: {issue.Message}");
            }

            return 0;
        });
        return command;
    }

    public static Command Games(IServiceProvider services)
    {
        var refresh = new Option<bool>("--refresh") { Description = "Rescan all launchers." };
        var command = new Command("games", "List installed and running games.") { refresh };
        command.SetAction(async (parse, cancellationToken) =>
        {
            var games = await services.GetRequiredService<IGameRegistry>().GetGamesAsync(parse.GetValue(refresh), cancellationToken);
            var client = services.GetRequiredService<IStormServiceClient>();
            var running = (await client.ConnectAsync(cancellationToken)).IsSuccess ? (await client.GetRunningGamesAsync(cancellationToken)).Value?.Games ?? [] : [];
            Output.Data(new { games, running });
            Output.Title($"Games ({games.Count})");
            foreach (var game in games)
            {
                var isRunning = running.Any(r => r.Game?.GameId == game.GameId);
                Output.Row(game.Name, $"{game.Launcher}{(game.ProfileId is null ? string.Empty : " · profile " + game.ProfileId)}{(isRunning ? " · RUNNING" : string.Empty)}");
            }

            return 0;
        });
        return command;
    }
}
