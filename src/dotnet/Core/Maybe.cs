using ActualChat.Serialization.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

/// <summary>
/// Factory methods for creating <see cref="Maybe{T}"/> instances.
/// </summary>
public static class Maybe
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Maybe<T> Value<T>(T value)
        => new(true, value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Maybe<T> None<T>()
        => new(false, default);
}

/// <summary>
/// Represents an optional value that may or may not be present.
/// </summary>
[Newtonsoft.Json.JsonObject(Newtonsoft.Json.MemberSerialization.OptOut)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
[MessagePackFormatter(typeof(MaybeMessagePackFormatter<>))]
[DebuggerDisplay("{" + nameof(DebugValue) + "}")]
[ParameterComparer(typeof(ByValueParameterComparer))]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor, SerializationConstructor]
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public partial class Maybe<T>(bool hasValue, T? valueOrDefault)
    : IEquatable<Maybe<T>>
{
    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)]
    public bool HasValue { get; } = hasValue;
    [DataMember(Order = 1), MemoryPackOrder(1), Key(1)]
    public T? ValueOrDefault { get; } = valueOrDefault;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public T Value {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get { AssertHasValue(); return ValueOrDefault!; }
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    protected string DebugValue => ToString();

    public override string ToString()
        => IsValue(out var v)
            ? $"Maybe.Value({v})"
            : "Maybe.None()";

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Deconstruct(out bool hasValue, [MaybeNullWhen(false)] out T value)
    {
        hasValue = HasValue;
        value = ValueOrDefault!;
    }

    // Useful methods

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsValue([MaybeNullWhen(false)] out T value)
    {
        value = ValueOrDefault!;
        return HasValue;
    }

    // Equality

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Maybe<T>? other)
    {
        if (ReferenceEquals(other, null))
            return false;

        return HasValue
            ? other.HasValue && EqualityComparer<T>.Default.Equals(ValueOrDefault!, other.ValueOrDefault!)
            : !other.HasValue;
    }

    public override bool Equals(object? obj)
        => obj is Maybe<T> other && Equals(other);
    public override int GetHashCode()
        => HasValue
            ? ValueOrDefault?.GetHashCode() ?? 0
            : int.MinValue;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Maybe<T>? left, Maybe<T>? right)
    {
        if (ReferenceEquals(left, right))
            return true;

        if (left is null || right is null)
            return false;

        return left.Equals(right);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Maybe<T>? left, Maybe<T>? right) => !(left == right);

    // Operators

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Maybe<T>(T source)
        => new(true, source);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Maybe<T>((bool HasValue, T Value) source)
        => new(source.HasValue, source.Value);

    // Private helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void AssertHasValue()
    {
        if (!HasValue)
            throw StandardError.Constraint($"{GetType().GetName()} is expected to have Value here.");
    }
}
