using StormOS.Infrastructure.Ipc;
using StormOS.Infrastructure.Persistence;
using StormOS.Service.Handlers;

namespace StormOS.Service.Hosting;

/// <summary>Starts and stops the named pipe server.</summary>
public sealed class IpcHostedService(IpcServer server, ServiceRuntime runtime, SqliteDatabase database, ILogger<IpcHostedService> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        runtime.Server = server;
        server.Start();
        logger.LogInformation("StormOSService {Version} listening", runtime.Version);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await server.StopAsync().ConfigureAwait(false);
        logger.LogInformation("StormOSService stopped");
    }
}
