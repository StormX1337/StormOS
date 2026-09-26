namespace StormOS.Performance.Collections;

/// <summary>A fixed capacity circular buffer. Not thread-safe; callers synchronize.</summary>
/// <typeparam name="T">Item type.</typeparam>
public sealed class RingBuffer<T>
{
    private readonly T[] _items;
    private int _start;

    /// <summary>Initializes a new instance of the <see cref="RingBuffer{T}"/> class.</summary>
    /// <param name="capacity">Maximum number of items.</param>
    public RingBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _items = new T[capacity];
    }

    /// <summary>Gets the number of items.</summary>
    public int Count { get; private set; }

    /// <summary>Gets the capacity.</summary>
    public int Capacity => _items.Length;

    /// <summary>Adds an item, overwriting the oldest when full.</summary>
    /// <param name="item">The item.</param>
    public void Add(T item)
    {
        if (Count < _items.Length)
        {
            _items[(_start + Count) % _items.Length] = item;
            Count++;
        }
        else
        {
            _items[_start] = item;
            _start = (_start + 1) % _items.Length;
        }
    }

    /// <summary>Removes all items.</summary>
    public void Clear()
    {
        Array.Clear(_items);
        _start = 0;
        Count = 0;
    }

    /// <summary>Copies the items in insertion order.</summary>
    /// <returns>The items, oldest first.</returns>
    public T[] ToArray()
    {
        var result = new T[Count];
        for (var i = 0; i < Count; i++)
        {
            result[i] = _items[(_start + i) % _items.Length];
        }

        return result;
    }

    /// <summary>Copies the newest <paramref name="count"/> items.</summary>
    /// <param name="count">Number of items.</param>
    /// <returns>The items, oldest first.</returns>
    public T[] TakeLast(int count)
    {
        count = Math.Clamp(count, 0, Count);
        var result = new T[count];
        var offset = Count - count;
        for (var i = 0; i < count; i++)
        {
            result[i] = _items[(_start + offset + i) % _items.Length];
        }

        return result;
    }
}
