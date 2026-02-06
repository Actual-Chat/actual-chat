using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Users;

/// <summary>
/// Backend service for tracking user online presence and last activity.
/// </summary>
public interface IUserPresencesBackend : IComputeService, IBackendService
{
    [ComputeMethod(MinCacheDuration = 30)]
    Task<Presence> Get(UserId userId, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 30)]
    Task<ApiNullable8<Moment>> GetLastCheckIn(UserId userId, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnCheckIn(UserPresencesBackend_CheckIn command, CancellationToken cancellationToken);
}

/// <summary>
/// Command to record a user's presence check-in.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record UserPresencesBackend_CheckIn(
    [property: DataMember, MemoryPackOrder(0)] UserId UserId,
    [property: DataMember, MemoryPackOrder(1)] Moment At,
    [property: DataMember, MemoryPackOrder(2)] bool IsActive
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public UserId ShardKey => UserId;
}
