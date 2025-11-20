using ActualLab.Rpc;
namespace ActualChat.Rpc;

public sealed class MeshRpcPeerRef : RpcPeerRef
{
    private readonly CancellationTokenSource? _routeChangedSource;

    public readonly ResolvedMeshRef Target;
    public readonly int Version;

    internal MeshRpcPeerRef(ResolvedMeshRef target, int version)
    {
        Target = target;
        Version = version;
        IsBackend = true;
        ConnectionKind = target.ConnectionKind;
        HostInfo = $"{target.ToString()}-v{version.Format()}";
        UseReferentialEquality = true;
        _routeChangedSource = ConnectionKind is RpcPeerConnectionKind.None ? null : new();
        var routeChangedToken = _routeChangedSource?.Token ?? CancellationToken.None;
        RouteState = routeChangedToken.CanBeCanceled ? new RpcRouteState(routeChangedToken) : null;
        Initialize();
    }

    // Private and internal methods

    internal void MarkRerouted()
        => _routeChangedSource.CancelAndDisposeSilently();
}
