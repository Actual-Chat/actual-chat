using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Chat;

// TODO(FC): remove this model since it should not be used from client side

/// <summary>
/// Represents an emoji reaction to a chat entry.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record Reaction : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(0), Key(0)] public required Symbol Id { get; init; }
    [DataMember, MemoryPackOrder(1), Key(1)] public long Version { get; init; }
    [DataMember, MemoryPackOrder(2), Key(2)] public required AuthorId AuthorId { get; init; }
    [DataMember, MemoryPackOrder(3), Key(3)] public required ChatEntryId EntryId { get; init; }
    [DataMember, MemoryPackOrder(4), Key(4)] public required Emoji Emoji { get; init; }
    [DataMember, MemoryPackOrder(5), Key(5)] public Moment ModifiedAt { get; init; }

    // This record relies on referential equality
    public bool Equals(Reaction? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
