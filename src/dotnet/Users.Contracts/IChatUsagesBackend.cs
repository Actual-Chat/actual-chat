using ActualLab.Rpc;
using MemoryPack;

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
    Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken);
}

/// <summary>
/// Command to record a user's chat access.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatUsagesBackend_RegisterUsage(
    [property: DataMember, MemoryPackOrder(0)] UserId UserId,
    [property: DataMember, MemoryPackOrder(1)] ChatUsageListKind Kind,
    [property: DataMember, MemoryPackOrder(2)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(3)] DateTime? AccessTime
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public UserId ShardKey => UserId;
}

/// <summary>
/// Command to trim the recency list to a maximum size.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatUsagesBackend_PurgeRecencyList(
    [property: DataMember, MemoryPackOrder(0)] UserId UserId,
    [property: DataMember, MemoryPackOrder(1)] ChatUsageListKind Kind,
    [property: DataMember, MemoryPackOrder(2)] int Size
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public UserId ShardKey => UserId;
}
