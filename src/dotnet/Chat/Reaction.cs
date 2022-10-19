namespace ActualChat.Chat;

// TODO(FC): remove this model since it should not be used from client side
public record Reaction : IHasId<string>, IRequirementTarget
{
    [DataMember] public string Id { get; init; } = "";
    [DataMember] public string AuthorId { get; init; } = "";
    [DataMember] public string ChatId { get; init; } = ""; // TODO(FC): try replacing with ChatEntryId
    [DataMember] public long EntryId { get; init; }
    [DataMember] public string Emoji { get; init; } = "";

    public void Deconstruct(out string authorId, out string chatId, out long entryId, out string emoji)
    {
        authorId = AuthorId;
        chatId = ChatId;
        entryId = EntryId;
        emoji = Emoji;
    }
}
