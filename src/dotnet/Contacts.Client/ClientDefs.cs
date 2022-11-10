using RestEase;

namespace ActualChat.Contacts;

[BasePath("contacts")]
public interface IContactsClientDef
{
    [Get(nameof(Get))]
    Task<Contact?> Get(Session session, string id, CancellationToken cancellationToken);
    [Get(nameof(GetForChat))]
    Task<Contact?> GetForChat(Session session, string chatId, CancellationToken cancellationToken);
    [Get(nameof(GetForUser))]
    Task<Contact?> GetForUser(Session session, string userId, CancellationToken cancellationToken);
    [Get(nameof(ListIds))]
    Task<ImmutableArray<ContactId>> ListIds(Session session, CancellationToken cancellationToken);

    [Post(nameof(Change))]
    Task<Contact> Change([Body] IContacts.ChangeCommand command, CancellationToken cancellationToken);
    [Post(nameof(Touch))]
    Task Touch([Body] IContacts.TouchCommand command, CancellationToken cancellationToken);
}
