namespace ActualChat.Chat;

/// <summary>
/// Service for sharing and observing authors' live locations within a chat.
/// </summary>
public interface ILiveLocations : IComputeService
{
    [ComputeMethod]
    Task<LiveLocation?> Get(Session session, ChatId chatId, AuthorId authorId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<LiveLocation>> List(Session session, ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<bool> IsSharing(Session session, ChatId chatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnStart(LiveLocations_Start command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnUpdate(LiveLocations_Update command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnStop(LiveLocations_Stop command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record LiveLocations_Start(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] GeoPoint Point,
    [property: DataMember, MemoryPackOrder(3), Key(3)] TimeSpan Duration
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record LiveLocations_Update(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] GeoPoint Point
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record LiveLocations_Stop(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] ChatId ChatId
) : ISessionCommand<Unit>, IApiCommand;
