namespace StormOS.Security.Ipc;

/// <summary>A simple thread-safe token bucket used to rate limit IPC connections.</summary>
public sealed class TokenBucket
{
    private readonly object _gate = new();
    private readonly TimeProvider _time;
    private readonly double _capacity;
    private readonly double _refillPerSecond;
    private double _tokens;
    private long _lastRefill;

    /// <summary>Initializes a new instance of the <see cref="TokenBucket"/> class.</summary>
    /// <param name="capacity">Maximum burst size.</param>
    /// <param name="refillPerSecond">Tokens added per second.</param>
    /// <param name="timeProvider">Time source.</param>
    public TokenBucket(double capacity, double refillPerSecond, TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(refillPerSecond);
        _capacity = capacity;
        _refillPerSecond = refillPerSecond;
        _tokens = capacity;
        _time = timeProvider ?? TimeProvider.System;
        _lastRefill = _time.GetTimestamp();
    }

    /// <summary>Attempts to consume tokens.</summary>
    /// <param name="cost">Tokens to consume.</param>
    /// <returns><see langword="true"/> when enough tokens were available.</returns>
    public bool TryConsume(int cost = 1)
    {
        lock (_gate)
        {
            var now = _time.GetTimestamp();
            var elapsed = _time.GetElapsedTime(_lastRefill, now).TotalSeconds;
            _lastRefill = now;
            _tokens = Math.Min(_capacity, _tokens + (elapsed * _refillPerSecond));
            if (_tokens < cost)
            {
                return false;
            }

            _tokens -= cost;
            return true;
        }
    }
}
