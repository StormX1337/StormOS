using System.Collections.Concurrent;
using System.IO.Pipes;
using Microsoft.Extensions.Logging;
using StormOS.Core.Common;
using StormOS.Core.Ipc;
using StormOS.Security.Ipc;

namespace StormOS.Infrastructure.Ipc;

/// <summary>Creates server pipe instances. The Windows implementation applies a restrictive ACL.</summary>
public interface IPipeServerFactory
{
    /// <summary>Creates a server pipe instance.</summary>
    /// <param name="pipeName">Pipe name.</param>
    /// <param name="firstInstance">Whether this is the first instance (prevents pipe squatting on Windows).</param>
    /// <param name="maxInstances">Maximum number of instances.</param>
    /// <returns>The server stream.</returns>
    NamedPipeServerStream Create(string pipeName, bool firstInstance, int maxInstances);
}

/// <summary>Pipe factory without ACLs, used on non-Windows development hosts and in tests.</summary>
public sealed class DefaultPipeServerFactory : IPipeServerFactory
{
    /// <inheritdoc />
    public NamedPipeServerStream Create(string pipeName, bool firstInstance, int maxInstances) =>
        new(pipeName, PipeDirection.InOut, maxInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
}

/// <summary>Server options.</summary>
public sealed class IpcServerOptions
{
    /// <summary>Gets or sets the pipe name.</summary>
    public string PipeName { get; set; } = IpcProtocol.PipeName;

    /// <summary>Gets or sets the maximum concurrent connections.</summary>
    public int MaxConnections { get; set; } = 8;

    /// <summary>Gets or sets the maximum concurrently executing requests per connection.</summary>
    public int MaxInFlightPerConnection { get; set; } = 8;

    /// <summary>Gets or sets the rate limiter burst capacity per connection.</summary>
    public double RateLimitBurst { get; set; } = 100;

    /// <summary>Gets or sets the rate limiter refill per second per connection.</summary>
    public double RateLimitPerSecond { get; set; } = 40;
}

/// <summary>Named pipe server hosting the STORM OS service API.</summary>
public sealed class IpcServer : IAsyncDisposable
{
    private readonly IPipeServerFactory _factory;
    private readonly IClientIdentityResolver _identityResolver;
    private readonly IpcDispatcher _dispatcher;
    private readonly IpcServerOptions _options;
    private readonly ILogger<IpcServer> _logger;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<Guid, ServerSession> _sessions = new();
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _acceptLoop;

    /// <summary>Initializes a new instance of the <see cref="IpcServer"/> class.</summary>
    /// <param name="factory">Pipe factory.</param>
    /// <param name="identityResolver">Client identity resolver.</param>
    /// <param name="dispatcher">Request dispatcher.</param>
    /// <param name="options">Options.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="timeProvider">Time source.</param>
    public IpcServer(IPipeServerFactory factory, IClientIdentityResolver identityResolver, IpcDispatcher dispatcher, IpcServerOptions options, ILogger<IpcServer> logger, TimeProvider? timeProvider = null)
    {
        _factory = factory;
        _identityResolver = identityResolver;
        _dispatcher = dispatcher;
        _options = options;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Gets the number of connected clients.</summary>
    public int ConnectionCount => _sessions.Count;

    /// <summary>Gets the connected sessions.</summary>
    public IEnumerable<IIpcSession> Sessions => _sessions.Values;

    /// <summary>Starts accepting connections.</summary>
    public void Start()
    {
        if (_acceptLoop is not null)
        {
            throw new InvalidOperationException("The IPC server is already running.");
        }

        _acceptLoop = Task.Run(() => AcceptLoopAsync(_shutdown.Token));
    }

    /// <summary>Stops accepting connections and closes all sessions.</summary>
    /// <returns>A task representing the operation.</returns>
    public async Task StopAsync()
    {
        if (_shutdown.IsCancellationRequested)
        {
            return;
        }

        await _shutdown.CancelAsync().ConfigureAwait(false);
        foreach (var session in _sessions.Values)
        {
            session.Close();
        }

        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _shutdown.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        var first = true;
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = _factory.Create(_options.PipeName, first, NamedPipeServerStream.MaxAllowedServerInstances);
                first = false;
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Could not create the service pipe; retrying");
                await Task.Delay(TimeSpan.FromSeconds(1), _time, cancellationToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                break;
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Pipe connection failed");
                await pipe.DisposeAsync().ConfigureAwait(false);
                continue;
            }

            if (_sessions.Count >= _options.MaxConnections)
            {
                _logger.LogWarning("Rejected IPC connection: {Count} connections already open", _sessions.Count);
                await pipe.DisposeAsync().ConfigureAwait(false);
                continue;
            }

            _ = Task.Run(() => RunSessionAsync(pipe, cancellationToken), CancellationToken.None);
        }
    }

