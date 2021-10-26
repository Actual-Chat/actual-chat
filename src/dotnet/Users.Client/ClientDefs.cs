using RestEase;

namespace ActualChat.Users.Client;
// BasePath == /api/
[BasePath("session")]
public interface ISessionInfoServiceDef
{
    [Post(nameof(Update))]
    Task Update([Body] ISessionInfoService.UpsertData command, CancellationToken cancellationToken);
}

[BasePath("users/author")]
public interface IDefaultAuthorDef
{
    [Get(nameof(Get))]
    Task<DefaultAuthor> Get(UserId userId, CancellationToken cancellationToken);
}

// unused for now
[BasePath("userInfo")]
public interface IUserInfoClientDef
{
    [Get(nameof(TryGet))]
    Task<UserInfo?> TryGet(UserId userId, CancellationToken cancellationToken);
    [Get(nameof(TryGetByName))]
    Task<UserInfo?> TryGetByName(UserId name, CancellationToken cancellationToken);
}

[BasePath("userState")]
public interface IUserStateClientDef
{
    [Get(nameof(IsOnline))]
    Task<bool> IsOnline(UserId userId, CancellationToken cancellationToken);
}
