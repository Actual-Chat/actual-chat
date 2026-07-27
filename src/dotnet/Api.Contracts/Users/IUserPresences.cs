namespace ActualChat.Users;

/// <summary>
/// Service for tracking and querying user online presence.
/// </summary>
public interface IUserPresences : IComputeService
{
    // Consolidated here, not on GetLastCheckIn: a check-in moves the timestamp every time, but rarely
    // the presence - and GetLastCheckIn's own readers want each new timestamp.
    [ComputeMethod(MinCacheDuration = 30, ConsolidationDelay = 0.5)]
    Task<Presence> Get(UserId userId, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 30)]
    Task<ApiNullable8<Moment>> GetLastCheckIn(UserId userId, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnCheckIn(UserPresences_CheckIn command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record UserPresences_CheckIn(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] bool IsActive
) : ISessionCommand<Unit>, IApiCommand;
