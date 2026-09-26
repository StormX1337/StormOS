using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StormOS.App.Services;
using StormOS.Core.Optimization;
using StormOS.Core.Startup;

namespace StormOS.App.ViewModels;

/// <summary>A startup entry row.</summary>
public sealed record StartupRow(StartupEntry Entry)
{
    public string Name => Entry.Name;

    public string Publisher => Entry.Publisher ?? "Unknown publisher";

    public string Command => Entry.Command;

    public string Impact => Entry.Impact;

    public string Status => Entry.IsEnabled ? "Enabled" : "Disabled";

    public bool IsEnabled => Entry.IsEnabled;

    public string Source => Entry.Source switch
    {
        StartupSource.RegistryUserRun => "Registry (you)",
        StartupSource.RegistryMachineRun => "Registry (all users)",
        StartupSource.RegistryMachineRun32 => "Registry (all users, 32-bit)",
        StartupSource.UserStartupFolder => "Startup folder (you)",
        StartupSource.CommonStartupFolder => "Startup folder (all users)",
        StartupSource.ScheduledTask => "Scheduled task",
        _ => Entry.Source.ToString(),
    };

    public string Access => Entry.CanToggle ? Entry.RequiresAdmin ? "Needs service (admin)" : string.Empty : "Managed elsewhere";

    public bool CanToggle => Entry.CanToggle;

    public string ToggleLabel => Entry.IsEnabled ? "DISABLE" : "ENABLE";
}

/// <summary>Startup programs: enable/disable reversibly, never delete.</summary>
public sealed partial class StartupViewModel(IStartupManager startup, ChangeService changes, NotificationService notifications) : PageViewModel
{
    public ObservableCollection<StartupRow> Entries { get; } = [];

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    public override void Activate(object? parameter) => _ = RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var entries = await startup.ListAsync();
            Entries.Clear();
            foreach (var entry in entries.OrderByDescending(e => e.IsEnabled).ThenByDescending(e => e.RuntimeCpuSeconds ?? -1).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                Entries.Add(new StartupRow(entry));
            }

            Summary = $"{entries.Count} entries · {entries.Count(e => e.IsEnabled)} enabled. Impact is measured from the running process (CPU time since start); entries are never deleted.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ToggleAsync(StartupRow row)
    {
        if (!row.CanToggle)
        {
            notifications.Info("This entry is managed by its own program or by Task Scheduler. Change it there.");
            return;
        }

        var ruleId = row.Entry.RequiresAdmin ? "startup.entry-machine" : "startup.entry";
        var parameters = new Dictionary<string, string> { ["entryId"] = row.Entry.Id, ["state"] = row.IsEnabled ? "disabled" : "enabled" };
        var reason = row.IsEnabled
            ? $"{row.Name} will no longer start with Windows. Impact: {row.Impact}. The entry stays in place and can be enabled again."
            : $"{row.Name} will start with Windows again.";
        if (await changes.ApplyWithConsentAsync(ruleId, parameters, reason) is { Outcome: OptimizationOutcome.Applied })
        {
            await RefreshAsync();
        }
    }

    public static void OpenLocation(StartupRow row)
    {
        if (row.Entry.ExecutablePath is { } path)
        {
            ShellLauncher.ShowInExplorer(path);
        }
    }
}
