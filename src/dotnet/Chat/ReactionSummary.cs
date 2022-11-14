using Stl.Versioning;

namespace ActualChat.Chat;

public record ReactionSummary : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    [DataMember] public Symbol Id { get; init; } = "";
    [DataMember] public long Version { get; init; }
    [DataMember] public Symbol EntryId { get; init; } = "";
    [DataMember] public string Emoji { get; init; } = "";
    [DataMember] public long Count { get; init; }
    public ImmutableList<Symbol> FirstAuthorIds { get; init; } = ImmutableList<Symbol>.Empty;

    public ReactionSummary Increase()
        => this with { Count = Count + 1 };
    public ReactionSummary Decrease()
    {
        var result = this with { Count = Count - 1 };
        if (result.Count < 0)
            throw StandardError.Constraint("Summary cannot have negative reactions count");
        return result;
    }

    public ReactionSummary AddAuthor(Symbol authorId)
        => FirstAuthorIds.Count < Constants.Chat.ReactionFirstAuthorIdsLimit
            ? this with { FirstAuthorIds = FirstAuthorIds.Add(authorId) }
            : this;

    public ReactionSummary RemoveAuthor(Symbol authorId)
        => this with { FirstAuthorIds = FirstAuthorIds.Remove(authorId) };
}
