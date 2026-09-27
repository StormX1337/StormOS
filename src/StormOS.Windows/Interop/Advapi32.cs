using System.Runtime.InteropServices;

namespace StormOS.Windows.Interop;

/// <summary>advapi32.dll declarations.</summary>
internal static partial class Advapi32
{
    public const uint ServiceNoChange = 0xFFFFFFFF;
    public const uint ServiceChangeConfig = 0x0002;
    public const uint ServiceQueryConfig = 0x0001;
    public const uint ScManagerConnect = 0x0001;
    public const uint ServiceBootStart = 0;
    public const uint ServiceSystemStart = 1;
    public const uint ServiceAutoStart = 2;
    public const uint ServiceDemandStart = 3;
    public const uint ServiceDisabled = 4;

    public const uint TokenQuery = 0x0008;

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenProcessToken(Microsoft.Win32.SafeHandles.SafeProcessHandle process, uint desiredAccess, out IntPtr token);

    [LibraryImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [LibraryImport("advapi32.dll", EntryPoint = "OpenServiceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr OpenService(IntPtr scManager, string serviceName, uint desiredAccess);

    [LibraryImport("advapi32.dll", EntryPoint = "ChangeServiceConfigW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ChangeServiceConfig(IntPtr service, uint serviceType, uint startType, uint errorControl, string? binaryPathName, string? loadOrderGroup, IntPtr tagId, string? dependencies, string? serviceStartName, string? password, string? displayName);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseServiceHandle(IntPtr handle);
}
