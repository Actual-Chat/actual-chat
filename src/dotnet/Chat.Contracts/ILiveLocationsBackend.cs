using ActualLab.Rpc;

namespace ActualChat.Chat;

/// <summary>
/// Backend service for authors' live location shares within a chat.
/// </summary>
public interface ILiveLocationsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<LiveLocation?> Get(ChatId chatId, AuthorId authorId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<LiveLocation>> List(ChatId chatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnStart(LiveLocationsBackend_Start command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnUpdate(LiveLocationsBackend_Update command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnStop(LiveLocationsBackend_Stop command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record LiveLocationsBackend_Start(
    [property: DataMember, MemoryPackOrder(0), Key(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1), Key(1)] AuthorId AuthorId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] GeoPoint Point,
    [property: DataMember, MemoryPackOrder(3), Key(3)] TimeSpan Duration
) : ICommand<Unit>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChatId ShardKey => ChatId;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record LiveLocationsBackend_Update(
    [property: DataMember, MemoryPackOrder(0), Key(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1), Key(1)] AuthorId AuthorId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] GeoPoint Point
) : ICommand<Unit>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChatId ShardKey => ChatId;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record LiveLocationsBackend_Stop(
    [property: DataMember, MemoryPackOrder(0), Key(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1), Key(1)] AuthorId AuthorId
) : ICommand<Unit>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChatId ShardKey => ChatId;
}
