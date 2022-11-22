using System.Numerics;

namespace ActualChat.Mathematics;

[StructLayout(LayoutKind.Auto)]
[DataContract]
public readonly record struct Trimmed<T>
    : IAdditionOperators<Trimmed<T>, Trimmed<T>, Trimmed<T>>, IComparable<Trimmed<T>>
    where T : IAdditionOperators<T, T, T>, IComparable<T>, IEquatable<T>
{
    [DataMember] public T Value { get; }
    [DataMember] public T? Limit { get; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsTrimmed => Limit is { } t && EqualityComparer<T>.Default.Equals(Value, t);

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public Trimmed(T value, T? limit)
    {
        Limit = limit;
        if (limit == null)
            Value = value;
        else
            Value = Comparer<T>.Default.Compare(value, limit) < 0 ? value : limit;
    }

    public override string ToString()
        => Format();
    public string Format(string trimmedSuffix = "+")
        => Invariant($"{Value}{(IsTrimmed ? trimmedSuffix : "")}");

    public static implicit operator Trimmed<T>((T Value, T TrimValue) source)
        => new(source.Value, source.TrimValue);

    public static Trimmed<T> operator +(Trimmed<T> left, Trimmed<T> right)
    {
        var minLimit = left.Limit;
        if (minLimit == null)
            minLimit = right.Limit;
        else if (right.Limit is { } rightTrimmedAt)
            minLimit = Comparer<T>.Default.Compare(minLimit, rightTrimmedAt) < 0 ? minLimit : rightTrimmedAt;
        return new (left.Value + right.Value, minLimit);
    }

    public int CompareTo(Trimmed<T> other)
        => Comparer<T>.Default.Compare(Value, other.Value);
}
