using Microsoft.Extensions.Hosting;

namespace ActualChat.Streaming;

internal class VideoBackendWarmup(IServiceProvider services) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Resolve ShardOwner to start shard ownership acquisition.
        // Then wait for all VideoBackend shards to settle (owned by this node
        // or mapped to another node). This takes ~1s due to LockToUseDelay.
        var shardOwner = services.ShardOwner(ShardScheme.VideoBackend);
        var tasks = Enumerable.Range(0, ShardScheme.VideoBackend.ShardCount)
            .Select(i => shardOwner.States[i].Value
                .When(s => s.OwnershipStatus != ShardOwnershipStatus.MappedToThisNode, cancellationToken)
            ).ToList();
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
