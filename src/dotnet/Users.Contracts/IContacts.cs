namespace ActualChat.Users;

public interface IContacts : IComputeService
{
    [ComputeMethod]
    public Task<Contact?> GetOwn(Session session, string contactId, CancellationToken cancellationToken);
    [ComputeMethod]
    public Task<ImmutableArray<Contact>> ListOwn(Session session, CancellationToken cancellationToken);
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
