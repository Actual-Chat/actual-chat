namespace ActualChat.Chat;

// TODO(FC): remove this model since it should not be used from client side
public record Reaction : IHasId<Symbol>, IRequirementTarget
{
    [DataMember] public Symbol Id { get; init; }
    [DataMember] public Symbol AuthorId { get; init; }
    [DataMember] public Symbol ChatId { get; init; } // TODO(FC): try replacing with ChatEntryId
    [DataMember] public long EntryId { get; init; }
    [DataMember] public string Emoji { get; init; } = "";

    public void Deconstruct(out Symbol authorId, out Symbol chatId, out long entryId, out string emoji)
    {
        authorId = AuthorId;
        chatId = ChatId;
        entryId = EntryId;
        emoji = Emoji;
    }
}
