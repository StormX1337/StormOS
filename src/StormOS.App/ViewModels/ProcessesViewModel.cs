using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StormOS.App.Services;
using StormOS.Core.Common;
using StormOS.Core.Processes;
using StormOS.Services.Client;

namespace StormOS.App.ViewModels;

/// <summary>A process row.</summary>
public sealed record ProcessRow(ProcessEntry Entry)
{
    public int Pid => Entry.ProcessId;

    public string Name => Entry.Name;

    public string PidText => Entry.ProcessId.ToString(CultureInfo.InvariantCulture);

    public string Cpu => Entry.CpuPercent.ToString("0.0", CultureInfo.CurrentCulture) + " %";

    public string Memory => Units.FormatBytes(Entry.WorkingSetBytes);

    public string Gpu => Entry.GpuPercent is { } g ? g.ToString("0.0", CultureInfo.CurrentCulture) + " %" : "—";

    public string Priority => Entry.Priority?.ToString() ?? "No access";

    public string Publisher => Entry.Publisher ?? "—";

    public string Signature => Entry.Signature switch
    {
        SignatureStatus.Valid => "Signed",
        SignatureStatus.NotSigned => "Not signed",
        SignatureStatus.Invalid => "INVALID signature",
        _ => "Not verified",
    };

    public string PathText => Entry.Path ?? "Path not accessible";

    public bool IsCritical => Entry.IsCritical;
}

