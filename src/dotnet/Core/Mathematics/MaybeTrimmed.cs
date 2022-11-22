using System.Numerics;

namespace ActualChat.Mathematics;

[StructLayout(LayoutKind.Auto)]
[DataContract]
public readonly record struct MaybeTrimmed<T>
    : IAdditionOperators<MaybeTrimmed<T>, MaybeTrimmed<T>, MaybeTrimmed<T>>, IComparable<MaybeTrimmed<T>>
    where T : IAdditionOperators<T, T, T>, IComparable<T>, IEquatable<T>
{
    [DataMember] public T Value { get; }
    [DataMember] public T? TrimmedAt { get; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsTrimmed => TrimmedAt is { } t && EqualityComparer<T>.Default.Equals(Value, t);

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public MaybeTrimmed(T value, T? trimmedAt)
    {
        TrimmedAt = trimmedAt;
        if (trimmedAt == null)
            Value = value;
        else
            Value = Comparer<T>.Default.Compare(value, trimmedAt) < 0 ? value : trimmedAt;
    }

    public override string ToString()
        => Format();
    public string Format(string trimmedSuffix = "+")
        => Invariant($"{Value}{(IsTrimmed ? trimmedSuffix : "")}");

    public static implicit operator MaybeTrimmed<T>((T Value, T TrimValue) source)
        => new(source.Value, source.TrimValue);

    public static MaybeTrimmed<T> operator +(MaybeTrimmed<T> left, MaybeTrimmed<T> right)
    {
        var minTrimmedAt = left.TrimmedAt;
        if (minTrimmedAt == null)
            minTrimmedAt = right.TrimmedAt;
        else if (right.TrimmedAt is { } rightTrimmedAt)
            minTrimmedAt = Comparer<T>.Default.Compare(minTrimmedAt, rightTrimmedAt) < 0 ? minTrimmedAt : rightTrimmedAt;
        return new (left.Value + right.Value, minTrimmedAt);
    }

    public int CompareTo(MaybeTrimmed<T> other)
        => Comparer<T>.Default.Compare(Value, other.Value);
}
