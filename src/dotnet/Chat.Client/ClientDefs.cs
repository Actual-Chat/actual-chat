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

    [Get(nameof(GetPermissions))]
    Task<ChatPermissions> GetPermissions(
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

    [Get(nameof(CanSendUserPeerChatMessage))]
    Task<bool> CanSendUserPeerChatMessage(Session session, string chatAuthorId, CancellationToken cancellationToken);

    [Get(nameof(GetUserPeerChatId))]
    Task<string?> GetUserPeerChatId(Session session, string chatAuthorId, CancellationToken cancellationToken);

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
    [Get(nameof(GetChatAuthor))]
    Task<ChatAuthor?> GetChatAuthor(Session session, string chatId, CancellationToken cancellationToken);
    [Get(nameof(GetChatPrincipalId))]
    Task<string> GetChatPrincipalId(Session session, string chatId, CancellationToken cancellationToken);
    [Get(nameof(GetAuthor))]
    Task<Author?> GetAuthor(string chatId, string authorId, bool inherit, CancellationToken cancellationToken);
    [Get(nameof(GetAuthorPresence))]
    Task<Presence> GetAuthorPresence(string chatId, string authorId, CancellationToken cancellationToken);
    [Get(nameof(GetChatIds))]
    Task<string[]> GetChatIds(Session session, CancellationToken cancellationToken);
    [Get(nameof(CanAddToContacts))]
    Task<bool> CanAddToContacts(Session session, string chatAuthorId, CancellationToken cancellationToken);
    [Post(nameof(AddToContacts))]
    Task<UserContact> AddToContacts([Body] IChatAuthors.AddToContactsCommand command, CancellationToken cancellationToken);
}

[BasePath("chatUserSettings")]
public interface IChatUserSettingsClientDef
{
    [Get(nameof(Get))]
    Task<ChatUserSettings?> Get(Session session, string chatId, CancellationToken cancellationToken);
    [Post(nameof(Set))]
    Task Set([Body] IChatUserSettings.SetCommand command, CancellationToken cancellationToken);
}

[BasePath("userAuthors")]
public interface IUserAuthorsClientDef
{
    [Get(nameof(Get))]
    Task<UserAuthor?> Get(string userId, bool inherit, CancellationToken cancellationToken);
}

[BasePath("inviteCodes")]
public interface IInviteCodesClientDef
{
    [Get(nameof(Get))]
    Task<ImmutableArray<InviteCode>> Get(Session session, string chatId, CancellationToken cancellationToken);

    [Post(nameof(Generate))]
    Task<InviteCode> Generate([Body] IInviteCodes.GenerateCommand command, CancellationToken cancellationToken);

    [Post(nameof(UseInviteCode))]
    Task<InviteCodeUseResult> UseInviteCode([Body] IInviteCodes.UseInviteCodeCommand command, CancellationToken cancellationToken);
}

[BasePath("userContacts")]
public interface IUserContactsClientDef
{
    [Get(nameof(GetContacts))]
    Task<ImmutableArray<UserContact>> GetContacts(Session session, CancellationToken cancellationToken);
}
