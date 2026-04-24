namespace ActualChat.Contacts;

/// <summary>
/// Service for tracking external contact sync state via hashes.
/// </summary>
public interface IExternalContactHashes : IComputeService
{
    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<ExternalContactsHash?> Get(Session session, Symbol deviceId, CancellationToken cancellationToken);
    [CommandHandler]
    Task<ExternalContactsHash?> OnChange(ExternalContactHashes_Change command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ExternalContactHashes_Change(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] Symbol DeviceId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(3), Key(3)] Change<ExternalContactsHash> Change
) : ISessionCommand<ExternalContactsHash?>, IApiCommand;
