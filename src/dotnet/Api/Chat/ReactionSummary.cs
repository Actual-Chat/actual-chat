using ActualLab.Fusion.Blazor;
using MemoryPack;
using ActualLab.Versioning;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record ReactionSummary : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(0)] public Symbol Id { get; init; } = "";
    [DataMember, MemoryPackOrder(1)] public long Version { get; init; }
    [DataMember, MemoryPackOrder(2)] public required TextEntryId EntryId { get; init; }
    [DataMember, MemoryPackOrder(3)] public Symbol EmojiId { get; init; }
    [DataMember, MemoryPackOrder(4)] public long Count { get; init; }

    // Set on read; ImmutableList is justified here, see Add/RemoveAuthor
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

    // This record relies on referential equality
    public bool Equals(ReactionSummary? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

}
