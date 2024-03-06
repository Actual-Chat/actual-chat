using MemoryPack;

namespace ActualChat.MLSearch;



/// <summary>
/// This command is what passes event from an app code
/// into the correct shard.
/// </summary>
/// <param name="Id"></param>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming

public sealed partial record MLSearch_TriggerContinueConversationWithBot (
    [property: DataMember, MemoryPackOrder(0)] ChatId Id
) : IBackendCommand, IHasShardKey<ChatId>, ICommand<Unit>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => Id;
}

public interface IChatBot
{
    [CommandHandler]
    Task OnContinueConversation(MLSearch_TriggerContinueConversationWithBot e, CancellationToken cancellationToken);

}