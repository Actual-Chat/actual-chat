using MemoryPack;

namespace ActualChat.Contacts;

public interface IExternalContacts : IComputeService
{
    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<ExternalContact[]> List(Session session, Symbol deviceId, CancellationToken cancellationToken);
    [CommandHandler]
    Task<Result<ExternalContactFull?>[]> OnBulkChange(ExternalContacts_BulkChange command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ExternalContacts_BulkChange(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] ExternalContactChange[] Changes
) : ISessionCommand<Result<ExternalContactFull?>[]>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record ExternalContactChange(
    [property: DataMember, MemoryPackOrder(1)] ExternalContactId Id,
    [property: DataMember, MemoryPackOrder(2)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(3)] Change<ExternalContactFull> Change
);
