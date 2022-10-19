namespace ActualChat.Chat;

public record ReactionSummary
{
    [DataMember] public string Id { get; init; } = "";
    [DataMember] public string ChatEntryId { get; init; } = "";
    [DataMember] public string Emoji { get; init; } = "";
    [DataMember] public long Count { get; init; }
    [DataMember] public long Version { get; init; }
    public ImmutableList<string> FirstAuthorIds { get; init; } = ImmutableList<string>.Empty;

    public ReactionSummary Increase()
        => this with { Count = Count + 1 };
    public ReactionSummary Decrease()
    {
        var result = this with { Count = Count - 1 };
        if (result.Count < 0)
            throw StandardError.Constraint("Summary cannot have negative reactions count");
        return result;
    }

    public ReactionSummary AddAuthor(string authorId)
        => FirstAuthorIds.Count < Constants.Chat.ReactionFirstAuthorIdsLimit
            ? this with { FirstAuthorIds = FirstAuthorIds.Add(authorId) }
            : this;

    public ReactionSummary RemoveAuthor(string authorId)
        => this with { FirstAuthorIds = FirstAuthorIds.Remove(authorId, StringComparer.Ordinal) };
}
