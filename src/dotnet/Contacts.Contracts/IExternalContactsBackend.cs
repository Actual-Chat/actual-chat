using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Contacts;

public interface IExternalContactsBackend : IComputeService, IBackendService
{
    [ComputeMethod, Obsolete("2024.04: Replaced with List")]
    Task<ApiArray<ExternalContactFull>> ListFull(UserId ownerId, Symbol deviceId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<ExternalContact>> List(UserDeviceId userDeviceId, CancellationToken cancellationToken);
    [CommandHandler]
    Task<ApiArray<Result<ExternalContactFull?>>> OnBulkChange(ExternalContactsBackend_BulkChange command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRemoveAccount(ExternalContactsBackend_RemoveAccount command, CancellationToken cancellationToken);
    // Not compute method!
    Task<ApiSet<UserId>> ListReferencingUserIds(UserId userId, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ExternalContactsBackend_BulkChange(
    [property: DataMember, MemoryPackOrder(0)] ApiArray<ExternalContactChange> Changes
) : ICommand<ApiArray<Result<ExternalContactFull?>>>, IBackendCommand;

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
