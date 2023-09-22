using MemoryPack;
using Stl.Versioning;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ReactionSummary : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(0)] public Symbol Id { get; init; } = "";
    [DataMember, MemoryPackOrder(1)] public long Version { get; init; }
    [DataMember, MemoryPackOrder(2)] public TextEntryId EntryId { get; init; }
    [DataMember, MemoryPackOrder(3)] public Symbol EmojiId { get; init; }
    [DataMember, MemoryPackOrder(4)] public long Count { get; init; }

    // Set on reads
    [DataMember, MemoryPackOrder(5)]
    public ImmutableList<AuthorId> FirstAuthorIds { get; init; } = ImmutableList<AuthorId>.Empty;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public Emoji Emoji => Emoji.Get(EmojiId);

    public ReactionSummary IncrementCount(long diff = 1)
    {
        var result = this with { Count = Count + diff };
        if (result.Count < 0)
            throw StandardError.Constraint("Summary cannot have negative reaction count.");
        return result;
    }

    public ReactionSummary AddAuthor(AuthorId authorId)
        => FirstAuthorIds.Count < Constants.Chat.ReactionFirstAuthorIdsLimit
            ? this with { FirstAuthorIds = FirstAuthorIds.Add(authorId) }
            : this;

    public ReactionSummary RemoveAuthor(AuthorId authorId)
        => this with { FirstAuthorIds = FirstAuthorIds.Remove(authorId) };
}
