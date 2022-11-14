namespace ActualChat.Chat;

// TODO(FC): remove this model since it should not be used from client side
public record Reaction : IHasId<Symbol>, IRequirementTarget
{
    [DataMember] public Symbol Id { get; init; }
    [DataMember] public long Version { get; init; }
    [DataMember] public Symbol AuthorId { get; init; }
    [DataMember] public ChatEntryId EntryId { get; init; }
    [DataMember] public string Emoji { get; init; } = "";
    [DataMember] public Moment ModifiedAt { get; init; }
}
