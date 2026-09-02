using ActualChat.Mesh;
using ActualChat.Sharding;
using ActualLab.Rpc;

namespace ActualChat.App.Server;

/// <summary>
/// Hands this node's shards over and closes its inbound RPC connections while the host still
/// listens, so clients and peer nodes move to live replicas instead of waiting for a lease to expire.
/// </summary>
internal sealed class GracefulShutdown(IServiceProvider services) : IHostedLifecycleService
{
    private static readonly TimeSpan HandoverTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DisconnectTimeout = TimeSpan.FromSeconds(3);

    private IServiceProvider Services { get; } = services;
    private MeshWatcher MeshWatcher => field ??= Services.MeshWatcher();
    private ShardOwners ShardOwners => field ??= Services.GetRequiredService<ShardOwners>();
    private RpcHub RpcHub => field ??= Services.RpcHub();
    private ILogger Log => field ??= Services.LogFor(GetType());

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StoppingAsync(CancellationToken cancellationToken)
    {
        // ApplicationStopping has already fired, so MeshWatcher and ShardOwners are releasing their
        // leases. The peers stay open until that's done: a client's next call may still reach a shard
        // this node owns. Kestrel stops listening only after this method returns.
        var startedAt = CpuTimestamp.Now;
        var handoverTask = Task.WhenAll(MeshWatcher.Stop(), ShardOwners.Stop());
        await handoverTask.WaitAsync(HandoverTimeout, cancellationToken).SilentAwait(false);
        Log.LogInformation("Mesh handover {Outcome} in {Elapsed}",
            handoverTask.IsCompleted ? "completed" : "timed out", startedAt.Elapsed.ToShortString());

        var peers = RpcHub.InternalServices.Peers.Values.OfType<RpcServerPeer>().ToList();
        var disconnectTask = Task.WhenAll(peers.Select(peer => peer.DisposeAsync().AsTask()));
        await disconnectTask.WaitAsync(DisconnectTimeout, cancellationToken).SilentAwait(false);
        Log.LogInformation("Closing {PeerCount} inbound RPC connection(s) {Outcome} in {Elapsed}",
            peers.Count, disconnectTask.IsCompleted ? "completed" : "timed out", startedAt.Elapsed.ToShortString());
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
