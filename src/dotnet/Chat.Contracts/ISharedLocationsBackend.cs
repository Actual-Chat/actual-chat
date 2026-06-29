using ActualLab.Rpc;

namespace ActualChat.Chat;

/// <summary>
/// Backend service for locations shared into a chat (live or frozen one-shot).
/// </summary>
public interface ISharedLocationsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<SharedLocation?> Get(ChatId chatId, SharedLocationId id, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<SharedLocation>> ListLive(ChatId chatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnReport(SharedLocationsBackend_Report command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnStop(SharedLocationsBackend_Stop command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record SharedLocationsBackend_Report(
    [property: DataMember, MemoryPackOrder(0), Key(0)] SharedLocationId Id,
    [property: DataMember, MemoryPackOrder(1), Key(1)] AuthorId AuthorId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] GeoPoint Point,
    [property: DataMember, MemoryPackOrder(3), Key(3)] TimeSpan LiveDuration
) : ICommand<Unit>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChatId ShardKey => AuthorId.ChatId;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record SharedLocationsBackend_Stop(
    [property: DataMember, MemoryPackOrder(0), Key(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1), Key(1)] SharedLocationId Id
) : ICommand<Unit>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChatId ShardKey => ChatId;
}
