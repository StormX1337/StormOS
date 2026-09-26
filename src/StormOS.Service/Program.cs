using Serilog;
using StormOS.Infrastructure.Configuration;
using StormOS.Infrastructure.Logging;
using StormOS.Infrastructure.Paths;
using StormOS.Service.Hosting;

// StormOSService: the privileged half of STORM OS. It runs as LocalSystem, exposes a narrow, validated IPC API
// over a named pipe and performs only allow-listed operations (see docs/SECURITY.md).
var paths = new StormPaths(StormProcessKind.Service, Environment.GetEnvironmentVariable("STORMOS_DATA_ROOT"));
paths.EnsureCreated(StormProcessKind.Service);

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
    EnvironmentName = StormConfiguration.EnvironmentName,
});
builder.Configuration.Sources.Clear();
builder.Configuration.AddStormConfiguration(AppContext.BaseDirectory);

Log.Logger = StormLogging.Create(builder.Configuration, paths.Logs, LogCategories.Service, builder.Configuration.GetValue("Logging:Developer", false));
builder.Logging.ClearProviders();
builder.Logging.AddSerilog(Log.Logger, dispose: true);
if (OperatingSystem.IsWindows())
{
    builder.Logging.AddEventLog(o =>
    {
        o.SourceName = "StormOSService";
        o.Filter = (_, level) => level >= LogLevel.Error;
    });
}

builder.Services.AddWindowsService(options => options.ServiceName = "StormOSService");
builder.Services.AddStormService(builder.Configuration, paths);

try
{
    using var host = builder.Build();
    await host.RunAsync();
    return 0;
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    Log.Fatal(ex, "StormOSService terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
