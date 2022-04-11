using RestEase;

namespace ActualChat.Users.Client;

[BasePath("userInfos")]
public interface IUserInfosClientDef
{
    [Get(nameof(Get))]
    Task<UserInfo?> Get(string userId, bool inherit, CancellationToken cancellationToken);
    [Get(nameof(GetByName))]
    Task<UserInfo?> GetByName(string name, CancellationToken cancellationToken);
    [Get(nameof(GetGravatarHash))]
    Task<string> GetGravatarHash(string userId, CancellationToken cancellationToken);
    [Get(nameof(IsAdmin))]
    Task<bool> IsAdmin(string userId, CancellationToken cancellationToken);
}

[BasePath("userStates")]
public interface IUserStatesClientDef
{
    [Get(nameof(IsOnline))]
    Task<bool> IsOnline(string userId, CancellationToken cancellationToken);
}

[BasePath("userAuthors")]
public interface IUserAuthorsClientDef
{
    [Get(nameof(Get))]
    Task<UserAuthor?> Get(string userId, CancellationToken cancellationToken);
    [Get(nameof(GetByName))]
    Task<UserInfo?> GetByName(string name, CancellationToken cancellationToken);
}

[BasePath("userAvatars")]
public interface IUserAvatarsClientDef
{
    [Get(nameof(Get))]
    Task<UserAvatar?> Get(Session session, string avatarId, CancellationToken cancellationToken);
    [Get(nameof(GetAvatarIds))]
    Task<string[]> GetAvatarIds(Session session, CancellationToken cancellationToken);
    [Get(nameof(GetDefaultAvatarId))]
    Task<string> GetDefaultAvatarId(Session session, CancellationToken cancellationToken);
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
