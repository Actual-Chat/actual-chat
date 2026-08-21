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
public sealed partial record ExternalContactHashes_Change : ApiCommand<ExternalContactsHash?>
{
    [DataMember(Order = 2), Key(2)] public required Symbol DeviceId { get; init; }
    [DataMember(Order = 3), Key(3)] public required long? ExpectedVersion { get; init; }
    [DataMember(Order = 4), Key(4)] public required Change<ExternalContactsHash> Change { get; init; }
}
