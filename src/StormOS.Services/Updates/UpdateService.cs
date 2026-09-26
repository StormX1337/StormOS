using System.Reflection;
using Microsoft.Extensions.Logging;
using StormOS.Core.Common;
using StormOS.Core.Settings;
using StormOS.Security.Integrity;
using StormOS.Security.Ipc;
using StormOS.Services.Cloud;

namespace StormOS.Services.Updates;

/// <summary>Result of an update check.</summary>
/// <param name="CurrentVersion">Installed version.</param>
/// <param name="Release">Latest release, when newer.</param>
public sealed record UpdateCheckResult(string CurrentVersion, ReleaseInfo? Release)
{
    /// <summary>Gets a value indicating whether an update is available.</summary>
    public bool IsAvailable => Release is not null;
}

/// <summary>
/// Checks STORM Cloud for updates and downloads installers. A download is only returned when its SHA-256 matches the
/// release metadata and, for signed builds, it carries the same Authenticode signer as the running app.
/// Installation (MSI major upgrade) is started by the UI after user confirmation; MSI rolls back on failure.
/// </summary>
public sealed class UpdateService(CloudClient cloud, ISettingsStore settings, IHttpClientFactory httpFactory, ILogger<UpdateService> logger, IAuthenticodeVerifier? authenticode = null)
{
    /// <summary>Gets the installed version.</summary>
    public static string CurrentVersion =>
        (Assembly.GetEntryAssembly() ?? typeof(UpdateService).Assembly).GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    /// <summary>
    /// Local file name for a downloaded installer: the setup executable when the release points to an <c>.exe</c>,
    /// otherwise the MSI. Only letters, digits, dots and dashes of the version are kept.
    /// </summary>
    /// <param name="version">Release version.</param>
    /// <param name="url">Download URL.</param>
    /// <returns>The file name.</returns>
    public static string InstallerFileName(string version, Uri url)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(url);
        var safeVersion = string.Concat(version.Where(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-'));
        return url.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? $"StormOS-Setup-{safeVersion}.exe"
            : $"StormOS-{safeVersion}.msi";
    }

    /// <summary>Compares semantic versions (pre-release versions sort before releases).</summary>
    /// <param name="left">Left.</param>
    /// <param name="right">Right.</param>
    /// <returns>Comparison result.</returns>
    public static int CompareVersions(string left, string right)
    {
        static (Version Core, string? Pre) Parse(string value)
        {
            var dash = value.IndexOf('-', StringComparison.Ordinal);
            var core = dash >= 0 ? value[..dash] : value;
            return (Version.TryParse(core, out var parsed) ? parsed : new Version(0, 0), dash >= 0 ? value[(dash + 1)..] : null);
        }

        var (a, aPre) = Parse(left);
        var (b, bPre) = Parse(right);
        var core = a.CompareTo(b);
        if (core != 0)
        {
            return core;
        }

        return (aPre, bPre) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            _ => string.CompareOrdinal(aPre, bPre),
        };
    }

    /// <summary>Checks for a newer release on the configured channel.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The check result.</returns>
    public async Task<Result<UpdateCheckResult>> CheckAsync(CancellationToken cancellationToken = default)
    {
        var channel = settings.Current.Updates.Channel == UpdateChannel.Beta ? "beta" : "stable";
        var latest = await cloud.GetLatestReleaseAsync(channel, cancellationToken).ConfigureAwait(false);
        if (!latest.IsSuccess)
        {
            return Result<UpdateCheckResult>.Fail(latest.Error);
        }

        var newer = CompareVersions(latest.Value!.Version, CurrentVersion) > 0 ? latest.Value : null;
        return Result<UpdateCheckResult>.Ok(new UpdateCheckResult(CurrentVersion, newer));
    }

    /// <summary>Downloads and verifies an installer.</summary>
    /// <param name="release">The release.</param>
    /// <param name="progress">Progress 0–100.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The verified installer path.</returns>
    public async Task<Result<string>> DownloadAsync(ReleaseInfo release, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (!Uri.TryCreate(release.Url, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps || release.Sha256.Length != 64)
        {
            return Result<string>.Fail(StormErrorCodes.ValidationFailed, "The update metadata is invalid.");
        }

        var directory = Path.Combine(Path.GetTempPath(), "StormOS", "updates");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, InstallerFileName(release.Version, url));
        try
        {
            using var client = httpFactory.CreateClient(CloudClient.HttpClientName);
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? release.SizeBytes;
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = File.Create(target))
            {
                var buffer = new byte[81920];
                long written = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    written += read;
                    if (total > 0)
                    {
                        progress?.Report(100.0 * written / total);
                    }
                }
            }

            if (!await FileIntegrity.VerifySha256Async(target, release.Sha256, cancellationToken).ConfigureAwait(false))
            {
                File.Delete(target);
                return Result<string>.Fail(StormErrorCodes.VerificationFailed, "The downloaded update failed its integrity check and was deleted.");
            }

            var entry = Environment.ProcessPath;
            if (authenticode is not null && entry is not null && authenticode.GetTrustedSignerThumbprint(entry) is { } expected
                && !string.Equals(authenticode.GetTrustedSignerThumbprint(target), expected, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(target);
                return Result<string>.Fail(StormErrorCodes.VerificationFailed, "The downloaded update is not signed by the STORM OS publisher and was deleted.");
            }

            return Result<string>.Ok(target);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Update download failed");
            return Result<string>.Fail(StormError.FromException(StormErrorCodes.ServiceUnavailable, "The update could not be downloaded.", ex));
        }
    }
}
