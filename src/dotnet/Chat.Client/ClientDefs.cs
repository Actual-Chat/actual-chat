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

    [Get(nameof(GetIdRange))]
    Task<Range<long>> GetIdRange(
        Session session,
        string chatId,
        ChatEntryType entryType,
        CancellationToken cancellationToken);

    [Get(nameof(GetLastIdTile0))]
    Task<Range<long>> GetLastIdTile0(
        Session session,
        string chatId,
        ChatEntryType entryType,
        CancellationToken cancellationToken);

    [Get(nameof(GetLastIdTile1))]
    Task<Range<long>> GetLastIdTile1(
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

    [Get(nameof(GetRules))]
    Task<ChatAuthorRules> GetRules(
        Session session,
        string chatId,
        CancellationToken cancellationToken);

    [Get(nameof(CanJoin))]
    Task<bool> CanJoin(
        Session session,
        string chatId,
        CancellationToken cancellationToken);

    [Get(nameof(GetTextEntryAttachments))]
    Task<ImmutableArray<TextEntryAttachment>> GetTextEntryAttachments(
        Session session, string chatId, long entryId,
        CancellationToken cancellationToken);

    [Get(nameof(CanSendPeerChatMessage))]
    Task<bool> CanSendPeerChatMessage(Session session, string chatPrincipalId, CancellationToken cancellationToken);

    [Get(nameof(GetPeerChatId))]
    Task<string?> GetPeerChatId(Session session, string chatPrincipalId, CancellationToken cancellationToken);

    [Get(nameof(GetPeerChatContact))]
    Task<UserContact?> GetPeerChatContact(Session session, Symbol chatId, CancellationToken cancellationToken);

    [Get(nameof(ListMentionableAuthor))]
    Task<ImmutableArray<Author>> ListMentionableAuthor(Session session, string chatId, CancellationToken cancellationToken);

    [Post(nameof(ChangeChat))]
    Task<Chat?> ChangeChat([Body] IChats.ChangeChatCommand command, CancellationToken cancellationToken);
    [Post(nameof(JoinChat))]
    Task<Unit> JoinChat([Body] IChats.JoinChatCommand command, CancellationToken cancellationToken);
    [Post(nameof(LeaveChat))]
    Task LeaveChat([Body] IChats.LeaveChatCommand command, CancellationToken cancellationToken);

    [Post(nameof(UpsertTextEntry))]
    Task<ChatEntry> UpsertTextEntry([Body] IChats.UpsertTextEntryCommand command, CancellationToken cancellationToken);
    [Post(nameof(RemoveTextEntry))]
    Task RemoveTextEntry([Body] IChats.RemoveTextEntryCommand command, CancellationToken cancellationToken);
}

[BasePath("chatAuthors")]
public interface IChatAuthorsClientDef
{
    [Get(nameof(Get))]
    Task<ChatAuthor?> Get(Session session, string chatId, CancellationToken cancellationToken);
    [Get(nameof(GetPrincipalId))]
    Task<Symbol> GetPrincipalId(Session session, string chatId, CancellationToken cancellationToken);
    [Get(nameof(ListChatIds))]
    Task<ImmutableArray<Symbol>> ListChatIds(Session session, CancellationToken cancellationToken);
    [Get(nameof(ListAuthorIds))]
    Task<ImmutableArray<Symbol>> ListAuthorIds(Session session, string chatId, CancellationToken cancellationToken);
    [Get(nameof(ListUserIds))]
    Task<ImmutableArray<Symbol>> ListUserIds(Session session, string chatId, CancellationToken cancellationToken);

    [Get(nameof(GetAuthor))]
    Task<Author?> GetAuthor(Session session, string chatId, string authorId, bool inherit, CancellationToken cancellationToken);
    [Get(nameof(GetAuthorPresence))]
    Task<Presence> GetAuthorPresence(Session session, string chatId, string authorId, CancellationToken cancellationToken);
    [Get(nameof(CanAddToContacts))]
    Task<bool> CanAddToContacts(Session session, string chatPrincipalId, CancellationToken cancellationToken);

    [Post(nameof(AddToContacts))]
    Task AddToContacts([Body] IChatAuthors.AddToContactsCommand command, CancellationToken cancellationToken);
    [Post(nameof(CreateChatAuthors))]
    Task CreateChatAuthors([Body] IChatAuthors.CreateChatAuthorsCommand command, CancellationToken cancellationToken);
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
    Task<ChatRole?> Change([Body] IChatRoles.ChangeCommand command, CancellationToken cancellationToken);
}

[BasePath("chatUserSettings")]
public interface IChatUserSettingsClientDef
{
    [Get(nameof(Get))]
    Task<ChatUserSettings?> Get(Session session, string chatId, CancellationToken cancellationToken);

    [Post(nameof(Set))]
    Task Set([Body] IChatUserSettings.SetCommand command, CancellationToken cancellationToken);
}
