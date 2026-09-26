using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using StormOS.Core.Common;
using StormOS.Core.Ipc;

namespace StormOS.Infrastructure.Ipc;

/// <summary>Verifies that the pipe server is the genuine STORM OS service (protects against pipe squatting).</summary>
public interface IPipeServerVerifier
{
    /// <summary>Verifies the server end of a connected client pipe.</summary>
    /// <param name="pipe">The connected client pipe.</param>
    /// <param name="reason">Why verification failed.</param>
    /// <returns><see langword="true"/> when the server is trusted.</returns>
    bool Verify(NamedPipeClientStream pipe, out string? reason);
}

/// <summary>Client options.</summary>
public sealed class IpcClientOptions
{
    /// <summary>Gets or sets the pipe name.</summary>
    public string PipeName { get; set; } = IpcProtocol.PipeName;

    /// <summary>Gets or sets the connect timeout.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Gets or sets the default request timeout.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>Named pipe client with request/response correlation and server push events.</summary>
public sealed class IpcClient : IAsyncDisposable
{
    private readonly IpcClientOptions _options;
    private readonly IPipeServerVerifier? _verifier;
    private readonly ILogger<IpcClient> _logger;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<IpcResponse>> _pending = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private CancellationTokenSource? _readCts;

    /// <summary>Initializes a new instance of the <see cref="IpcClient"/> class.</summary>
    /// <param name="options">Options.</param>
    /// <param name="verifier">Optional server verifier (required in production on Windows).</param>
    /// <param name="logger">Logger.</param>
    /// <param name="timeProvider">Time source.</param>
    public IpcClient(IpcClientOptions options, IPipeServerVerifier? verifier, ILogger<IpcClient> logger, TimeProvider? timeProvider = null)
    {
        _options = options;
        _verifier = verifier;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Raised for server push events. Invoked on a background thread.</summary>
    public event EventHandler<IpcEvent>? EventReceived;

    /// <summary>Raised when the connection is lost.</summary>
    public event EventHandler? Disconnected;

    /// <summary>Gets a value indicating whether the client is connected.</summary>
    public bool IsConnected => _pipe is { IsConnected: true };

    /// <summary>Connects to the service if not already connected.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or a friendly error.</returns>
    public async Task<Result> ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsConnected)
            {
                return Result.Success;
            }

            await CleanupAsync().ConfigureAwait(false);
            var pipe = new NamedPipeClientStream(".", _options.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
            try
            {
                await pipe.ConnectAsync(_options.ConnectTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                return Result.Failure(StormError.FromException(StormErrorCodes.ServiceUnavailable, "The STORM OS service is not running.", ex));
            }

            if (_verifier is not null && !_verifier.Verify(pipe, out var reason))
            {
                _logger.LogError("Refusing to talk to an unverified pipe server: {Reason}", reason);
                await pipe.DisposeAsync().ConfigureAwait(false);
                return Result.Failure(StormErrorCodes.Unauthorized, "The STORM OS service could not be verified.", reason);
            }

            _pipe = pipe;
            _readCts = new CancellationTokenSource();
            _ = Task.Run(() => ReadLoopAsync(pipe, _readCts.Token), CancellationToken.None);
            return Result.Success;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    /// <summary>Sends a request and waits for the typed response.</summary>
    /// <typeparam name="T">Response payload type.</typeparam>
    /// <param name="operation">Operation name.</param>
    /// <param name="payload">Request payload.</param>
    /// <param name="timeout">Optional timeout overriding the default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response payload or an error.</returns>
    public async Task<Result<T>> RequestAsync<T>(string operation, object? payload = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var connect = await ConnectAsync(cancellationToken).ConfigureAwait(false);
        if (!connect.IsSuccess)
        {
            return Result<T>.Fail(connect.Error);
        }

        var request = new IpcRequest
        {
            RequestId = Guid.NewGuid(),
            Operation = operation,
            Payload = IpcPayload.From(payload),
            Timestamp = _time.GetUtcNow(),
        };
        var completion = new TaskCompletionSource<IpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[request.RequestId] = completion;
        try
        {
            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await IpcFraming.WriteAsync(_pipe!, request, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }

            var response = await completion.Task.WaitAsync(timeout ?? _options.RequestTimeout, _time, cancellationToken).ConfigureAwait(false);
            if (!response.Success)
            {
                var error = response.Error ?? new IpcError(StormErrorCodes.Internal, "The service returned an error.");
                return Result<T>.Fail(error.Code, error.Message);
            }

            return Result<T>.Ok(IpcPayload.To<T>(response.Payload)!);
        }
        catch (TimeoutException ex)
        {
            return Result<T>.Fail(StormError.FromException(StormErrorCodes.Timeout, "The STORM OS service did not respond in time.", ex));
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            return Result<T>.Fail(StormError.FromException(StormErrorCodes.ServiceUnavailable, "The connection to the STORM OS service was lost.", ex));
        }
        catch (System.Text.Json.JsonException ex)
        {
            return Result<T>.Fail(StormError.FromException(StormErrorCodes.Internal, "The service response could not be read.", ex));
        }
        finally
        {
            _pending.TryRemove(request.RequestId, out _);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await CleanupAsync().ConfigureAwait(false);
        _sendLock.Dispose();
        _connectLock.Dispose();
    }

    private async Task ReadLoopAsync(NamedPipeClientStream pipe, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await IpcFraming.ReadAsync(pipe, cancellationToken).ConfigureAwait(false);
                switch (message)
                {
                    case null:
                        return;
                    case IpcResponse response when _pending.TryRemove(response.RequestId, out var tcs):
                        tcs.TrySetResult(response);
                        break;
                    case IpcEvent evt:
                        try
                        {
                            EventReceived?.Invoke(this, evt);
                        }
#pragma warning disable CA1031 // A faulty event subscriber must not break the connection.
                        catch (Exception ex)
#pragma warning restore CA1031
                        {
                            _logger.LogError(ex, "IPC event handler for {Topic} failed", evt.Topic);
                        }

                        break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException or ObjectDisposedException)
        {
            _logger.LogDebug(ex, "IPC read loop ended");
        }
        finally
        {
            foreach (var pending in _pending.Values)
            {
                pending.TrySetException(new IOException("The connection to the STORM OS service was lost."));
            }

            _pending.Clear();
            if (!cancellationToken.IsCancellationRequested)
            {
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private async Task CleanupAsync()
    {
        if (_readCts is not null)
        {
            await _readCts.CancelAsync().ConfigureAwait(false);
            _readCts.Dispose();
            _readCts = null;
        }

        if (_pipe is not null)
        {
            await _pipe.DisposeAsync().ConfigureAwait(false);
            _pipe = null;
        }
    }
}
