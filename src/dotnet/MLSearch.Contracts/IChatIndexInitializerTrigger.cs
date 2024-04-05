using MemoryPack;

namespace ActualChat.MLSearch;

public static class ChatIndexInitializerShardKey
{
    public static string Value => nameof(ChatIndexInitializerShardKey);
}

/// <summary>
/// This command carries chat init completion event to the active shard
/// of the chat index initializer.
/// </summary>
/// <param name="Id">Identifier of a chat where initialization is completed.</param>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record MLSearch_TriggerChatIndexingCompletion(
    [property: DataMember, MemoryPackOrder(0)] ChatId Id
) : IBackendCommand, IHasId<ChatId>, IHasShardKey<string>, ICommand<Unit>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string ShardKey => ChatIndexInitializerShardKey.Value;
}

public interface IChatIndexInitializerTrigger
{
    [CommandHandler]
    Task OnCommand(MLSearch_TriggerChatIndexingCompletion e, CancellationToken cancellationToken);
}
