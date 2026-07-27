namespace ActualChat.Contacts;

/// <summary>
/// Service for managing external contacts imported from devices.
/// </summary>
public interface IExternalContacts : IComputeService
{
    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<ExternalContact[]> List(Session session, Symbol deviceId, CancellationToken cancellationToken);
    [CommandHandler]
    Task<Result<ExternalContactFull?>[]> OnBulkChange(ExternalContacts_BulkChange command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ExternalContacts_BulkChange(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] ExternalContactChange[] Changes
) : ISessionCommand<Result<ExternalContactFull?>[]>, IApiCommand
{
    public const int MaxChangeCount = 1_000;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record ExternalContactChange(
    [property: DataMember, MemoryPackOrder(1), Key(0)] ExternalContactId Id,
    [property: DataMember, MemoryPackOrder(2), Key(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(3), Key(2)] Change<ExternalContactFull> Change
);
