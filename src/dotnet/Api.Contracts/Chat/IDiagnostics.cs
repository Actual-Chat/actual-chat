using ActualChat.Flows;
using ActualLab.Rpc.Infrastructure;

namespace ActualChat.Chat;

/// <summary>
/// Service for retrieving server mesh diagnostic information.
/// </summary>
public interface IDiagnostics : IComputeService
{
    [ComputeMethod]
    Task<MeshDiagInfo> GetMeshDiagInfo(Session session, string tag, CancellationToken cancellationToken);

    // Regular RPC methods - the Flows dashboard polls these (no Fusion invalidation).
    Task<FlowTypeStat[]> GetFlowStats(Session session, CancellationToken cancellationToken);
    Task<FlowSummary[]> GetFlows(Session session, FlowsQuery query, CancellationToken cancellationToken);
    // Computed: TryGetData invalidates on every flow store, so an expanded row's console log
    // updates live as the flow runs.
    [ComputeMethod]
    Task<FlowDetails?> GetFlowDetails(Session session, string flowId, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record MeshDiagInfo(
    [property: DataMember, MemoryPackOrder(0), Key(0)] string ThisNodeId,
    [property: DataMember, MemoryPackOrder(1), Key(1)] string Tag,
    [property: DataMember, MemoryPackOrder(2), Key(2)] Moment Timestamp,
    [property: DataMember, MemoryPackOrder(3), Key(3)] NodeDiagInfo[] Nodes,
    [property: DataMember, MemoryPackOrder(4), Key(4)] RpcPeerDiagInfo[] RpcPeers,
    [property: DataMember, MemoryPackOrder(5), Key(5)] MeshRpcPeerRefDiagInfo[] MeshRpcPeerRefs,
    [property: DataMember, MemoryPackOrder(6), Key(6)] MeshDiagInfo[] Others,
    [property: DataMember, MemoryPackOrder(7), Key(7)] string Extra);

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record NodeDiagInfo(
    [property: DataMember, MemoryPackOrder(0), Key(0)] string Id,
    [property: DataMember, MemoryPackOrder(1), Key(1)] string Endpoint,
    [property: DataMember, MemoryPackOrder(2), Key(2)] string State,
    [property: DataMember, MemoryPackOrder(3), Key(3)] bool IsThis,
    [property: DataMember, MemoryPackOrder(4), Key(4)] string Roles,
    [property: DataMember, MemoryPackOrder(5), Key(5)] string Extra);

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record RpcPeerDiagInfo(
    [property: DataMember, MemoryPackOrder(0), Key(0)] string Id,
    [property: DataMember, MemoryPackOrder(1), Key(1)] string Peer,
    [property: DataMember, MemoryPackOrder(2), Key(2)] string ConnectionKind,
    [property: DataMember, MemoryPackOrder(3), Key(3)] RpcPeerConnectionStateKind ConnectionStateKind,
    [property: DataMember, MemoryPackOrder(4), Key(4)] string ConnectionInfo,
    [property: DataMember, MemoryPackOrder(5), Key(5)] string Extra);

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record MeshRpcPeerRefDiagInfo(
    [property: DataMember, MemoryPackOrder(0), Key(0)] string MeshRef,
    [property: DataMember, MemoryPackOrder(1), Key(1)] string PeerRef,
    [property: DataMember, MemoryPackOrder(2), Key(2)] string Address,
    [property: DataMember, MemoryPackOrder(3), Key(3)] string NodeId,
    [property: DataMember, MemoryPackOrder(4), Key(4)] int Version,
    [property: DataMember, MemoryPackOrder(5), Key(5)] string Extra);
