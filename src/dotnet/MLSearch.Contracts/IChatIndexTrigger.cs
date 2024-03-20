using ActualChat.Chat.Events;
using ActualChat.Commands;
using MemoryPack;

namespace ActualChat.MLSearch;

/// <summary>
/// This command is what passes event from an app code
/// into the correct shard.
/// </summary>
/// <param name="Id">Identifier of a chat to start indexing.</param>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record MLSearch_TriggerChatIndexing(
    [property: DataMember, MemoryPackOrder(0)] ChatId Id
) : IBackendCommand, IHasId<ChatId>, IHasShardKey<ChatId>, ICommand<Unit>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => Id;
}

/// <summary>
/// This command carries cancellation request from the app code to the correct shard
/// </summary>
/// <param name="Id">Identifier of a job to be cancelled.</param>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record MLSearch_CancelChatIndexing(
    [property: DataMember, MemoryPackOrder(0)] ChatId Id
) : IBackendCommand, IHasId<ChatId>, IHasShardKey<ChatId>, ICommand<Unit>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => Id;
}

/// <summary>
/// Note:
/// This is a temporary solution before sharded Events are fully available.
/// Its responsibility is to translate incoming events into corresponding shards
/// through commands.
/// </summary>
public interface IChatIndexTrigger
{
    [EventHandler]
    Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnCommand(MLSearch_TriggerChatIndexing e, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnCommand(MLSearch_CancelChatIndexing e, CancellationToken cancellationToken);
}
