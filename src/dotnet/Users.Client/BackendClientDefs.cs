using RestEase;

namespace ActualChat.Users.Client;

// All backend clients & controllers are unused for now

[BasePath("sessionOptionsBackend")]
public interface ISessionOptionsBackendClientDef
{
    [Post(nameof(Update))]
    Task Update([Body] ISessionOptionsBackend.UpdateCommand command, CancellationToken cancellationToken);
}

