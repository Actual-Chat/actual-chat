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

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ExternalContactHashes_Change(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] Symbol DeviceId,
    [property: DataMember, Key(2)] long? ExpectedVersion,
    [property: DataMember, Key(3)] Change<ExternalContactsHash> Change
) : ISessionCommand<ExternalContactsHash?>, IApiCommand;
