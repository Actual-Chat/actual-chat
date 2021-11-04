using RestEase;

namespace ActualChat.Users.Client;
// BasePath == /api/
[BasePath("session")]
public interface ISessionInfoServiceDef
{
    [Post(nameof(Update))]
    Task Update([Body] ISessionInfoService.UpsertCommand command, CancellationToken cancellationToken);
}

// unused for now
[BasePath("userInfo")]
public interface IUserInfoClientDef
{
    [Get(nameof(Get))]
    Task<UserInfo?> Get(UserId userId, CancellationToken cancellationToken);
    [Get(nameof(GetByName))]
    Task<UserInfo?> GetByName(UserId name, CancellationToken cancellationToken);
}

[BasePath("userState")]
public interface IUserStateClientDef
{
    [Get(nameof(IsOnline))]
    Task<bool> IsOnline(UserId userId, CancellationToken cancellationToken);
}
