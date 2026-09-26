namespace StormOS.Core.Common;

/// <summary>
/// A user-presentable error. <see cref="Message"/> is safe to show in the UI; <see cref="Detail"/>
/// carries technical information intended for the developer log only.
/// </summary>
/// <param name="Code">Stable machine readable error code.</param>
/// <param name="Message">Friendly message for the UI.</param>
/// <param name="Detail">Optional technical detail for logs.</param>
public sealed record StormError(string Code, string Message, string? Detail = null)
{
    /// <summary>Creates an error from an exception without leaking exception text into the UI message.</summary>
    /// <param name="code">Error code.</param>
    /// <param name="userMessage">Friendly message.</param>
    /// <param name="exception">The exception providing technical detail.</param>
    /// <returns>A new error.</returns>
    public static StormError FromException(string code, string userMessage, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new StormError(code, userMessage, $"{exception.GetType().FullName}: {exception.Message}");
    }

    /// <inheritdoc />
    public override string ToString() => $"{Code}: {Message}";
}

/// <summary>Well known error codes.</summary>
public static class StormErrorCodes
{
    /// <summary>The request payload failed validation.</summary>
    public const string ValidationFailed = "validation_failed";

    /// <summary>The caller is not permitted to perform the operation.</summary>
    public const string Unauthorized = "unauthorized";

    /// <summary>The requested operation is unknown.</summary>
    public const string UnknownOperation = "unknown_operation";

    /// <summary>The requested item was not found.</summary>
    public const string NotFound = "not_found";

    /// <summary>The operation is not supported on this system.</summary>
    public const string NotSupported = "not_supported";

    /// <summary>The operation requires administrative rights.</summary>
    public const string RequiresAdmin = "requires_admin";

    /// <summary>The STORM OS service is unavailable.</summary>
    public const string ServiceUnavailable = "service_unavailable";

    /// <summary>The caller exceeded the request rate limit.</summary>
    public const string RateLimited = "rate_limited";

    /// <summary>The operation timed out.</summary>
    public const string Timeout = "timeout";

    /// <summary>An unexpected internal error occurred.</summary>
    public const string Internal = "internal_error";

    /// <summary>Verification of a change failed.</summary>
    public const string VerificationFailed = "verification_failed";

    /// <summary>A rollback could not be completed.</summary>
    public const string RollbackFailed = "rollback_failed";

    /// <summary>The operation conflicts with the current state.</summary>
    public const string Conflict = "conflict";

    /// <summary>The feature requires a higher license tier.</summary>
    public const string EntitlementRequired = "entitlement_required";
}
