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

    Task<FlowTypeStat[]> GetFlowStats(Session session, CancellationToken cancellationToken);
    Task<FlowSummary[]> GetFlows(Session session, FlowsQuery query, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<FlowDetails?> GetFlowDetails(Session session, string flowId, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
public sealed partial record MeshDiagInfo(
    [property: DataMember, Key(0)] string ThisNodeId,
    [property: DataMember, Key(1)] string Tag,
    [property: DataMember, Key(2)] Moment Timestamp,
    [property: DataMember, Key(3)] NodeDiagInfo[] Nodes,
    [property: DataMember, Key(4)] RpcPeerDiagInfo[] RpcPeers,
    [property: DataMember, Key(5)] MeshRpcRefDiagInfo[] MeshRpcRefs,
    [property: DataMember, Key(6)] MeshDiagInfo[] Others,
    [property: DataMember, Key(7)] string Extra);

[DataContract, MessagePackObject]
public sealed partial record NodeDiagInfo(
    [property: DataMember, Key(0)] string Id,
    [property: DataMember, Key(1)] string Endpoint,
    [property: DataMember, Key(2)] string State,
    [property: DataMember, Key(3)] bool IsThis,
    [property: DataMember, Key(4)] string Roles,
    [property: DataMember, Key(5)] string Extra);

[DataContract, MessagePackObject]
public sealed partial record RpcPeerDiagInfo(
    [property: DataMember, Key(0)] string Id,
    [property: DataMember, Key(1)] string Peer,
    [property: DataMember, Key(2)] string ConnectionKind,
    [property: DataMember, Key(3)] RpcPeerConnectionStateKind ConnectionStateKind,
    [property: DataMember, Key(4)] string ConnectionInfo,
    [property: DataMember, Key(5)] string Extra);

[DataContract, MessagePackObject]
public sealed partial record MeshRpcRefDiagInfo(
    [property: DataMember, Key(0)] string MeshRef,
    [property: DataMember, Key(1)] string Route,
    [property: DataMember, Key(2)] string Address,
    [property: DataMember, Key(3)] string NodeId,
    [property: DataMember, Key(4)] int Version,
    [property: DataMember, Key(5)] string Extra);
