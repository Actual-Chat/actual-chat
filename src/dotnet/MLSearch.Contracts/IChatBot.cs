using ActualChat.Chat.Events;
using ActualChat.Commands;
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
    Task OnCommand(MLSearch_TriggerContinueConversationWithBot e, CancellationToken cancellationToken);

    [EventHandler]
    Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken);
}
