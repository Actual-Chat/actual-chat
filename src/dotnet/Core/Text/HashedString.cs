using Microsoft.Toolkit.HighPerformance;

namespace ActualChat;

[DataContract]
public readonly record struct HashedString(
    [property: DataMember(Order = 0)] int HashCode,
    [property: DataMember(Order = 0)] string Value)
{
    public HashedString(string Value) : this(Value.GetDjb2HashCode(), Value) { }

    // Conversion

    public static implicit operator HashedString(string value) => new(value);

    public bool Equals(HashedString other)
        => HashCode == other.HashCode && OrdinalEquals(Value, other.Value);

    public override int GetHashCode()
        => HashCode;
}
