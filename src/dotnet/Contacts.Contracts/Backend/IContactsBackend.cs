namespace ActualChat.Contacts;

public interface IContactsBackend : IComputeService
{
    [ComputeMethod]
    public Task<Contact> Get(UserId ownerId, ContactId contactId, CancellationToken cancellationToken);
    public Task<ImmutableArray<ContactId>> ListIds(UserId ownerId, CancellationToken cancellationToken);

    // Not a [ComputeMethod]!
    public Task<Contact> GetOrCreateUserContact(UserId ownerId, UserId userId, CancellationToken cancellationToken);

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
