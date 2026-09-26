namespace StormOS.Security.Ipc;

/// <summary>How much the service trusts a connected client.</summary>
public enum ClientTrustLevel
{
    /// <summary>The client could not be identified; only read-only health operations are allowed.</summary>
    Untrusted,

    /// <summary>A local interactive user process; read-only operations are allowed.</summary>
    LocalUser,

    /// <summary>
    /// A STORM OS binary located in the protected installation directory (and, for signed builds,
    /// signed by the same publisher as the service). Privileged, allow-listed operations are allowed.
    /// </summary>
    TrustedClient,
}

/// <summary>Identity of the process on the other end of the pipe, resolved by the server.</summary>
public sealed record ClientIdentity
{
    /// <summary>Gets the client process id.</summary>
    public int ProcessId { get; init; }

    /// <summary>Gets the client executable path.</summary>
    public string? ImagePath { get; init; }

    /// <summary>Gets the client user name (DOMAIN\user).</summary>
    public string? UserName { get; init; }

    /// <summary>Gets the client user SID.</summary>
    public string? UserSid { get; init; }

    /// <summary>Gets the client's Windows session id.</summary>
    public int? SessionId { get; init; }

    /// <summary>Gets the evaluated trust level.</summary>
    public ClientTrustLevel Trust { get; init; }

    /// <summary>Gets a short description for audit logs.</summary>
    public string AuditName => $"{UserName ?? "unknown"} (pid {ProcessId}, {Path.GetFileName(ImagePath) ?? "unknown image"})";

    /// <summary>Gets an anonymous identity used when resolution failed.</summary>
    public static ClientIdentity Unknown { get; } = new() { Trust = ClientTrustLevel.Untrusted };
}

/// <summary>Resolves the identity of a connected pipe client.</summary>
public interface IClientIdentityResolver
{
    /// <summary>Resolves the client identity for a connected server pipe.</summary>
    /// <param name="pipe">The server pipe stream.</param>
    /// <returns>The identity.</returns>
    ClientIdentity Resolve(System.IO.Pipes.NamedPipeServerStream pipe);
}

/// <summary>Verifies Authenticode signatures.</summary>
public interface IAuthenticodeVerifier
{
    /// <summary>Returns the signer certificate thumbprint when the file carries a valid, trusted signature.</summary>
    /// <param name="filePath">File to verify.</param>
    /// <returns>The SHA-1 thumbprint of the signer, or <see langword="null"/> when unsigned or invalid.</returns>
    string? GetTrustedSignerThumbprint(string filePath);
}
