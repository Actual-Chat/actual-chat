using ActualChat.Users;
using RestEase;

namespace ActualChat.Chat;

[BasePath("chats")]
public interface IChatsClientDef
{
    [Get(nameof(Get))]
    Task<Chat?> Get(Session session, ChatId chatId, CancellationToken cancellationToken);

    [Get(nameof(GetRules))]
    Task<AuthorRules> GetRules(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken);

    [Get(nameof(GetNews))]
    Task<ChatNews> GetNews(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken);

    [Get(nameof(GetIdRange))]
    Task<Range<long>> GetIdRange(
        Session session,
        ChatId chatId,
        ChatEntryKind entryKind,
        CancellationToken cancellationToken);

    [Get(nameof(GetEntryCount))]
    Task<long> GetEntryCount(
        Session session,
        ChatId chatId,
        ChatEntryKind entryKind,
        Range<long>? idTileRange,
        CancellationToken cancellationToken);

    [Get(nameof(GetTile))]
    Task<ChatTile> GetTile(
        Session session,
        ChatId chatId,
        ChatEntryKind entryKind,
        Range<long> idTileRange,
        CancellationToken cancellationToken);

    [Get(nameof(ListMentionableAuthors))]
    Task<ImmutableArray<Author>> ListMentionableAuthors(Session session, ChatId chatId, CancellationToken cancellationToken);

    [Get(nameof(FindNext))]
    Task<ChatEntry?> FindNext(Session session, ChatId chatId, long? startEntryId, string text, CancellationToken cancellationToken);

    [Post(nameof(Change))]
    Task<Chat> Change([Body] Chats_Change command, CancellationToken cancellationToken);

    [Post(nameof(UpsertTextEntry))]
    Task<ChatEntry> UpsertTextEntry([Body] Chats_UpsertTextEntry command, CancellationToken cancellationToken);

    [Post(nameof(RemoveTextEntry))]
    Task RemoveTextEntry([Body] Chats_RemoveTextEntry command, CancellationToken cancellationToken);

    [Post(nameof(GetOrCreateFromTemplate))]
    Task<Chat> GetOrCreateFromTemplate([Body] Chats_GetOrCreateFromTemplate command, CancellationToken cancellationToken);
}

[BasePath("authors")]
public interface IAuthorsClientDef
{
    [Get(nameof(Get))]
    Task<Author?> Get(Session session, ChatId chatId, AuthorId authorId, CancellationToken cancellationToken);
    [Get(nameof(GetOwn))]
    Task<AuthorFull?> GetOwn(Session session, ChatId chatId, CancellationToken cancellationToken);
    [Get(nameof(GetFull))]
    Task<AuthorFull?> GetFull(Session session, ChatId chatId, AuthorId authorId, CancellationToken cancellationToken);
    [Get(nameof(GetAccount))]
    Task<Account?> GetAccount(Session session, ChatId chatId, AuthorId authorId, CancellationToken cancellationToken);
    [Get(nameof(GetPresence))]
    Task<Presence> GetPresence(Session session, ChatId chatId, AuthorId authorId, CancellationToken cancellationToken);

    [Get(nameof(ListAuthorIds))]
    Task<ImmutableArray<AuthorId>> ListAuthorIds(Session session, ChatId chatId, CancellationToken cancellationToken);
    [Get(nameof(ListUserIds))]
    Task<ImmutableArray<UserId>> ListUserIds(Session session, ChatId chatId, CancellationToken cancellationToken);

    [Post(nameof(Join))]
    Task<AuthorFull> Join([Body] Authors_Join command, CancellationToken cancellationToken);
    [Post(nameof(Leave))]
    Task Leave([Body] Authors_Leave command, CancellationToken cancellationToken);
    [Post(nameof(Invite))]
    Task Invite([Body] Authors_Invite command, CancellationToken cancellationToken);
    [Post(nameof(SetAvatar))]
    Task SetAvatar([Body] Authors_SetAvatar command, CancellationToken cancellationToken);
}

[BasePath("roles")]
public interface IRolesClientDef
{
    [Get(nameof(Get))]
    Task<Role?> Get(Session session, ChatId chatId, RoleId roleId, CancellationToken cancellationToken);

    [Get(nameof(List))]
    Task<ImmutableArray<Role>> List(Session session, ChatId chatId, CancellationToken cancellationToken);
    [Get(nameof(ListAuthorIds))]
    Task<ImmutableArray<AuthorId>> ListAuthorIds(Session session, ChatId chatId, RoleId roleId, CancellationToken cancellationToken);
    [Get(nameof(ListOwnerIds))]
    Task<ImmutableArray<AuthorId>> ListOwnerIds(Session session, ChatId chatId, CancellationToken cancellationToken);

    [Post(nameof(Change))]
    Task<Role> Change([Body] Roles_Change command, CancellationToken cancellationToken);
}

[BasePath("mentions")]
public interface IMentionsClientDef
{
    [Get(nameof(GetLastOwn))]
    Task<Mention?> GetLastOwn(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken);
}

[BasePath("reactions")]
public interface IReactionsClientDef
{
    [Get(nameof(Get))]
    Task<Reaction?> Get(Session session, TextEntryId entryId, CancellationToken cancellationToken);

    [Get(nameof(ListSummaries))]
    Task<ImmutableArray<ReactionSummary>> ListSummaries(
        Session session,
        TextEntryId entryId,
        CancellationToken cancellationToken);

    [Post(nameof(React))]
    Task React([Body] Reactions_React command, CancellationToken cancellationToken);
}
