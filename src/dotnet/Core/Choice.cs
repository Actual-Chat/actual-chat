using ActualChat.Serialization.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

/// <summary>
/// Factory methods for creating <see cref="Choice{T, TAlt}"/> instances.
/// </summary>
public static class Choice
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Choice<T, TAlt> Value<T, TAlt>(T value)
        => new(true, value, default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Choice<T, TAlt> Alternative<T, TAlt>(TAlt alternative)
        => new(false, default, alternative);
}

/// <summary>
/// Represents a value that is either <typeparamref name="T"/> or <typeparamref name="TAlt"/>.
/// </summary>
[Newtonsoft.Json.JsonObject(Newtonsoft.Json.MemberSerialization.OptOut)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
[MessagePackFormatter(typeof(ChoiceMessagePackFormatter<,>))]
[DebuggerDisplay("{" + nameof(DebugValue) + "}")]
[ParameterComparer(typeof(ByValueParameterComparer))]
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor, SerializationConstructor]
public partial class Choice<T, TAlt>(
    bool hasValue,
    T? valueOrDefault,
    TAlt? alternativeOrDefault
    ) : Maybe<T>(hasValue, valueOrDefault), IEquatable<Choice<T, TAlt>>
{
    [DataMember(Order = 2), MemoryPackOrder(2), Key(2)]
    public TAlt? AlternativeOrDefault { get; } = alternativeOrDefault;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public TAlt Alternative {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get { AssertHasAlternative(); return AlternativeOrDefault!; }
    }

    public override string ToString()
        => IsValue(out var value, out var alt)
            ? $"Choice.Value({value})"
            : $"Choice.Alternative({alt})";

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Deconstruct(out bool hasValue, out T? valueOrDefault, out TAlt? alternativeOrDefault)
    {
        hasValue = HasValue;
        valueOrDefault = ValueOrDefault;
        alternativeOrDefault = AlternativeOrDefault;
    }

    // Useful methods

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsValue([MaybeNullWhen(false)] out T value, [MaybeNullWhen(true)] out TAlt alternative)
    {
        value = ValueOrDefault!;
        alternative = AlternativeOrDefault!;
        return HasValue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAlternative([MaybeNullWhen(false)] out TAlt alternative)
    {
        alternative = AlternativeOrDefault!;
        return !HasValue;
    }

    // Equality

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Choice<T, TAlt>? other)
    {
        if (ReferenceEquals(other, null))
            return false;

        return HasValue
            ? other.HasValue && EqualityComparer<T>.Default.Equals(ValueOrDefault!, other.ValueOrDefault!)
            : !other.HasValue && EqualityComparer<TAlt>.Default.Equals(AlternativeOrDefault!, other.AlternativeOrDefault!);
    }

    public override bool Equals(object? obj)
        => obj is Choice<T, TAlt> other && Equals(other);
    public override int GetHashCode()
        => HasValue
            ? ValueOrDefault?.GetHashCode() ?? 0
            : AlternativeOrDefault?.GetHashCode() ?? 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Choice<T, TAlt>? left, Choice<T, TAlt>? right)
    {
        if (ReferenceEquals(left, right))
            return true;

        if (left is null || right is null)
            return false;

        return left.Equals(right);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Choice<T, TAlt>? left, Choice<T, TAlt>? right) => !(left == right);

    // Operators

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Choice<T, TAlt>(T source)
        => new(true, source, default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Choice<T, TAlt>((bool HasValue, T Value, TAlt Alternative) source)
        => new(source.HasValue, source.Value, source.Alternative);

    // Private helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AssertHasAlternative()
    {
        if (HasValue)
            throw StandardError.Constraint($"{GetType().GetName()} is expected to have Alternative here.");
    }
}
