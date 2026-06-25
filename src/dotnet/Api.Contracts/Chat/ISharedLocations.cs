namespace ActualChat.Chat;

/// <summary>
/// Service for observing and updating locations shared into a chat (live or frozen one-shot).
/// </summary>
public interface ISharedLocations : IComputeService
{
    [ComputeMethod]
    Task<SharedLocation?> Get(Session session, ChatId chatId, SharedLocationId id, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<SharedLocation>> List(Session session, ChatId chatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnReport(SharedLocations_Report command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnStop(SharedLocations_Stop command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record SharedLocations_Report(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] SharedLocationId Id,
    [property: DataMember, MemoryPackOrder(3), Key(3)] GeoPoint Point
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record SharedLocations_Stop(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] SharedLocationId Id
) : ISessionCommand<Unit>, IApiCommand;
