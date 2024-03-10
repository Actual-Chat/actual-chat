using ActualChat.MLSearch.ApiAdapters;


namespace ActualChat.MLSearch.Indexing;


/// <summary>
/// Note:
/// This is a temporary solution before sharded Events are fully available.
/// Its responsibility is to translate incoming events into corresponding shards.
/// </summary>
internal interface IChatIndexerWorker
{
    ChannelWriter<MLSearch_TriggerChatIndexing> Trigger { get; }
    Task ExecuteAsync(int shardIndex, CancellationToken cancellationToken);
}