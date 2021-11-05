using RestEase;

namespace ActualChat.Users.Client;

[BasePath("userInfos")]
public interface IUserInfosClientDef
{
    [Get(nameof(Get))]
    Task<UserInfo?> Get(UserId userId, CancellationToken cancellationToken);
    [Get(nameof(GetByName))]
    Task<UserInfo?> GetByName(UserId name, CancellationToken cancellationToken);
}

[BasePath("userStates")]
public interface IUserStatesClientDef
{
    [Get(nameof(IsOnline))]
    Task<bool> IsOnline(UserId userId, CancellationToken cancellationToken);
}
