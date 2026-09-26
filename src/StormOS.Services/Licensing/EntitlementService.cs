using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StormOS.Core.Common;
using StormOS.Core.Licensing;
using StormOS.Core.Settings;
using StormOS.Infrastructure.Persistence;
using StormOS.Security.Licensing;
using StormOS.Services.Cloud;

namespace StormOS.Services.Licensing;

/// <summary>Licensing configuration.</summary>
public sealed class LicensingOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Licensing";

    /// <summary>Gets or sets the licensing mode.</summary>
    public LicensingMode Mode { get; set; } = LicensingMode.Community;

    /// <summary>Gets or sets the STORM Cloud entitlement signing public key (PEM).</summary>
    public string? PublicKeyPem { get; set; }
}

/// <summary>
/// Resolves entitlements. In community mode every local feature is enabled. In commercial mode entitlements come
/// from a server-signed token (tier-to-feature mapping lives on the server); without a valid token the Free tier applies.
/// </summary>
public sealed class EntitlementService : IEntitlementService
{
    private const string TokenKey = "license.token";
    private readonly LicensingOptions _options;
    private readonly SecureValueStore _secrets;
    private readonly ISettingsStore _settings;
    private readonly ILogger<EntitlementService> _logger;
    private Entitlements _current;

    /// <summary>Initializes a new instance of the <see cref="EntitlementService"/> class.</summary>
    /// <param name="options">Options.</param>
    /// <param name="secrets">Secret store.</param>
    /// <param name="settings">Settings.</param>
    /// <param name="logger">Logger.</param>
    public EntitlementService(IOptions<LicensingOptions> options, SecureValueStore secrets, ISettingsStore settings, ILogger<EntitlementService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _secrets = secrets;
        _settings = settings;
        _logger = logger;
        _current = _options.Mode == LicensingMode.Community ? Entitlements.Community : Entitlements.Free;
    }

    /// <inheritdoc />
    public Entitlements Current => Volatile.Read(ref _current);

    /// <inheritdoc />
    public bool IsEnabled(string feature) => Current.Has(feature);

    /// <summary>Loads and verifies the cached token.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The entitlements.</returns>
    public async Task<Entitlements> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_options.Mode == LicensingMode.Community)
        {
            return Current;
        }

        var token = await _secrets.GetAsync(TokenKey, cancellationToken).ConfigureAwait(false);
        Apply(token);
        return Current;
    }

    /// <summary>Fetches a fresh token from STORM Cloud, verifies and stores it.</summary>
    /// <param name="cloud">Cloud client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The entitlements or an error.</returns>
    public async Task<Result<Entitlements>> RefreshAsync(CloudClient cloud, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cloud);
        if (_options.Mode == LicensingMode.Community)
        {
            return Result<Entitlements>.Ok(Current);
        }

        var token = await cloud.FetchEntitlementTokenAsync(cancellationToken).ConfigureAwait(false);
        if (!token.IsSuccess)
        {
            return Result<Entitlements>.Fail(token.Error);
        }

        var verified = Verify(token.Value!);
        if (!verified.IsSuccess)
        {
            return verified;
        }

        await _secrets.SetAsync(TokenKey, token.Value!, cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref _current, verified.Value!);
        return verified;
    }

    private void Apply(string? token)
    {
        if (token is null)
        {
            Volatile.Write(ref _current, Entitlements.Free);
            return;
        }

        var verified = Verify(token);
        Volatile.Write(ref _current, verified.IsSuccess ? verified.Value! : Entitlements.Free);
        if (!verified.IsSuccess)
        {
            _logger.LogInformation("Stored license token rejected: {Reason}", verified.Error.Message);
        }
    }

    private Result<Entitlements> Verify(string token)
    {
        if (string.IsNullOrWhiteSpace(_options.PublicKeyPem) || _settings.Current.Cloud.DeviceId is not { } deviceId)
        {
            return Result<Entitlements>.Fail(StormErrorCodes.ValidationFailed, "Licensing is not configured on this PC.");
        }

        using var verifier = new EntitlementTokenVerifier(_options.PublicKeyPem);
        return verifier.Verify(token, deviceId);
    }
}
