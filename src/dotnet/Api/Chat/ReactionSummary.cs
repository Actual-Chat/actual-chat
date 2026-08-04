using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Chat;

/// <summary>
/// Aggregates reaction counts and first authors for a chat entry.
/// </summary>
[DataContract, MessagePackObject]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record ReactionSummary : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, Key(0)] public required Symbol Id { get; init; }
    [DataMember, Key(1)] public long Version { get; init; }
    [DataMember, Key(2)] public required ChatEntryId EntryId { get; init; }
    [DataMember, Key(3)] public required Emoji Emoji { get; init; }
    [DataMember, Key(4)] public required long Count { get; init; }

    // Set on read; ImmutableList is justified here, see Add/RemoveAuthor
    [DataMember, Key(5)]
    public ImmutableList<AuthorId> FirstAuthorIds { get; init; } = ImmutableList<AuthorId>.Empty;

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

    // This record relies on referential equality
    public bool Equals(ReactionSummary? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

}
