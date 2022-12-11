using Stl.Versioning;

namespace ActualChat.Chat;

[DataContract]
public record ReactionSummary : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    [DataMember] public Symbol Id { get; init; } = "";
    [DataMember] public long Version { get; init; }
    [DataMember] public ChatEntryId EntryId { get; init; }
    [DataMember] public Symbol EmojiId { get; init; }
    [DataMember] public long Count { get; init; }

    // Set on reads
    public ImmutableList<AuthorId> FirstAuthorIds { get; init; } = ImmutableList<AuthorId>.Empty;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
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
