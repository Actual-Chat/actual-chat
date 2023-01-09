namespace ActualChat.Contacts;

public interface IContacts : IComputeService
{
    [ComputeMethod]
    Task<Contact?> Get(Session session, ContactId contactId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<Contact?> GetForChat(Session session, ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<ContactId>> ListIds(Session session, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Contact?> Change(ChangeCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task Touch(TouchCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record ChangeCommand(
        [property: DataMember] Session Session,
        [property: DataMember] ContactId Id,
        [property: DataMember] long? ExpectedVersion,
        [property: DataMember] Change<Contact> Change
    ) : ISessionCommand<Contact?>;

    [DataContract]
    public sealed record TouchCommand(
        [property: DataMember] Session Session,
        [property: DataMember] ContactId Id
    ) : ISessionCommand<Unit>;
}
