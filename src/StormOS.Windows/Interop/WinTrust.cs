using System.Runtime.InteropServices;

namespace StormOS.Windows.Interop;

/// <summary>wintrust.dll declarations for Authenticode verification.</summary>
internal static partial class WinTrust
{
    public static readonly Guid ActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
    public const uint UiNone = 2;
    public const uint RevokeNone = 0;
    public const uint ChoiceFile = 1;
    public const uint StateActionVerify = 1;
    public const uint StateActionClose = 2;
    public const uint ProviderFlagCacheOnlyUrlRetrieval = 0x00001000;

    [StructLayout(LayoutKind.Sequential)]
    public struct FileInfo
    {
        public uint StructSize;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr File;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProvFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }

    [LibraryImport("wintrust.dll")]
    public static partial int WinVerifyTrust(IntPtr window, in Guid actionId, ref TrustData data);
}
