using ActualChat.Users;
using RestEase;

namespace ActualChat.Chat.Client;

[BasePath("chats")]
public interface IChatsClientDef
{
    [Get(nameof(Get))]
    Task<Chat?> Get(Session session, ChatId chatId, CancellationToken cancellationToken);

    [Get(nameof(GetIdRange))]
    Task<Range<long>> GetIdRange(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken);

    [Get(nameof(GetEntryCount))]
    Task<long> GetEntryCount(
        Session session,
        ChatId chatId,
        Range<long>? idTileRange,
        CancellationToken cancellationToken);

    [Get(nameof(GetTile))]
    Task<ChatTile> GetTile(
        Session session,
        ChatId chatId,
        Range<long> idTileRange,
        CancellationToken cancellationToken);

    [Get(nameof(GetPermissions))]
    Task<ChatPermissions> GetPermissions(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken);

    [Post(nameof(CreateChat))]
    Task<Chat> CreateChat([Body] IChats.CreateChatCommand command, CancellationToken cancellationToken);
    [Post(nameof(CreateEntry))]
    Task<ChatEntry> CreateEntry([Body] IChats.CreateEntryCommand command, CancellationToken cancellationToken);
}

[BasePath("chatAuthors")]
public interface IChatAuthorsClientDef
{
    [Get(nameof(GetSessionChatAuthor))]
    Task<ChatAuthor?> GetSessionChatAuthor(Session session, ChatId chatId, CancellationToken cancellationToken);
    [Get(nameof(GetAuthor))]
    Task<Author?> GetAuthor(ChatId chatId, AuthorId authorId, bool inherit, CancellationToken cancellationToken);
}
