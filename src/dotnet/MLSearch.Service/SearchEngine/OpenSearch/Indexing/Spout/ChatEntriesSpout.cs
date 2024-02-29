using MemoryPack;

namespace ActualChat.MLSearch.SearchEngine.OpenSearch.Indexing.Spout;


/// <summary>
/// This command is what passes event from an app code
/// into the correct shard.
/// </summary>
/// <param name="Id"></param>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record MLSearch_TriggerContinueChatIndexing(
    [property: DataMember, MemoryPackOrder(0)] ChatId Id
) : IBackendCommand, IHasShardKey<ChatId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => Id;
}



// Note: solution drawback
// It has a chance that the receiving counter part will fail to complete
// event handling while the event will be marked as complete.
// This means: At most once logic.
public class ChatEntriesSpout(ChannelWriter<MLSearch_TriggerContinueChatIndexing> send) : IComputeService
{
    [CommandHandler]
    // ReSharper disable once UnusedMember.Global
    public async Task OnEvent(MLSearch_TriggerContinueChatIndexing e, CancellationToken cancellationToken)
        => await send.WriteAsync(e, cancellationToken).ConfigureAwait(false);
}
