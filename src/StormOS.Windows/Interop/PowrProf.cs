using System.Runtime.InteropServices;

namespace StormOS.Windows.Interop;

/// <summary>powrprof.dll declarations (documented power scheme management API).</summary>
internal static partial class PowrProf
{
    public const uint AccessScheme = 16;
    public const uint ErrorNoMoreItems = 259;
    public const uint ErrorMoreData = 234;

    [LibraryImport("powrprof.dll")]
    public static partial uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [LibraryImport("powrprof.dll")]
    public static partial uint PowerSetActiveScheme(IntPtr userRootPowerKey, in Guid schemeGuid);

    [LibraryImport("powrprof.dll")]
    public static partial uint PowerEnumerate(IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subGroupOfPowerSettingsGuid, uint accessFlags, uint index, IntPtr buffer, ref uint bufferSize);

    [LibraryImport("powrprof.dll")]
    public static partial uint PowerReadFriendlyName(IntPtr rootPowerKey, in Guid schemeGuid, IntPtr subGroupOfPowerSettingsGuid, IntPtr powerSettingGuid, IntPtr buffer, ref uint bufferSize);

    [LibraryImport("powrprof.dll")]
    public static partial uint PowerReadDescription(IntPtr rootPowerKey, in Guid schemeGuid, IntPtr subGroupOfPowerSettingsGuid, IntPtr powerSettingGuid, IntPtr buffer, ref uint bufferSize);

    [LibraryImport("powrprof.dll")]
    public static partial uint PowerDuplicateScheme(IntPtr rootPowerKey, in Guid sourceSchemeGuid, ref IntPtr destinationSchemeGuid);

    [LibraryImport("powrprof.dll")]
    public static partial uint PowerDeleteScheme(IntPtr rootPowerKey, in Guid schemeGuid);

    public delegate void EffectivePowerModeCallback(int mode, IntPtr context);

    [LibraryImport("powrprof.dll")]
    public static partial int PowerRegisterForEffectivePowerModeNotifications(uint version, EffectivePowerModeCallback callback, IntPtr context, out IntPtr registrationHandle);

    [LibraryImport("powrprof.dll")]
    public static partial int PowerUnregisterFromEffectivePowerModeNotifications(IntPtr registrationHandle);
}
