using RestEase;

namespace ActualChat.Users.Client;

// All backend clients & controllers are unused for now

[BasePath("sessionOptionsBackend")]
public interface ISessionOptionsBackendClientDef
{
    [Post(nameof(Upsert))]
    Task Upsert([Body] ISessionOptionsBackend.UpsertCommand command, CancellationToken cancellationToken);
}

