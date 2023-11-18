using Microsoft.Toolkit.HighPerformance;

namespace ActualChat;

#pragma warning disable CA1815

public readonly struct HashRing<T>
    where T : notnull
{
    private static readonly IComparer<(T Value, int Hash)> Comparer = new ItemComparer();

    public static readonly Func<T, int> DefaultHasher = static v => v.GetHashCode();
    public static readonly Func<T, int> DefaultStringHasher = static v => v is string s ? s.GetDjb2HashCode() : v.GetHashCode();
    public static readonly HashRing<T> Empty = new(Array.Empty<T>());

    private readonly T[] _doubleItems;
    public (T Value, int Hash)[] Items { get; }
    public int Count => Items.Length;
    public bool IsEmpty => Count == 0;
    public T this[int index] => Items[Mod(index)].Value;

    public HashRing(IEnumerable<T> values, Func<T, int>? hasher = null)
    {
        hasher ??= typeof(T) == typeof(string) ? DefaultStringHasher : DefaultHasher;
        Items = values
            .Select(v => (Value: v, Hash: hasher.Invoke(v)))
            .OrderBy(i => i.Hash)
            .ToArray();
        _doubleItems = new T[Items.Length * 2];
        for (var i = 0; i < _doubleItems.Length; i++)
            _doubleItems[i] = this[i];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Mod(int index)
    {
        var m = index % Count;
        return m >= 0 ? m : m + Count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ClampCount(int count)
        => Math.Min(Math.Max(count, 0), Count);

    public T Get(int hash, int offset = 0)
        => this[offset + ~Array.BinarySearch(Items, (default!, hash), Comparer)];

    public ReadOnlySpan<T> Span(int hash, int count, int offset = 0)
    {
        count = ClampCount(count);
        if (count == 0)
            return Span<T>.Empty;

        offset = Mod(offset + ~Array.BinarySearch(Items, (default!, hash), Comparer));
        return _doubleItems.AsSpan(offset, count);
    }

    public ArraySegment<T> Segment(int hash, int count, int offset = 0)
    {
        count = ClampCount(count);
        if (count == 0)
            return ArraySegment<T>.Empty;

        offset = Mod(offset + ~Array.BinarySearch(Items, (default!, hash), Comparer));
        return new ArraySegment<T>(_doubleItems, offset, count);
    }

    // Nested types

    private sealed class ItemComparer : IComparer<(T Value, int Hash)>
    {
        public int Compare((T Value, int Hash) x, (T Value, int Hash) y)
        {
            var d = x.Hash - y.Hash;
            // We map "equals" here to 1 to make sure we find the item with higher or equal Hash
            return d >= 0 ? 1 : -1;
        }
    }
}
