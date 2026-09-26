using System.Diagnostics;
using Microsoft.Extensions.Logging;
using StormOS.Core.Common;
using StormOS.Core.Processes;
using StormOS.Windows.Interop;

namespace StormOS.Windows.Processes;

/// <summary>Sets priorities and ends processes with protection and PID-reuse checks.</summary>
public sealed class ProcessController : IProcessController
{
    private readonly ILogger<ProcessController> _logger;

    /// <summary>Initializes a new instance of the <see cref="ProcessController"/> class.</summary>
    /// <param name="logger">Logger.</param>
    public ProcessController(ILogger<ProcessController> logger) => _logger = logger;

    /// <inheritdoc />
    public ProcessPriority? GetPriority(int processId)
    {
        using var handle = Kernel32.OpenProcess(Kernel32.ProcessQueryLimitedInformation, false, processId);
        return handle.IsInvalid ? null : PriorityMapping.FromNative(Kernel32.GetPriorityClass(handle));
    }

    /// <inheritdoc />
    public Result SetPriority(int processId, ProcessPriority priority)
    {
        var name = GetName(processId);
        if (name is null)
        {
            return Result.Failure(StormErrorCodes.NotFound, "The process is no longer running.");
        }

        if (ProcessProtection.IsProtected(name))
        {
            return Result.Failure(StormErrorCodes.Unauthorized, $"STORM OS does not change the priority of {name} because it is a protected system, security or anti-cheat process.");
        }

        using var handle = Kernel32.OpenProcess(Kernel32.ProcessSetInformation | Kernel32.ProcessQueryLimitedInformation, false, processId);
        if (handle.IsInvalid)
        {
            return Result.Failure(StormErrorCodes.RequiresAdmin, $"Access to {name} was denied.");
        }

        if (!Kernel32.SetPriorityClass(handle, PriorityMapping.ToNative(priority)))
        {
            return Result.Failure(StormErrorCodes.Internal, $"The priority of {name} could not be changed.", $"SetPriorityClass failed: {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}");
        }

        _logger.LogInformation("Priority of {Process} ({Pid}) set to {Priority}", name, processId, priority);
        return Result.Success;
    }

    /// <inheritdoc />
    public Result Terminate(int processId, string expectedName)
    {
        var name = GetName(processId);
        if (name is null)
        {
            return Result.Failure(StormErrorCodes.NotFound, "The process is no longer running.");
        }

        if (!string.Equals(name, Path.GetFileNameWithoutExtension(expectedName), StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(StormErrorCodes.Conflict, "The process id now belongs to a different program. Refresh the list and try again.");
        }

        if (ProcessProtection.IsProtected(name))
        {
            return Result.Failure(StormErrorCodes.Unauthorized, $"STORM OS will not end {name} because it is a protected system, security or anti-cheat process.");
        }

        using var handle = Kernel32.OpenProcess(Kernel32.ProcessTerminate | Kernel32.ProcessQueryLimitedInformation, false, processId);
        if (handle.IsInvalid)
        {
            return Result.Failure(StormErrorCodes.RequiresAdmin, $"Access to {name} was denied.");
        }

        if (!Kernel32.TerminateProcess(handle, 1))
        {
            return Result.Failure(StormErrorCodes.Internal, $"{name} could not be ended.", $"TerminateProcess failed: {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}");
        }

        _logger.LogInformation("Ended process {Process} ({Pid})", name, processId);
        return Result.Success;
    }

    private static string? GetName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}

/// <summary>Foreground window helpers.</summary>
public static class ForegroundProcess
{
    /// <summary>Gets the process id owning the foreground window.</summary>
    /// <returns>The process id or <see langword="null"/>.</returns>
    public static int? GetProcessId()
    {
        var window = User32.GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            return null;
        }

        _ = User32.GetWindowThreadProcessId(window, out var pid);
        return pid > 0 ? pid : null;
    }
}
