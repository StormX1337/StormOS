using System.Security.Cryptography;
using StormOS.Security.Secrets;

namespace StormOS.Windows.Security;

/// <summary>DPAPI (current user scope) secret protection with an application specific entropy.</summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = "StormOS.Secrets.v1"u8.ToArray();

    /// <inheritdoc />
    public byte[] Protect(ReadOnlySpan<byte> plaintext) =>
        ProtectedData.Protect(plaintext.ToArray(), Entropy, DataProtectionScope.CurrentUser);

    /// <inheritdoc />
    public byte[] Unprotect(ReadOnlySpan<byte> ciphertext) =>
        ProtectedData.Unprotect(ciphertext.ToArray(), Entropy, DataProtectionScope.CurrentUser);
}
