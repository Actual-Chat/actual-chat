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

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ExternalContacts_BulkChange : ApiCommand<Result<ExternalContactFull?>[]>
{
    [DataMember(Order = 2), Key(2)] public required ExternalContactChange[] Changes { get; init; }
    public const int MaxChangeCount = 1_000;
    // The change count alone doesn't bound the batch - each contact carries names and hash sets
    public const int MaxNameLength = 256;
    public const int MaxHashCount = 64;
    public const int MaxHashLength = 64;
}

[DataContract, MessagePackObject]
public sealed partial record ExternalContactChange(
    [property: DataMember, Key(0)] ExternalContactId Id,
    [property: DataMember, Key(1)] long? ExpectedVersion,
    [property: DataMember, Key(2)] Change<ExternalContactFull> Change
);
