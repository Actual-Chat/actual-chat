using ActualChat.MLSearch.ApiAdapters.ShardWorker;

namespace ActualChat.MLSearch.Indexing;

internal interface IChatIndexInitializer
{
    ValueTask PostAsync(MLSearch_TriggerChatIndexingCompletion job, CancellationToken cancellationToken);
}

internal sealed class ChatIndexInitializer(
    IServiceProvider services,
    ShardScheme shardScheme,
    IShardIndexResolver<string> shardIndexResolver,
    IChatIndexInitializerShard chatIndexInitializerShard,
    ILogger<ChatIndexInitializer> log
) : ShardWorker(services, shardScheme, nameof(ChatIndexInitializer)), IChatIndexInitializer
{
    private record DummyEvent : IHasShardKey<string>
    {
        public string ShardKey => ChatIndexInitializerShardKey.Value;
    }

    private bool _isHostingActiveShard;
    private int? _activeShardIndex;
    private int ActiveShardIndex => _activeShardIndex ??= shardIndexResolver.Resolve(new DummyEvent(), ShardScheme);

    public async ValueTask PostAsync(MLSearch_TriggerChatIndexingCompletion evt, CancellationToken cancellationToken)
    {
        if (!_isHostingActiveShard) {
            throw StandardError.NotFound<ChatIndexInitializerShard>(
                $"There is no active {nameof(ChatIndexInitializerShard)} at this node.");
        }
        await chatIndexInitializerShard.PostAsync(evt, cancellationToken).ConfigureAwait(false);
    }

    protected override async Task OnRun(int shardIndex, CancellationToken cancellationToken)
    {
        var isActiveShard = shardIndex == ActiveShardIndex;
        _isHostingActiveShard |= isActiveShard;
        if (isActiveShard) {
            log.LogInformation("Activating {IndexInitializer} at shard #{ShardIndex}", typeof(ChatIndexInitializer), shardIndex);
            await chatIndexInitializerShard.UseAsync(cancellationToken).ConfigureAwait(false);
        }
        else {
            // Lets wait for never ending task till cancellation
            // TODO: instead of this use a dedicated shard scheme with just a single shard
            // to process MLSearch_TriggerChatIndexingCompletion events
            var tcs = new TaskCompletionSource();
            await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
