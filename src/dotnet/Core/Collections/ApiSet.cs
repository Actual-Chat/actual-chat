using ActualChat.Collections.Internal;
using Cysharp.Text;
using MemoryPack;

namespace ActualChat.Collections;

[DataContract, MemoryPackable(GenerateType.Collection)]
public sealed partial class ApiSet<T> : HashSet<T>, ICloneable<ApiSet<T>>
{
    public ApiSet() { }
    public ApiSet(IEnumerable<T> collection) : base(collection) { }
    public ApiSet(IEnumerable<T> collection, IEqualityComparer<T>? comparer) : base(collection, comparer) { }
    public ApiSet(IEqualityComparer<T>? comparer) : base(comparer) { }
    public ApiSet(int capacity) : base(capacity) { }
    public ApiSet(int capacity, IEqualityComparer<T>? comparer) : base(capacity, comparer) { }
    private ApiSet(SerializationInfo info, StreamingContext context) : base(info, context) { }

    object ICloneable.Clone() => Clone();
    public ApiSet<T> Clone() => new(this);

    public override string ToString()
    {
        using var sb = ZString.CreateStringBuilder();
        sb.Append('<');
        sb.Append(typeof(T).GetName());
        sb.Append(">{");
        if (Count == 0) {
            sb.Append('}');
            return sb.ToString();
        }
        var i = 0;
        foreach (var item in this) {
            if (i >= ApiConstants.MaxToStringItems) {
                sb.Append($", ...{Count - ApiConstants.MaxToStringItems} more");
                break;
            }
            sb.Append(i > 0 ? ", " : " ");
            sb.Append(item);
            i++;
        }
        sb.Append(" }");
        return sb.ToString();
    }
}
