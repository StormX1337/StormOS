using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using StormOS.Core.Analysis;
using StormOS.Core.Benchmark;
using StormOS.Core.Common;
using StormOS.Core.Games;
using StormOS.Core.History;
using StormOS.Core.Settings;
using StormOS.Infrastructure.Persistence;

namespace StormOS.Services.Cloud;

/// <summary>
/// Client for the optional STORM Cloud API. Every call is opt-in: the app works fully offline, and data uploads
/// additionally require the "cloud sync" privacy setting. Refresh tokens are stored encrypted (DPAPI).
/// </summary>
public sealed class CloudClient : IDisposable
{
    /// <summary>Name of the configured HTTP client.</summary>
    public const string HttpClientName = "storm-cloud";

    private const string RefreshTokenKey = "cloud.refreshToken";
    private readonly IHttpClientFactory _httpFactory;
    private readonly SecureValueStore _secrets;
    private readonly ISettingsStore _settings;
    private readonly ILogger<CloudClient> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessExpires;

    /// <summary>Initializes a new instance of the <see cref="CloudClient"/> class.</summary>
    /// <param name="httpFactory">HTTP client factory.</param>
    /// <param name="secrets">Encrypted secret store.</param>
    /// <param name="settings">Settings.</param>
    /// <param name="logger">Logger.</param>
    public CloudClient(IHttpClientFactory httpFactory, SecureValueStore secrets, ISettingsStore settings, ILogger<CloudClient> logger)
    {
        _httpFactory = httpFactory;
        _secrets = secrets;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Gets a value indicating whether the cloud integration is enabled in settings.</summary>
    public bool IsEnabled => _settings.Current.Cloud.Enabled;

    /// <summary>Validates that an API base URL is HTTPS (plain HTTP only for localhost development).</summary>
    /// <param name="baseUrl">Base URL.</param>
    /// <returns><see langword="true"/> when acceptable.</returns>
    public static bool IsAcceptableBaseUrl(string? baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
        && string.IsNullOrEmpty(uri.UserInfo);

    /// <summary>Signs in.</summary>
    /// <param name="email">E-mail.</param>
    /// <param name="password">Password (never stored or logged).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or error.</returns>
    public Task<Result> LoginAsync(string email, string password, CancellationToken cancellationToken = default) =>
        AuthenticateAsync("auth/login", new { email, password }, cancellationToken);

    /// <summary>Creates an account.</summary>
    /// <param name="email">E-mail.</param>
    /// <param name="password">Password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or error.</returns>
    public Task<Result> RegisterAsync(string email, string password, CancellationToken cancellationToken = default) =>
        AuthenticateAsync("auth/register", new { email, password }, cancellationToken);

    /// <summary>Signs out and removes stored tokens.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var refresh = await _secrets.GetAsync(RefreshTokenKey, cancellationToken).ConfigureAwait(false);
        if (refresh is not null)
        {
            await SendAsync<object>(HttpMethod.Post, "auth/logout", new { refreshToken = refresh }, authorize: false, cancellationToken).ConfigureAwait(false);
        }

        await _secrets.RemoveAsync(RefreshTokenKey, cancellationToken).ConfigureAwait(false);
        _accessToken = null;
        await _settings.SaveAsync(_settings.Current with { Cloud = _settings.Current.Cloud with { AccountEmail = null, DeviceId = null } }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Registers this PC as a device.</summary>
    /// <param name="name">Device name.</param>
    /// <param name="hardware">Hardware summary.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The device.</returns>
    public async Task<Result<CloudDevice>> RegisterDeviceAsync(string name, string hardware, CancellationToken cancellationToken = default)
    {
        var result = await SendAsync<CloudDevice>(HttpMethod.Post, "devices", new { name, hardware, platform = "windows" }, authorize: true, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await _settings.SaveAsync(_settings.Current with { Cloud = _settings.Current.Cloud with { DeviceId = result.Value!.Id } }, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>Requests a signed entitlement token for this device.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The compact JWS token.</returns>
    public async Task<Result<string>> FetchEntitlementTokenAsync(CancellationToken cancellationToken = default)
    {
        var deviceId = _settings.Current.Cloud.DeviceId;
        if (deviceId is null)
        {
            return Result<string>.Fail(StormErrorCodes.NotFound, "This PC is not registered with STORM Cloud.");
        }

        var result = await SendAsync<EntitlementTokenResponse>(HttpMethod.Post, $"devices/{Uri.EscapeDataString(deviceId)}/entitlements", null, authorize: true, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Result<string>.Ok(result.Value!.Token) : Result<string>.Fail(result.Error);
    }

    /// <summary>Uploads a benchmark result (requires the cloud sync privacy setting).</summary>
    /// <param name="result">The result.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or error.</returns>
    public async Task<Result> UploadBenchmarkAsync(BenchmarkResult result, CancellationToken cancellationToken = default) =>
        !_settings.Current.Privacy.CloudSync
            ? Result.Failure(StormErrorCodes.Unauthorized, "Cloud sync is turned off in Privacy settings.")
            : await SendAsync<object>(HttpMethod.Post, "benchmarks", result, authorize: true, cancellationToken).ConfigureAwait(false);

    /// <summary>Uploads a game session (requires the cloud sync privacy setting).</summary>
    /// <param name="session">The session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or error.</returns>
    public async Task<Result> UploadSessionAsync(GameSession session, CancellationToken cancellationToken = default) =>
        !_settings.Current.Privacy.CloudSync
            ? Result.Failure(StormErrorCodes.Unauthorized, "Cloud sync is turned off in Privacy settings.")
            : await SendAsync<object>(HttpMethod.Post, "sessions", session, authorize: true, cancellationToken).ConfigureAwait(false);

    /// <summary>Downloads the published game profiles.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Profiles.</returns>
    public Task<Result<List<GameProfile>>> FetchProfilesAsync(CancellationToken cancellationToken = default) =>
        SendAsync<List<GameProfile>>(HttpMethod.Get, "profiles", null, authorize: false, cancellationToken);

    /// <summary>Gets current announcements.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Announcements.</returns>
    public Task<Result<List<Announcement>>> GetAnnouncementsAsync(CancellationToken cancellationToken = default) =>
        SendAsync<List<Announcement>>(HttpMethod.Get, "system/announcements", null, authorize: false, cancellationToken);

    /// <summary>Gets the latest release of a channel.</summary>
    /// <param name="channel">"stable" or "beta".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The release.</returns>
    public Task<Result<ReleaseInfo>> GetLatestReleaseAsync(string channel, CancellationToken cancellationToken = default) =>
        SendAsync<ReleaseInfo>(HttpMethod.Get, $"releases/latest?channel={Uri.EscapeDataString(channel)}&arch={System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}", null, authorize: false, cancellationToken);

    /// <summary>Requests AI analysis of structured measurements (Ultimate tier, opt-in).</summary>
    /// <param name="window">Measurements.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Recommendations.</returns>
    public Task<Result<AnalysisResponse>> AnalyzeAsync(AnalysisWindow window, CancellationToken cancellationToken = default) =>
        SendAsync<AnalysisResponse>(HttpMethod.Post, "ai/analyze", window, authorize: true, cancellationToken);

    /// <inheritdoc />
    public void Dispose() => _refreshLock.Dispose();

    private async Task<Result> AuthenticateAsync(string path, object body, CancellationToken cancellationToken)
    {
        var result = await SendAsync<AuthResponse>(HttpMethod.Post, path, body, authorize: false, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return result;
        }

        await StoreTokensAsync(result.Value!.Tokens, cancellationToken).ConfigureAwait(false);
        await _settings.SaveAsync(_settings.Current with { Cloud = _settings.Current.Cloud with { Enabled = true, AccountEmail = result.Value.Email } }, cancellationToken).ConfigureAwait(false);
        return Result.Success;
    }

    private async Task StoreTokensAsync(AuthTokens tokens, CancellationToken cancellationToken)
    {
        _accessToken = tokens.AccessToken;
        _accessExpires = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, tokens.ExpiresIn - 30));
        await _secrets.SetAsync(RefreshTokenKey, tokens.RefreshToken, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && _accessToken is not null && DateTimeOffset.UtcNow < _accessExpires)
        {
            return _accessToken;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var refresh = await _secrets.GetAsync(RefreshTokenKey, cancellationToken).ConfigureAwait(false);
            if (refresh is null)
            {
                return null;
            }

            var result = await SendAsync<AuthTokens>(HttpMethod.Post, "auth/refresh", new { refreshToken = refresh }, authorize: false, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                _logger.LogInformation("Cloud session expired; sign-in required");
                return null;
            }

            await StoreTokensAsync(result.Value!, cancellationToken).ConfigureAwait(false);
            return _accessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<Result<T>> SendAsync<T>(HttpMethod method, string path, object? body, bool authorize, CancellationToken cancellationToken)
    {
        var baseUrl = _settings.Current.Cloud.ApiBaseUrl;
        if (!IsAcceptableBaseUrl(baseUrl))
        {
            return Result<T>.Fail(StormErrorCodes.ValidationFailed, "The STORM Cloud address must use HTTPS.");
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = new HttpRequestMessage(method, new Uri(new Uri(baseUrl.TrimEnd('/') + "/api/v1/"), path));
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, body.GetType(), options: StormJson.Options);
            }

            if (authorize)
            {
                var token = await GetAccessTokenAsync(attempt > 0, cancellationToken).ConfigureAwait(false);
                if (token is null)
                {
                    return Result<T>.Fail(StormErrorCodes.Unauthorized, "Sign in to STORM Cloud first.");
                }

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            try
            {
                using var client = _httpFactory.CreateClient(HttpClientName);
                using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.Unauthorized && authorize && attempt == 0)
                {
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return Result<T>.Fail(MapStatus(response.StatusCode), await ReadErrorAsync(response, cancellationToken).ConfigureAwait(false));
                }

                if (response.StatusCode == HttpStatusCode.NoContent || typeof(T) == typeof(object))
                {
                    return Result<T>.Ok(default!);
                }

                var value = await response.Content.ReadFromJsonAsync<T>(StormJson.Options, cancellationToken).ConfigureAwait(false);
                return value is null ? Result<T>.Fail(StormErrorCodes.Internal, "STORM Cloud returned an empty response.") : Result<T>.Ok(value);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                _logger.LogInformation(ex, "Cloud request {Method} {Path} failed", method, path);
                return Result<T>.Fail(StormError.FromException(StormErrorCodes.ServiceUnavailable, "STORM Cloud is not reachable. STORM OS keeps working offline.", ex));
            }
        }

        return Result<T>.Fail(StormErrorCodes.Unauthorized, "Your STORM Cloud session expired. Please sign in again.");
    }

    private static string MapStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => StormErrorCodes.Unauthorized,
        HttpStatusCode.NotFound => StormErrorCodes.NotFound,
        HttpStatusCode.PaymentRequired => StormErrorCodes.EntitlementRequired,
        HttpStatusCode.TooManyRequests => StormErrorCodes.RateLimited,
        HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => StormErrorCodes.ValidationFailed,
        HttpStatusCode.Conflict => StormErrorCodes.Conflict,
        _ => StormErrorCodes.ServiceUnavailable,
    };

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<CloudError>(StormJson.Options, cancellationToken).ConfigureAwait(false);
            if (error?.Message is { Length: > 0 and < 300 } message)
            {
                return message;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Fall through to the generic message.
        }

        return $"STORM Cloud rejected the request ({(int)response.StatusCode}).";
    }

    private sealed record CloudError(string? Message);

    private sealed record EntitlementTokenResponse(string Token);
}
