using ActualLab.Rpc;

namespace ActualChat.Contacts;

/// <summary>
/// Backend service for managing external contact hash checksums for sync.
/// </summary>
public interface IExternalContactHashesBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ExternalContactsHash?> Get(UserDeviceId userDeviceId, CancellationToken cancellationToken);
    [CommandHandler]
    Task<ExternalContactsHash?> OnChange(ExternalContactHashesBackend_Change command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRemoveAccount(ExternalContactHashesBackend_RemoveAccount command, CancellationToken cancellationToken);
}

/// <summary>
/// Command to update the external contacts hash for a device.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ExternalContactHashesBackend_Change(
    [property: DataMember, Key(0)] UserDeviceId Id,
    [property: DataMember, Key(1)] long? ExpectedVersion,
    [property: DataMember, Key(2)] Change<ExternalContactsHash> Change
) : ICommand<ExternalContactsHash?>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, IgnoreMember]
    public UserId ShardKey => Id.OwnerId;
}

/// <summary>
/// Command to remove external contact hashes for a deleted account.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ExternalContactHashesBackend_RemoveAccount(
    [property: DataMember, Key(0)] UserId UserId
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, IgnoreMember]
    public UserId ShardKey => UserId;
}
