namespace ActualChat.Users;

[DataContract]
public sealed record UnregisteredUserSettings
{
    public const string KvasKey = nameof(UnregisteredUserSettings);

    // ChatId -> AuthorId map
    [DataMember] public ImmutableDictionary<string, AuthorId> Chats { get; init; } = ImmutableDictionary<string, AuthorId>.Empty;

    public UnregisteredUserSettings WithChat(ChatId chatId, AuthorId authorId)
        => this with { Chats = Chats.SetItem(chatId, authorId) };
    public UnregisteredUserSettings WithoutChat(ChatId chatId)
        => this with { Chats = Chats.Remove(chatId) };
}
