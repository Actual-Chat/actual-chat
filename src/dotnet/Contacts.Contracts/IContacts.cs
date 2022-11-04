namespace ActualChat.Contacts;

public interface IContacts : IComputeService
{
    [ComputeMethod]
    public Task<Contact?> Get(Session session, string id, CancellationToken cancellationToken);
    [ComputeMethod]
    public Task<ImmutableArray<ContactId>> ListIds(Session session, CancellationToken cancellationToken);

    [CommandHandler]
    public Task<Contact> Change(ChangeCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    public Task Touch(TouchCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record ChangeCommand(
        [property: DataMember] Session Session,
        [property: DataMember] ContactId Id,
        [property: DataMember] long? ExpectedVersion,
        [property: DataMember] Change<Contact> Change
    ) : ISessionCommand<Contact>;

    [DataContract]
    public sealed record TouchCommand(
        [property: DataMember] Session Session,
        [property: DataMember] ContactId Id
    ) : ISessionCommand<Unit>;
}
