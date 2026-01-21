using ActualChat.Attributes;
using ActualChat.Hosting;
using ActualLab.Rpc;

namespace ActualChat.Chat;

[BackendService(nameof(HostRole.DiagnosticsBackend), ServiceMode.Distributed)]
[BackendClient(nameof(HostRole.DiagnosticsBackend))]
public interface IDiagnosticsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<MeshDiagInfo> GetMeshInfo(NodeRef nodeRef, string tag, int extraLevel, CancellationToken cancellationToken);
}
