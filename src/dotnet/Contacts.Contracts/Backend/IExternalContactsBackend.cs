using MemoryPack;

namespace ActualChat.Contacts;

public interface IExternalContactsBackend : IComputeService
{
    [ComputeMethod]
    Task<ApiArray<ExternalContact>> List(UserId ownerId, Symbol deviceId, CancellationToken cancellationToken);
    [CommandHandler]
    Task<ExternalContact?> OnChange(ExternalContactsBackend_Change command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRemoveAccount(ExternalContactsBackend_RemoveAccount command, CancellationToken cancellationToken);
    // Not compute method!
    Task<ApiSet<UserId>> ListReferencingUserIds(UserId userId, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ExternalContactsBackend_Change(
    [property: DataMember, MemoryPackOrder(0)] ExternalContactId Id,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2)] Change<ExternalContact> Change
) : ICommand<ExternalContact?>, IBackendCommand;

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
