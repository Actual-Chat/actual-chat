using MemoryPack;
using Microsoft.Toolkit.HighPerformance;

namespace ActualChat;

#pragma warning disable CA1721 // HashCode is confusing w/ GetHashCode

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public readonly partial record struct HashedString(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] int HashCode,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] string Value)
{
    // Conversion

    public static implicit operator HashedString(string value) => new(value.GetDjb2HashCode(), value);

    public bool Equals(HashedString other)
        => HashCode == other.HashCode && OrdinalEquals(Value, other.Value);

    public override int GetHashCode()
        => HashCode;
}
