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

[BasePath("chatReadPositions")]
public interface IChatReadPositionsClientDef
{
    [Get(nameof(GetReadPosition))]
    public Task<long?> GetReadPosition(Session session, string chatId, CancellationToken cancellationToken);

    [Post(nameof(UpdateReadPosition))]
    public Task UpdateReadPosition(
        [Body] IChatReadPositions.UpdateReadPositionCommand command,
        CancellationToken cancellationToken);
}
