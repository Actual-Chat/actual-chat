using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.MLSearch;

public static class ChatIndexInitializerShardKey
{
    public static string Value => nameof(ChatIndexInitializerShardKey);
}

public interface IChatIndexInitializerTrigger: IComputeService, IBackendService
{
    [CommandHandler]
    Task OnContinuation(MLSearch_SignalChatIndexingContinuation e, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnCompletion(MLSearch_SignalChatIndexingCompletion e, CancellationToken cancellationToken);
}

/// <summary>
/// This command carries chat init completion event to the active shard
/// of the chat index initializer.
/// </summary>
/// <param name="Id">Identifier of a chat where initialization is completed.</param>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record MLSearch_SignalChatIndexingCompletion(
    [property: DataMember, MemoryPackOrder(0)] ChatId Id
) : IBackendCommand, IHasId<ChatId>, IHasShardKey<string>, ICommand<Unit>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string ShardKey => ChatIndexInitializerShardKey.Value;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record MLSearch_SignalChatIndexingContinuation(
    [property: DataMember, MemoryPackOrder(0)] ChatId Id
) : IBackendCommand, IHasId<ChatId>, IHasShardKey<string>, ICommand<Unit>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string ShardKey => ChatIndexInitializerShardKey.Value;
}
