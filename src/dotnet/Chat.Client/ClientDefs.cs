using ActualChat.Users;
using RestEase;

namespace ActualChat.Chat.Client;

[BasePath("chats")]
public interface IChatsClientDef
{
    [Get(nameof(Get))]
    Task<Chat?> Get(Session session, string chatId, CancellationToken cancellationToken);

    [Get(nameof(List))]
    Task<ImmutableArray<Chat>> List(Session session, CancellationToken cancellationToken);

    [Get(nameof(GetRules))]
    Task<ChatAuthorRules> GetRules(
        Session session,
        string chatId,
        CancellationToken cancellationToken);

    [Get(nameof(GetSummary))]
    Task<ChatSummary?> GetSummary(
        Session session,
        string chatId,
        CancellationToken cancellationToken);

    [Get(nameof(GetIdRange))]
    Task<Range<long>> GetIdRange(
        Session session,
        string chatId,
        ChatEntryType entryType,
        CancellationToken cancellationToken);

    [Get(nameof(GetEntryCount))]
    Task<long> GetEntryCount(
        Session session,
        string chatId,
        ChatEntryType entryType,
        Range<long>? idTileRange,
        CancellationToken cancellationToken);

    [Get(nameof(GetTile))]
    Task<ChatTile> GetTile(
        Session session,
        string chatId,
        ChatEntryType entryType,
        Range<long> idTileRange,
        CancellationToken cancellationToken);

    [Get(nameof(CanJoin))]
    Task<bool> CanJoin(
        Session session,
        string chatId,
        CancellationToken cancellationToken);

    [Get(nameof(CanPeerChat))]
    Task<bool> CanPeerChat(Session session, string chatId, string authorId, CancellationToken cancellationToken);
    [Get(nameof(GetPeerChatId))]
    Task<string?> GetPeerChatId(Session session, string chatId, string authorId, CancellationToken cancellationToken);
    [Get(nameof(GetPeerChatContact))]
    Task<Contact?> GetPeerChatContact(Session session, string chatId, CancellationToken cancellationToken);

    [Get(nameof(ListMentionableAuthors))]
    Task<ImmutableArray<ChatAuthor>> ListMentionableAuthors(Session session, string chatId, CancellationToken cancellationToken);
    [Get(nameof(FindNext))]
    Task<ChatEntry?> FindNext(Session session, string chatId, long? startEntryId, string text, CancellationToken cancellationToken);

    [Post(nameof(Change))]
    Task<Chat> Change([Body] IChats.ChangeCommand command, CancellationToken cancellationToken);
    [Post(nameof(Join))]
    Task<Unit> Join([Body] IChats.JoinCommand command, CancellationToken cancellationToken);
    [Post(nameof(Leave))]
    Task Leave([Body] IChats.LeaveCommand command, CancellationToken cancellationToken);

    [Post(nameof(UpsertTextEntry))]
    Task<ChatEntry> UpsertTextEntry([Body] IChats.UpsertTextEntryCommand command, CancellationToken cancellationToken);
    [Post(nameof(RemoveTextEntry))]
    Task RemoveTextEntry([Body] IChats.RemoveTextEntryCommand command, CancellationToken cancellationToken);
}

[BasePath("chatAuthors")]
public interface IChatAuthorsClientDef
{
    [Get(nameof(Get))]
    Task<ChatAuthor?> Get(Session session, string chatId, string authorId, CancellationToken cancellationToken);
    [Get(nameof(GetOwn))]
    Task<ChatAuthor?> GetOwn(Session session, string chatId, CancellationToken cancellationToken);
    [Get(nameof(GetFull))]
    Task<ChatAuthorFull?> GetFull(Session session, string chatId, string authorId, CancellationToken cancellationToken);
    [Get(nameof(ListChatIds))]
    Task<ImmutableArray<Symbol>> ListChatIds(Session session, CancellationToken cancellationToken);
    [Get(nameof(ListAuthorIds))]
    Task<ImmutableArray<Symbol>> ListAuthorIds(Session session, string chatId, CancellationToken cancellationToken);
    [Get(nameof(ListUserIds))]
    Task<ImmutableArray<Symbol>> ListUserIds(Session session, string chatId, CancellationToken cancellationToken);

    [Get(nameof(GetAuthor))]
    Task<ChatAuthor?> GetAuthor(Session session, string chatId, string authorId, CancellationToken cancellationToken);
    [Get(nameof(GetAuthorPresence))]
    Task<Presence> GetAuthorPresence(Session session, string chatId, string authorId, CancellationToken cancellationToken);
    [Get(nameof(CanAddToContacts))]
    Task<bool> CanAddToContacts(Session session, string chatId, string authorId, CancellationToken cancellationToken);

    [Post(nameof(AddToContacts))]
    Task AddToContacts([Body] IChatAuthors.AddToContactsCommand command, CancellationToken cancellationToken);
    [Post(nameof(CreateChatAuthors))]
    Task CreateChatAuthors([Body] IChatAuthors.CreateChatAuthorsCommand command, CancellationToken cancellationToken);
    [Post(nameof(SetAvatar))]
    Task SetAvatar([Body] IChatAuthors.SetAvatarCommand command, CancellationToken cancellationToken);
}

[BasePath("chatRoles")]
public interface IChatRolesClientDef
{
    [Get(nameof(Get))]
    Task<ChatRole?> Get(Session session, string chatId, string roleId, CancellationToken cancellationToken);

    [Get(nameof(List))]
    Task<ImmutableArray<ChatRole>> List(Session session, string chatId, CancellationToken cancellationToken);
    [Get(nameof(ListAuthorIds))]
    Task<ImmutableArray<Symbol>> ListAuthorIds(Session session, string chatId, string roleId, CancellationToken cancellationToken);

    [Post(nameof(Change))]
    Task<ChatRole> Change([Body] IChatRoles.ChangeCommand command, CancellationToken cancellationToken);
}

[BasePath("mentions")]
public interface IMentionsClientDef
{
    [Get(nameof(GetLast))]
    Task<Mention?> GetLast(
        Session session,
        Symbol chatId,
        CancellationToken cancellationToken);
}

[BasePath("reactions")]
public interface IReactionsClientDef
{
    [Get(nameof(List))]
    Task<ImmutableArray<ReactionSummary>> List(
        Session session,
        Symbol chatEntryId,
        CancellationToken cancellationToken);

    [Post(nameof(React))]
    Task React([Body] IReactions.ReactCommand command, CancellationToken cancellationToken);
}
