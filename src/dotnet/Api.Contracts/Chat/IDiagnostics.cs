namespace ActualChat.Chat;

/// <summary>
/// Service for retrieving server mesh diagnostic information.
/// </summary>
public interface IDiagnostics : IComputeService
{
    [ComputeMethod]
    Task<MeshDiagInfo> GetMeshDiagInfo(Session session, string tag, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record MeshDiagInfo(
    [property: DataMember, MemoryPackOrder(0)] string ThisNodeId,
    [property: DataMember, MemoryPackOrder(1)] string Tag,
    [property: DataMember, MemoryPackOrder(2)] Moment Timestamp,
    [property: DataMember, MemoryPackOrder(3)] NodeDiagInfo[] Nodes,
    [property: DataMember, MemoryPackOrder(4)] RpcPeerDiagInfo[] RpcPeers,
    [property: DataMember, MemoryPackOrder(5)] MeshRpcPeerRefDiagInfo[] MeshRpcPeerRefs,
    [property: DataMember, MemoryPackOrder(6)] MeshDiagInfo[] Others,
    [property: DataMember, MemoryPackOrder(7)] string Extra);

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record NodeDiagInfo(
    [property: DataMember, MemoryPackOrder(0)] string Id,
    [property: DataMember, MemoryPackOrder(1)] string Endpoint,
    [property: DataMember, MemoryPackOrder(2)] string State,
    [property: DataMember, MemoryPackOrder(3)] bool IsThis,
    [property: DataMember, MemoryPackOrder(5)] string Roles,
    [property: DataMember, MemoryPackOrder(6)] string Extra);

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record RpcPeerDiagInfo(
    [property: DataMember, MemoryPackOrder(0)] string Id,
    [property: DataMember, MemoryPackOrder(1)] string Peer,
    [property: DataMember, MemoryPackOrder(2)] string ConnectionKind,
    [property: DataMember, MemoryPackOrder(3)] bool IsConnected,
    [property: DataMember, MemoryPackOrder(4)] string ConnectionInfo,
    [property: DataMember, MemoryPackOrder(5)] string Extra);

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record MeshRpcPeerRefDiagInfo(
    [property: DataMember, MemoryPackOrder(0)] string MeshRef,
    [property: DataMember, MemoryPackOrder(1)] string PeerRef,
    [property: DataMember, MemoryPackOrder(2)] string Address,
    [property: DataMember, MemoryPackOrder(3)] string NodeId,
    [property: DataMember, MemoryPackOrder(4)] int Version,
    [property: DataMember, MemoryPackOrder(5)] string Extra);
