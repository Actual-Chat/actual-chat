using MemoryPack;

namespace ActualChat.Users;

public interface IUserPresences : IComputeService
{
    [ComputeMethod(MinCacheDuration = 30)]
    Task<Presence> Get(UserId userId, CancellationToken cancellationToken);

    [CommandHandler]
    public Task OnCheckIn(UserPresences_CheckIn command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record UserPresences_CheckIn(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] bool IsActive
) : ISessionCommand<Unit>;
