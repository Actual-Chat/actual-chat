using ActualChat.Collections.Internal;
using Cysharp.Text;
using MemoryPack;

namespace ActualChat.Collections;

public static class ApiArray
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ApiArray<T> New<T>(params T[] items)
        => new(items);
}

[JsonConverter(typeof(ApiArrayJsonConverter))]
[Newtonsoft.Json.JsonConverter(typeof(ApiArrayNewtonsoftJsonConverter))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public readonly partial struct ApiArray<T> : IReadOnlyList<T>, ICloneable<ApiArray<T>>, IEquatable<ApiArray<T>>
{
    public static readonly ApiArray<T> Empty = new(Array.Empty<T>());

    private readonly T[]? _items;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public T[] Items => _items ?? Array.Empty<T>();
    [MemoryPackIgnore]
    public int Count => _items?.Length ?? 0;
    [MemoryPackIgnore]
    public bool IsEmpty => _items == null;

    public T this[int index]
        => _items == null
            ? throw new ArgumentOutOfRangeException(nameof(index))
            : _items[index];

    public ApiArray<T> this[Range range]
        => new(Items[range]);

    [MemoryPackConstructor]
    public ApiArray(params T[]? items)
        => _items = items?.Length > 0 ? items : default;
    public ApiArray(List<T>? source)
        => _items = source == null || source.Count == 0 ? null : source.ToArray();
    public ApiArray(IEnumerable<T>? source)
        : this(source?.ToArray()) { }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

    object ICloneable.Clone() => Clone();
    public ApiArray<T> Clone() => new(_items?.ToArray());

    public override string ToString()
    {
        using var sb = ZString.CreateStringBuilder();
        sb.Append('<');
        sb.Append(typeof(T).GetName());
        sb.Append(">[");
        var i = 0;
        foreach (var item in Items) {
            if (i >= ApiConstants.MaxToStringItems) {
                sb.Append($", ...{Count - ApiConstants.MaxToStringItems} more");
                break;
            }
            if (i > 0)
                sb.Append(", ");
            sb.Append(item);
            i++;
        }
        sb.Append(']');
        return sb.ToString();
    }

    public bool Contains(T item)
        => IndexOf(item) >= 0;

    public bool TryGetValue(T item, out T existingItem)
    {
        var index = IndexOf(item);
        if (index < 0) {
            existingItem = default!;
            return false;
        }

        existingItem = _items![index];
        return true;
    }

    public int IndexOf(T item)
    {
        var items = _items;
        if (items == null || items.Length == 0)
            return -1;

        for (var i = 0; i < items.Length; i++) {
            var existingItem = items[i];
            if (EqualityComparer<T>.Default.Equals(existingItem, item))
                return i;
        }
        return -1;
    }

    public int LastIndexOf(T item)
    {
        var items = _items;
        if (items == null || items.Length == 0)
            return -1;

        for (var i = items.Length - 1; i >= 0; i--) {
            var existingItem = items[i];
            if (EqualityComparer<T>.Default.Equals(existingItem, item))
                return i;
        }
        return -1;
    }

    public ApiArray<T> Add(T item)
    {
        var newItems = new T[Count + 1];
        Items.CopyTo(newItems, 0);
        newItems[^1] = item;
        return new ApiArray<T>(newItems);
    }

    public ApiArray<T> TryAdd(T item)
        => Contains(item) ? this : Add(item);

    public ApiArray<T> AddOrReplace(T item)
        => AddOrUpdate(item, _ => item);

    public ApiArray<T> AddOrUpdate(T item, Func<T, T> updater)
    {
        var index = IndexOf(item);
        if (index < 0)
            return Add(item);

        var copy = Clone();
        copy.Items[index] = updater(copy.Items[index]);
        return copy;
    }

    public ApiArray<T> UpdateWhere(Func<T, bool> where, Func<T, T> updater)
    {
        ApiArray<T>? copy = null;
        for (var i = 0; i < Items.Length; i++)
            if (where(Items[i])) {
                copy ??= Clone();
                copy.Value.Items[i] = updater(copy.Value.Items[i]);
            }
        return copy ?? this;
    }

    public ApiArray<T> RemoveAll(T item)
    {
        var items = _items;
        if (items == null || items.Length == 0)
            return this;

        var list = new List<T>(items.Length);
        foreach (var existingItem in items) {
            if (!EqualityComparer<T>.Default.Equals(existingItem, item))
                list.Add(existingItem);
        }
        return list.Count == items.Length
            ? this
            : new ApiArray<T>(list.ToArray());
    }

    public ApiArray<T> RemoveAll(Func<T, bool> predicate)
    {
        var items = _items;
        if (items == null || items.Length == 0)
            return this;

        var list = new List<T>(items.Length);
        foreach (var item in items) {
            if (!predicate.Invoke(item))
                list.Add(item);
        }
        return list.Count == items.Length
            ? this
            : new ApiArray<T>(list.ToArray());
    }

    public ApiArray<T> RemoveAll(Func<T, int, bool> predicate)
    {
        var items = _items;
        if (items == null || items.Length == 0)
            return this;

        var list = new List<T>(items.Length);
        for (var i = 0; i < items.Length; i++) {
            var item = items[i];
            if (!predicate.Invoke(item, i))
                list.Add(item);
        }
        return list.Count == items.Length
            ? this
            : new ApiArray<T>(list.ToArray());
    }

    public ApiArray<T> Trim(int maxCount)
    {
        if (maxCount < 0)
            throw new ArgumentOutOfRangeException(nameof(maxCount));

        var items = _items;
        if (items == null || items.Length <= maxCount)
            return this;

        var copy = new T[maxCount];
        Array.Copy(items, 0, copy, 0, maxCount);
        return new ApiArray<T>(copy);
    }

    // Equality

    public bool Equals(ApiArray<T> other) => Equals(_items, other._items);
    public override bool Equals(object? obj) => obj is ApiArray<T> other && Equals(other);
    public override int GetHashCode() => _items?.GetHashCode() ?? 0;

    public static bool operator ==(ApiArray<T> left, ApiArray<T> right) => left.Equals(right);
    public static bool operator !=(ApiArray<T> left, ApiArray<T> right) => !left.Equals(right);
}
