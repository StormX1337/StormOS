using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using StormOS.Windows.Processes;
using StormOS.Windows.Wmi;

namespace StormOS.Games.Detection;

/// <summary>Process source backed by System.Diagnostics and WMI (command lines are only read on demand).</summary>
public sealed class WindowsProcessSource : IProcessSource
{
    private readonly ConcurrentDictionary<(int, DateTime), string?> _paths = new();

    /// <inheritdoc />
    public IReadOnlyList<ProcessSnapshot> List()
    {
        var list = new List<ProcessSnapshot>();
        var alive = new HashSet<(int, DateTime)>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id <= 4)
                    {
                        continue;
                    }

                    var start = process.StartTime;
                    var key = (process.Id, start);
                    alive.Add(key);
                    var path = _paths.GetOrAdd(key, static (_, p) =>
                    {
                        try
                        {
                            return p.MainModule?.FileName;
                        }
                        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                        {
                            return null;
                        }
                    }, process);
                    list.Add(new ProcessSnapshot(process.Id, process.ProcessName, path, start));
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Access denied (protected process) or exited during enumeration.
                }
            }
        }

        foreach (var key in _paths.Keys)
        {
            if (!alive.Contains(key))
            {
                _paths.TryRemove(key, out _);
            }
        }

        return list;
    }

    /// <inheritdoc />
    public string? GetCommandLine(int processId)
    {
        var rows = WmiQuery.Query(@"root\cimv2", "SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + processId.ToString(CultureInfo.InvariantCulture), TimeSpan.FromSeconds(3));
        return rows.Count > 0 ? rows[0].Str("CommandLine") : null;
    }

    /// <inheritdoc />
    public int? GetForegroundProcessId() => ForegroundProcess.GetProcessId();
}
