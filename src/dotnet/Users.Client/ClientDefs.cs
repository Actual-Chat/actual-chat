using ActualChat.Kvas;
using RestEase;

namespace ActualChat.Users.Client;

[BasePath("accounts")]
public interface IAccountsClientDef
{
    [Get(nameof(GetOwn))]
    Task<AccountFull?> GetOwn(Session session, CancellationToken cancellationToken);
    [Get(nameof(Get))]
    Task<Account?> Get(Session session, string userId, CancellationToken cancellationToken);
    [Get(nameof(GetFull))]
    Task<AccountFull?> GetFull(Session session, string userId, CancellationToken cancellationToken);
    [Post(nameof(Update))]
    Task Update([Body] IAccounts.UpdateCommand command, CancellationToken cancellationToken);
}

[BasePath("userPresences")]
public interface IUserPresencesClientDef
{
    [Get(nameof(Get))]
    Task<Presence> Get(string userId, CancellationToken cancellationToken);
}

[BasePath("avatars")]
public interface IAvatarsClientDef
{
    [Get(nameof(GetOwn))]
    Task<AvatarFull?> GetOwn(Session session, string avatarId, CancellationToken cancellationToken);
    [Get(nameof(Get))]
    Task<Avatar?> Get(Session session, string avatarId, CancellationToken cancellationToken);
    [Get(nameof(ListOwnAvatarIds))]
    Task<ImmutableArray<Symbol>> ListOwnAvatarIds(Session session, CancellationToken cancellationToken);

    [Post(nameof(Change))]
    Task<AvatarFull> Change([Body] IAvatars.ChangeCommand command, CancellationToken cancellationToken);
    [Post(nameof(SetDefault))]
    Task SetDefault([Body] IAvatars.SetDefaultCommand command, CancellationToken cancellationToken);
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

[BasePath("contacts")]
public interface IContactsClientDef
{
    [Get(nameof(Get))]
    Task<Contact?> Get(Session session, string contactId, CancellationToken cancellationToken);
    [Get(nameof(List))]
    Task<ImmutableArray<Contact>> List(Session session, CancellationToken cancellationToken);
    [Post(nameof(Change))]
    Task<Contact> Change([Body] IContacts.ChangeCommand command, CancellationToken cancellationToken);
}
