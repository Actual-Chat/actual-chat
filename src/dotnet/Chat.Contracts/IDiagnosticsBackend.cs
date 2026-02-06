using ActualChat.Attributes;
using ActualChat.Hosting;
using ActualLab.Rpc;

namespace ActualChat.Chat;

/// <summary>
/// Backend service for system diagnostics and mesh health information.
/// </summary>
[BackendService(nameof(HostRole.DiagnosticsBackend), ServiceMode.Distributed)]
[BackendShardScheme(nameof(HostRole.DiagnosticsBackend))]
public interface IDiagnosticsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<MeshDiagInfo> GetMeshInfo(NodeRef nodeRef, string tag, int extraLevel, CancellationToken cancellationToken);
}
