namespace StormOS.Security.Ipc;

/// <summary>
/// Determines the trust level of a client. A client is trusted when its image lives inside the service's
/// installation directory (writable by administrators only) and, when the service itself is signed,
/// when the client is signed by the same publisher.
/// </summary>
public sealed class ClientTrustEvaluator
{
    private readonly string _installDirectory;
    private readonly IAuthenticodeVerifier? _verifier;
    private readonly string? _serviceSignerThumbprint;
    private readonly HashSet<string> _trustedImageNames;

    /// <summary>Initializes a new instance of the <see cref="ClientTrustEvaluator"/> class.</summary>
    /// <param name="installDirectory">Directory containing the service executable.</param>
    /// <param name="trustedImageNames">Executable file names allowed as trusted clients.</param>
    /// <param name="verifier">Optional signature verifier.</param>
    /// <param name="serviceSignerThumbprint">Signer thumbprint of the service, or <see langword="null"/> for unsigned builds.</param>
    public ClientTrustEvaluator(string installDirectory, IEnumerable<string> trustedImageNames, IAuthenticodeVerifier? verifier, string? serviceSignerThumbprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
        ArgumentNullException.ThrowIfNull(trustedImageNames);
        _installDirectory = NormalizeDirectory(installDirectory);
        _trustedImageNames = new HashSet<string>(trustedImageNames, StringComparer.OrdinalIgnoreCase);
        _verifier = verifier;
        _serviceSignerThumbprint = serviceSignerThumbprint;
    }

    /// <summary>Evaluates the trust level for a client image and session.</summary>
    /// <param name="imagePath">Client executable path.</param>
    /// <param name="userResolved">Whether the client user could be resolved.</param>
    /// <returns>The trust level.</returns>
    public ClientTrustLevel Evaluate(string? imagePath, bool userResolved)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !userResolved)
        {
            return ClientTrustLevel.Untrusted;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(imagePath);
        }
        catch (ArgumentException)
        {
            return ClientTrustLevel.Untrusted;
        }
        catch (NotSupportedException)
        {
            return ClientTrustLevel.Untrusted;
        }

        var directory = NormalizeDirectory(Path.GetDirectoryName(fullPath) ?? string.Empty);
        var inInstallDir = string.Equals(directory, _installDirectory, StringComparison.OrdinalIgnoreCase);
        if (!inInstallDir || !_trustedImageNames.Contains(Path.GetFileName(fullPath)))
        {
            return ClientTrustLevel.LocalUser;
        }

        if (_serviceSignerThumbprint is null || _verifier is null)
        {
            return ClientTrustLevel.TrustedClient;
        }

        var clientThumbprint = _verifier.GetTrustedSignerThumbprint(fullPath);
        return string.Equals(clientThumbprint, _serviceSignerThumbprint, StringComparison.OrdinalIgnoreCase)
            ? ClientTrustLevel.TrustedClient
            : ClientTrustLevel.LocalUser;
    }

    private static string NormalizeDirectory(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Length == 0 ? "." : path));
}
