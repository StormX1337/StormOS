using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using StormOS.Security.Ipc;
using StormOS.Windows.Interop;

namespace StormOS.Windows.Security;

/// <summary>Authenticode verification through WinVerifyTrust.</summary>
public sealed class AuthenticodeVerifier : IAuthenticodeVerifier
{
    /// <summary>Verifies the signature of a file.</summary>
    /// <param name="filePath">File path.</param>
    /// <returns><see langword="true"/> when the signature is valid and trusted.</returns>
    public static bool IsTrusted(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var pathPointer = Marshal.StringToHGlobalUni(filePath);
        var fileInfo = new WinTrust.FileInfo { StructSize = (uint)Marshal.SizeOf<WinTrust.FileInfo>(), FilePath = pathPointer };
        var filePointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrust.FileInfo>());
        try
        {
            Marshal.StructureToPtr(fileInfo, filePointer, false);
            var data = new WinTrust.TrustData
            {
                StructSize = (uint)Marshal.SizeOf<WinTrust.TrustData>(),
                UiChoice = WinTrust.UiNone,
                RevocationChecks = WinTrust.RevokeNone,
                UnionChoice = WinTrust.ChoiceFile,
                File = filePointer,
                StateAction = WinTrust.StateActionVerify,
                ProvFlags = WinTrust.ProviderFlagCacheOnlyUrlRetrieval,
            };
            var result = WinTrust.WinVerifyTrust(IntPtr.Zero, WinTrust.ActionGenericVerifyV2, ref data);
            data.StateAction = WinTrust.StateActionClose;
            _ = WinTrust.WinVerifyTrust(IntPtr.Zero, WinTrust.ActionGenericVerifyV2, ref data);
            return result == 0;
        }
        finally
        {
            Marshal.FreeHGlobal(filePointer);
            Marshal.FreeHGlobal(pathPointer);
        }
    }

    /// <inheritdoc />
    public string? GetTrustedSignerThumbprint(string filePath)
    {
        if (!File.Exists(filePath) || !IsTrusted(filePath))
        {
            return null;
        }

        try
        {
#pragma warning disable SYSLIB0057 // Reading the Authenticode signer requires CreateFromSignedFile.
            using var certificate = X509Certificate.CreateFromSignedFile(filePath);
#pragma warning restore SYSLIB0057
            return certificate.GetCertHashString();
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}