/// <summary>Live process list with safe actions.</summary>
public sealed partial class ProcessesViewModel(
    IProcessInspector inspector,
    IProcessController controller,
    IStormServiceClient service,
    UiDispatcher ui,
    DialogService dialogs,
    NotificationService notifications) : PageViewModel
{
    private CancellationTokenSource? _loop;
    private IReadOnlyList<ProcessEntry> _latest = [];

    public ObservableCollection<ProcessRow> Items { get; } = [];

    public IReadOnlyList<string> SortOptions { get; } = ["CPU", "Memory", "GPU", "Name"];

    public IReadOnlyList<string> Filters { get; } = ["All processes", "Apps (my session)", "System (session 0)"];

    public IReadOnlyList<ProcessPriority> Priorities { get; } = Enum.GetValues<ProcessPriority>();

    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int SortIndex { get; set; }

    [ObservableProperty]
    public partial int FilterIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial ProcessRow? Selected { get; set; }

    [ObservableProperty]
    public partial int PriorityIndex { get; set; } = (int)ProcessPriority.Normal;

    private ProcessPriority SelectedPriority => (ProcessPriority)Math.Clamp(PriorityIndex, 0, Priorities.Count - 1);

    [ObservableProperty]
    public partial bool Paused { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    public bool HasSelection => Selected is not null;

    public override void Activate(object? parameter)
    {
        _loop = new CancellationTokenSource();
        _ = RunLoopAsync(_loop.Token);
    }

    public override void Deactivate()
    {
        _loop?.Cancel();
        _loop?.Dispose();
        _loop = null;
    }

    partial void OnSearchChanged(string value) => Apply();

    partial void OnSortIndexChanged(int value) => Apply();

    partial void OnFilterIndexChanged(int value) => Apply();

    partial void OnSelectedChanged(ProcessRow? value)
    {
        if (value?.Entry.Priority is { } priority)
        {
            PriorityIndex = (int)priority;
        }
    }

    [RelayCommand]
    private async Task EndProcessAsync()
    {
        if (Selected is not { } row)
        {
            return;
        }

        if (inspector.IsProtected(row.Name) || row.IsCritical)
        {
            notifications.Warning($"{row.Name} is a protected system, security or anti-cheat process and cannot be ended from STORM OS.");
            return;
        }

        if (!await dialogs.ConfirmAsync("End process", $"End {row.Name} (PID {row.PidText})?\n\nUnsaved work in this program will be lost.", "End process"))
        {
            return;
        }

        var result = controller.Terminate(row.Pid, row.Name);
        if (!result.IsSuccess && result.Error.Code == StormErrorCodes.RequiresAdmin && service.State == ServiceConnectionState.Connected)
        {
            result = await service.TerminateProcessAsync(row.Pid, row.Name);
        }

        Report(result, $"{row.Name} was ended.");
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task SetPriorityAsync()
    {
        if (Selected is not { } row)
        {
            return;
        }

        if (SelectedPriority == ProcessPriority.High && !await dialogs.ConfirmAsync("High priority", $"High priority can make {row.Name} compete with Windows for CPU time and cause stutter elsewhere. Continue?", "Set high"))
        {
            return;
        }

        var result = controller.SetPriority(row.Pid, SelectedPriority);
        if (!result.IsSuccess && result.Error.Code == StormErrorCodes.RequiresAdmin && service.State == ServiceConnectionState.Connected)
        {
            result = await service.SetProcessPriorityAsync(row.Pid, SelectedPriority);
        }

        Report(result, $"{row.Name} now runs at {SelectedPriority} priority until it exits.");
        await RefreshAsync();
    }

    [RelayCommand]
    private void OpenLocation()
    {
        if (Selected?.Entry.Path is { } path)
        {
            ShellLauncher.ShowInExplorer(path);
        }
        else
        {
            notifications.Info("The file location of this process is not accessible without administrator rights.");
        }
    }

    [RelayCommand]
    private async Task VerifySignatureAsync()
    {
        if (Selected?.Entry.Path is not { } path)
        {
            notifications.Info("The file of this process is not accessible, so its signature cannot be verified.");
            return;
        }

        var name = Selected.Name;
        var status = await Task.Run(() => inspector.VerifySignature(path));
        var message = status switch
        {
            SignatureStatus.Valid => $"{name}: valid Authenticode signature ({Selected?.Publisher}).",
            SignatureStatus.NotSigned => $"{name} is not digitally signed. That is common for small tools, but check where it came from.",
            SignatureStatus.Invalid => $"{name} has an INVALID signature. The file may have been modified after signing.",
            _ => $"The signature of {name} could not be checked.",
        };
        if (status == SignatureStatus.Invalid)
        {
            notifications.Warning(message, "Signature");
        }
        else
        {
            notifications.Info(message, "Signature");
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        _latest = await Task.Run(inspector.Snapshot);
        Apply();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            // The first sample only establishes the CPU baseline.
            await Task.Run(inspector.Snapshot, cancellationToken);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            do
            {
                if (!Paused)
                {
                    var snapshot = await Task.Run(inspector.Snapshot, cancellationToken);
                    ui.Post(() =>
                    {
                        _latest = snapshot;
                        Apply();
                    });
                }
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Apply()
    {
        IEnumerable<ProcessEntry> query = _latest;
        query = FilterIndex switch
        {
            1 => query.Where(p => p.SessionId != 0),
            2 => query.Where(p => p.SessionId == 0),
            _ => query,
        };
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = Search.Trim();
            query = query.Where(p => p.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (p.Publisher?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || p.ProcessId.ToString(CultureInfo.InvariantCulture) == term);
        }

        query = SortIndex switch
        {
            1 => query.OrderByDescending(p => p.WorkingSetBytes),
            2 => query.OrderByDescending(p => p.GpuPercent ?? -1),
            3 => query.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => query.OrderByDescending(p => p.CpuPercent),
        };

        var rows = query.Select(p => new ProcessRow(p)).ToList();
        var selectedPid = Selected?.Pid;
        for (var i = 0; i < rows.Count; i++)
        {
            if (i < Items.Count)
            {
                if (!Items[i].Equals(rows[i]))
                {
                    Items[i] = rows[i];
                }
            }
            else
            {
                Items.Add(rows[i]);
            }
        }

        while (Items.Count > rows.Count)
        {
            Items.RemoveAt(Items.Count - 1);
        }

        if (selectedPid is { } pid && Selected?.Pid != pid)
        {
            Selected = Items.FirstOrDefault(r => r.Pid == pid);
        }

        var totalCpu = _latest.Sum(p => p.CpuPercent);
        var totalMemory = _latest.Sum(p => p.WorkingSetBytes);
        Summary = string.Create(CultureInfo.CurrentCulture, $"{_latest.Count} processes · {rows.Count} shown · CPU {Math.Min(totalCpu, 100):0} % · working sets {Units.FormatBytes(totalMemory)}");
    }

    private void Report(Result result, string success)
    {
        if (result.IsSuccess)
        {
            notifications.Success(success);
        }
        else
        {
            notifications.Warning(result.Error.Message);
        }
    }
}
