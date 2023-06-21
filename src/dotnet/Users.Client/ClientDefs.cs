using ActualChat.Hosting;
using ActualChat.Kvas;
using RestEase;

namespace ActualChat.Users;

[BasePath("systemProperties")]
public interface ISystemPropertiesClientDef
{
    [Get(nameof(GetTime))]
    Task<double> GetTime(CancellationToken cancellationToken);
    [Get(nameof(GetMinClientVersion))]
    Task<string?> GetMinClientVersion(AppKind appKind, CancellationToken cancellationToken);
}

[BasePath("accounts")]
public interface IAccountsClientDef
{
    [Get(nameof(GetOwn))]
    Task<AccountFull> GetOwn(Session session, CancellationToken cancellationToken);
    [Get(nameof(Get))]
    Task<Account?> Get(Session session, UserId userId, CancellationToken cancellationToken);
    [Get(nameof(GetFull))]
    Task<AccountFull?> GetFull(Session session, UserId userId, CancellationToken cancellationToken);
    [Post(nameof(Update))]
    Task Update([Body] Accounts_Update command, CancellationToken cancellationToken);
    [Post(nameof(InvalidateEverything))]
    Task InvalidateEverything([Body] Accounts_InvalidateEverything command, CancellationToken cancellationToken);
}

[BasePath("userPresences")]
public interface IUserPresencesClientDef
{
    [Get(nameof(Get))]
    Task<Presence> Get(UserId userId, CancellationToken cancellationToken);
    [Post(nameof(CheckIn))]
    Task CheckIn([Body] UserPresences_CheckIn command, CancellationToken cancellationToken);
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
    Task<AvatarFull> Change([Body] Avatars_Change command, CancellationToken cancellationToken);
    [Post(nameof(SetDefault))]
    Task SetDefault([Body] Avatars_SetDefault command, CancellationToken cancellationToken);
}

[BasePath("chatPositions")]
public interface IChatPositionsClientDef
{
    [Get(nameof(GetOwn))]
    public Task<ChatPosition> GetOwn(Session session, ChatId chatId, ChatPositionKind kind, CancellationToken cancellationToken);

    [Post(nameof(Set))]
    public Task Set([Body] ChatPositions_Set command, CancellationToken cancellationToken);
}

[BasePath("serverKvas")]
public interface IServerKvasClientDef
{
    [Get(nameof(Get))]
    Task<Option<string>> Get(Session session, string key, CancellationToken cancellationToken = default);

    [Post(nameof(Set))]
    Task Set([Body] ServerKvas_Set command, CancellationToken cancellationToken = default);
    [Post(nameof(SetMany))]
    Task SetMany([Body] ServerKvas_SetMany command, CancellationToken cancellationToken = default);
    [Post(nameof(MigrateGuestKeys))]
    Task MigrateGuestKeys([Body] ServerKvas_MigrateGuestKeys command, CancellationToken cancellationToken = default);
}
