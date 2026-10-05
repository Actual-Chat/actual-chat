using ActualChat.Attributes;
using ActualLab.Rpc;

namespace ActualChat.OAuth;

[BackendService(nameof(HostRole.Api), ServiceMode.Local)]
public interface IOAuthGrantsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ApiArray<OAuthGrant>> List(UserId userId, CancellationToken cancellationToken);
    Task<OAuthClientInfo?> GetClient(string clientId, CancellationToken cancellationToken);

    Task<string> Approve(UserId userId, string clientId, ApiArray<string> scopes, CancellationToken cancellationToken);
    Task Revoke(UserId userId, string authorizationId, CancellationToken cancellationToken);
}
