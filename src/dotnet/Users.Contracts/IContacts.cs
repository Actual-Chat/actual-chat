namespace ActualChat.Users;

public interface IContacts : IComputeService
{
    [ComputeMethod]
    public Task<ImmutableArray<Contact>> List(Session session, CancellationToken cancellationToken);

    [ComputeMethod]
    public Task<Contact?> Get(Session session, string contactId, CancellationToken cancellationToken);

    [CommandHandler]
    public Task<Contact> Change(ChangeCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record ChangeCommand(
        [property: DataMember] Session Session,
        [property: DataMember] Symbol Id,
        [property: DataMember] long? ExpectedVersion,
        [property: DataMember] Change<ContactDiff> Change
    ) : ISessionCommand<Contact>;
}
