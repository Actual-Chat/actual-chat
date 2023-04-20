using Stl.Versioning;

namespace ActualChat.Chat;

// TODO(FC): remove this model since it should not be used from client side
[DataContract]
public sealed record Reaction : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    [DataMember] public Symbol Id { get; init; }
    [DataMember] public long Version { get; init; }
    [DataMember] public AuthorId AuthorId { get; init; }
    [DataMember] public TextEntryId EntryId { get; init; }
    [DataMember] public Symbol EmojiId { get; init; }
    [DataMember] public Moment ModifiedAt { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public Emoji Emoji => Emoji.Get(EmojiId);
}
