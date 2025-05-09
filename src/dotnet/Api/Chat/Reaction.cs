using ActualLab.Fusion.Blazor;
using MemoryPack;
using ActualLab.Versioning;

namespace ActualChat.Chat;

// TODO(FC): remove this model since it should not be used from client side
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record Reaction : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(0)] public Symbol Id { get; init; }
    [DataMember, MemoryPackOrder(1)] public long Version { get; init; }
    [DataMember, MemoryPackOrder(2)] public required AuthorId AuthorId { get; init; }
    [DataMember, MemoryPackOrder(3)] public required TextEntryId EntryId { get; init; }
    [DataMember, MemoryPackOrder(4)] public Symbol EmojiId { get; init; }
    [DataMember, MemoryPackOrder(5)] public Moment ModifiedAt { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    [field: AllowNull, MaybeNull]
    public Emoji Emoji => field ??= Emoji.Get(EmojiId);

    // This record relies on referential equality
    public bool Equals(Reaction? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
