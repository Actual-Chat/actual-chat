using ActualChat.MLSearch.ApiAdapters;


namespace ActualChat.MLSearch.Indexing;


internal class ChatIndexerWorker(
    IDataIndexer<ChatId> dataIndexer,
    ILoggerSource loggerSource
) : IChatIndexerWorker
{
    private const int ChannelCapacity = 10;
    private ILogger? _log;
    private ILogger Log => _log ??= loggerSource.GetLogger(GetType());

    private readonly Channel<MLSearch_TriggerChatIndexing> _channel =
        Channel.CreateBounded<MLSearch_TriggerChatIndexing>(new BoundedChannelOptions(ChannelCapacity){
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });

    public ChannelWriter<MLSearch_TriggerChatIndexing> Trigger => _channel.Writer;

    public async Task ExecuteAsync(int _shardIndex, CancellationToken cancellationToken)
    {
        // This method is a single unit of work.
        // As far as I understood there's an embedded assumption made
        // that it is possible to rehash shards attached to the host
        // between OnRun method executions.
        //
        // We calculate stream cursor each call to prevent
        // issues in case of re-sharding or new cluster rollouts.
        // It might have some other concurrent worker has updated
        // a cursor. TLDR: prevent stale cursor data.
        while (!cancellationToken.IsCancellationRequested) {
            try {
                var e = await _channel
                    .Reader
                    .ReadAsync(cancellationToken)
                    .ConfigureAwait(false);
                var result = await dataIndexer.IndexNextAsync(e.Id, cancellationToken).ConfigureAwait(false);
                if (!result.IsEndReached) {
                    // Enqueue event to continue indexing.
                    if (!Trigger.TryWrite(new MLSearch_TriggerChatIndexing(e.Id))) {
                        Log.LogWarning("Event queue is full: We can't process till this indexing is fully complete.");
                        while (!result.IsEndReached) {
                            result = await dataIndexer.IndexNextAsync(e.Id, cancellationToken).ConfigureAwait(false);
                        }
                        Log.LogWarning("Event queue is full: exiting an element.");
                    }
                }
            }
            catch (Exception e){
                Log.LogError(e.Message);
                throw;
            }
        }
    }
}
