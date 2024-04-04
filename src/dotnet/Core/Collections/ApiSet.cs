using ActualChat.Collections.Internal;
using MemoryPack;

namespace ActualChat.Collections;

public static class ApiSet
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ApiSet<T> Empty<T>()
        => ApiSet<T>.Empty;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ApiSet<T> New<T>(params T[] items)
        => new(items);
}

[DataContract, MemoryPackable(GenerateType.Collection)]
public sealed partial class ApiSet<T> : HashSet<T>, ICloneable<ApiSet<T>>
{
    public static readonly ApiSet<T> Empty = new(Array.Empty<T>());

    public ApiSet() { }
    public ApiSet(IEnumerable<T> collection) : base(collection) { }
    public ApiSet(IEnumerable<T> collection, IEqualityComparer<T>? comparer) : base(collection, comparer) { }
    public ApiSet(IEqualityComparer<T>? comparer) : base(comparer) { }
    public ApiSet(int capacity) : base(capacity) { }
    public ApiSet(int capacity, IEqualityComparer<T>? comparer) : base(capacity, comparer) { }
#pragma warning disable SYSLIB0051 // Type or member is obsolete
    private ApiSet(SerializationInfo info, StreamingContext context) : base(info, context) { }
#pragma warning restore SYSLIB0051

    object ICloneable.Clone() => Clone();
    public ApiSet<T> Clone() => new(this, Comparer);

    public override string ToString()
    {
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
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
                sb.Append(CultureInfo.InvariantCulture,
                    $", ...{Count - ApiConstants.MaxToStringItems} more");
                break;
            }
            sb.Append(i > 0 ? ", " : " ");
            sb.Append(item);
            i++;
        }
        sb.Append(" }");
        return sb.ToStringAndRelease();
    }
}
