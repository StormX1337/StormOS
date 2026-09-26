using StormOS.Windows.Interop;

namespace StormOS.Windows.Platform;

/// <summary>Mouse parameters (SPI_GETMOUSE / SPI_SETMOUSE).</summary>
/// <param name="Threshold1">First acceleration threshold.</param>
/// <param name="Threshold2">Second acceleration threshold.</param>
/// <param name="Acceleration">Acceleration level; 0 means "Enhance pointer precision" is off.</param>
public sealed record MouseParameters(int Threshold1, int Threshold2, int Acceleration)
{
    /// <summary>Gets a value indicating whether "Enhance pointer precision" is on.</summary>
    public bool EnhancePointerPrecision => Acceleration != 0;
}

/// <summary>Reads and writes the current user's mouse parameters.</summary>
public interface IMouseSettings
{
    /// <summary>Reads the parameters.</summary>
    /// <returns>The parameters.</returns>
    MouseParameters Read();

    /// <summary>Writes the parameters, persisting them to the user profile and broadcasting the change.</summary>
    /// <param name="parameters">The parameters.</param>
    void Write(MouseParameters parameters);
}

/// <summary>User32 based implementation of <see cref="IMouseSettings"/>.</summary>
public sealed class MouseSettings : IMouseSettings
{
    /// <inheritdoc />
    public MouseParameters Read()
    {
        var values = new int[3];
        if (!User32.SystemParametersInfo(User32.SpiGetMouse, 0, values, 0))
        {
            throw new InvalidOperationException("SPI_GETMOUSE failed.");
        }

        return new MouseParameters(values[0], values[1], values[2]);
    }

    /// <inheritdoc />
    public void Write(MouseParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        int[] values = [parameters.Threshold1, parameters.Threshold2, parameters.Acceleration];
        if (!User32.SystemParametersInfo(User32.SpiSetMouse, 0, values, User32.SpifUpdateIniFile | User32.SpifSendChange))
        {
            throw new InvalidOperationException("SPI_SETMOUSE failed.");
        }
    }
}
