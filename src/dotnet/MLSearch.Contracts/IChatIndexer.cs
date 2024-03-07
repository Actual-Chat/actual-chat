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
public sealed partial record MLSearch_TriggerChatIndexing(
    [property: DataMember, MemoryPackOrder(0)] ChatEntryId Id,
    [property: DataMember, MemoryPackOrder(1)] ChangeKind ChangeKind
) : IBackendCommand, IHasShardKey<ChatId>, ICommand<Unit>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => Id.ChatId;
}

public interface IChatIndexer
{
    [CommandHandler]
    Task OnCommand(MLSearch_TriggerChatIndexing e, CancellationToken cancellationToken);

    [EventHandler]
    Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken);
}
