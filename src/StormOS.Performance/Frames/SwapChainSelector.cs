namespace StormOS.Performance.Frames;

/// <summary>
/// Tracks presents per swap chain and selects the dominant one (the game's main swap chain), so that overlays or
/// secondary windows presenting from the same process do not distort frame times.
/// </summary>
public sealed class SwapChainSelector
{
    private readonly Dictionary<ulong, (double LastMs, int Count)> _chains = [];
    private double _windowStartMs = double.NaN;
    private ulong? _dominant;

    /// <summary>Gets the length of the dominance evaluation window.</summary>
    public const double WindowMs = 2000;

    /// <summary>Registers a present and returns the frame time when it belongs to the dominant swap chain.</summary>
    /// <param name="swapChain">Swap chain address (0 when unknown).</param>
    /// <param name="timestampMs">Present timestamp in milliseconds.</param>
    /// <returns>The frame time, or <see langword="null"/>.</returns>
    public double? OnPresent(ulong swapChain, double timestampMs)
    {
        if (double.IsNaN(_windowStartMs))
        {
            _windowStartMs = timestampMs;
        }

        _chains.TryGetValue(swapChain, out var state);
        var frameTime = state.Count > 0 || state.LastMs > 0 ? timestampMs - state.LastMs : (double?)null;
        _chains[swapChain] = (timestampMs, state.Count + 1);

        if (timestampMs - _windowStartMs >= WindowMs || _dominant is null)
        {
            _dominant = _chains.MaxBy(c => c.Value.Count).Key;
            if (timestampMs - _windowStartMs >= WindowMs)
            {
                foreach (var key in _chains.Keys.ToList())
                {
                    _chains[key] = (_chains[key].LastMs, 0);
                }

                _windowStartMs = timestampMs;
            }
        }

        return swapChain == _dominant && frameTime is > 0 ? frameTime : null;
    }
}
