using RestEase;

namespace ActualChat.Contacts;

[BasePath("contacts")]
public interface IContactsClientDef
{
    [Get(nameof(GetOwn))]
    Task<Contact?> GetOwn(Session session, string contactId, CancellationToken cancellationToken);
    [Get(nameof(ListOwn))]
    Task<ImmutableArray<Contact>> ListOwn(Session session, CancellationToken cancellationToken);
    [Get(nameof(GetPeerChatContact))]
    Task<Contact?> GetPeerChatContact(Session session, string chatId, CancellationToken cancellationToken);

    [Post(nameof(Change))]
    Task<Contact> Change([Body] IContacts.ChangeCommand command, CancellationToken cancellationToken);
}
