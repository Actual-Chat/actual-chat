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
public readonly partial struct ApiArray<T> : IReadOnlyList<T>
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
    public ApiArray(params T[] items)
        => _items = items.Length == 0 ? default : items;
    public ApiArray(IEnumerable<T> collection)
        : this(collection.ToArray()) { }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

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

    public ApiArray<T> Add(T item)
    {
        var newItems = new T[Count + 1];
        Items.CopyTo(newItems, 0);
        newItems[^1] = item;
        return new ApiArray<T>(newItems);
    }

    public ApiArray<T> RemoveAll(Predicate<T> predicate)
        => new(Items.ToImmutableArray().RemoveAll(predicate).ToArray()); // Not quite efficient, but fine for now
}
