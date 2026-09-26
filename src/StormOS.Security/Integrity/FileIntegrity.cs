using System.Security.Cryptography;

namespace StormOS.Security.Integrity;

/// <summary>SHA-256 based file integrity checks for downloaded components and update packages.</summary>
public static class FileIntegrity
{
    /// <summary>Computes the SHA-256 hash of a file as lowercase hex.</summary>
    /// <param name="path">File path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The hex digest.</returns>
    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>Verifies a file against an expected SHA-256 digest using a constant time comparison.</summary>
    /// <param name="path">File path.</param>
    /// <param name="expectedSha256Hex">Expected digest as hex.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the digest matches.</returns>
    public static async Task<bool> VerifySha256Async(string path, string expectedSha256Hex, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256Hex);
        byte[] expected;
        try
        {
            expected = Convert.FromHexString(expectedSha256Hex.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        if (expected.Length != SHA256.HashSizeInBytes)
        {
            return false;
        }

        var actual = Convert.FromHexString(await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false));
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
