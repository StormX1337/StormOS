using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StormOS.Core.Common;
using StormOS.Core.Licensing;

namespace StormOS.Security.Licensing;

/// <summary>
/// Verifies entitlement tokens issued by STORM Cloud. Tokens are compact JWS (ES256) whose payload lists the
/// tier and features; the mapping of tiers to features lives on the server, the client only trusts signed data.
/// </summary>
public sealed class EntitlementTokenVerifier : IDisposable
{
    private const string ExpectedIssuer = "storm-cloud";
    private readonly ECDsa _publicKey;
    private readonly TimeProvider _time;

    /// <summary>Initializes a new instance of the <see cref="EntitlementTokenVerifier"/> class.</summary>
    /// <param name="publicKeyPem">The server's P-256 public key in PEM (SubjectPublicKeyInfo) format.</param>
    /// <param name="timeProvider">Time source.</param>
    public EntitlementTokenVerifier(string publicKeyPem, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyPem);
        _publicKey = ECDsa.Create();
        _publicKey.ImportFromPem(publicKeyPem);
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Maximum tolerated clock skew.</summary>
    public static TimeSpan ClockSkew { get; } = TimeSpan.FromMinutes(5);

    /// <summary>Verifies a token and returns the entitlements it grants.</summary>
    /// <param name="token">Compact JWS.</param>
    /// <param name="expectedDeviceId">The device id the token must be bound to.</param>
    /// <returns>The entitlements or an error.</returns>
    public Result<Entitlements> Verify(string token, string expectedDeviceId)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 16_384)
        {
            return Invalid("The license token is missing or malformed.");
        }

        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return Invalid("The license token is malformed.");
        }

        try
        {
            using var header = JsonDocument.Parse(Base64UrlDecode(parts[0]));
            if (!header.RootElement.TryGetProperty("alg", out var alg) || alg.GetString() != "ES256")
            {
                return Invalid("The license token uses an unsupported algorithm.");
            }

            var signed = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
            var signature = Base64UrlDecode(parts[2]);
            if (!_publicKey.VerifyData(signed, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                return Invalid("The license token signature is invalid.");
            }

            using var payload = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            return ReadPayload(payload.RootElement, expectedDeviceId);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or CryptographicException or InvalidOperationException or KeyNotFoundException)
        {
            return Result<Entitlements>.Fail(StormError.FromException(StormErrorCodes.ValidationFailed, "The license token could not be read.", ex));
        }
    }

    private Result<Entitlements> ReadPayload(JsonElement root, string expectedDeviceId)
    {
        if (root.GetProperty("iss").GetString() != ExpectedIssuer)
        {
            return Invalid("The license token was not issued by STORM Cloud.");
        }

        if (!string.Equals(root.GetProperty("did").GetString(), expectedDeviceId, StringComparison.Ordinal))
        {
            return Invalid("The license token belongs to a different device.");
        }

        var now = _time.GetUtcNow();
        var expires = DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("exp").GetInt64());
        if (now > expires + ClockSkew)
        {
            return Invalid("The license token has expired. Connect to STORM Cloud to refresh it.");
        }

        if (root.TryGetProperty("nbf", out var nbf) && now + ClockSkew < DateTimeOffset.FromUnixTimeSeconds(nbf.GetInt64()))
        {
            return Invalid("The license token is not valid yet.");
        }

        if (!Enum.TryParse<LicenseTier>(root.GetProperty("tier").GetString(), ignoreCase: true, out var tier))
        {
            return Invalid("The license token contains an unknown tier.");
        }

        var features = new HashSet<string>(StringComparer.Ordinal);
        foreach (var feature in root.GetProperty("features").EnumerateArray())
        {
            if (feature.GetString() is { Length: > 0 and <= 64 } name)
            {
                features.Add(name);
            }
        }

        return Result<Entitlements>.Ok(new Entitlements { Tier = tier, Features = features, ExpiresAt = expires, Source = "cloud" });
    }

    /// <inheritdoc />
    public void Dispose() => _publicKey.Dispose();

    private static Result<Entitlements> Invalid(string message) => Result<Entitlements>.Fail(StormErrorCodes.ValidationFailed, message);

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", 0 => string.Empty, _ => throw new FormatException("Invalid base64url length.") };
        return Convert.FromBase64String(padded);
    }
}
