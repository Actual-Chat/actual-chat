using ActualChat.Attributes;
using ActualChat.Hosting;
using ActualLab.Rpc;

namespace ActualChat.Users;

/// <summary>
/// Backend service for tracking user online presence and last activity.
/// </summary>
[BackendService(nameof(HostRole.UsersBackend), ServiceMode.Distributed)]
public interface IUserPresencesBackend : IComputeService, IBackendService
{
    [ComputeMethod(MinCacheDuration = 30)]
    Task<Moment?> GetLastCheckIn(UserId userId, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnCheckIn(UserPresencesBackend_CheckIn command, CancellationToken cancellationToken);
}

/// <summary>
/// Command to record a user's presence check-in.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record UserPresencesBackend_CheckIn(
    [property: DataMember, MemoryPackOrder(0), Key(0)] UserId UserId,
    [property: DataMember, MemoryPackOrder(1), Key(1)] Moment At,
    [property: DataMember, MemoryPackOrder(2), Key(2)] bool IsActive
) : IDelegatingCommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public UserId ShardKey => UserId;
}
