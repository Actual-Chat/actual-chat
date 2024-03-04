using ActualChat.Mesh;
using ActualLab.Rpc;

namespace ActualChat.Rpc;

public sealed record RpcBackendNodePeerRef : RpcPeerRef
{
    private readonly TaskCompletionSource<MeshNode?> _whenReadySource = new();
    private readonly CancellationTokenSource _stopTokenSource = new();

    public MeshWatcher MeshWatcher { get; }
    public NodeRef NodeRef { get; }
    public Task<MeshNode?> WhenReady => _whenReadySource.Task;
    public CancellationToken StopToken { get; }

    public RpcBackendNodePeerRef(MeshWatcher meshWatcher, NodeRef nodeRef)
        : base(GetKey(nodeRef), false, true)
    {
        if (nodeRef.IsNone)
            throw new ArgumentOutOfRangeException(nameof(nodeRef));

        MeshWatcher = meshWatcher;
        NodeRef = nodeRef;
        StopToken = _stopTokenSource.Token;
        _ = Update();
    }

    public override string ToString()
        => $"backend @ {NodeRef}";

    // This record relies on referential equality
    public bool Equals(RpcBackendNodePeerRef? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

    // Private methods

    private static Symbol GetKey(NodeRef nodeRef)
        => $"@{nodeRef}";

    private async Task Update()
    {
        var meshState = MeshWatcher.State;
        try {
            if (meshState.Value.NodeByRef.TryGetValue(NodeRef, out var meshNode))
                _whenReadySource.TrySetResult(meshNode);
            while (true) {
                // Await for node registration + fail on timeout
                var c = await meshState
                    .When(x => x.NodeByRef.ContainsKey(NodeRef), CancellationToken.None)
                    .WaitAsync(MeshWatcher.NodeTimeout, CancellationToken.None) // Throws TimeoutException
                    .ConfigureAwait(false);
                _whenReadySource.TrySetResult(c.Value.NodeByRef[NodeRef]);

                // Await for node unregistration
                await meshState
                    .When(x => !x.NodeByRef.ContainsKey(NodeRef), CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        finally {
            _whenReadySource.TrySetResult(null);
            _stopTokenSource.CancelAndDisposeSilently();
        }
    }

}
