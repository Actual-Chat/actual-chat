using RestEase;

namespace ActualChat.Users.Client;

[BasePath("userInfos")]
public interface IUserInfosClientDef
{
    [Get(nameof(Get))]
    Task<UserInfo?> Get(string userId, bool inherit, CancellationToken cancellationToken);
    [Get(nameof(GetByName))]
    Task<UserInfo?> GetByName(string name, CancellationToken cancellationToken);
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

