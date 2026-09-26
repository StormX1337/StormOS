using StormOS.Core.Analysis;

namespace StormOS.Services.Cloud;

/// <summary>Tokens returned by STORM Cloud authentication.</summary>
/// <param name="AccessToken">Short-lived access token.</param>
/// <param name="RefreshToken">Rotating refresh token (stored encrypted).</param>
/// <param name="ExpiresIn">Access token lifetime in seconds.</param>
public sealed record AuthTokens(string AccessToken, string RefreshToken, int ExpiresIn);

/// <summary>Authentication response.</summary>
/// <param name="Tokens">Tokens.</param>
/// <param name="Email">Account e-mail.</param>
public sealed record AuthResponse(AuthTokens Tokens, string Email);

/// <summary>Registered device.</summary>
/// <param name="Id">Device id.</param>
/// <param name="Name">Device name.</param>
public sealed record CloudDevice(string Id, string Name);

/// <summary>A published release.</summary>
public sealed record ReleaseInfo
{
    /// <summary>Gets the semantic version.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>Gets the channel.</summary>
    public string Channel { get; init; } = "stable";

    /// <summary>Gets the HTTPS download URL of the installer.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>Gets the SHA-256 of the installer.</summary>
    public string Sha256 { get; init; } = string.Empty;

    /// <summary>Gets the changelog (Markdown).</summary>
    public string? Notes { get; init; }

    /// <summary>Gets the publish time.</summary>
    public DateTimeOffset PublishedAt { get; init; }

    /// <summary>Gets the installer size.</summary>
    public long SizeBytes { get; init; }
}

/// <summary>AI analysis response.</summary>
/// <param name="Recommendations">Recommendations produced by the server, already validated against the evidence.</param>
public sealed record AnalysisResponse(IReadOnlyList<Recommendation> Recommendations);

/// <summary>Service announcement.</summary>
/// <param name="Id">Id.</param>
/// <param name="Title">Title.</param>
/// <param name="Body">Body.</param>
/// <param name="Severity">"info", "warning" or "critical".</param>
public sealed record Announcement(string Id, string Title, string Body, string Severity);
