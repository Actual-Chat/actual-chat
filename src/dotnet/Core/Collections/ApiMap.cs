using ActualChat.Collections.Internal;
using Cysharp.Text;
using MemoryPack;

namespace ActualChat.Collections;

[DataContract, MemoryPackable(GenerateType.Collection)]
public sealed partial class ApiMap<TKey, TValue> : Dictionary<TKey, TValue>, ICloneable<ApiMap<TKey, TValue>>
    where TKey : notnull
{
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

    public override string ToString()
    {
        using var sb = ZString.CreateStringBuilder();
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
                sb.Append($", ...{Count - ApiConstants.MaxToStringItems} more");
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
        return sb.ToString();
    }
}
