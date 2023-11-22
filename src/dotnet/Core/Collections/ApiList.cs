using ActualChat.Collections.Internal;
using Cysharp.Text;
using MemoryPack;

namespace ActualChat.Collections;

[DataContract, MemoryPackable(GenerateType.Collection)]
public sealed partial class ApiList<T> : List<T>, ICloneable<ApiList<T>>
{
    public ApiList() { }
    public ApiList(IEnumerable<T> collection) : base(collection) { }
    public ApiList(int capacity) : base(capacity) { }

    object ICloneable.Clone() => Clone();
    public ApiList<T> Clone() => new(this);

    public override string ToString()
    {
        var sb = StringBuilderExt.Acquire();
        sb.Append('<');
        sb.Append(typeof(T).GetName());
        sb.Append(">[");
        var i = 0;
        foreach (var item in this) {
            if (i >= ApiConstants.MaxToStringItems) {
                sb.Append(CultureInfo.InvariantCulture,
                    $", ...{Count - ApiConstants.MaxToStringItems} more");
                break;
            }
            if (i > 0)
                sb.Append(", ");
            sb.Append(item);
            i++;
        }
        sb.Append(']');
        return sb.ToStringAndRelease();
    }
}
