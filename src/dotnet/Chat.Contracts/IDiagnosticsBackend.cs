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

    // Shard routing/invalidation probes: both return the NodeRef of the executing host.
    // The compute one is never invalidated explicitly - only via the shard ownership
    // dependency, so a stale result signals a frozen computed (see ShardRoutingMonitor).
    [ComputeMethod]
    Task<string> GetShardHostId(int shardKey, CancellationToken cancellationToken);
    Task<string> GetShardHostIdDirect(int shardKey, CancellationToken cancellationToken);
}
