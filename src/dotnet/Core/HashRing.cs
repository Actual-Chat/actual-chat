namespace ActualChat;

public readonly struct HashRing<T>
    where T : notnull
{
    private static IComparer<(T Value, int Hash)> Comparer { get; } = new ItemComparer();

    public static HashRing<T> Empty { get; } = new(Array.Empty<(T, int)>());

    private (T Value, int Hash)[] Items { get; }
    public int Count => Items.Length;
    public bool IsEmpty => Count == 0;
    public T this[int index]
        => index < 0
            ? throw new ArgumentOutOfRangeException(nameof(index))
            : Items[index % Count].Value;

    public HashRing((T Value, int Hash)[] items)
        => Items = items;
    public HashRing(IEnumerable<T> values, Func<T, int>? hasher = null)
    {
        var realHasher = hasher ?? (static v => v.GetHashCode());
        Items = values
            .Select(v => (Value: v, Hash: realHasher.Invoke(v)))
            .OrderBy(i => i.Hash)
            .ToArray();
    }

    public T Get(int hash, int offset = 0)
        => this[offset + ~Array.BinarySearch(Items, (default!, hash), Comparer)];

    public T GetRandom(int hash, int replicaCount)
        => Get(hash, Random.Shared.Next(replicaCount));

    public IEnumerable<T> GetMany(int hash, int count, int offset = 0)
    {
        offset += ~Array.BinarySearch(Items, (default!, hash), Comparer);
        for (var i = 0; i < count; i++)
            yield return this[offset + i];
    }

    public IEnumerable<T> GetManyRandom(int hash, int count, int replicaCount, int offset = 0)
    {
        if (count > replicaCount)
            throw new ArgumentOutOfRangeException(nameof(count));
        offset += ~Array.BinarySearch(Items, (default!, hash), Comparer);
        var indexes = Enumerable.Range(0, replicaCount).ToList();
        for (var i = 0; i < count; i++) {
            var j = Random.Shared.Next(indexes.Count);
            var index = indexes[j];
            indexes.RemoveAt(j);
            yield return this[offset + index];
        }
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
