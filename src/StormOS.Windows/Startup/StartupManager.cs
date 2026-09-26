using System.Diagnostics;
using StormOS.Core.Startup;
using StormOS.Windows.RegistryAccess;

namespace StormOS.Windows.Startup;

/// <summary>
/// Lists and toggles startup entries using the StartupApproved registry keys, exactly like Task Manager.
/// Entries are never deleted; disabling writes the "disabled" marker and enabling restores the "enabled" marker.
/// </summary>
public sealed class StartupManager : IStartupManager
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunKey32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRoot = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    private readonly IRegistryAccess _registry;
    private readonly TimeProvider _time;

    /// <summary>Initializes a new instance of the <see cref="StartupManager"/> class.</summary>
    /// <param name="registry">Registry access.</param>
    /// <param name="timeProvider">Time source.</param>
    public StartupManager(IRegistryAccess registry, TimeProvider? timeProvider = null)
    {
        _registry = registry;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Gets the user Startup folder.</summary>
    public static string UserStartupFolder => Environment.GetFolderPath(Environment.SpecialFolder.Startup);

    /// <summary>Gets the common Startup folder.</summary>
    public static string CommonStartupFolder => Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);

    /// <inheritdoc />
    public Task<IReadOnlyList<StartupEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        var entries = new List<StartupEntry>();
        AddRegistryEntries(entries, RegistryHive.CurrentUser, RunKey, StartupSource.RegistryUserRun);
        AddRegistryEntries(entries, RegistryHive.LocalMachine, RunKey, StartupSource.RegistryMachineRun);
        AddRegistryEntries(entries, RegistryHive.LocalMachine, RunKey32, StartupSource.RegistryMachineRun32);
        AddFolderEntries(entries, UserStartupFolder, StartupSource.UserStartupFolder);
        AddFolderEntries(entries, CommonStartupFolder, StartupSource.CommonStartupFolder);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<StartupEntry>>(AttachRuntime(entries));
    }

    /// <inheritdoc />
    public bool? IsEnabled(string entryId)
    {
        if (!TryParseId(entryId, out var source, out var name))
        {
            return null;
        }

        var (hive, approvedKey) = ApprovedLocation(source);
        return IsApproved(_registry.GetValue(hive, approvedKey, name));
    }

    /// <inheritdoc />
    public void SetEnabled(string entryId, bool enabled)
    {
        if (!TryParseId(entryId, out var source, out var name))
        {
            throw new ArgumentException("Unknown startup entry id.", nameof(entryId));
        }

        var (hive, approvedKey) = ApprovedLocation(source);
        var data = new byte[12];
        data[0] = enabled ? (byte)0x02 : (byte)0x03;
        if (!enabled)
        {
            BitConverter.TryWriteBytes(data.AsSpan(4), _time.GetUtcNow().UtcDateTime.ToFileTimeUtc());
        }

        _registry.SetValue(hive, approvedKey, name, new RegistryValue(RegistryKind.Binary, data));
    }

    /// <summary>Creates an entry id.</summary>
    /// <param name="source">Source.</param>
    /// <param name="name">Value or file name.</param>
    /// <returns>The id.</returns>
    public static string CreateId(StartupSource source, string name) => $"{source}|{name}";

    /// <summary>Interprets a StartupApproved value. Absent values mean enabled; an odd first byte means disabled.</summary>
    /// <param name="value">The registry value.</param>
    /// <returns><see langword="true"/> when enabled.</returns>
    public static bool IsApproved(RegistryValue? value) =>
        value is not { Kind: RegistryKind.Binary, Value: byte[] { Length: > 0 } bytes } || (bytes[0] & 0x1) == 0;

    private static bool TryParseId(string entryId, out StartupSource source, out string name)
    {
        name = string.Empty;
        source = default;
        var separator = entryId?.IndexOf('|', StringComparison.Ordinal) ?? -1;
        if (separator <= 0 || !Enum.TryParse(entryId![..separator], out source) || source == StartupSource.ScheduledTask)
        {
            return false;
        }

        name = entryId[(separator + 1)..];
        return name.Length is > 0 and < 260 && !name.Contains('\\', StringComparison.Ordinal);
    }

    private static (RegistryHive Hive, string Key) ApprovedLocation(StartupSource source) => source switch
    {
        StartupSource.RegistryUserRun => (RegistryHive.CurrentUser, ApprovedRoot + "Run"),
        StartupSource.RegistryMachineRun => (RegistryHive.LocalMachine, ApprovedRoot + "Run"),
        StartupSource.RegistryMachineRun32 => (RegistryHive.LocalMachine, ApprovedRoot + "Run32"),
        StartupSource.UserStartupFolder => (RegistryHive.CurrentUser, ApprovedRoot + "StartupFolder"),
        StartupSource.CommonStartupFolder => (RegistryHive.LocalMachine, ApprovedRoot + "StartupFolder"),
        _ => throw new ArgumentOutOfRangeException(nameof(source)),
    };

    private void AddRegistryEntries(List<StartupEntry> entries, RegistryHive hive, string key, StartupSource source)
    {
        var (approvedHive, approvedKey) = ApprovedLocation(source);
        foreach (var name in _registry.GetValueNames(hive, key))
        {
            if (string.IsNullOrEmpty(name) || _registry.GetValue(hive, key, name) is not { Value: string command })
            {
                continue;
            }

            var exe = CommandLineParser.ExtractExecutable(command);
            entries.Add(new StartupEntry
            {
                Id = CreateId(source, name),
                Name = name,
                Command = command,
                ExecutablePath = exe,
                Publisher = ReadPublisher(exe),
                Source = source,
                IsEnabled = IsApproved(_registry.GetValue(approvedHive, approvedKey, name)),
                RequiresAdmin = hive == RegistryHive.LocalMachine,
            });
        }
    }

    private void AddFolderEntries(List<StartupEntry> entries, string folder, StartupSource source)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return;
        }

        var (approvedHive, approvedKey) = ApprovedLocation(source);
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var link = file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ShellLinkReader.Read(file) : null;
            var target = link?.TargetPath ?? file;
            entries.Add(new StartupEntry
            {
                Id = CreateId(source, fileName),
                Name = Path.GetFileNameWithoutExtension(fileName),
                Command = link is null ? file : $"\"{target}\" {link.Arguments}".Trim(),
                ExecutablePath = target,
                Publisher = ReadPublisher(target),
                Source = source,
                IsEnabled = IsApproved(_registry.GetValue(approvedHive, approvedKey, fileName)),
                RequiresAdmin = source == StartupSource.CommonStartupFolder,
            });
        }
    }

    private static List<StartupEntry> AttachRuntime(List<StartupEntry> entries)
    {
        var byPath = new Dictionary<string, (double Cpu, long Memory)>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (path is null)
                    {
                        continue;
                    }

                    byPath.TryGetValue(path, out var existing);
                    byPath[path] = (existing.Cpu + process.TotalProcessorTime.TotalSeconds, existing.Memory + process.WorkingSet64);
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Elevated or exited processes are skipped; their impact stays "not measured".
                }
            }
        }

        return entries.Select(e => e.ExecutablePath is not null && byPath.TryGetValue(e.ExecutablePath, out var runtime)
            ? e with { RuntimeCpuSeconds = runtime.Cpu, RuntimeMemoryBytes = runtime.Memory }
            : e).ToList();
    }

    private static string? ReadPublisher(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        var company = FileVersionInfo.GetVersionInfo(path).CompanyName;
        return string.IsNullOrWhiteSpace(company) ? null : company.Trim();
    }
}
