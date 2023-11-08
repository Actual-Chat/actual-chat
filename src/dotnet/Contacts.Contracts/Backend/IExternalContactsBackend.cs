using MemoryPack;

namespace ActualChat.Contacts;

public interface IExternalContactsBackend : IComputeService
{
    [ComputeMethod]
    Task<ApiArray<ExternalContact>> List(UserId ownerId, Symbol deviceId, CancellationToken cancellationToken);
    [CommandHandler]
    Task<ApiArray<Result<ExternalContact?>>> OnBulkChange(ExternalContactsBackend_BulkChange command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRemoveAccount(ExternalContactsBackend_RemoveAccount command, CancellationToken cancellationToken);
    // Not compute method!
    Task<ApiSet<UserId>> ListReferencingUserIds(UserId userId, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ExternalContactsBackend_BulkChange(
    [property: DataMember, MemoryPackOrder(0)] ApiArray<ExternalContactChange> Changes
) : ICommand<ApiArray<Result<ExternalContact?>>>, IBackendCommand;

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
