using RestEase;

namespace ActualChat.Users.Client;

[BasePath("userProfiles")]
public interface IUserProfilesClientDef
{
    [Get(nameof(Get))]
    Task<UserProfile?> Get(Session session, CancellationToken cancellationToken);
}

[BasePath("userPresences")]
public interface IUserPresencesClientDef
{
    [Get(nameof(Get))]
    Task<Presence> Get(string userId, CancellationToken cancellationToken);
}

[BasePath("userAuthors")]
public interface IUserAuthorsClientDef
{
    [Get(nameof(Get))]
    Task<UserAuthor?> Get(string userId, bool inherit, CancellationToken cancellationToken);
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
