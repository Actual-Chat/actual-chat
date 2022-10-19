namespace ActualChat.Chat;

public sealed record ChatAuthorSettings
{
    public const string KvasKey = nameof(ChatAuthorSettings);

    public ImmutableDictionary<string, string> ChatAuthors { get; init; } = ImmutableDictionary<string, string>.Empty;

    public ChatAuthorSettings WithChatAuthor(string chatId, string authorId)
        => this with { ChatAuthors = ChatAuthors.SetItem(chatId, authorId) };
}
