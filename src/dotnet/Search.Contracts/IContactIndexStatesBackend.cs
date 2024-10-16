using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Search;

public interface IContactIndexStatesBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ContactIndexState> GetForUsers(CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ContactIndexState> GetForPlaceAuthors(CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ContactIndexState> GetForChats(CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ContactIndexState> GetForPlaces(CancellationToken cancellationToken);

    [CommandHandler]
    Task<ContactIndexState> OnChange(
        ContactIndexStatesBackend_Change command,
        CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ContactIndexStatesBackend_Change(
    [property: DataMember, MemoryPackOrder(0)] Symbol Id,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2)] Change<ContactIndexState> Change
) : ICommand<ContactIndexState>, IBackendCommand, IHasShardKey<Symbol>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public Symbol ShardKey => Id;
}
