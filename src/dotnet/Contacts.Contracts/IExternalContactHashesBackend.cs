using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Contacts;

public interface IExternalContactHashesBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ExternalContactsHash?> Get(UserDeviceId userDeviceId, CancellationToken cancellationToken);
    [CommandHandler]
    Task<ExternalContactsHash?> OnChange(ExternalContactHashesBackend_Change command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRemoveAccount(ExternalContactHashesBackend_RemoveAccount command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ExternalContactHashesBackend_Change(
    [property: DataMember, MemoryPackOrder(0)] UserDeviceId Id,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2)] Change<ExternalContactsHash> Change
) : ICommand<ExternalContactsHash?>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public UserId ShardKey => Id.OwnerId;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ExternalContactHashesBackend_RemoveAccount(
    [property: DataMember, MemoryPackOrder(0)] UserId UserId
) : ICommand<Unit>, IBackendCommand;
