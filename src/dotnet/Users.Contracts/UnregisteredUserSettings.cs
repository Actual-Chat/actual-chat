namespace ActualChat.Users;

[DataContract]
public sealed record UnregisteredUserSettings
{
    internal const string KvasKey = nameof(UnregisteredUserSettings);

    // ChatId -> AuthorId map
    [DataMember] public ImmutableDictionary<string, string> ChatAuthors { get; init; } = ImmutableDictionary<string, string>.Empty;

    public UnregisteredUserSettings WithChat(string chatId, string authorId)
        => this with { ChatAuthors = ChatAuthors.SetItem(chatId, authorId) };
    public UnregisteredUserSettings WithoutChat(string chatId)
        => this with { ChatAuthors = ChatAuthors.Remove(chatId) };
}
