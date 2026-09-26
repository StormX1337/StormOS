using System.Collections.Concurrent;
using StormOS.Core.Ipc;
using StormOS.Security.Ipc;

namespace StormOS.Infrastructure.Ipc;

/// <summary>A connected client as seen by operation handlers.</summary>
public interface IIpcSession
{
    /// <summary>Gets the connection id.</summary>
    Guid Id { get; }

    /// <summary>Gets the client identity.</summary>
    ClientIdentity Client { get; }

    /// <summary>Gets a token cancelled when the client disconnects.</summary>
    CancellationToken Disconnected { get; }

    /// <summary>Gets per-connection state owned by handlers (for example subscriptions).</summary>
    ConcurrentDictionary<string, IDisposable> Resources { get; }

    /// <summary>Pushes an event to the client.</summary>
    /// <param name="topic">Topic.</param>
    /// <param name="payload">Payload.</param>
    /// <returns>A task representing the operation.</returns>
    Task SendEventAsync(string topic, object? payload);
}

/// <summary>Handles one IPC operation.</summary>
public interface IIpcOperationHandler
{
    /// <summary>Gets the operation name.</summary>
    string Operation { get; }

    /// <summary>Handles a request. Throw <see cref="IpcValidationException"/> for invalid payloads.</summary>
    /// <param name="request">The request.</param>
    /// <param name="session">The calling session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response payload.</returns>
    Task<object?> HandleAsync(IpcRequest request, IIpcSession session, CancellationToken cancellationToken);
}

/// <summary>Thrown by handlers when a payload fails validation.</summary>
public sealed class IpcValidationException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="IpcValidationException"/> class.</summary>
    public IpcValidationException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="IpcValidationException"/> class.</summary>
    /// <param name="message">A user-safe validation message.</param>
    public IpcValidationException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="IpcValidationException"/> class.</summary>
    /// <param name="message">A user-safe validation message.</param>
    /// <param name="innerException">Inner exception.</param>
    public IpcValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Thrown by handlers for expected failures with a stable error code and a user-safe message.</summary>
public sealed class IpcOperationException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="IpcOperationException"/> class.</summary>
    public IpcOperationException()
    {
        Code = Core.Common.StormErrorCodes.Internal;
    }

    /// <summary>Initializes a new instance of the <see cref="IpcOperationException"/> class.</summary>
    /// <param name="message">User-safe message.</param>
    public IpcOperationException(string message)
        : base(message)
    {
        Code = Core.Common.StormErrorCodes.Internal;
    }

    /// <summary>Initializes a new instance of the <see cref="IpcOperationException"/> class.</summary>
    /// <param name="message">User-safe message.</param>
    /// <param name="innerException">Inner exception.</param>
    public IpcOperationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = Core.Common.StormErrorCodes.Internal;
    }

    /// <summary>Initializes a new instance of the <see cref="IpcOperationException"/> class.</summary>
    /// <param name="code">Error code.</param>
    /// <param name="message">User-safe message.</param>
    public IpcOperationException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>Gets the error code.</summary>
    public string Code { get; }
}

/// <summary>Base class for handlers with a typed, validated payload.</summary>
/// <typeparam name="TRequest">Payload type. Use <see cref="NoPayload"/> for operations without payload.</typeparam>
public abstract class IpcHandler<TRequest> : IIpcOperationHandler
    where TRequest : class
{
    /// <inheritdoc />
    public abstract string Operation { get; }

    /// <inheritdoc />
    public async Task<object?> HandleAsync(IpcRequest request, IIpcSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        TRequest? payload;
        if (typeof(TRequest) == typeof(NoPayload))
        {
            payload = (TRequest)(object)NoPayload.Instance;
        }
        else
        {
            try
            {
                payload = IpcPayload.To<TRequest>(request.Payload);
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new IpcValidationException("The request payload is malformed.", ex);
            }

            if (payload is null)
            {
                throw new IpcValidationException("The request payload is required.");
            }
        }

        var error = Validate(payload);
        if (error is not null)
        {
            throw new IpcValidationException(error);
        }

        return await HandleAsync(payload, session, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Validates the payload.</summary>
    /// <param name="payload">The payload.</param>
    /// <returns><see langword="null"/> when valid, otherwise a user-safe message.</returns>
    protected virtual string? Validate(TRequest payload) => null;

    /// <summary>Handles a validated request.</summary>
    /// <param name="payload">The payload.</param>
    /// <param name="session">The session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response payload.</returns>
    protected abstract Task<object?> HandleAsync(TRequest payload, IIpcSession session, CancellationToken cancellationToken);
}

/// <summary>Marker payload for operations without input.</summary>
public sealed class NoPayload
{
    private NoPayload()
    {
    }

    /// <summary>Gets the singleton instance.</summary>
    public static NoPayload Instance { get; } = new();
}
