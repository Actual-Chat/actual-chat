using ActualChat.Attributes;
using ActualLab.Rpc;

namespace ActualChat.Users;

[BackendService(nameof(HostRole.Api), ServiceMode.Local)]
public interface IAppUpdatesBackend : IComputeService, IBackendService
{
    [ComputeMethod(ConsolidationDelay = 0)]
    Task<AppUpdateInfo?> GetLatestUpdateInfo(AppKind appKind, CancellationToken cancellationToken);
}
