using ActualChat.Users;
using RestEase;

namespace ActualChat.Chat.Client;

[BasePath("chats")]
public interface IChatsClientDef
{
    [Get(nameof(Get))]
    Task<Chat?> Get(Session session, string chatId, CancellationToken cancellationToken);

    [Get(nameof(GetChats))]
    Task<Chat[]> GetChats(Session session, CancellationToken cancellationToken);

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

    [Get(nameof(CheckCanJoin))]
    Task<bool> CheckCanJoin(
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

    [Get(nameof(GetMentionCandidates))]
    Task<MentionCandidate[]> GetMentionCandidates(Session session, string chatId, CancellationToken cancellationToken);

    [Post(nameof(CreateChat))]
    Task<Chat> CreateChat([Body] IChats.CreateChatCommand command, CancellationToken cancellationToken);
    [Post(nameof(UpdateChat))]
    Task<Unit> UpdateChat([Body] IChats.UpdateChatCommand command, CancellationToken cancellationToken);
    [Post(nameof(JoinChat))]
    Task<Unit> JoinChat([Body] IChats.JoinChatCommand command, CancellationToken cancellationToken);
    [Post(nameof(CreateTextEntry))]
    Task<ChatEntry> CreateTextEntry([Body] IChats.CreateTextEntryCommand command, CancellationToken cancellationToken);
    [Post(nameof(RemoveTextEntry))]
    Task RemoveTextEntry([Body] IChats.RemoveTextEntryCommand command, CancellationToken cancellationToken);
}

[BasePath("chatAuthors")]
public interface IChatAuthorsClientDef
{
    [Get(nameof(GetOwnAuthor))]
    Task<ChatAuthor?> GetOwnAuthor(Session session, string chatId, CancellationToken cancellationToken);
    [Get(nameof(GetOwnPrincipalId))]
    Task<Symbol> GetOwnPrincipalId(Session session, string chatId, CancellationToken cancellationToken);
    [Get(nameof(ListOwnChatIds))]
    Task<ImmutableArray<Symbol>> ListOwnChatIds(Session session, CancellationToken cancellationToken);
    [Get(nameof(ListAuthorIds))]
    Task<ImmutableArray<Symbol>> ListAuthorIds(Session session, string chatId, CancellationToken cancellationToken);
    [Get(nameof(ListUserIds))]
    Task<ImmutableArray<Symbol>> ListUserIds(Session session, string chatId, CancellationToken cancellationToken);

    [Get(nameof(GetAuthor))]
    Task<Author?> GetAuthor(string chatId, string authorId, bool inherit, CancellationToken cancellationToken);
    [Get(nameof(GetAuthorPresence))]
    Task<Presence> GetAuthorPresence(string chatId, string authorId, CancellationToken cancellationToken);
    [Get(nameof(CanAddToContacts))]
    Task<bool> CanAddToContacts(Session session, string chatPrincipalId, CancellationToken cancellationToken);

    [Post(nameof(AddToContacts))]
    Task<UserContact> AddToContacts([Body] IChatAuthors.AddToContactsCommand command, CancellationToken cancellationToken);
    [Post(nameof(CreateChatAuthors))]
    Task CreateChatAuthors([Body] IChatAuthors.CreateChatAuthorsCommand command, CancellationToken cancellationToken);
}

[BasePath("chatRoles")]
public interface IChatRolesClientDef
{
    [Get(nameof(ListOwnRoleIds))]
    Task<ImmutableArray<Symbol>> ListOwnRoleIds(Session session, string chatId, CancellationToken cancellationToken);

    [Post(nameof(Upsert))]
    Task Upsert(IChatRoles.UpsertCommand command, CancellationToken cancellationToken);
}

[BasePath("chatUserSettings")]
public interface IChatUserSettingsClientDef
{
    [Get(nameof(Get))]
    Task<ChatUserSettings?> Get(Session session, string chatId, CancellationToken cancellationToken);

    [Post(nameof(Set))]
    Task Set([Body] IChatUserSettings.SetCommand command, CancellationToken cancellationToken);
}

[BasePath("userContacts")]
public interface IUserContactsClientDef
{
    [Get(nameof(GetAll))]
    Task<ImmutableArray<UserContact>> GetAll(Session session, CancellationToken cancellationToken);
}
