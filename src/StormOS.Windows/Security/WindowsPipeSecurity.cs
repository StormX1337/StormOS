using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using StormOS.Infrastructure.Ipc;
using StormOS.Security.Ipc;
using StormOS.Windows.Interop;

namespace StormOS.Windows.Security;

/// <summary>
/// Creates the service pipe with a restrictive DACL: LocalSystem and Administrators have full control,
/// interactive users may read and write (but not create instances), network logons are denied.
/// The first instance uses FILE_FLAG_FIRST_PIPE_INSTANCE so the service fails fast if the name was squatted.
/// </summary>
public sealed class WindowsPipeServerFactory : IPipeServerFactory
{
    private const int BufferSize = 64 * 1024;

    /// <inheritdoc />
    public NamedPipeServerStream Create(string pipeName, bool firstInstance, int maxInstances)
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.InteractiveSid, null), PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));

        var options = PipeOptions.Asynchronous | PipeOptions.WriteThrough;
        if (firstInstance)
        {
            options |= PipeOptions.FirstPipeInstance;
        }

        return NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, maxInstances, PipeTransmissionMode.Byte, options, BufferSize, BufferSize, security);
    }
}

/// <summary>Resolves the pipe client's process, session, user and trust level.</summary>
public sealed class WindowsClientIdentityResolver : IClientIdentityResolver
{
    private readonly ClientTrustEvaluator _trust;
    private readonly ILogger<WindowsClientIdentityResolver> _logger;

    /// <summary>Initializes a new instance of the <see cref="WindowsClientIdentityResolver"/> class.</summary>
    /// <param name="trust">Trust evaluator.</param>
    /// <param name="logger">Logger.</param>
    public WindowsClientIdentityResolver(ClientTrustEvaluator trust, ILogger<WindowsClientIdentityResolver> logger)
    {
        _trust = trust;
        _logger = logger;
    }

    /// <inheritdoc />
    public ClientIdentity Resolve(NamedPipeServerStream pipe)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        if (!Kernel32.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var processId))
        {
            return ClientIdentity.Unknown;
        }

        int? sessionId = Kernel32.GetNamedPipeClientSessionId(pipe.SafePipeHandle, out var session) ? session : null;
        string? imagePath = null;
        string? userName = null;
        string? userSid = null;
        using (var process = Kernel32.OpenProcess(Kernel32.ProcessQueryLimitedInformation, false, processId))
        {
            if (!process.IsInvalid)
            {
                imagePath = Kernel32.GetProcessImagePath(process);
                (userName, userSid) = ReadProcessUser(process, processId);
            }
        }

        return new ClientIdentity
        {
            ProcessId = processId,
            ImagePath = imagePath,
            SessionId = sessionId,
            UserName = userName,
            UserSid = userSid,
            Trust = _trust.Evaluate(imagePath, userSid is not null),
        };
    }

    /// <summary>
    /// Reads the user from the client's process token (the process id comes from the kernel via the pipe).
    /// The service never impersonates the client, so its own threads keep the LocalSystem security context.
    /// </summary>
    private (string? Name, string? Sid) ReadProcessUser(Microsoft.Win32.SafeHandles.SafeProcessHandle process, int processId)
    {
        if (!Advapi32.OpenProcessToken(process, Advapi32.TokenQuery, out var rawToken))
        {
            _logger.LogWarning("Could not open the token of pipe client {ProcessId} (error {Error})", processId, System.Runtime.InteropServices.Marshal.GetLastPInvokeError());
            return (null, null);
        }

        using var token = new Microsoft.Win32.SafeHandles.SafeAccessTokenHandle(rawToken);
        try
        {
            using var identity = new WindowsIdentity(token.DangerousGetHandle());
            return (identity.Name, identity.User?.Value);
        }
        catch (Exception ex) when (ex is ArgumentException or System.Security.SecurityException or UnauthorizedAccessException or IdentityNotMappedException or System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning(ex, "Could not read the identity of pipe client {ProcessId}", processId);
            return (null, null);
        }
    }
}

/// <summary>
/// Verifies from the client side that the pipe was created by LocalSystem or an administrator, so a
/// standard user process cannot impersonate the service by squatting on the pipe name.
/// </summary>
public sealed class WindowsPipeServerVerifier : IPipeServerVerifier
{
    private readonly bool _allowUnverified;

    /// <summary>Initializes a new instance of the <see cref="WindowsPipeServerVerifier"/> class.</summary>
    /// <param name="allowUnverified">Development only: accept a service running under a normal account.</param>
    public WindowsPipeServerVerifier(bool allowUnverified) => _allowUnverified = allowUnverified;

    /// <inheritdoc />
    public bool Verify(NamedPipeClientStream pipe, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        try
        {
            var owner = pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            if (owner is not null && (owner.IsWellKnown(WellKnownSidType.LocalSystemSid) || owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid)))
            {
                reason = null;
                return true;
            }

            reason = $"The pipe is owned by {owner?.Value ?? "an unknown account"}, not by LocalSystem.";
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or IOException)
        {
            reason = "The pipe owner could not be read: " + ex.Message;
        }

        return _allowUnverified;
    }
}
