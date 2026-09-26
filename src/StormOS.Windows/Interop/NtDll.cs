using System.Runtime.InteropServices;

namespace StormOS.Windows.Interop;

/// <summary>ntdll.dll declarations.</summary>
internal static partial class NtDll
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct OsVersionInfoEx
    {
        public uint OsVersionInfoSize;
        public uint MajorVersion;
        public uint MinorVersion;
        public uint BuildNumber;
        public uint PlatformId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string CsdVersion;
        public ushort ServicePackMajor;
        public ushort ServicePackMinor;
        public ushort SuiteMask;
        public byte ProductType;
        public byte Reserved;
    }

#pragma warning disable SYSLIB1054 // ByValTStr structs require the built-in marshaller.
    [DllImport("ntdll.dll", CharSet = CharSet.Unicode)]
    public static extern int RtlGetVersion(ref OsVersionInfoEx versionInfo);
#pragma warning restore SYSLIB1054
}
