using ActualLab.Rpc;

namespace ActualChat.Contacts;

/// <summary>
/// Backend service for managing external contacts synced from device address books.
/// </summary>
public interface IExternalContactsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ExternalContactFull?> Get(ExternalContactId externalContactId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ExternalContact[]> List(UserDeviceId userDeviceId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<string> GetDisplayNameFor(UserId ownerId, UserId peerUserId, CancellationToken cancellationToken);
    [CommandHandler]
    Task<Result<ExternalContactFull?>[]> OnBulkChange(ExternalContactsBackend_BulkChange command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRemoveAccount(ExternalContactsBackend_RemoveAccount command, CancellationToken cancellationToken);
    // Not compute method!
    Task<ApiSet<ExternalContactId>> ListReferencingContactIds(UserId userId, CancellationToken cancellationToken);
}

/// <summary>
/// Command to bulk create, update, or delete external contacts.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ExternalContactsBackend_BulkChange(
    [property: DataMember, Key(0)] ExternalContactChange[] Changes
) : ICommand<Result<ExternalContactFull?>[]>, IBackendCommand, IHasShardKey<UserId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UserId ShardKey => Changes.Length > 0 ? Changes[0].Id.UserDeviceId.OwnerId : throw new ArgumentException("No changes provided", nameof(Changes));
}

/// <summary>
/// Command to update an external contact's last sync timestamp.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ExternalContactsBackend_Touch(
    [property: DataMember, Key(0)] ExternalContactId Id
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UserId ShardKey => Id.UserDeviceId.OwnerId;
}

/// <summary>
/// Command to remove all external contacts for a deleted account.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ExternalContactsBackend_RemoveAccount(
    [property: DataMember, Key(0)] UserId UserId
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UserId ShardKey => UserId;
}
