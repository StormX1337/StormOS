using System.Collections.Concurrent;
using System.Diagnostics;
using StormOS.Core.Processes;
using StormOS.Windows.Interop;
using StormOS.Windows.Security;

namespace StormOS.Windows.Processes;

/// <summary>Enumerates processes with CPU, memory, priority, publisher and (on demand) signature information.</summary>
public sealed class ProcessInspector : IProcessInspector
{
    private readonly IProcessGpuUsageProvider? _gpu;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, string?> _publisherCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SignatureStatus> _signatureCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private Dictionary<int, (ulong CpuTime, DateTime Start)> _previous = [];
    private long _previousTimestamp;

    /// <summary>Initializes a new instance of the <see cref="ProcessInspector"/> class.</summary>
    /// <param name="gpu">Optional per-process GPU usage source.</param>
    /// <param name="timeProvider">Time source.</param>
    public ProcessInspector(IProcessGpuUsageProvider? gpu = null, TimeProvider? timeProvider = null)
    {
        _gpu = gpu;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public IReadOnlyList<ProcessEntry> Snapshot()
    {
        lock (_gate)
        {
            var now = _time.GetTimestamp();
            var elapsed = _previousTimestamp == 0 ? TimeSpan.Zero : _time.GetElapsedTime(_previousTimestamp, now);
            var capacity = elapsed.TotalMilliseconds * 10_000 * Environment.ProcessorCount;
            var gpu = _gpu?.GetGpuUsageByProcess();
            var current = new Dictionary<int, (ulong, DateTime)>();
            var list = new List<ProcessEntry>();

            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        list.Add(Describe(process, current, capacity, gpu));
                    }
                    catch (InvalidOperationException)
                    {
                        // The process exited during enumeration.
                    }
                }
            }

            _previous = current;
            _previousTimestamp = now;
            return list;
        }
    }

    /// <inheritdoc />
    public SignatureStatus VerifySignature(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return SignatureStatus.Unknown;
        }

        return _signatureCache.GetOrAdd(path, static p =>
        {
            if (AuthenticodeVerifier.IsTrusted(p))
            {
                return SignatureStatus.Valid;
            }

            try
            {
#pragma warning disable SYSLIB0057 // Presence check of an embedded signature.
                using var certificate = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(p);
#pragma warning restore SYSLIB0057
                return SignatureStatus.Invalid;
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                return SignatureStatus.NotSigned;
            }
        });
    }

    /// <inheritdoc />
    public bool IsProtected(string processName) => ProcessProtection.IsProtected(processName);

    private ProcessEntry Describe(Process process, Dictionary<int, (ulong, DateTime)> current, double capacity, IReadOnlyDictionary<int, double>? gpu)
    {
        var pid = process.Id;
        string? path = null;
        ProcessPriority? priority = null;
        double cpuPercent = 0;

        using (var handle = Kernel32.OpenProcess(Kernel32.ProcessQueryLimitedInformation, false, pid))
        {
            if (!handle.IsInvalid)
            {
                path = Kernel32.GetProcessImagePath(handle);
                priority = PriorityMapping.FromNative(Kernel32.GetPriorityClass(handle));
                if (Kernel32.GetProcessTimes(handle, out var creation, out _, out var kernel, out var user))
                {
                    var cpu = kernel.Value + user.Value;
                    var start = DateTime.FromFileTimeUtc((long)creation.Value);
                    current[pid] = (cpu, start);
                    if (capacity > 0 && _previous.TryGetValue(pid, out var prev) && prev.Start == start && cpu >= prev.CpuTime)
                    {
                        cpuPercent = Math.Clamp(100.0 * (cpu - prev.CpuTime) / capacity, 0, 100);
                    }
                }
            }
        }

        var name = process.ProcessName;
        return new ProcessEntry
        {
            ProcessId = pid,
            Name = name,
            Path = path,
            CpuPercent = cpuPercent,
            WorkingSetBytes = process.WorkingSet64,
            GpuPercent = gpu is not null && gpu.TryGetValue(pid, out var g) ? g : null,
            Priority = priority,
            Publisher = path is null ? null : _publisherCache.GetOrAdd(path, ReadPublisher),
            Signature = path is not null && _signatureCache.TryGetValue(path, out var sig) ? sig : SignatureStatus.Unknown,
            SessionId = process.SessionId,
            HandleCount = process.HandleCount,
            IsCritical = ProcessProtection.IsProtected(name),
        };
    }

    private static string? ReadPublisher(string path)
    {
        try
        {
            var company = FileVersionInfo.GetVersionInfo(path).CompanyName;
            return string.IsNullOrWhiteSpace(company) ? null : company.Trim();
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}

/// <summary>Maps between <see cref="ProcessPriority"/> and Win32 priority classes.</summary>
public static class PriorityMapping
{
    /// <summary>Converts a Win32 priority class.</summary>
    /// <param name="priorityClass">Priority class.</param>
    /// <returns>The priority, or <see langword="null"/> for unknown or realtime classes.</returns>
    public static ProcessPriority? FromNative(uint priorityClass) => priorityClass switch
    {
        Kernel32.IdlePriorityClass => ProcessPriority.Idle,
        Kernel32.BelowNormalPriorityClass => ProcessPriority.BelowNormal,
        Kernel32.NormalPriorityClass => ProcessPriority.Normal,
        Kernel32.AboveNormalPriorityClass => ProcessPriority.AboveNormal,
        Kernel32.HighPriorityClass => ProcessPriority.High,
        _ => null,
    };

    /// <summary>Converts to a Win32 priority class.</summary>
    /// <param name="priority">Priority.</param>
    /// <returns>The priority class.</returns>
    public static uint ToNative(ProcessPriority priority) => priority switch
    {
        ProcessPriority.Idle => Kernel32.IdlePriorityClass,
        ProcessPriority.BelowNormal => Kernel32.BelowNormalPriorityClass,
        ProcessPriority.AboveNormal => Kernel32.AboveNormalPriorityClass,
        ProcessPriority.High => Kernel32.HighPriorityClass,
        _ => Kernel32.NormalPriorityClass,
    };
}
