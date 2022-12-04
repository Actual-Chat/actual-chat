using System.Numerics;
using Stl.Fusion.Blazor;

namespace ActualChat.Mathematics;

[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
[DataContract]
public readonly record struct Trimmed<T>
    : IAdditionOperators<Trimmed<T>, Trimmed<T>, Trimmed<T>>, IComparable<Trimmed<T>>
    where T : struct, IAdditionOperators<T, T, T>, IComparable<T>, IEquatable<T>
{
    [DataMember] public T Value { get; }
    [DataMember] public T? Limit { get; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsTrimmed => Limit is { } t && EqualityComparer<T>.Default.Equals(Value, t);

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public Trimmed(T value, T? limit)
    {
        Limit = limit;
        if (limit is not { } vLimit)
            Value = value;
        else
            Value = Comparer<T>.Default.Compare(value, vLimit) < 0 ? value : vLimit;
    }

    public override string ToString()
        => Format();
    public string Format(string trimmedSuffix = "+")
        => Invariant($"{Value}{(IsTrimmed ? trimmedSuffix : "")}");

    public static implicit operator Trimmed<T>(T value)
        => new(value, default);
    public static implicit operator Trimmed<T>((T Value, T TrimValue) source)
        => new(source.Value, source.TrimValue);

    public static Trimmed<T> operator +(Trimmed<T> left, Trimmed<T> right)
    {
        var minLimit = left.Limit;
        if (minLimit is not { } vMinLimit)
            minLimit = right.Limit;
        else if (right.Limit is { } vRightLimit)
            minLimit = Comparer<T>.Default.Compare(vMinLimit, vRightLimit) < 0 ? vMinLimit : vRightLimit;
        return new (left.Value + right.Value, minLimit);
    }

    public int CompareTo(Trimmed<T> other)
        => Comparer<T>.Default.Compare(Value, other.Value);
}
