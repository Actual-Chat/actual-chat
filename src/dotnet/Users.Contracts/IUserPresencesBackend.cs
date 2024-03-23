using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Users;

public interface IUserPresencesBackend : IComputeService, IBackendService
{
    [ComputeMethod(MinCacheDuration = 30)]
    Task<Presence> Get(UserId userId, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 30)]
    Task<Moment?> GetLastCheckIn(UserId userId, CancellationToken cancellationToken);

    [CommandHandler]
    public Task OnCheckIn(UserPresencesBackend_CheckIn command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record UserPresencesBackend_CheckIn(
    [property: DataMember, MemoryPackOrder(0)] UserId UserId,
    [property: DataMember, MemoryPackOrder(1)] Moment At,
    [property: DataMember, MemoryPackOrder(2)] bool IsActive
) : ICommand<Unit>, IBackendCommand;
