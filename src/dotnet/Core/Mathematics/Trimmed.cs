using System.Numerics;
using MemoryPack;
using ActualLab.Fusion.Blazor;

namespace ActualChat.Mathematics;

[StructLayout(LayoutKind.Auto)]
[ParameterComparer(typeof(ByValueParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public readonly partial record struct Trimmed<T>
    : IAdditionOperators<Trimmed<T>, Trimmed<T>, Trimmed<T>>,
        IComparisonOperators<Trimmed<T>, Trimmed<T>, bool>,
        IComparable<Trimmed<T>>
    where T : struct, IAdditionOperators<T, T, T>, IComparable<T>, IEquatable<T>
{
    [DataMember, MemoryPackOrder(0)] public T Value { get; }
    [DataMember, MemoryPackOrder(1)] public T? Limit { get; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsTrimmed => Limit is { } t && EqualityComparer<T>.Default.Equals(Value, t);

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
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

    // Addition

    public static Trimmed<T> operator +(Trimmed<T> left, Trimmed<T> right)
    {
        var minLimit = left.Limit;
        if (minLimit is not { } vMinLimit)
            minLimit = right.Limit;
        else if (right.Limit is { } vRightLimit)
            minLimit = Comparer<T>.Default.Compare(vMinLimit, vRightLimit) < 0 ? vMinLimit : vRightLimit;
        return new (left.Value + right.Value, minLimit);
    }

    // Comparison

    public int CompareTo(Trimmed<T> other) => Comparer<T>.Default.Compare(Value, other.Value);
    public static bool operator <(Trimmed<T> left, Trimmed<T> right) => left.CompareTo(right) < 0;
    public static bool operator >(Trimmed<T> left, Trimmed<T> right) => left.CompareTo(right) > 0;
    public static bool operator <=(Trimmed<T> left, Trimmed<T> right) => left.CompareTo(right) <= 0;
    public static bool operator >=(Trimmed<T> left, Trimmed<T> right) => left.CompareTo(right) >= 0;

    // Equality

    public bool Equals(Trimmed<T> other) => Value.Equals(other.Value);
    public override int GetHashCode() => Value.GetHashCode();
}
