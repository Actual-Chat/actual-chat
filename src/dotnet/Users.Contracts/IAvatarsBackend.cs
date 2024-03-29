using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Users;

public interface IAvatarsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<AvatarFull?> Get(Symbol avatarId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<AvatarFull> OnChange(AvatarsBackend_Change command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record AvatarsBackend_Change(
    [property: DataMember, MemoryPackOrder(0)] Symbol AvatarId,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2)] Change<AvatarFull> Change
) : ICommand<AvatarFull>, IBackendCommand, IHasShardKey<Symbol>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public Symbol ShardKey => AvatarId;
}
