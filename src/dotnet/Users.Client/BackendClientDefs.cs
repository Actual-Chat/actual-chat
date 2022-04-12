using RestEase;

namespace ActualChat.Users.Client;

// All backend clients & controllers are unused for now

[BasePath("userProfilesBackend")]
public interface IUserProfilesBackendClientDef
{
    [Get(nameof(Get))]
    Task<UserProfile?> Get(string userId, CancellationToken cancellationToken);
    [Get(nameof(GetByName))]
    Task<UserProfile?> GetByName(string name, CancellationToken cancellationToken);
}

[BasePath("sessionOptionsBackend")]
public interface ISessionOptionsBackendClientDef
{
    [Post(nameof(Upsert))]
    Task Upsert([Body] ISessionOptionsBackend.UpsertCommand command, CancellationToken cancellationToken);
}

