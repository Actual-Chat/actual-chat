using ActualChat.Collections.Internal;
using MemoryPack;

namespace ActualChat.Collections;

[DataContract, MemoryPackable(GenerateType.Collection)]
public sealed partial class ApiMap<TKey, TValue>
    : Dictionary<TKey, TValue>, ICloneable<ApiMap<TKey, TValue>>, IEnumerable<KeyValuePair<TKey, TValue>>
    where TKey : notnull
{
    public static readonly ApiMap<TKey, TValue> Empty = new();

    private SortedItemCache? _sortedItemCache;

    public ApiMap() { }
    public ApiMap(IDictionary<TKey, TValue> dictionary) : base(dictionary) { }
    public ApiMap(IDictionary<TKey, TValue> dictionary, IEqualityComparer<TKey>? comparer) : base(dictionary, comparer) { }
    public ApiMap(IEnumerable<KeyValuePair<TKey, TValue>> collection) : base(collection) { }
    public ApiMap(IEnumerable<KeyValuePair<TKey, TValue>> collection, IEqualityComparer<TKey>? comparer) : base(collection, comparer) { }
    public ApiMap(IEqualityComparer<TKey>? comparer) : base(comparer) { }
    public ApiMap(int capacity) : base(capacity) { }
    public ApiMap(int capacity, IEqualityComparer<TKey>? comparer) : base(capacity, comparer) { }
#pragma warning disable SYSLIB0051 // Type or member is obsolete
    private ApiMap(SerializationInfo info, StreamingContext context) : base(info, context) { }
#pragma warning restore SYSLIB0051

    object ICloneable.Clone() => Clone();
    public ApiMap<TKey, TValue> Clone() => new(this, Comparer);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public new IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => GetSortedItemCache().Items.GetEnumerator();

    private SortedItemCache GetSortedItemCache()
    {
        if (_sortedItemCache is not { IsValid: true })
            _sortedItemCache = new SortedItemCache(base.GetEnumerator(), Count);
        return _sortedItemCache;
    }

    public override string ToString()
    {
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        sb.Append('<');
        sb.Append(typeof(TKey).GetName());
        sb.Append(',');
        sb.Append(typeof(TValue).GetName());
        sb.Append(">{");
        if (Count == 0) {
            sb.Append('}');
            return sb.ToString();
        }
        var i = 0;
        foreach (var (key, value) in this) {
            if (i >= ApiConstants.MaxToStringItems) {
                sb.Append(CultureInfo.InvariantCulture,
                    $", ...{Count - ApiConstants.MaxToStringItems} more");
                break;
            }
            sb.Append(i > 0 ? ", " : " ");
            sb.Append('(');
            sb.Append(key);
            sb.Append(", ");
            sb.Append(value);
            sb.Append(')');
            i++;
        }
        sb.Append(" }");
        return sb.ToStringAndRelease();
    }

    // Nested types

    private sealed class SortedItemCache(IEnumerator<KeyValuePair<TKey, TValue>> enumerator, int count)
    {
        public readonly IEnumerable<KeyValuePair<TKey, TValue>> Items = NewItems(enumerator, count);

        public bool IsValid {
            get {
                try
                {
                    enumerator.Reset();
                    return true;
                }
                catch (InvalidOperationException)
                {
                    // If we're here, the collection was changed.
                    // Technically this should never happen, coz all ApiXxx collections are ~ immutable,
                    // but we still need to handle this gracefully - just in case.
                    return false;
                }
            }
        }

        private static KeyValuePair<TKey, TValue>[] NewItems(IEnumerator<KeyValuePair<TKey, TValue>> e, int count)
        {
            var items = new KeyValuePair<TKey, TValue>[count];
            var i = 0;
            while (e.MoveNext())
                items[i++] = e.Current;
            return items.SortInPlace(x => x.Key);
        }
    }
}
