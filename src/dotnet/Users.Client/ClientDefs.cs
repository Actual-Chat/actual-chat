using ActualChat.Kvas;
using RestEase;

namespace ActualChat.Users.Client;

[BasePath("accounts")]
public interface IAccountsClientDef
{
    [Get(nameof(Get))]
    Task<Account?> Get(Session session, CancellationToken cancellationToken);
    [Get(nameof(GetByUserId))]
    Task<Account?> GetByUserId(Session session, string userId, CancellationToken cancellationToken);
    [Get(nameof(GetUserAuthor))]
    Task<UserAuthor?> GetUserAuthor(string userId, CancellationToken cancellationToken);
    [Post(nameof(Update))]
    Task Update([Body] IAccounts.UpdateCommand command, CancellationToken cancellationToken);
}

[BasePath("userPresences")]
public interface IUserPresencesClientDef
{
    [Get(nameof(Get))]
    Task<Presence> Get(string userId, CancellationToken cancellationToken);
}

[BasePath("userAvatars")]
public interface IUserAvatarsClientDef
{
    [Get(nameof(Get))]
    Task<UserAvatar?> Get(Session session, string avatarId, CancellationToken cancellationToken);
    [Get(nameof(GetDefaultAvatarId))]
    Task<Symbol> GetDefaultAvatarId(Session session, CancellationToken cancellationToken);
    [Get(nameof(ListAvatarIds))]
    Task<ImmutableArray<Symbol>> ListAvatarIds(Session session, CancellationToken cancellationToken);

    [Post(nameof(Create))]
    Task<UserAvatar> Create([Body] IUserAvatars.CreateCommand command, CancellationToken cancellationToken);
    [Post(nameof(Update))]
    Task Update([Body] IUserAvatars.UpdateCommand command, CancellationToken cancellationToken);
    [Post(nameof(SetDefault))]
    Task SetDefault([Body] IUserAvatars.SetDefaultCommand command, CancellationToken cancellationToken);
}

[BasePath("recentEntries")]
public interface IRecentEntriesClientDef
{
    [Get(nameof(List))]
    Task<ImmutableArray<RecentEntry>> List(
        Session session,
        RecencyScope scope,
        int limit,
        CancellationToken cancellationToken);
    [Post(nameof(Update))]
    Task<RecentEntry?> Update([Body] IRecentEntries.UpdateCommand command, CancellationToken cancellationToken);
}

[BasePath("chatReadPositions")]
public interface IChatReadPositionsClientDef
{
    [Get(nameof(Get))]
    public Task<long?> Get(Session session, string chatId, CancellationToken cancellationToken);

    [Post(nameof(Set))]
    public Task Set([Body] IChatReadPositions.SetReadPositionCommand command, CancellationToken cancellationToken);
}

[BasePath("serverKvas")]
public interface IServerKvasClientDef
{
    [Get(nameof(Get))]
    Task<Option<string>> Get(Session session, string key, CancellationToken cancellationToken = default);

    [Post(nameof(Set))]
    Task Set([Body] IServerKvas.SetCommand command, CancellationToken cancellationToken = default);
    [Post(nameof(SetMany))]
    Task SetMany([Body] IServerKvas.SetManyCommand command, CancellationToken cancellationToken = default);
    [Post(nameof(MoveSessionKeys))]
    Task MoveSessionKeys([Body] IServerKvas.MoveSessionKeysCommand command, CancellationToken cancellationToken = default);
}

[BasePath("userContacts")]
public interface IUserContactsClientDef
{
    [Get(nameof(Get))]
    Task<UserContact?> Get(Session session, string contactId, CancellationToken cancellationToken);
    [Get(nameof(List))]
    Task<ImmutableArray<UserContact>> List(Session session, CancellationToken cancellationToken);
    [Post(nameof(Change))]
    Task<UserContact?> Change([Body] IUserContacts.ChangeCommand command, CancellationToken cancellationToken);
}
