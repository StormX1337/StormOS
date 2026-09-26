using Microsoft.Extensions.Logging;
using StormOS.Core.Common;
using StormOS.Core.Ipc;
using StormOS.Security.Ipc;

namespace StormOS.Infrastructure.Ipc;

/// <summary>
/// Validates, authorizes and routes requests to operation handlers. Unknown operations, stale timestamps,
/// incompatible protocol versions and unauthorized callers are rejected before any handler runs.
/// </summary>
public sealed class IpcDispatcher
{
    private readonly Dictionary<string, IIpcOperationHandler> _handlers;
    private readonly ILogger _logger;
    private readonly ILogger _audit;
    private readonly TimeProvider _time;

    /// <summary>Initializes a new instance of the <see cref="IpcDispatcher"/> class.</summary>
    /// <param name="handlers">Registered handlers.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    /// <param name="timeProvider">Time source.</param>
    public IpcDispatcher(IEnumerable<IIpcOperationHandler> handlers, ILoggerFactory loggerFactory, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _handlers = new Dictionary<string, IIpcOperationHandler>(StringComparer.Ordinal);
        foreach (var handler in handlers)
        {
            if (!OperationPolicies.TryGet(handler.Operation, out _))
            {
                throw new InvalidOperationException($"Handler for '{handler.Operation}' has no registered operation policy.");
            }

            _handlers.Add(handler.Operation, handler);
        }

        _logger = loggerFactory.CreateLogger<IpcDispatcher>();
        _audit = loggerFactory.CreateLogger("StormOS.Security.IpcAudit");
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Gets the registered operation names.</summary>
    public IReadOnlyCollection<string> Operations => _handlers.Keys;

    /// <summary>Dispatches a request and always produces a response (never throws for request errors).</summary>
    /// <param name="request">The request.</param>
    /// <param name="session">The calling session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response.</returns>
    public async Task<IpcResponse> DispatchAsync(IpcRequest request, IIpcSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(session);

        if (request.RequestId == Guid.Empty)
        {
            return Fail(request, StormErrorCodes.ValidationFailed, "The request id is missing.");
        }

        if (request.ProtocolVersion != IpcProtocol.Version)
        {
            return Fail(request, StormErrorCodes.NotSupported, "The STORM OS app and service versions are incompatible. Please update STORM OS.");
        }

        var skew = (_time.GetUtcNow() - request.Timestamp).Duration();
        if (skew > IpcProtocol.MaxClockSkew)
        {
            return Fail(request, StormErrorCodes.ValidationFailed, "The request timestamp is outside the accepted window.");
        }

        var authorization = OperationPolicies.Authorize(request.Operation, session.Client);
        if (!authorization.IsSuccess)
        {
            _audit.LogWarning("Rejected {Operation} from {Client}: {Code}", request.Operation, session.Client.AuditName, authorization.Error.Code);
            return Fail(request, authorization.Error.Code, authorization.Error.Message);
        }

        if (!_handlers.TryGetValue(request.Operation, out var handler))
        {
            return Fail(request, StormErrorCodes.NotSupported, "This operation is not available on this system.");
        }

        OperationPolicies.TryGet(request.Operation, out var policy);
        try
        {
            var result = await handler.HandleAsync(request, session, cancellationToken).ConfigureAwait(false);
            if (policy.Mutating)
            {
                _audit.LogInformation("Executed {Operation} for {Client} (user {User})", request.Operation, session.Client.AuditName, session.Client.UserName);
            }

            return new IpcResponse { RequestId = request.RequestId, Success = true, Payload = IpcPayload.From(result), Timestamp = _time.GetUtcNow() };
        }
        catch (IpcValidationException ex)
        {
            _audit.LogWarning("Validation failed for {Operation} from {Client}: {Reason}", request.Operation, session.Client.AuditName, ex.Message);
            return Fail(request, StormErrorCodes.ValidationFailed, ex.Message);
        }
        catch (IpcOperationException ex)
        {
            _logger.LogWarning(ex, "Operation {Operation} failed with {Code}", request.Operation, ex.Code);
            return Fail(request, ex.Code, ex.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Fail(request, StormErrorCodes.Timeout, "The operation was cancelled.");
        }
#pragma warning disable CA1031 // The dispatcher is the fault boundary: every failure must become a response, never a crash.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "Unhandled error in {Operation}", request.Operation);
            return Fail(request, StormErrorCodes.Internal, "The STORM OS service could not complete the request. Details were written to the service log.");
        }
    }

    private IpcResponse Fail(IpcRequest request, string code, string message) =>
        new() { RequestId = request.RequestId, Success = false, Error = new IpcError(code, message), Timestamp = _time.GetUtcNow() };
}
