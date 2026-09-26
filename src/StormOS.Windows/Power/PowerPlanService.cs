using System.Runtime.InteropServices;
using StormOS.Core.Power;
using StormOS.Windows.Interop;

namespace StormOS.Windows.Power;

/// <summary>Power scheme management through the documented PowrProf API (no powercfg shell-out).</summary>
public sealed class PowerPlanService : IPowerPlanService
{
    /// <inheritdoc />
    public IReadOnlyList<PowerPlan> GetPlans()
    {
        var active = GetActivePlanId();
        var plans = new List<PowerPlan>();
        var guidSize = (uint)Marshal.SizeOf<Guid>();
        var buffer = Marshal.AllocHGlobal((int)guidSize);
        try
        {
            for (uint index = 0; ; index++)
            {
                var size = guidSize;
                var status = PowrProf.PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, PowrProf.AccessScheme, index, buffer, ref size);
                if (status == PowrProf.ErrorNoMoreItems)
                {
                    break;
                }

                if (status != 0)
                {
                    throw new InvalidOperationException($"PowerEnumerate failed with error {status}.");
                }

                var id = Marshal.PtrToStructure<Guid>(buffer);
                plans.Add(new PowerPlan
                {
                    Id = id,
                    Name = ReadString(id, description: false) ?? KnownPowerSchemes.Describe(id) ?? id.ToString(),
                    Description = ReadString(id, description: true),
                    IsActive = id == active,
                    Personality = KnownPowerSchemes.Describe(id),
                });
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return plans;
    }

    /// <inheritdoc />
    public Guid GetActivePlanId()
    {
        var status = PowrProf.PowerGetActiveScheme(IntPtr.Zero, out var pointer);
        if (status != 0)
        {
            throw new InvalidOperationException($"PowerGetActiveScheme failed with error {status}.");
        }

        try
        {
            return Marshal.PtrToStructure<Guid>(pointer);
        }
        finally
        {
            Kernel32.LocalFree(pointer);
        }
    }

    /// <inheritdoc />
    public void SetActivePlan(Guid schemeId)
    {
        var status = PowrProf.PowerSetActiveScheme(IntPtr.Zero, schemeId);
        if (status != 0)
        {
            throw new InvalidOperationException($"PowerSetActiveScheme failed with error {status}.");
        }
    }

    /// <inheritdoc />
    public Guid DuplicatePlan(Guid templateId)
    {
        var destination = IntPtr.Zero;
        var status = PowrProf.PowerDuplicateScheme(IntPtr.Zero, templateId, ref destination);
        if (status != 0)
        {
            throw new InvalidOperationException($"PowerDuplicateScheme failed with error {status}.");
        }

        try
        {
            return Marshal.PtrToStructure<Guid>(destination);
        }
        finally
        {
            Kernel32.LocalFree(destination);
        }
    }

    /// <inheritdoc />
    public void DeletePlan(Guid schemeId)
    {
        if (schemeId == KnownPowerSchemes.Balanced || schemeId == KnownPowerSchemes.HighPerformance || schemeId == KnownPowerSchemes.PowerSaver)
        {
            throw new InvalidOperationException("Built-in Windows power schemes are never deleted.");
        }

        var status = PowrProf.PowerDeleteScheme(IntPtr.Zero, schemeId);
        if (status != 0)
        {
            throw new InvalidOperationException($"PowerDeleteScheme failed with error {status}.");
        }
    }

    /// <inheritdoc />
    public string? GetEffectivePowerMode()
    {
        // PowerRegisterForEffectivePowerModeNotifications (Windows 10 1809+) invokes the callback immediately with the current mode.
        var mode = -1;
        using var signal = new ManualResetEventSlim();
        PowrProf.EffectivePowerModeCallback callback = (value, _) =>
        {
            mode = value;
            signal.Set();
        };

        try
        {
            if (PowrProf.PowerRegisterForEffectivePowerModeNotifications(1, callback, IntPtr.Zero, out var handle) != 0)
            {
                return null;
            }

            signal.Wait(TimeSpan.FromSeconds(1));
            _ = PowrProf.PowerUnregisterFromEffectivePowerModeNotifications(handle);
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }

        GC.KeepAlive(callback);
        return mode switch
        {
            0 => "Battery saver",
            1 => "Better battery",
            2 => "Balanced",
            3 => "High performance",
            4 => "Max performance",
            5 => "Game mode",
            6 => "Mixed reality",
            _ => null,
        };
    }

    private static string? ReadString(Guid scheme, bool description)
    {
        uint size = 0;
        var status = description
            ? PowrProf.PowerReadDescription(IntPtr.Zero, scheme, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size)
            : PowrProf.PowerReadFriendlyName(IntPtr.Zero, scheme, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size);
        if (status != 0 || size == 0)
        {
            return null;
        }

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            status = description
                ? PowrProf.PowerReadDescription(IntPtr.Zero, scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size)
                : PowrProf.PowerReadFriendlyName(IntPtr.Zero, scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size);
            return status == 0 ? Marshal.PtrToStringUni(buffer) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
