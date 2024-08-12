using ActualLab.Rpc;

namespace ActualChat.Rpc;

public sealed record RpcMeshPeerRef : RpcPeerRef
{
    private readonly CancellationTokenSource _rerouteTokenSource;

    public readonly MeshRefTarget Target;
    public readonly int Version;
    public override CancellationToken RerouteToken { get; }

    internal RpcMeshPeerRef(MeshRefTarget target, int version)
        : base($"{target.ToString()}-v{version.Format()}", false, true)
    {
        Target = target;
        Version = version;
        _rerouteTokenSource = new();
        RerouteToken = _rerouteTokenSource.Token;
    }

    public override RpcPeerConnectionKind GetConnectionKind(RpcHub hub)
        => Target.IsLocal ? RpcPeerConnectionKind.Local : RpcPeerConnectionKind.Remote;

    public override string ToString()
        => Key;

    // This record relies on referential equality
    public bool Equals(RpcMeshPeerRef? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

    // Private and internal methods

    internal void MarkRerouted()
        => _rerouteTokenSource.CancelAndDisposeSilently();
}
