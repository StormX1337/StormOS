using System.Diagnostics;
using System.ServiceProcess;
using StormOS.Core.Ipc;
using StormOS.Windows.Interop;
using StormOS.Windows.RegistryAccess;
using StormOS.Windows.Wmi;

namespace StormOS.Windows.Services;

/// <summary>Service start types STORM OS can read and restore.</summary>
public enum ServiceStartKind
{
    /// <summary>Automatic.</summary>
    Automatic,

    /// <summary>Automatic (delayed start).</summary>
    AutomaticDelayed,

    /// <summary>Manual.</summary>
    Manual,

    /// <summary>Disabled.</summary>
    Disabled,

    /// <summary>Boot or system driver start.</summary>
    System,
}

/// <summary>Reads and changes Windows service configuration.</summary>
public interface IServiceConfigurator
{
    /// <summary>Gets the start type of a service.</summary>
    /// <param name="serviceName">Service name.</param>
    /// <returns>The start type or <see langword="null"/> when the service does not exist.</returns>
    ServiceStartKind? GetStartKind(string serviceName);

    /// <summary>Changes the start type of a service (requires administrative rights).</summary>
    /// <param name="serviceName">Service name.</param>
    /// <param name="kind">New start type.</param>
    void SetStartKind(string serviceName, ServiceStartKind kind);

    /// <summary>Lists services.</summary>
    /// <returns>Service entries.</returns>
    IReadOnlyList<ServiceEntry> List();
}

/// <summary>Service configuration through the Service Control Manager.</summary>
public sealed class ServiceConfigurator : IServiceConfigurator
{
    private const string ServicesKey = @"SYSTEM\CurrentControlSet\Services\";
    private readonly IRegistryAccess _registry;

    /// <summary>Initializes a new instance of the <see cref="ServiceConfigurator"/> class.</summary>
    /// <param name="registry">Registry access (for the delayed auto-start flag).</param>
    public ServiceConfigurator(IRegistryAccess registry) => _registry = registry;

    /// <inheritdoc />
    public ServiceStartKind? GetStartKind(string serviceName)
    {
        try
        {
            using var controller = new ServiceController(serviceName);
            return controller.StartType switch
            {
                ServiceStartMode.Automatic => IsDelayed(serviceName) ? ServiceStartKind.AutomaticDelayed : ServiceStartKind.Automatic,
                ServiceStartMode.Manual => ServiceStartKind.Manual,
                ServiceStartMode.Disabled => ServiceStartKind.Disabled,
                _ => ServiceStartKind.System,
            };
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void SetStartKind(string serviceName, ServiceStartKind kind)
    {
        if (kind == ServiceStartKind.System)
        {
            throw new ArgumentException("Driver start types are never changed by STORM OS.", nameof(kind));
        }

        var scm = Advapi32.OpenSCManager(null, null, Advapi32.ScManagerConnect);
        if (scm == IntPtr.Zero)
        {
            throw new UnauthorizedAccessException("The Service Control Manager could not be opened.");
        }

        try
        {
            var service = Advapi32.OpenService(scm, serviceName, Advapi32.ServiceChangeConfig | Advapi32.ServiceQueryConfig);
            if (service == IntPtr.Zero)
            {
                throw new InvalidOperationException($"The service '{serviceName}' could not be opened (error {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}).");
            }

            try
            {
                var startType = kind switch
                {
                    ServiceStartKind.Automatic or ServiceStartKind.AutomaticDelayed => Advapi32.ServiceAutoStart,
                    ServiceStartKind.Manual => Advapi32.ServiceDemandStart,
                    _ => Advapi32.ServiceDisabled,
                };
                if (!Advapi32.ChangeServiceConfig(service, Advapi32.ServiceNoChange, startType, Advapi32.ServiceNoChange, null, null, IntPtr.Zero, null, null, null, null))
                {
                    throw new InvalidOperationException($"ChangeServiceConfig failed with error {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}.");
                }
            }
            finally
            {
                Advapi32.CloseServiceHandle(service);
            }
        }
        finally
        {
            Advapi32.CloseServiceHandle(scm);
        }

        if (kind is ServiceStartKind.Automatic or ServiceStartKind.AutomaticDelayed)
        {
            _registry.SetValue(RegistryHive.LocalMachine, ServicesKey + serviceName, "DelayedAutostart", new RegistryValue(RegistryKind.DWord, kind == ServiceStartKind.AutomaticDelayed ? 1 : 0));
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ServiceEntry> List()
    {
        var rows = WmiQuery.Query(@"root\cimv2", "SELECT Name, DisplayName, State, StartMode, ProcessId, DelayedAutoStart FROM Win32_Service");
        var memory = new Dictionary<int, long>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                memory[process.Id] = process.WorkingSet64;
            }
        }

        return rows.Select(row =>
        {
            var pid = (int?)row.Int64Value("ProcessId");
            var startMode = row.Str("StartMode") ?? string.Empty;
            var delayed = row.TryGetValue("DelayedAutoStart", out var d) && d is true;
            return new ServiceEntry
            {
                Name = row.Str("Name") ?? string.Empty,
                DisplayName = row.Str("DisplayName") ?? string.Empty,
                Status = row.Str("State") ?? string.Empty,
                StartType = startMode == "Auto" && delayed ? "Automatic (Delayed)" : startMode == "Auto" ? "Automatic" : startMode,
                ProcessId = pid is > 0 ? pid : null,
                WorkingSetBytes = pid is > 0 && memory.TryGetValue(pid.Value, out var ws) ? ws : null,
            };
        }).OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private bool IsDelayed(string serviceName) =>
        _registry.GetValue(RegistryHive.LocalMachine, ServicesKey + serviceName, "DelayedAutostart") is { Kind: RegistryKind.DWord, Value: 1 };
}
