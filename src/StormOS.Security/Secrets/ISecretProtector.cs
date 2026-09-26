namespace StormOS.Security.Secrets;

/// <summary>Encrypts secrets at rest (for example cloud refresh tokens). On Windows this uses DPAPI (current user).</summary>
public interface ISecretProtector
{
    /// <summary>Encrypts plaintext.</summary>
    /// <param name="plaintext">Plaintext bytes.</param>
    /// <returns>Ciphertext.</returns>
    byte[] Protect(ReadOnlySpan<byte> plaintext);

    /// <summary>Decrypts ciphertext.</summary>
    /// <param name="ciphertext">Ciphertext.</param>
    /// <returns>Plaintext bytes.</returns>
    byte[] Unprotect(ReadOnlySpan<byte> ciphertext);
}
