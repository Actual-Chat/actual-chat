namespace ActualChat.Users;

public interface IContactsBackend : IComputeService
{
    public Task<Contact> GetOrCreate(string ownerUserId, string targetUserId, CancellationToken cancellationToken);
    [ComputeMethod]
    public Task<Contact?> Get(string contactId, CancellationToken cancellationToken);
    [ComputeMethod]
    public Task<Contact?> Get(string ownerUserId, string targetUserId, CancellationToken cancellationToken);
    [ComputeMethod]
    public Task<string[]> GetContactIds(string userId, CancellationToken cancellationToken);

    [CommandHandler]
    public Task<Contact> Change(ChangeCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record ChangeCommand(
        [property: DataMember] Symbol Id,
        [property: DataMember] long? ExpectedVersion,
        [property: DataMember] Change<ContactDiff> Change
    ) : ICommand<Contact>, IBackendCommand;
}
