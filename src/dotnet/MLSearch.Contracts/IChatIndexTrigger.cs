using ActualChat.Backend.Events;
using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.MLSearch;

public enum IndexingKind
{
    ChatContent = 0,
    ChatInfo = 1,
}

/// <summary>
/// This command carries request of indexing for the chat with the
/// specified <paramref name="ChatId"/>.
/// </summary>
/// <remarks>
/// <paramref name="ChatId"/> serves as a shard key so command delivered
/// into the correct shard.
/// </remarks>
/// <param name="ChatId">Identifier of a chat to start content indexing.</param>
/// <param name="IndexingKind">Identifyes type of content to be indexed.</param>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record MLSearch_TriggerChatIndexing(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] IndexingKind IndexingKind
) : IBackendCommand, IHasId<(ChatId, IndexingKind)>, IHasShardKey<ChatId>, ICommand<Unit>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public (ChatId, IndexingKind) Id => (ChatId, IndexingKind);

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => ChatId;
}

/// <summary>
/// This command carries cancellation request from the app code to the correct shard
/// </summary>
/// <param name="ChatId">Identifier of a job to be cancelled.</param>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record MLSearch_CancelChatIndexing(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] IndexingKind IndexingKind
) : IBackendCommand, IHasId<(ChatId, IndexingKind)>, IHasShardKey<ChatId>, ICommand<Unit>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public (ChatId, IndexingKind) Id => (ChatId, IndexingKind);

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => ChatId;
}

/// <summary>
/// Note:
/// This is a temporary solution before sharded Events are fully available.
/// Its responsibility is to translate incoming events into corresponding shards
/// through commands.
/// </summary>
public interface IChatIndexTrigger: IComputeService, IBackendService
{
    // Commands

    [CommandHandler]
    Task OnCommand(MLSearch_TriggerChatIndexing e, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnCancelCommand(MLSearch_CancelChatIndexing e, CancellationToken cancellationToken);

    // Events

    [EventHandler]
    Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnChatChangedEvent(ChatChangedEvent eventCommand, CancellationToken cancellationToken);
}
