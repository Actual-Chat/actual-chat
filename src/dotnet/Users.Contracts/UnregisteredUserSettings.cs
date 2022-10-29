namespace ActualChat.Users;

[DataContract]
public sealed record UnregisteredUserSettings
{
    public const string KvasKey = nameof(UnregisteredUserSettings);

    // ChatId -> AuthorId map
    [DataMember] public ImmutableDictionary<string, string> Chats { get; init; } = ImmutableDictionary<string, string>.Empty;

    public UnregisteredUserSettings WithChat(string chatId, string authorId)
        => this with { Chats = Chats.SetItem(chatId, authorId) };
    public UnregisteredUserSettings WithoutChat(string chatId)
        => this with { Chats = Chats.Remove(chatId) };
}
