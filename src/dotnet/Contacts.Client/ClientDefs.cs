using RestEase;

namespace ActualChat.Contacts;

[BasePath("contacts")]
public interface IContactsClientDef
{
    [Get(nameof(Get))]
    Task<Contact?> Get(Session session, string contactId, CancellationToken cancellationToken);
    [Get(nameof(ListIds))]
    Task<ImmutableArray<ContactId>> ListIds(Session session, CancellationToken cancellationToken);

    [Post(nameof(Change))]
    Task<Contact> Change([Body] IContacts.ChangeCommand command, CancellationToken cancellationToken);
    [Post(nameof(Touch))]
    Task Touch([Body] IContacts.TouchCommand command, CancellationToken cancellationToken);
}
