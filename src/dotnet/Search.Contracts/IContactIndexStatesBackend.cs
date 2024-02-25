using MemoryPack;

namespace ActualChat.Search;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ContactIndexStatesBackend_Change(
    [property: DataMember, MemoryPackOrder(0)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(1)] Change<ContactIndexState> Change
) : ICommand<ContactIndexState>, IBackendCommand;

public interface IContactIndexStatesBackend : IComputeService
{
    [ComputeMethod]
    Task<ContactIndexState> GetForUsers(CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ContactIndexState> GetForChats(CancellationToken cancellationToken);

    [CommandHandler]
    Task<ContactIndexState> OnChange(
        ContactIndexStatesBackend_Change command,
        CancellationToken cancellationToken);
}
