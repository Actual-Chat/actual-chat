namespace ActualChat.Contacts;

public interface IContactsBackend : IComputeService
{
    [ComputeMethod]
    public Task<Contact?> Get(string ownerId, string id, CancellationToken cancellationToken);
    [ComputeMethod]
    public Task<Contact?> GetForChat(string ownerId, string chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    public Task<Contact?> GetForUser(string ownerId, string userId, CancellationToken cancellationToken);
    [ComputeMethod]
    public Task<ImmutableArray<ContactId>> ListIds(string ownerId, CancellationToken cancellationToken);

    // Not a [ComputeMethod]!
    public Task<Contact> GetOrCreateUserContact(string ownerId, string userId, CancellationToken cancellationToken);

    [CommandHandler]
    public Task<Contact> Change(ChangeCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    public Task Touch(TouchCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record ChangeCommand(
        [property: DataMember] ContactId Id,
        [property: DataMember] long? ExpectedVersion,
        [property: DataMember] Change<Contact> Change
    ) : ICommand<Contact>, IBackendCommand;

    [DataContract]
    public sealed record TouchCommand(
        [property: DataMember] ContactId Id
    ) : ICommand<Unit>, IBackendCommand;
}
