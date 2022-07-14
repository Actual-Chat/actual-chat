using RestEase;

namespace ActualChat.Users.Client;

// All backend clients & controllers are unused for now

[BasePath("accountsBackend")]
public interface IAccountsBackendClientDef
{
    [Get(nameof(Get))]
    Task<Account?> Get(string userId, CancellationToken cancellationToken);
    [Get(nameof(GetUserAuthor))]
    Task<UserAuthor?> GetUserAuthor(string userId, CancellationToken cancellationToken);
    [Post(nameof(Update))]
    Task Update([Body] IAccountsBackend.UpdateCommand command, CancellationToken cancellationToken);
}

[BasePath("sessionOptionsBackend")]
public interface ISessionOptionsBackendClientDef
{
    [Post(nameof(Upsert))]
    Task Upsert([Body] ISessionOptionsBackend.UpsertCommand command, CancellationToken cancellationToken);
}

