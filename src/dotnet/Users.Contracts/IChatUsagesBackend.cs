using ActualLab.Rpc;

namespace ActualChat.Users;

/// <summary>
/// Backend service for tracking user chat access patterns and recency lists.
/// </summary>
public interface IChatUsagesBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ChatId[]> GetRecencyList(UserId userId, ChatUsageListKind kind, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task OnRegisterUsage(ChatUsagesBackend_RegisterUsage command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnPurgeRecencyList(ChatUsagesBackend_PurgeRecencyList command, CancellationToken cancellationToken);

    // Events

    [EventHandler]
    Task OnChatEntryChangedEvent(ChatEntryChangedEvent eventCommand, CancellationToken cancellationToken);
}

/// <summary>
/// Command to record a user's chat access.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatUsagesBackend_RegisterUsage(
    [property: DataMember, Key(0)] UserId UserId,
    [property: DataMember, Key(1)] ChatUsageListKind Kind,
    [property: DataMember, Key(2)] ChatId ChatId,
    [property: DataMember, Key(3)] DateTime? AccessTime
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UserId ShardKey => UserId;
}

/// <summary>
/// Command to trim the recency list to a maximum size.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatUsagesBackend_PurgeRecencyList(
    [property: DataMember, Key(0)] UserId UserId,
    [property: DataMember, Key(1)] ChatUsageListKind Kind,
    [property: DataMember, Key(2)] int Size
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UserId ShardKey => UserId;
}
