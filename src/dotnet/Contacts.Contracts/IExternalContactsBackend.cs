using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Contacts;

public interface IExternalContactsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ExternalContactFull?> Get(ExternalContactId externalContactId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ExternalContact[]> List(UserDeviceId userDeviceId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<string> GetDisplayNameFor(UserId ownerId, UserId peerUserId, CancellationToken cancellationToken);
    [CommandHandler]
    Task<Result<ExternalContactFull?>[]> OnBulkChange(ExternalContactsBackend_BulkChange command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRemoveAccount(ExternalContactsBackend_RemoveAccount command, CancellationToken cancellationToken);
    // Not compute method!
    Task<ApiSet<ExternalContactId>> ListReferencingContactIds(UserId userId, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ExternalContactsBackend_BulkChange(
    [property: DataMember, MemoryPackOrder(0)] ExternalContactChange[] Changes
) : ICommand<Result<ExternalContactFull?>[]>, IBackendCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ExternalContactsBackend_Touch(
    [property: DataMember, MemoryPackOrder(0)] ExternalContactId Id
) : ICommand<Unit>, IBackendCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ExternalContactsBackend_RemoveAccount(
    [property: DataMember, MemoryPackOrder(0)] UserId UserId
) : ICommand<Unit>, IBackendCommand;