    private async Task RunSessionAsync(NamedPipeServerStream pipe, CancellationToken serverToken)
    {
        ClientIdentity identity;
        try
        {
            identity = _identityResolver.Resolve(pipe);
        }
#pragma warning disable CA1031 // Identity resolution failures downgrade trust instead of dropping the service.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogWarning(ex, "Could not resolve IPC client identity");
            identity = ClientIdentity.Unknown;
        }

        using var session = new ServerSession(pipe, identity, _options, _time, serverToken);
        _sessions[session.Id] = session;
        _logger.LogInformation("IPC client connected: {Client} trust {Trust}", identity.AuditName, identity.Trust);
        try
        {
            while (!session.Disconnected.IsCancellationRequested)
            {
                IpcMessage? message;
                try
                {
                    message = await IpcFraming.ReadAsync(pipe, session.Disconnected).ConfigureAwait(false);
                }
                catch (InvalidDataException ex)
                {
                    _logger.LogWarning(ex, "Dropping IPC client {Client}: malformed frame", identity.AuditName);
                    break;
                }

                if (message is null)
                {
                    break;
                }

                if (message is not IpcRequest request)
                {
                    continue;
                }

                OperationPolicies.TryGet(request.Operation, out var policy);
                if (!session.RateLimiter.TryConsume(policy.Cost))
                {
                    await session.SendAsync(new IpcResponse
                    {
                        RequestId = request.RequestId,
                        Success = false,
                        Error = new IpcError(StormErrorCodes.RateLimited, "Too many requests. Please slow down."),
                        Timestamp = _time.GetUtcNow(),
                    }).ConfigureAwait(false);
                    continue;
                }

                await session.InFlight.WaitAsync(session.Disconnected).ConfigureAwait(false);
                _ = ProcessRequestAsync(session, request);
            }
        }
        catch (OperationCanceledException)
        {
            // Session closed.
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "IPC client {Client} disconnected", identity.AuditName);
        }
        finally
        {
            _sessions.TryRemove(session.Id, out _);
            session.Close();
            _logger.LogInformation("IPC client disconnected: {Client}", identity.AuditName);
        }
    }

    private async Task ProcessRequestAsync(ServerSession session, IpcRequest request)
    {
        try
        {
            var response = await _dispatcher.DispatchAsync(request, session, session.Disconnected).ConfigureAwait(false);
            await session.SendAsync(response).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            _logger.LogDebug(ex, "Could not deliver response for {Operation}", request.Operation);
        }
        finally
        {
            session.InFlight.Release();
        }
    }

    private sealed class ServerSession : IIpcSession, IDisposable
    {
        private readonly NamedPipeServerStream _pipe;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly CancellationTokenSource _disconnected;
        private readonly TimeProvider _time;
        private int _closed;

        public ServerSession(NamedPipeServerStream pipe, ClientIdentity client, IpcServerOptions options, TimeProvider time, CancellationToken serverToken)
        {
            _pipe = pipe;
            Client = client;
            _time = time;
            _disconnected = CancellationTokenSource.CreateLinkedTokenSource(serverToken);
            RateLimiter = new TokenBucket(options.RateLimitBurst, options.RateLimitPerSecond, time);
            InFlight = new SemaphoreSlim(options.MaxInFlightPerConnection, options.MaxInFlightPerConnection);
        }

        public Guid Id { get; } = Guid.NewGuid();

        public ClientIdentity Client { get; }

        public CancellationToken Disconnected => _disconnected.Token;

        public ConcurrentDictionary<string, IDisposable> Resources { get; } = new(StringComparer.Ordinal);

        public TokenBucket RateLimiter { get; }

        public SemaphoreSlim InFlight { get; }

        public Task SendEventAsync(string topic, object? payload) =>
            SendAsync(new IpcEvent { Topic = topic, Payload = IpcPayload.From(payload), Timestamp = _time.GetUtcNow() });

        public async Task SendAsync(IpcMessage message)
        {
            if (Volatile.Read(ref _closed) != 0)
            {
                return;
            }

            await _sendLock.WaitAsync(Disconnected).ConfigureAwait(false);
            try
            {
                await IpcFraming.WriteAsync(_pipe, message, Disconnected).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public void Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0)
            {
                return;
            }

            try
            {
                _disconnected.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed.
            }

            foreach (var resource in Resources.Values)
            {
                resource.Dispose();
            }

            Resources.Clear();
            _pipe.Dispose();
        }

        public void Dispose()
        {
            Close();
            _disconnected.Dispose();
            _sendLock.Dispose();
            InFlight.Dispose();
        }
    }
}
