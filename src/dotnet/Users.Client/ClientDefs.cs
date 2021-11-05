using RestEase;

namespace ActualChat.Users.Client;

[BasePath("userInfos")]
public interface IUserInfosClientDef
{
    [Get(nameof(Get))]
    Task<UserInfo?> Get(UserId userId, bool inherit, CancellationToken cancellationToken);
    [Get(nameof(GetByName))]
    Task<UserInfo?> GetByName(string name, CancellationToken cancellationToken);
}

[BasePath("userStates")]
public interface IUserStatesClientDef
{
    [Get(nameof(IsOnline))]
    Task<bool> IsOnline(UserId userId, CancellationToken cancellationToken);
}

[BasePath("userAuthors")]
public interface IUserAuthorsClientDef
{
    [Get(nameof(Get))]
    Task<UserAuthor?> Get(UserId userId, CancellationToken cancellationToken);
    [Get(nameof(GetByName))]
    Task<UserInfo?> GetByName(UserId name, CancellationToken cancellationToken);
}

