using MemoryPack;
using Stl.Versioning;

namespace ActualChat.Chat;

// TODO(FC): remove this model since it should not be used from client side
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record Reaction : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(0)] public Symbol Id { get; init; }
    [DataMember, MemoryPackOrder(1)] public long Version { get; init; }
    [DataMember, MemoryPackOrder(2)] public AuthorId AuthorId { get; init; }
    [DataMember, MemoryPackOrder(3)] public TextEntryId EntryId { get; init; }
    [DataMember, MemoryPackOrder(4)] public Symbol EmojiId { get; init; }
    [DataMember, MemoryPackOrder(5)] public Moment ModifiedAt { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public Emoji Emoji => Emoji.Get(EmojiId);
}
