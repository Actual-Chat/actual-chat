using ActualLab.Rpc;
namespace ActualChat.Rpc;

public sealed class MeshRpcPeerRef : RpcPeerRef
{
    private readonly CancellationTokenSource? _rerouteTokenSource;

    public readonly ResolvedMeshRef Target;
    public readonly int Version;
    public CancellationToken RerouteToken { get; }

    internal MeshRpcPeerRef(ResolvedMeshRef target, int version)
    {
        Target = target;
        Version = version;
        IsBackend = true;
        ConnectionKind = target.ConnectionKind;
        HostInfo = $"{target.ToString()}-v{version.Format()}";
        UseReferentialEquality = true;
        _rerouteTokenSource = ConnectionKind is RpcPeerConnectionKind.None ? null : new();
        RerouteToken = _rerouteTokenSource?.Token ?? CancellationToken.None;
        Initialize();
    }

    // Private and internal methods

    internal void MarkRerouted()
        => _rerouteTokenSource.CancelAndDisposeSilently();
}
