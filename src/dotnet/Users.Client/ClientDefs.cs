using ActualChat.Kvas;
using RestEase;

namespace ActualChat.Users;

[BasePath("accounts")]
public interface IAccountsClientDef
{
    [Get(nameof(GetOwn))]
    Task<AccountFull?> GetOwn(Session session, CancellationToken cancellationToken);
    [Get(nameof(Get))]
    Task<Account?> Get(Session session, UserId userId, CancellationToken cancellationToken);
    [Get(nameof(GetFull))]
    Task<AccountFull?> GetFull(Session session, UserId userId, CancellationToken cancellationToken);
    [Post(nameof(Update))]
    Task Update([Body] IAccounts.UpdateCommand command, CancellationToken cancellationToken);
}

[BasePath("userPresences")]
public interface IUserPresencesClientDef
{
    [Get(nameof(Get))]
    Task<Presence> Get(UserId userId, CancellationToken cancellationToken);
}

[BasePath("avatars")]
public interface IAvatarsClientDef
{
    [Get(nameof(GetOwn))]
    Task<AvatarFull?> GetOwn(Session session, Symbol avatarId, CancellationToken cancellationToken);
    [Get(nameof(Get))]
    Task<Avatar?> Get(Session session, Symbol avatarId, CancellationToken cancellationToken);
    [Get(nameof(ListOwnAvatarIds))]
    Task<ImmutableArray<Symbol>> ListOwnAvatarIds(Session session, CancellationToken cancellationToken);

    [Post(nameof(Change))]
    Task<AvatarFull> Change([Body] IAvatars.ChangeCommand command, CancellationToken cancellationToken);
    [Post(nameof(SetDefault))]
    Task SetDefault([Body] IAvatars.SetDefaultCommand command, CancellationToken cancellationToken);
}

[BasePath("readPositions")]
public interface IReadPositionsClientDef
{
    [Get(nameof(GetOwn))]
    public Task<long?> GetOwn(Session session, ChatId chatId, CancellationToken cancellationToken);

    [Post(nameof(Set))]
    public Task Set([Body] IReadPositions.SetCommand command, CancellationToken cancellationToken);
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
